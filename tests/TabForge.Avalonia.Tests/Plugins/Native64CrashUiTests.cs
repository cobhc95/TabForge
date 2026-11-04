using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the real main-window proof that a 64-bit VST2 fault in the native route leaves the window alive, shows the engine's
///     crash notice on the top bar, and lets the window restart the engine without the crashed plug-in.
/// Does not own: the engine/client mechanics (Native64EngineCrashTests) or the editor-window paths.
/// Tests: this class.
/// </summary>
[Collection("EngineAudible")]
public sealed class Native64CrashUiTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly ITestOutputHelper _output;

    public Native64CrashUiTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void TheWindowShowsTheCrashNoticeAndStaysOpenWhileTheEngineRestarts()
    {
        var synth1 = Environment.GetEnvironmentVariable("TABFORGE_SYNTH1_VST64");
        if (string.IsNullOrWhiteSpace(synth1) || !File.Exists(synth1)) return;   // owner-PC real fixture; never committed

        var folder = Path.Combine(Path.GetTempPath(), "tf-native64-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settingsPath = Path.Combine(folder, "settings.json");
            File.WriteAllText(settingsPath, "{\"Plugins\":{\"Driver\":\"Null\",\"Device\":\"\"}}");
            using var settings = MainWindowSettings.Open(settingsPath);
            var plugins = settings.Settings.Plugins;
            var trust = new PluginTrust();
            trust.Approve(plugins, synth1, new Vst32TestAssets.NoFiles());

            var project = new SongProject();
            var track = NewTrackFactory.CreateTrack(project, TrackKind.Guitar);
            track.SoundSource = SoundSources.Plugins;
            track.Rig.Plugins.Add(new PluginSlot
            {
                Name = "Synth1", Path = synth1, Format = PluginFormats.Vst2, Type = PluginSlotType.Instrument,
            });
            project.Tracks.Add(track);

            using var client = new AudioEngineClient(trust)
            {
                EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe"),
                WarmIdle = TimeSpan.Zero,
            };
            var routing = new RoutedMidiOutput(new NullMidiOutput(), client);
            using var sessions = new AvaloniaPlaybackSessions(new AvaloniaUiThread(Dispatcher.UIThread), () => new PlaybackEngine(routing))
            {
                Engine = client, Trust = trust,
            };
            var song = new SongDocument(() => null) { Project = project };
            var window = new MainWindow(settings, new TabWindowRegistry(new AppServices(trust)), first: song, playback: sessions)
            {
                Width = 1600, Height = 1000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                var owner = song;
                AvaloniaEngineRouting.Apply(project, routing, client, plugins, AvaloniaEngineRouting.FullMasterPercent, owner);
                Assert.True(client.IsRunning, "the engine did not start");
                var firstPid = client.EngineProcessId;
                Assert.NotNull(firstPid);
                WaitUi(() => client.Output is not null && ChainsReady(client), "the engine chain did not get ready");

                var slot = client.SlotOf(track);
                Assert.True(slot >= 0, "the Synth1 track has no engine slot");
                Send(client, slot, 0x90, 60, 100);
                Thread.Sleep(250);
                Send(client, slot, 0x80, 60, 0);
                Thread.Sleep(100);
                Assert.True(client.IsRunning, "the first note-on killed the engine");
                Send(client, slot, 0x90, 64, 100);   // the second note-on faults Synth1 64

                string? notice = null;
                WaitUi(() =>
                {
                    notice = TopBarText(window);
                    return notice is not null && notice.Contains("stopped working", StringComparison.Ordinal);
                }, "the window never showed the crash notice");
                _output.WriteLine("notice: " + notice);

                Assert.Contains("Synth1 VST64 stopped working and was switched off; playback continues", notice);
                Assert.True(window.IsVisible, "the main window must stay open");
                Assert.Equal(Environment.ProcessId, Process.GetCurrentProcess().Id);
                Assert.False(Process.GetCurrentProcess().HasExited, "the UI process must survive");

                WaitUi(() => client.IsRunning && client.Output is not null, "the window did not restart the engine");
                Assert.NotEqual(firstPid, client.EngineProcessId);
                WaitUi(() => plugins.Quarantined.Any(p => string.Equals(p, synth1, StringComparison.OrdinalIgnoreCase)),
                    "the crashed Synth1 was not added to the quarantine");
                Assert.True(window.IsVisible, "the main window must still be open after the restart");
            }
            finally
            {
                window.Close();
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        }
        finally { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } }
    }

    private static string? TopBarText(MainWindow window) =>
        (typeof(MainWindow).GetField("_topBar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as NotificationBar)?.CurrentText;

    private static bool ChainsReady(AudioEngineClient client)
    {
        var readiness = new ChainReadiness(client.RequestedChains);
        foreach (var ack in client.LastAcknowledgements) readiness.Acknowledge(ack);
        return readiness.IsReady;
    }

    private static void Send(AudioEngineClient client, int slot, byte status, byte data1, byte data2) =>
        Assert.True(client.Write(new TabForge.Audio.Contracts.TimedMidi
        {
            Timestamp = Stopwatch.GetTimestamp(), Slot = slot, Status = status, Data1 = data1, Data2 = data2,
        }), "the MIDI ring refused the note");

    private static void WaitUi(Func<bool> ready, string failure)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(clock.Elapsed < Timeout, failure);
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
