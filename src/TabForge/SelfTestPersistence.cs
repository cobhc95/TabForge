using System.IO;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

/// <summary>Save integrity: transactional .gp / .gp + .tfaudio writes, plug-in state capture results, sidecar routing identity, disk schema.</summary>
public static partial class SelfTest
{
    private static SongProject PersistenceSong(params string[] names)
    {
        var p = new SongProject { Title = "Persist" };
        foreach (var name in names) p.Tracks.Add(new TrackModel { Name = name, Measures = TemplateFactory.Measures(1) });
        return p;
    }

    private static DocumentSession SessionFor(SongProject project) => DocumentSession.FromProject(project, null);

    // Async save: states are awaited (the starting call returns at once), a second save is refused meanwhile, and the write runs only after the states arrived.
    private static void TestAsyncSaveSequencing()
    {
        // The save resumes on the caller's synchronization context; the self-test runs on the UI thread and would block it
        // with Wait(), so run the sequence on a pool thread (the real UI awaits and never blocks).
        if (SynchronizationContext.Current is not null) { Task.Run(TestAsyncSaveSequencing).Wait(); return; }
        var controller = new DocumentController();
        var gate = new TaskCompletionSource<StateCollection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var savingEvents = 0;
        controller.SavingChanged += () => savingEvents++;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var save = controller.SaveAsync(() => { order.Add("collect"); return gate.Task; }, _ => order.Add("write"));
        var returnedAt = started.ElapsedMilliseconds;
        Check("save starts and returns without waiting for the plug-in states", !save.IsCompleted && returnedAt < 500 && controller.IsSaving);
        var second = controller.SaveAsync(() => Task.FromResult(new StateCollection()), _ => order.Add("second")).GetAwaiter().GetResult();
        Check("a second save while one is reading states is refused", second is null && !order.Contains("second"));
        Check("nothing is written before the states arrive", order.SequenceEqual(new[] { "collect" }));
        Thread.Sleep(150);
        Check("the delayed state source does not make the save complete early", !save.IsCompleted);
        gate.SetResult(new StateCollection());
        var result = save.Wait(5000);
        Check("the write runs once, after the states arrived", result && order.SequenceEqual(new[] { "collect", "write" }) && !controller.IsSaving && savingEvents == 2);
        var failed = controller.SaveAsync(() => Task.FromResult(new StateCollection()), _ => throw new IOException("x"));
        try { failed.GetAwaiter().GetResult(); } catch (IOException) { }
        Check("a failed write releases the saving lock", !controller.IsSaving);
    }

    // Audit #1: an injected failure while writing the .gp, or while committing the .tfaudio, leaves the previous files byte-identical and readable.
    private static void TestSaveTransactions()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var tfaudio = AudioDataFile.PathFor(gp);
            GuitarProExporter.Save(PersistenceSong("Old"), gp, embedProject: false);
            var before = File.ReadAllBytes(gp);
            FilePathPolicy.FaultInjection = stage => { if (stage == "staged:song.gp") throw new IOException("simulated disk full"); };
            try { GuitarProExporter.Save(PersistenceSong("New"), gp, embedProject: false); } catch (IOException) { }
            finally { FilePathPolicy.FaultInjection = null; }
            Check("a failed .gp write leaves the previous song byte-identical and readable",
                File.ReadAllBytes(gp).AsSpan().SequenceEqual(before) && GuitarProImporter.Import(gp).Tracks[0].Name == "Old");
            Check("a failed .gp write leaves no temporary files behind", Directory.GetFiles(folder).Length == 1, string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));

            // The pair: old .gp + .tfaudio, then a new save that fails at the second commit (after the .gp was already replaced).
            var old = PersistenceSong("Lead");
            old.Tracks[0].SoundSource = SoundSources.Plugins;
            old.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "ReaEQ", Path = @"C:\x\reaeq.dll", Format = "VST2" });
            new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(old), gp, "");
            var oldGp = File.ReadAllBytes(gp);
            var oldAudio = File.ReadAllBytes(tfaudio);
            foreach (var stage in new[] { "staged:song.gp", "staged:song.tfaudio", "commit:song.gp", "commit:song.tfaudio" })
            {
                var changed = PersistenceSong("Lead");
                changed.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 7 });
                changed.Tracks[0].SoundSource = SoundSources.Plugins;
                changed.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "ReaComp", Path = @"C:\x\reacomp.dll", Format = "VST2" });
                var failed = false;
                FilePathPolicy.FaultInjection = s => { if (s == stage) throw new IOException("simulated failure"); };
                try { new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(changed), gp, ""); }
                catch (IOException) { failed = true; }
                finally { FilePathPolicy.FaultInjection = null; }
                var reopened = new DocumentController().Open(gp);
                Check($".gp + .tfaudio failing at {stage} keeps the old pair byte-identical, readable and matching",
                    failed && File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldGp) && File.ReadAllBytes(tfaudio).AsSpan().SequenceEqual(oldAudio)
                    && reopened.Project.Tracks[0].Rig.Plugins.Single().Name == "ReaEQ" && reopened.Notice is null
                    && Directory.GetFiles(folder).Length == 2,
                    $"failed={failed}, notice={reopened.Notice}, files={string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName))}");
            }

            // The .tfaudio records the .gp it belongs to: a .gp changed elsewhere is applied behind a warning, never silently.
            GuitarProExporter.Save(PersistenceSong("Lead", "Other"), gp, embedProject: false);
            var edited = new DocumentController().Open(gp);
            Check("a .tfaudio saved with a different .gp is applied by name with a warning",
                edited.Notice?.Contains("different version", StringComparison.Ordinal) == true && edited.Project.Tracks[0].Rig.Plugins.Count == 1, edited.Notice);
        }
        finally { FilePathPolicy.FaultInjection = null; try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Pair save: a crash between the two commits (simulated by a failed restore, which leaves the same on-disk state) is recovered on the next open; a restore failure is reported with both paths and never deletes the backup.
    private static void TestPairSaveRecovery()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-pair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var tfaudio = AudioDataFile.PathFor(gp);
            new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(PersistenceSong("Old")), gp, "");
            var oldGp = File.ReadAllBytes(gp);
            var oldAudio = File.ReadAllBytes(tfaudio);
            Check("a completed pair save leaves no marker and nothing to recover",
                Directory.GetFiles(folder).Length == 2 && FilePathPolicy.RecoverInterruptedPair(gp, tfaudio) is null);

            var changed = PersistenceSong("New");
            changed.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
            FilePathPolicy.FaultInjection = s => { if (s is "commit:song.tfaudio" or "restore:song.gp") throw new IOException("simulated failure"); };
            string? message = null;
            try { new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(changed), gp, ""); }
            catch (IOException ex) { message = ex.Message; }
            finally { FilePathPolicy.FaultInjection = null; }
            var files = Directory.GetFiles(folder).Select(Path.GetFileName).ToList();
            Check("a failed restore is reported with both paths",
                message is not null && message.Contains(gp, StringComparison.Ordinal) && message.Contains(".bak", StringComparison.Ordinal) && message.Contains(tfaudio, StringComparison.Ordinal), message);
            Check("a failed restore keeps the backup and the save-in-progress marker",
                files.Any(f => f!.EndsWith(".bak", StringComparison.Ordinal)) && files.Any(f => f!.EndsWith(".pending", StringComparison.Ordinal)), string.Join(", ", files));
            Check("between the two commits the .gp is new and the .tfaudio old (the crash state)",
                !File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldGp) && File.ReadAllBytes(tfaudio).AsSpan().SequenceEqual(oldAudio));

            var opened = new DocumentController().Open(gp);
            Check("opening the file after the interruption restores the old pair and says so",
                File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldGp) && File.ReadAllBytes(tfaudio).AsSpan().SequenceEqual(oldAudio)
                && opened.Notice?.Contains("interrupted", StringComparison.Ordinal) == true && opened.Project.Tracks[0].Name == "Old", opened.Notice);
            Check("recovery removes the backup, the marker and the staged files",
                Directory.GetFiles(folder).Length == 2, string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
        }
        finally { FilePathPolicy.FaultInjection = null; try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Audit #6: per-plug-in state results; delayed / failed / empty / near-limit states; one frame per plug-in; the save stays dirty when incomplete.
    private static void TestPluginStateCollection()
    {
        Check("one plug-in state contract: the song limit is 16 MiB raw in base64, and one state fits one IPC frame",
            InputLimits.MaxPluginStateChars == PluginStateLimits.MaxBase64Chars && PluginStateLimits.MaxBase64Chars == 22_369_624
            && PluginStateLimits.MaxBase64Chars + 1024 < Frames.MaxFrameBytes);
        TestChainLoadStates();

        (byte, BinaryReader) Frame(Action<BinaryWriter> payload)
        {
            var stream = new MemoryStream();
            Frames.Write(stream, (byte)EngineEvent.PluginState, payload);
            stream.Position = 0;
            var frame = Frames.Read(stream)!.Value;
            return (frame.Type, frame.Reader);
        }
        Action<BinaryWriter> Reply(int slot, int id, int index, int count, PluginStateStatus status, string state) =>
            w => { w.Write(slot); w.Write(id); w.Write(index); w.Write(count); w.Write((byte)status); w.WriteString(state); };

        var client = new AudioEngineClient();
        var track = new TrackModel { Name = "Synths" };
        for (var i = 0; i < 5; i++) track.Rig.Plugins.Add(new PluginSlot { Name = $"P{i}", State = $"old{i}" });
        var request = client.BeginStateRequest(7);
        var nearLimit = new string('A', PluginStateLimits.MaxBase64Chars);
        client.OnPluginState(Frame(Reply(7, request.Id, 0, 5, PluginStateStatus.Captured, nearLimit)).Item2);
        client.OnPluginState(Frame(Reply(7, request.Id, 1, 5, PluginStateStatus.Failed, "")).Item2);
        client.OnPluginState(Frame(Reply(7, request.Id, 2, 5, PluginStateStatus.NoState, "")).Item2);
        client.OnPluginState(Frame(Reply(7, request.Id, 3, 5, PluginStateStatus.Captured, "")).Item2);   // an empty valid state
        client.OnPluginState(Frame(Reply(7, request.Id + 99, 4, 5, PluginStateStatus.Captured, "c3RhbGU=")).Item2);   // another request's reply
        Check("a state request is not complete while a plug-in has not answered", !request.Done.Task.IsCompleted);
        var results = AudioEngineClient.ApplyStates(track, request);
        var outcomes = string.Join(",", results.Select(r => r.Outcome));
        Check("each plug-in gets its own outcome: captured (near-limit), failed, no state, empty, no answer",
            outcomes == "Captured,Failed,Unchanged,Unchanged,TimedOut", outcomes);
        Check("only captured states replace stored ones; failed / unanswered plug-ins keep their previous state",
            track.Rig.Plugins[0].State == nearLimit && Enumerable.Range(1, 4).All(i => track.Rig.Plugins[i].State == $"old{i}"));
        var collection = new StateCollection();
        collection.Results.AddRange(results);
        Check("an incomplete capture produces the save warning (never silent success)",
            !collection.Complete && collection.IncompleteCount == 2
            && collection.Warning?.Contains("2 plug-in states could not be read", StringComparison.Ordinal) == true
            && collection.Warning.Contains("previous settings", StringComparison.Ordinal), collection.Warning);
        client.OnPluginState(Frame(Reply(7, request.Id, 4, 5, PluginStateStatus.Captured, "bmV3")).Item2);
        Check("the last plug-in's reply completes the request", request.Done.Task.IsCompleted);

        var overLimit = Throws(() => client.OnPluginState(Frame(Reply(7, request.Id, 0, 5, PluginStateStatus.Captured, nearLimit + "AAAA")).Item2));
        Check("a state over the contract is refused by the bounded read", overLimit);
        var aggregateRefused = Throws(() => Frame(w => { for (var i = 0; i < 2; i++) Reply(7, 1, i, 2, PluginStateStatus.Captured, nearLimit)(w); }));
        Check("two near-limit states exceed one 32 MiB frame together (so they travel one frame per plug-in)", aggregateRefused);

        var empty = new StateCollection();
        Check("no plug-ins (or no engine) is a complete capture", empty.Complete && empty.Warning is null);
        var doc = SessionFor(PersistenceSong("A"));
        doc.MarkClean();
        doc.MarkIncomplete();
        Check("an incomplete save keeps the song marked unsaved", doc.IsDirty && doc.HasUnsavedChanges);
    }

    // Chain loads: spec without states, then one frame per plug-in state, then a commit (a chain's states may exceed one frame together).
    private static void TestChainLoadStates()
    {
        var raw = new byte[12 * 1024 * 1024];
        string State(int seed) { raw.AsSpan().Fill((byte)(seed + 1)); raw[0] = (byte)(0x80 | seed); return Convert.ToBase64String(raw); }
        var specs = Enumerable.Range(0, 3).Select(i => new PluginSpec($"C:\\fx{i}.vst3", "VST3", false, true, 100, State(i), $"FX{i}", Id: $"id{i}")).ToList();
        var stream = new MemoryStream();
        var frames = 0;
        ChainLoadProtocol.Send((c, p) => { Frames.Write(stream, (byte)c, p); frames++; }, 4, false, "Guitar", specs, 11);
        stream.Position = 0;
        var assembler = new ChainLoadProtocol.Assembler();
        ChainLoadProtocol.Ready? ready = null;
        long largest = 0;
        for (var at = stream.Position; Frames.Read(stream) is { } f; at = stream.Position)
        {
            largest = Math.Max(largest, stream.Position - at);
            switch ((EngineCommand)f.Type)
            {
                case EngineCommand.LoadChain: assembler.OnLoadChain(f.Reader); break;
                case EngineCommand.ChainState: assembler.OnChainState(f.Reader); break;
                case EngineCommand.ChainCommit: ready = assembler.OnCommit(f.Reader); break;
            }
        }
        var aggregate = specs.Sum(s => (long)s.State!.Length);
        Check("a 3-plug-in chain with 12 MiB states each (> 32 MiB together) round-trips, one state per frame",
            aggregate > Frames.MaxFrameBytes && frames == 5 && largest <= Frames.MaxFrameBytes + 4 && ready is { Missing.Count: 0 }
            && ready.Specs.Select(s => s.State).SequenceEqual(specs.Select(s => s.State)) && ready.Specs.Select(s => s.Id).SequenceEqual(specs.Select(s => s.Id)),
            $"frames {frames}, largest {largest}, missing {ready?.Missing.Count}");

        // A state frame that never arrives: the chain still loads (that plug-in with defaults) and the gap is reported.
        stream = new MemoryStream();
        ChainLoadProtocol.Send((c, p) => { if (c != EngineCommand.ChainState) Frames.Write(stream, (byte)c, p); }, 4, false, "Guitar", specs.Take(1).ToList(), 12);
        stream.Position = 0;
        assembler = new ChainLoadProtocol.Assembler();
        ready = null;
        while (Frames.Read(stream) is { } f)
            if ((EngineCommand)f.Type == EngineCommand.LoadChain) assembler.OnLoadChain(f.Reader);
            else if ((EngineCommand)f.Type == EngineCommand.ChainCommit) ready = assembler.OnCommit(f.Reader);
        Check("a missing state frame loads the plug-in without a state and reports it", ready is { Missing: [0] } && ready.Specs[0].State is null);
    }

    // Audit #7: clean .gp + .tfaudio keeps routing identity (sidechain, MIDI forward, cross-track MIDI input, bus links) through fresh track ids.
    private static void TestSidecarRouting()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var song = PersistenceSong("Lead", "Bass", "Keys");
            var (lead, bass, keys) = (song.Tracks[0], song.Tracks[1], song.Tracks[2]);
            foreach (var t in song.Tracks) t.SoundSource = SoundSources.Plugins;
            lead.Rig.Plugins.Add(new PluginSlot { Name = "Comp", Path = @"C:\x\comp.dll", Format = "VST2", SidechainTrackId = bass.Id.ToString("N"), MidiOutTrackId = keys.Id.ToString("N") });
            keys.Rig.Plugins.Add(new PluginSlot { Name = "Synth", Path = @"C:\x\synth.dll", Format = "VST2", Type = PluginSlotType.Instrument, MidiIn = new PluginMidiIn { Source = PluginMidiIn.OtherTrack, TrackId = lead.Id.ToString("N") } });
            bass.Rig.Plugins.Add(new PluginSlot { Name = "Gate", Path = @"C:\x\gate.dll", Format = "VST2", MidiOutTrackId = Guid.NewGuid().ToString("N") });   // dangling
            song.Mixer.Master.Rig.Plugins.Add(new PluginSlot { Name = "Duck", Path = @"C:\x\duck.dll", Format = "VST2", SidechainTrackId = keys.Id.ToString("N") });
            new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(song), gp, "");

            var opened = new DocumentController().Open(gp);
            var p = opened.Project;
            TrackModel Named(string name) => p.Tracks.Single(t => t.Name == name);
            string Id(string name) => Named(name).Id.ToString("N");
            Check("re-importing a clean .gp gives the tracks fresh ids", Named("Lead").Id != lead.Id);
            var comp = Named("Lead").Rig.Plugins.Single();
            Check("sidechain and MIDI forward links follow their tracks through the .tfaudio",
                comp.SidechainTrackId == Id("Bass") && comp.MidiOutTrackId == Id("Keys"), $"{comp.SidechainTrackId} / {comp.MidiOutTrackId}");
            var synth = Named("Keys").Rig.Plugins.Single();
            Check("cross-track MIDI input follows its source track", synth.MidiIn.Source == PluginMidiIn.OtherTrack && synth.MidiIn.TrackId == Id("Lead"));
            Check("bus / master chain links follow their tracks", p.Mixer.Master.Rig.Plugins.Single().SidechainTrackId == Id("Keys"));
            Check("an unresolvable link is cleared and reported",
                Named("Bass").Rig.Plugins.Single().MidiOutTrackId == "" && opened.Notice?.Contains("could not be matched", StringComparison.Ordinal) == true, opened.Notice);

            // Reordered and extended in another editor: links still resolve by name, behind the "different version" warning.
            var reordered = PersistenceSong("Keys", "Drums", "Lead", "Bass");
            GuitarProExporter.Save(reordered, gp, embedProject: false);
            var again = new DocumentController().Open(gp);
            p = again.Project;
            comp = Named("Lead").Rig.Plugins.Single();
            Check("after reordering tracks externally the links still resolve to the right tracks",
                comp.SidechainTrackId == Id("Bass") && comp.MidiOutTrackId == Id("Keys") && Named("Keys").Rig.Plugins.Single().MidiIn.TrackId == Id("Lead")
                && again.Notice?.Contains("different version", StringComparison.Ordinal) == true, again.Notice);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Audit #12: .tforge files always carry FormatVersion and every value; old compacted files (no FormatVersion, omitted defaults) load identically.
    private static void TestPersistenceSchema()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            // A schema-2 file as the compacting writer produced it: FormatVersion and every initializer value omitted.
            var cells = string.Join(",", Enumerable.Range(0, 16).Select(i => i == 0 ? """{"Notes":[{"Fret":5}]}""" : "{}"));
            var fixture = $$"""{"Title":"Fixture","Tempo":140,"Tracks":[{"Id":"01234567-89ab-cdef-0123-456789abcdef","Name":"Lead","StringTunings":[64,59,55,50,45,40],"Measures":[{"Cells":[{{cells}}]}]}]}""";
            var legacy = Path.Combine(folder, "legacy.tforge");
            File.WriteAllText(legacy, fixture);
            var loaded = ProjectService.Load(legacy);
            var t = loaded.Tracks[0];
            var m = t.Measures[0];
            var n = m.Cells[0].Notes[0];
            Check("a legacy compacted file without FormatVersion reads as schema 2 with its frozen defaults",
                loaded.FormatVersion == ProjectService.LegacyCompactedFormatVersion && loaded.Title == "Fixture" && loaded.Tempo == 140
                && loaded.TimeSignatureNumerator == 4 && loaded.TimeSignatureDenominator == 4 && loaded.Mixer is not null
                && t.Volume == 100 && t.Pan == 64 && t.Reverb == 24 && t.MidiProgram == 29 && t.NumberOfFrets == 24 && t.Kind == TrackKind.Guitar
                && t.SoundSource == SoundSources.Midi && t.MidiSound && t.MonitorInput && t.Rig.Name == "Clean"
                && m.RepeatCount == 2 && m.Cells.Count == 16 && m.Cells[0].DurationDenominator == 8 && n.Fret == 5 && n.StringIndex == 0 && n.Velocity == 100);

            var full = Path.Combine(folder, "full.tforge");
            ProjectService.Save(full, loaded);
            var text = ProjectService.ReadJsonText(full);
            var reloaded = ProjectService.Load(full);
            Check("saved .tforge files always write FormatVersion and default values",
                text.Contains("\"FormatVersion\":2", StringComparison.Ordinal) && text.Contains("\"Velocity\":100", StringComparison.Ordinal) && text.Contains("\"RepeatCount\":2", StringComparison.Ordinal));
            Check("the legacy fixture and its full re-save load identically",
                ProjectService.ContentHash(reloaded).AsSpan().SequenceEqual(ProjectService.ContentHash(loaded)));
            File.WriteAllText(legacy, fixture.Replace("{\"Title\"", "{\"FormatVersion\":1,\"Title\"", StringComparison.Ordinal));
            Check("an explicit FormatVersion is kept", ProjectService.Load(legacy).FormatVersion == 1);
            Check("the project embedded in a .gp is written in full, and old compacted embeds still read as schema 2",
                ProjectService.RestorePersistedBytes(ProjectService.PersistBytes(loaded)).FormatVersion == 2
                && ProjectService.RestorePersistedBytes(ProjectService.SnapshotBytes(loaded)).FormatVersion == 2
                && ProjectService.HasRootProperty(System.Text.Encoding.UTF8.GetBytes(text), "FormatVersion")
                && !ProjectService.HasRootProperty(System.Text.Encoding.UTF8.GetBytes(fixture), "FormatVersion"));
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // A5-03: one .tfaudio size limit for writing and reading. An oversized pair save fails before either file is touched (the song stays
    // unsaved, the message names the large plug-in states); a sidecar that cannot be read is reported on open, never dropped silently.
    private static void TestAudioDataSizeLimit()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-a503-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var tfaudio = AudioDataFile.PathFor(gp);
            var old = PersistenceSong("Lead");
            old.Tracks[0].SoundSource = SoundSources.Plugins;
            old.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "ReaEQ", Path = @"C:\x\reaeq.dll", Format = "VST2" });
            new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(old), gp, "");
            var oldGp = File.ReadAllBytes(gp);
            var oldAudio = File.ReadAllBytes(tfaudio);

            var big = PersistenceSong("Lead");
            big.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 });
            big.Tracks[0].SoundSource = SoundSources.Plugins;
            big.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "HugeSampler", Path = @"C:\x\huge.dll", Format = "VST2", State = new string('A', (int)AudioDataFile.MaxBytes + 1024) });
            big.IsDirty = true;
            var session = SessionFor(big);
            string? message = null;
            try { new DocumentController().SaveCleanGuitarProWithAudioData(session, gp, ""); }
            catch (InvalidDataException ex) { message = ex.Message; }
            Check("A5-03: an oversized .tfaudio fails the save with a message naming the large plug-in state",
                message is not null && message.Contains("HugeSampler on Lead", StringComparison.Ordinal) && message.Contains("limit", StringComparison.Ordinal), message);
            Check("A5-03: the failed save leaves the old pair byte-identical and nothing else behind",
                File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldGp) && File.ReadAllBytes(tfaudio).AsSpan().SequenceEqual(oldAudio) && Directory.GetFiles(folder).Length == 2,
                string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            Check("A5-03: the song stays unsaved after the failed save", session.HasUnsavedChanges && session.IsNew && session.Path is null && big.IsDirty);
            big.Tracks[0].Rig.Plugins[0].State = null;   // release the large string

            // Reading: damaged, incomplete and oversized sidecars are each reported and the score still opens.
            File.WriteAllText(tfaudio, "{ this is not json");
            var damaged = new DocumentController().Open(gp);
            Check("A5-03: a damaged .tfaudio is reported on open", damaged.Notice?.Contains("was not applied (the file is damaged)", StringComparison.Ordinal) == true
                && damaged.Project.Tracks[0].Rig.Plugins.Count == 0, damaged.Notice);
            File.WriteAllText(tfaudio, "null");
            var incomplete = new DocumentController().Open(gp);
            Check("A5-03: an incomplete .tfaudio is reported on open", incomplete.Notice?.Contains("was not applied", StringComparison.Ordinal) == true, incomplete.Notice);
            using (var stream = new FileStream(tfaudio, FileMode.Create, FileAccess.Write)) stream.SetLength(AudioDataFile.MaxBytes + 1);
            var oversized = new DocumentController().Open(gp);
            Check("A5-03: an oversized .tfaudio is reported on open with the reason", oversized.Notice?.Contains("was not applied", StringComparison.Ordinal) == true
                && oversized.Notice.Contains("size limit", StringComparison.Ordinal) && oversized.Notice.Contains("mixer, plug-in and clip settings", StringComparison.Ordinal), oversized.Notice);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // A6-02: the project embedded in a .gp has one size limit for writing and reading. An over-limit save is refused before any file is
    // touched (song stays unsaved, message names the large plug-in state); a present-but-unusable embedded project is reported on open
    // (the file opens as plain Guitar Pro), an absent one is silent, and a normal round trip is unchanged.
    private static void TestEmbeddedProjectLimit()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-a602-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var gp = Path.Combine(folder, "song.gp");
            var old = PersistenceSong("Lead");
            old.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 5 });
            new DocumentController().Save(SessionFor(old), gp, "");
            var oldBytes = File.ReadAllBytes(gp);
            var normal = new DocumentController().Open(gp);
            Check("A6-02: a normal .gp round trip is unchanged (embedded project read back, no notice)",
                normal.Notice is null && GuitarProExporter.TryReadEmbedded(gp) is { FormatVersion: 2 } && normal.Project.Tracks[0].Name == "Lead", normal.Notice);

            var big = PersistenceSong("Lead");
            big.Tracks[0].SoundSource = SoundSources.Plugins;
            big.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "HugeSampler", Path = @"C:\x\huge.dll", Format = "VST2", State = new string('A', (int)InputLimits.MaxTforgeFileBytes + 1024) });
            big.IsDirty = true;
            var session = SessionFor(big);
            string? message = null;
            try { new DocumentController().Save(session, gp, ""); }
            catch (InvalidDataException ex) { message = ex.Message; }
            Check("A6-02: an over-limit .gp save is refused with a message naming the large plug-in state",
                message is not null && message.Contains("HugeSampler on Lead", StringComparison.Ordinal) && message.Contains("limit", StringComparison.Ordinal) && message.Contains("nothing was saved", StringComparison.Ordinal), message);
            Check("A6-02: the refused save leaves the old .gp byte-identical and nothing else behind",
                File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldBytes) && Directory.GetFiles(folder).Length == 1, string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            Check("A6-02: the song stays unsaved after the refused save", session.HasUnsavedChanges && session.IsNew && session.Path is null && big.IsDirty);
            big.Tracks[0].Rig.Plugins[0].State = null;   // release the large string

            // Reading: a crafted .gp with each kind of unusable embedded entry opens as plain Guitar Pro with a notice.
            var clean = GuitarProExporter.ToBytes(old, embedProject: false);
            static byte[] Gzip(Action<Stream> write)
            {
                using var output = new MemoryStream();
                using (var z = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true)) write(z);
                return output.ToArray();
            }
            byte[] WithEntry(byte[] entryData)
            {
                using var zipStream = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var part in GuitarProExporter.ReadZip(clean))
                    {
                        var e = zip.CreateEntry(part.Name, System.IO.Compression.CompressionLevel.Optimal);
                        if (part.Name.EndsWith('/')) continue;
                        using var s = e.Open();
                        s.Write(part.Data);
                    }
                    using var embedded = zip.CreateEntry(GuitarProExporter.EmbeddedProjectEntry, System.IO.Compression.CompressionLevel.Optimal).Open();
                    embedded.Write(entryData);
                }
                return zipStream.ToArray();
            }
            void Expect(string label, byte[] entryData, string reason)
            {
                var path = Path.Combine(folder, label + ".gp");
                File.WriteAllBytes(path, WithEntry(entryData));
                var opened = new DocumentController().Open(path);
                Check($"A6-02: a .gp whose embedded project is {label} opens as plain Guitar Pro with a notice giving the reason",
                    opened.Notice is not null && opened.Notice.Contains("TabForge project data could not be read (" + reason, StringComparison.Ordinal)
                    && opened.Notice.Contains("opened as a plain Guitar Pro file", StringComparison.Ordinal) && opened.Project.Tracks.Count == 1, opened.Notice);
                var viaWorker = new List<string>();
                SongProject project; try { project = ImportWorker.Import(path, null, viaWorker); } catch (ImportWorkerUnavailableException ex) { project = ImportWorker.ImportInProcess(path, viaWorker, ex.Message); }
                Check($"A6-02: the background import reports the same notice ({label})",
                    viaWorker.Any(n => n.Contains("TabForge project data could not be read (" + reason, StringComparison.Ordinal)) && project.Tracks.Count == 1, string.Join("; ", viaWorker));
            }
            Expect("oversize", Gzip(z => { var zeros = new byte[1024 * 1024]; for (var i = 0; i <= InputLimits.MaxTforgeFileBytes / zeros.Length; i++) z.Write(zeros, 0, zeros.Length); }), "it is over the");
            Expect("corrupt", System.Text.Encoding.ASCII.GetBytes("this is not a gzip stream"), "the data is damaged or invalid");
            var goodJson = System.Text.Encoding.UTF8.GetString(ProjectService.GunzipBounded(ProjectService.PersistBytes(old)));
            Check("A6-02: the bad-version fixture really carries a FormatVersion to replace", goodJson.Contains("\"FormatVersion\":2", StringComparison.Ordinal));
            Expect("bad-version", Gzip(z => z.Write(System.Text.Encoding.UTF8.GetBytes(goodJson.Replace("\"FormatVersion\":2", "\"FormatVersion\":99", StringComparison.Ordinal)))), "it was saved by a newer or unknown version");

            var plain = Path.Combine(folder, "plain.gp");
            File.WriteAllBytes(plain, clean);
            var absent = new DocumentController().Open(plain);
            Check("A6-02: a .gp with no embedded project opens silently", absent.Notice is null && absent.Project.Tracks.Count == 1, absent.Notice);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // A6-02 follow-up: a crash-recovery copy over the .tforge limit is written with the recovery bound and read back with the same bound,
    // but only from the app's own Recovery folder (elsewhere the normal limit still applies).
    private static void TestRecoveryCopyOverTforgeLimit()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-a602r-{Guid.NewGuid():N}");
        var other = Path.Combine(folder, "elsewhere");
        Directory.CreateDirectory(other);
        try
        {
            var state = new string('A', InputLimits.MaxPluginStateChars);   // the largest single state the validator accepts
            var big = PersistenceSong("T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8");
            foreach (var track in big.Tracks)
            {
                track.SoundSource = SoundSources.Plugins;
                track.Rig.Plugins.Add(new PluginSlot { Name = "Sampler", Path = @"C:\x\s.dll", Format = "VST2", State = state });
            }
            var name = $"autosave-4242-{Guid.NewGuid():N}-Big.tforge";
            var copy = Path.Combine(folder, name);
            var errors = App.WriteRecoveryCopy(big, copy);
            Check("A6-02: the recovery copy of a song over the .tforge limit is written", errors is null && File.Exists(copy), errors);
            var json = ProjectService.GunzipBounded(File.ReadAllBytes(copy), InputLimits.MaxRecoveryProjectBytes);
            Check("A6-02: that copy really is over the .tforge JSON limit (and within the recovery bound)",
                json.LongLength > InputLimits.MaxTforgeFileBytes && json.LongLength <= InputLimits.MaxRecoveryProjectBytes, $"{json.LongLength:N0} bytes");
            json = Array.Empty<byte>();
            var normal = false;
            try { ProjectService.Load(copy); } catch (InvalidDataException) { normal = true; }
            Check("A6-02: the normal .tforge reader still refuses it", normal);
            var recovered = new DocumentController { RecoveryFolder = folder }.Open(copy);
            Check("A6-02: at restart the recovery folder's copy is recovered", recovered.Project.Tracks.Count == 8 && recovered.Project.Tracks[7].Rig.Plugins[0].State?.Length == state.Length);
            recovered = default;
            var moved = Path.Combine(other, name);
            File.Copy(copy, moved);
            var refused = false;
            try { new DocumentController { RecoveryFolder = folder }.Open(moved); } catch (InvalidDataException) { refused = true; }
            Check("A6-02: the larger bound applies to the Recovery folder only, not to the same file elsewhere", refused);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // A5-04: opening a .gp keeps the song's own title (clean and TabForge-embedded); only an empty title falls back to the file name.
    private static void TestGpOpenKeepsTitle()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"tf-a504-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var clean = Path.Combine(folder, "rehearsal-copy.gp");
            var song = PersistenceSong("Lead");
            song.Title = "Original Song";
            new DocumentController().SaveCleanGuitarProWithAudioData(SessionFor(song), clean, "");
            var opened = new DocumentController().Open(clean).Project;
            Check("A5-04: \"Original Song\" saved as rehearsal-copy.gp keeps its title", opened.Title == "Original Song", opened.Title);

            var embedded = Path.Combine(folder, "embedded-copy.gp");
            var copy = PersistenceSong("Lead");
            copy.Title = "Original Song";
            new DocumentController().Save(SessionFor(copy), embedded, "");
            var openedEmbedded = new DocumentController().Open(embedded).Project;
            Check("A5-04: a .gp with the TabForge project embedded keeps its title", openedEmbedded.Title == "Original Song", openedEmbedded.Title);

            var untitledPath = Path.Combine(folder, "untitled-take.gp");
            var untitled = PersistenceSong("Lead");
            untitled.Title = "  ";
            new DocumentController().Save(SessionFor(untitled), untitledPath, "");
            var openedUntitled = new DocumentController().Open(untitledPath).Project;
            Check("A5-04: a .gp with an empty title is named after its file", openedUntitled.Title == "untitled-take", openedUntitled.Title);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
