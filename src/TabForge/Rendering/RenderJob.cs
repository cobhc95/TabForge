using System.Diagnostics;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Rendering;

public sealed class RenderRequest
{
    public required SongProject Project { get; init; }
    public required PluginSettings Plugins { get; init; }
    public int MasterPercent { get; init; } = 100;
    /// <summary>The rendered song's media context: its linked audio clips are judged with it (null: no document, nothing remote).</summary>
    public MediaContext? Media { get; init; }
    public required RenderSettings Settings { get; init; }
    public required ScoreTimeline Timeline { get; init; }
    public double StartMs, EndMs;
    /// <summary>Final master file (null = none) and stem files; the extension is .wav or .mp3 (MP3 is encoded from a temporary WAV).</summary>
    public string? MasterFile { get; init; }
    public IReadOnlyDictionary<TrackModel, string> Stems { get; init; } = new Dictionary<TrackModel, string>();
    /// <summary>Called on the UI thread when the render is over: re-sync the engine and re-send the programs.</summary>
    public Action? Restore { get; init; }
    /// <summary>
    /// Asked (with "name (reason)" entries) when some plug-ins did not load; true = render anyway without them.
    /// Null: a render with missing plug-ins is refused.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? ConfirmIncomplete { get; init; }
}

/// <summary>Runs one File > Render: forces engine routing, renders through the engine, encodes MP3, restores everything.</summary>
public static class RenderJob
{
    private static readonly TimeSpan ChainReadyTimeout = TimeSpan.FromSeconds(60);
    private static bool _mfStarted;
    private static readonly object MfLock = new();

    private static void EnsureMf()
    {
        lock (MfLock)
        {
            if (_mfStarted) return;
            MediaFoundationApi.Startup();
            _mfStarted = true;
        }
    }

    /// <summary>MP3 needs Media Foundation's encoder (missing on N editions and some LTSC installs).</summary>
    public static bool Mp3Available()
    {
        try
        {
            EnsureMf();
            return MediaFoundationEncoder.GetEncodeBitrates(AudioSubtypes.MFAudioFormat_MP3, 44100, 2).Length > 0;
        }
        catch (Exception) { return false; } // Not logged: job probe: false is reported by the caller
    }

    public static async Task<RenderResult> RunAsync(RenderRequest r, IProgress<RenderProgressInfo>? progress, CancellationToken cancel, AudioEngineClient engine)
    {
        var s = r.Settings;
        var mp3 = s.Format == 3;
        var mixer = engine.Mixer;
        var previousPlayAll = mixer.PlayAllThroughEngine;
        var temp = mp3 ? Path.Combine(Path.GetTempPath(), "TabForge-render-" + Guid.NewGuid().ToString("N")[..8]) : "";
        var eventFile = Path.Combine(Path.GetTempPath(), "TabForge-" + Guid.NewGuid().ToString("N")[..8] + ".tfrender");
        var succeeded = false;
        var jobId = Guid.NewGuid().ToString("N")[..8];
        var owned = new List<string>();       // staging files created by this job
        var published = new List<string>();   // files this job already renamed into place
        try
        {
            // Every track through the engine (its General MIDI synth or plug-ins), whatever the playback routing is.
            mixer.PlayAllThroughEngine = true;
            // The engine's clip sync never waits for the file system (a path still being resolved is left out until it resolves, and a render in
            // progress ignores that late refresh): classify every linked clip now so the render cannot miss one.
            MediaAccess.ResolveNow(r.Project.Tracks.SelectMany(t => t.AudioClips), r.Media);
            // Explicit readiness: every chain requested by the routing must acknowledge that request's generation.
            AudioRouting.Apply(r.Project, null, engine, r.Plugins, r.MasterPercent, media: r.Media);
            var readiness = new ChainReadiness(engine.RequestedChains);
            foreach (var a in engine.LastAcknowledgements) readiness.Acknowledge(a);   // no UI-thread yield since Apply: nothing can be missed
            void Ack(ChainAck a) => readiness.Acknowledge(a);
            engine.ChainAcknowledged += Ack;
            try
            {
                var wait = Stopwatch.StartNew();
                while (!readiness.IsReady && engine.IsRunning && wait.Elapsed < ChainReadyTimeout && !cancel.IsCancellationRequested)
                    await Task.Delay(20);
            }
            finally { engine.ChainAcknowledged -= Ack; }
            cancel.ThrowIfCancellationRequested();
            if (!engine.IsRunning) throw new RenderException("The audio engine could not be started (see Settings > Audio & Plug-ins).");
            if (!readiness.IsReady)
            {
                var names = readiness.MissingSlots.Select(s => r.Project.Tracks.FirstOrDefault(t => engine.SlotOf(t) == s)?.Name ?? $"mix bus (slot {s})");
                throw new RenderException($"The audio engine did not confirm these chains within {ChainReadyTimeout.TotalSeconds:0} s: {string.Join(", ", names)}. Nothing was rendered.");
            }
            if (readiness.Problems.Count > 0)
            {
                var problems = readiness.ProblemDescriptions;
                if (r.ConfirmIncomplete is null || !await r.ConfirmIncomplete(problems))
                    throw new RenderException("Render cancelled: plug-ins not loaded: " + string.Join(", ", problems), cancelled: r.ConfirmIncomplete is not null);
            }
            if (r.Project.Tracks.Any(t => engine.SlotOf(t) < 0)) throw new RenderException("Not every track could be given an engine slot; nothing was rendered.");
            cancel.ThrowIfCancellationRequested();

            var rate = engine.Output?.Rate ?? r.Plugins.SampleRate;
            if (rate <= 0) rate = 48000;
            long start = RenderSpecBuilder.ToFrames(r.StartMs, rate), end = RenderSpecBuilder.ToFrames(r.EndMs, rate);
            if (end <= start) throw new RenderException("The render range is empty.");

            // Every output is staged in a job-owned file next to its final path and only published on success.
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // final path -> owned staging file
            string Stage(string final)
            {
                var path = RenderStaging.TempPath(final, jobId);
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }   // exclusive: the job owns it from here
                owned.Add(path); staged[final] = path;
                return path;
            }
            string Target(string final) => mp3 ? Path.Combine(temp, Path.GetFileNameWithoutExtension(final) + ".wav") : Stage(final);
            if (mp3) Directory.CreateDirectory(temp);
            var stems = r.Stems.ToDictionary(k => k.Key, k => Target(k.Value));
            var spec = RenderSpecBuilder.Build(r.Project, r.Timeline, engine, r.MasterPercent, start, end, s, r.MasterFile is null ? "" : Target(r.MasterFile), stems, eventFile, rate);
            if (mp3 && spec.SampleRate is not (44100 or 48000)) spec.SampleRate = rate is 44100 or 48000 ? 0 : 48000;
            RenderEventFile.Write(eventFile, RenderSpecBuilder.Events(r.Timeline, r.Project, engine, rate, start, end));

            engine.Rendering = true;
            RenderResult result;
            try { result = await engine.RenderAsync(spec, progress, cancel); }
            finally { engine.Rendering = false; }

            if (mp3)
            {
                var pairs = new List<(string Wav, string Mp3)>();
                if (r.MasterFile is not null) pairs.Add((Target(r.MasterFile), r.MasterFile));
                foreach (var kv in r.Stems) pairs.Add((Target(kv.Value), kv.Value));
                await Task.Run(() =>
                {
                    EnsureMf();
                    foreach (var (wav, target) in pairs)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var stage = Stage(target);
                        using var reader = new WaveFileReader(wav);
                        MediaFoundationEncoder.EncodeToMp3(reader.ToSampleProvider().ToWaveProvider16(), stage, s.Mp3Kbps * 1000);
                    }
                });
            }
            // Publish: atomic rename, never overwriting; a name taken meanwhile gets the next free "(n)".
            foreach (var (final, stage) in staged) { cancel.ThrowIfCancellationRequested(); published.Add(RenderStaging.Publish(stage, final)); owned.Remove(stage); }
            succeeded = true;
            return result;
        }
        catch (OperationCanceledException) { throw new RenderException("Render cancelled.", cancelled: true); }
        finally
        {
            engine.Rendering = false;
            mixer.PlayAllThroughEngine = previousPlayAll;
            try { r.Restore?.Invoke(); } catch (Exception) { Services.Trace.Error(Services.Trace.Engine, "render: restore engine state: failed"); }
            try { ResendPrograms(r, engine); } catch (Exception) { Services.Trace.Error(Services.Trace.Engine, "render: resend programs: failed"); }
            if (!succeeded)
            {
                // Cancelled or failed: delete only what this job created (its staging files and anything it already published),
                // never a planned final path that might belong to someone else.
                foreach (var p in owned.Concat(published)) try { File.Delete(p); } catch (Exception) { } // Not logged: cleanup of files this job created; leftovers go to the temp sweep.
            }
            try { File.Delete(eventFile); } catch (Exception) { } // Not logged: cleanup of files this job created; leftovers go to the temp sweep.
            if (temp.Length > 0) try { Directory.Delete(temp, true); } catch (Exception) { } // Not logged: cleanup of files this job created; leftovers go to the temp sweep.
        }
    }

    /// <summary>The engine's General MIDI synths were driven by the render: give them their programs, volumes and pans again.</summary>
    private static void ResendPrograms(RenderRequest r, AudioEngineClient engine)
    {
        if (!engine.IsRunning) return;
        try
        {
            foreach (var e in r.Timeline.ChannelSetup)
            {
                if (e.TrackIndex < 0 || e.TrackIndex >= r.Project.Tracks.Count) continue;
                var slot = engine.SlotOf(r.Project.Tracks[e.TrackIndex]);
                if (slot < 0) continue;
                engine.Write(new TimedMidi { Timestamp = Stopwatch.GetTimestamp(), Slot = slot, Status = (byte)e.Status, Data1 = (byte)e.Data1, Data2 = (byte)e.Data2 });
            }
        }
        catch (Exception) { /* best effort */ } // Not logged: per-event MIDI feed on the render path; a full buffer is expected under load.
    }
}
