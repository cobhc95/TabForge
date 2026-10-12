using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

/// <summary>Metronome and count-in clicks play on the click's own engine slot, never through a song track's route.</summary>
public static partial class SelfTest
{
    private sealed class NoDeviceOutput : IMidiOutput
    {
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }

    private static void TestClickDestination()
    {
        Section("Click destination");
        var client = LiveInputFakeEngine();   // a running engine with no process: writes are captured, nothing is played
        var written = new List<TimedMidi>();
        client.WrittenForTest = m => written.Add(m);
        try
        {
            var settings = new PluginSettings { PlayAllThroughEngine = false };
            var doc = DocumentSession.Blank();
            var project = doc.Project;
            // A drum track that plays through the engine (a plug-in makes its MIDI engine MIDI) and a second track for the solo case.
            project.Tracks.Clear();
            var drums = new TrackModel { Name = "Drums", Kind = TrackKind.Drums };
            drums.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Kit", Path = @"C:\NoSuch\kit.vst3", Format = "VST3" });
            var other = new TrackModel { Name = "Bass" };
            project.Tracks.Add(drums);
            project.Tracks.Add(other);
            var routing = new RoutedMidiOutput(new NoDeviceOutput(), client);

            var cases = new (string Name, Action Setup)[]
            {
                ("muted", () => drums.Mute = true),
                ("another track soloed", () => other.Solo = true),
                ("sound source plug-in only (GM synth off)", () => { drums.SoundSource = SoundSources.Plugins; drums.MidiSound = false; }),
                ("volume 0", () => drums.Volume = 0),
            };
            foreach (var (name, setup) in cases)
            {
                drums.Mute = false; drums.Volume = 100; drums.Solo = false; other.Solo = false;
                drums.SoundSource = SoundSources.Midi; drums.MidiSound = true;
                setup();
                client.Sync(project.Tracks, settings, AudioRouting.Levels(project, 100), project, doc);
                var drumSlot = client.SlotOf(drums);
                var clickSlot = client.SlotOf(client.Click.Track);
                var routes = Enumerable.Repeat(-1, 16).ToArray();
                routes[9] = drumSlot;   // the drum channel routes to the drum track's engine slot, as AudioRouting.Apply sets it
                routing.SetRoutes(routes, true);
                written.Clear();
                routing.SendMetronome(0, 0x99, 36, 100);
                routing.SendMetronome(0, 0x99, 36, 0);
                var onDrums = written.Count(m => m.Slot == drumSlot);
                var onClick = written.Count(m => m.Slot == clickSlot && m.Status == 0x99);
                Check($"click destination ({name}): the click goes to its own slot, never to the drum track's slot",
                    drumSlot >= 0 && clickSlot >= 0 && clickSlot != drumSlot && onClick == 2 && onDrums == 0 && written.Count == 2,
                    $"drum slot {drumSlot}, click slot {clickSlot}, written {written.Count} (click {onClick}, drums {onDrums})");
            }

            Check("click destination: the stand-in is in no song (not in the track list)", !project.Tracks.Contains(client.Click.Track) && project.Tracks.Count == 2);
            Check("click destination: its slot is not a track slot of the song", client.SlotOf(client.Click.Track) is >= 0 and < MixerBuses.BusBase
                && client.SlotOf(client.Click.Track) != client.SlotOf(drums) && client.SlotOf(client.Click.Track) != client.SlotOf(other));
        }
        finally
        {
            client.WrittenForTest = null;
            client.Dispose();
        }
    }
}
