using TabForge.Audio;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presets;

namespace TabForge;

/// <summary>
/// Practice silence skips one track's note-ons at dispatch without touching its mute state, and cuts its channel once when set during playback.
/// Part of <see cref="SelfTest"/>.
/// </summary>
public static partial class SelfTest
{
    private static void TestPracticeSilence()
    {
        var song = new SongProject { Tempo = 240 };
        for (var track = 0; track < 2; track++)
        {
            var t = new TrackModel { Name = $"T{track}", Kind = TrackKind.Guitar, MidiChannel = track, Measures = TemplateFactory.Measures(8) };
            foreach (var measure in t.Measures)
                foreach (var cell in new[] { 0, 4, 8, 12 })
                {
                    measure.Cells[cell].DurationDenominator = 4;
                    measure.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 0, MidiValue = 60 + track });
                }
            song.Tracks.Add(t);
        }
        static ScoreEvent Ev(int status, int d1, int d2) => new() { Status = status, Data1 = d1, Data2 = d2 };
        Check("Practice silence: the silenced track's bend, sustain, volume, expression and program are dropped, other controllers kept",
            PlaybackEngine.IsPracticeSilencedState(Ev(0xE0, 0, 64)) && PlaybackEngine.IsPracticeSilencedState(Ev(0xB0, 64, 127)) && PlaybackEngine.IsPracticeSilencedState(Ev(0xB0, 7, 90))
            && PlaybackEngine.IsPracticeSilencedState(Ev(0xB0, 11, 90)) && PlaybackEngine.IsPracticeSilencedState(Ev(0xC0, 5, 0)) && !PlaybackEngine.IsPracticeSilencedState(Ev(0xB0, 1, 20)));
        var port = new StampingMidiOutput();
        var routed = new RoutedMidiOutput(port, AudioEngineClient.Instance);
        routed.SetRoutes(Enumerable.Repeat(-1, 16).ToArray());
        var engine = new DocumentSession(new PlaybackEngine(routed)) { Project = song }.Playback.Engine;
        (int NoteOns0, int NoteOns1, int Cuts0, int Cuts1) Counts()
        {
            lock (port.Sent)
                return (port.Sent.Count(x => (x.Status & 0xF0) == 0x90 && (x.Status & 0x0F) == 0),
                        port.Sent.Count(x => (x.Status & 0xF0) == 0x90 && (x.Status & 0x0F) == 1),
                        port.Sent.Count(x => (x.Status & 0xF0) == 0xB0 && x.Data1 == 123 && (x.Status & 0x0F) == 0),
                        port.Sent.Count(x => (x.Status & 0xF0) == 0xB0 && x.Data1 == 123 && (x.Status & 0x0F) == 1));
        }
        try
        {
            engine.SetPracticeSilence(1);
            Check("Practice silence: the index reads back and the mute flag is untouched", engine.PracticeSilence == 1 && !song.Tracks[1].Mute && !song.IsDirty);
            engine.Start(song, new PlaybackOptions { RepeatExpansion = true }, _ => { }, () => { });
            Thread.Sleep(900);
            var silenced = Counts();
            Check("Practice silence: the silenced track starts no notes while the others play", silenced.NoteOns1 == 0 && silenced.NoteOns0 >= 2, $"ch0 {silenced.NoteOns0}, ch1 {silenced.NoteOns1}");
            engine.Stop();
            Check("Practice silence survives Stop", engine.PracticeSilence == 1);
            engine.SetPracticeSilence(-1);
            lock (port.Sent) port.Sent.Clear();
            engine.Start(song, new PlaybackOptions { RepeatExpansion = true }, _ => { }, () => { });
            Thread.Sleep(900);
            var cleared = Counts();
            Check("Practice silence cleared: the track plays again", cleared.NoteOns1 >= 2 && cleared.NoteOns0 >= 2, $"ch0 {cleared.NoteOns0}, ch1 {cleared.NoteOns1}");
            lock (port.Sent) port.Sent.Clear();
            engine.SetPracticeSilence(1);
            engine.SetPracticeSilence(1);
            Thread.Sleep(300);
            var afterSet = Counts();
            Check("Practice silence set while running: its channel gets one All Notes Off, the other track none", afterSet.Cuts1 == 1 && afterSet.Cuts0 == 0, $"cuts ch1 {afterSet.Cuts1}, ch0 {afterSet.Cuts0}");
            var onsAtSet = afterSet.NoteOns1;
            Thread.Sleep(600);
            Check("Practice silence set while running: no later note-ons on that track, others continue", Counts().NoteOns1 == onsAtSet && Counts().NoteOns0 > afterSet.NoteOns0);
            engine.Stop();
        }
        finally { engine.Dispose(); }
    }
}
