using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;

namespace TabForge;

// Owns: the articulation compile check: palm mute sends its keyswitch before the note, a latched rule only on change, per-note, velocity, replace and the no-map cases.

/// <summary>A track played by an instrument plug-in with an articulation map gets keyswitches ahead of its notes; every other track compiles unchanged.</summary>
public static partial class SelfTest
{
    private static SongProject ArticulationSong(ArticulationMap? map, bool plugin = true, bool gmSound = false, params string[] techniques)
    {
        var p = SingleTrack();
        var track = p.Tracks[0];
        if (plugin)
        {
            track.SoundSource = SoundSources.Plugins;
            track.MidiSound = gmSound;
            track.Rig.Plugins.Add(new PluginSlot { Name = "Guitar", Type = PluginSlotType.Instrument, ArticulationBindings = map?.ToBindings() ?? new() });
        }
        for (var beat = 0; beat < 4; beat++)
            Beat(p, 0, 0, beat, 4, 52, 0, 0).Notes[0].Techniques.UnionWith(techniques.Length > beat && techniques[beat].Length > 0 ? new[] { techniques[beat] } : System.Array.Empty<string>());
        return p;
    }

    private static List<ScoreEvent> ArticulationEvents(ScoreTimeline timeline) => timeline.Events.Where(e => e.IsArticulation).ToList();

    private static void TestArticulationKeyswitches()
    {
        var ample = ArticulationPresets.FindBuiltIn(ArticulationPresets.AmpleName)!.ToMap();
        var timeline = new ScoreToMidiCompiler(ArticulationSong(ample, true, false, "", "PalmMute", "PalmMute", ""), new PlaybackOptions()).Build();
        var keys = ArticulationEvents(timeline).Where(e => e.IsNoteOn).ToList();
        var noteOns = timeline.Events.Where(e => e.IsNoteOn && !e.IsArticulation && !e.IsMetronome).OrderBy(e => e.TimeMs).ToList();
        Check("articulations: latched palm mute sends normal, palm mute, normal (24, 26, 24)", string.Join(",", keys.Select(e => e.Data1)) == "24,26,24", string.Join(",", keys.Select(e => e.Data1)));
        Check("articulations: the palm mute keyswitch is sent 10 ms before the note", keys.Count == 3 && noteOns.Count == 4 && System.Math.Abs(noteOns[1].TimeMs - keys[1].TimeMs - 10) < 0.5, $"keys {string.Join(",", keys.Select(e => e.TimeMs))} notes {string.Join(",", noteOns.Select(e => e.TimeMs))}");
        Check("articulations: the keyswitch is released and flagged, the written notes are unchanged", ArticulationEvents(timeline).Count(e => e.IsNoteOff) == 3 && noteOns.All(e => e.Data1 == 52));
        var first = timeline.Events.FindIndex(e => e.IsArticulation && e.IsNoteOn);
        Check("articulations: the first keyswitch comes before the first note even at time zero", first >= 0 && first < timeline.Events.FindIndex(e => e.IsNoteOn && !e.IsArticulation && !e.IsMetronome));

        var perNote = new ArticulationMap();
        perNote.Rules["PalmMute"] = new ArticulationRule(ArticulationAction.Keyswitch, 26, 0, ArticulationMode.PerNote);
        var perNoteKeys = ArticulationEvents(new ScoreToMidiCompiler(ArticulationSong(perNote, true, false, "", "PalmMute", "PalmMute", ""), new PlaybackOptions()).Build()).Where(e => e.IsNoteOn).ToList();
        Check("articulations: per note sends the keyswitch before every palm-muted note and nothing for plain notes", perNoteKeys.Count == 2 && perNoteKeys.All(e => e.Data1 == 26), string.Join(",", perNoteKeys.Select(e => e.Data1)));

        var other = new ArticulationMap { LeadMs = 30 };
        other.Rules["Dead"] = new ArticulationRule(ArticulationAction.Replace, 40, 0, ArticulationMode.PerNote);
        other.Rules["Ghost"] = new ArticulationRule(ArticulationAction.Velocity, 33, 0, ArticulationMode.PerNote);
        other.Rules["Accent"] = new ArticulationRule(ArticulationAction.Cc, 32, 127, ArticulationMode.Latched);
        var project = ArticulationSong(other, true, false, "Dead", "Ghost", "", "");
        project.Tracks[0].Measures[0].Cells[1].Notes[0].Ghost = true;
        project.Tracks[0].Measures[0].Cells[0].Notes[0].Dead = true;
        project.Tracks[0].Measures[0].Cells[2].Accent = 1;
        var mixed = new ScoreToMidiCompiler(project, new PlaybackOptions()).Build();
        var attacks = mixed.Events.Where(e => e.IsNoteOn && !e.IsArticulation && !e.IsMetronome).OrderBy(e => e.TimeMs).ToList();
        var controllers = ArticulationEvents(mixed).Where(e => (e.Status & 0xF0) == 0xB0).ToList();
        Check("articulations: replace plays the other pitch, velocity sets the level", attacks.Count == 4 && attacks[0].Data1 == 40 && attacks[1].Data2 == 33 && attacks[2].Data1 == 52, string.Join(",", attacks.Select(e => $"{e.Data1}/{e.Data2}")));
        Check("articulations: a controller rule sends number and value ahead of the note by the lead", controllers.Count == 1 && controllers[0].Data1 == 32 && controllers[0].Data2 == 127 && System.Math.Abs(attacks[2].TimeMs - controllers[0].TimeMs - 30) < 0.5);

        var plain = new ScoreToMidiCompiler(ArticulationSong(ample, false, false, "", "PalmMute", "PalmMute", ""), new PlaybackOptions()).Build();
        var withGm = new ScoreToMidiCompiler(ArticulationSong(ample, true, true, "", "PalmMute", "PalmMute", ""), new PlaybackOptions()).Build();
        var noMap = new ScoreToMidiCompiler(ArticulationSong(null, true, false, "", "PalmMute", "PalmMute", ""), new PlaybackOptions()).Build();
        Check("articulations: no plug-in, no map, or a sounding General MIDI voice sends no keyswitch note", ArticulationEvents(plain).Count == 0 && ArticulationEvents(noMap).Count == 0 && !ArticulationEvents(withGm).Any(e => e.IsNoteOn && e.Data1 < 30));

        ArticulationChase(ample);

        var stored = ample.ToBindings();
        var again = ArticulationMap.FromBindings(stored);
        Check("articulations: a map survives its stored form, and C#0 is MIDI 25", again.Rules.Count == ample.Rules.Count && again.LeadMs == 10 && ArticulationTechniques.TryParseNote("C#0", out var sharp) && sharp == 25 && ArticulationTechniques.NoteName(24) == "C0");
    }

    /// <summary>Bar 0 plain, bar 1 palm muted, bar 2 plain: a start or a seek into any bar finds the instrument in that bar articulation.</summary>
    private static void ArticulationChase(ArticulationMap ample)
    {
        var project = SingleTrack(3);
        project.Tracks[0].SoundSource = SoundSources.Plugins;
        project.Tracks[0].MidiSound = false;
        project.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "Guitar", Type = PluginSlotType.Instrument, ArticulationBindings = ample.ToBindings() });
        for (var bar = 0; bar < 3; bar++)
            for (var beat = 0; beat < 4; beat++)
            {
                var note = Beat(project, 0, bar, beat, 4, 52).Notes[0];
                if (bar == 1) note.Techniques.Add("PalmMute");
            }
        string Keys(ScoreTimeline timeline) => string.Join(",", timeline.Events.Where(e => e.IsArticulation && e.IsNoteOn).Select(e => e.Data1));
        Check("articulations: a fresh compile starting inside a palm-muted bar begins with its keyswitch, one starting after it with the normal one",
            Keys(new ScoreToMidiCompiler(project, new PlaybackOptions { StartBar = 1 }).Build()) == "26,24" && Keys(new ScoreToMidiCompiler(project, new PlaybackOptions { StartBar = 2 }).Build()) == "24");

        var channel = ChannelAllocator.Assign(project)[0];
        var output = new ScheduleReuseOutput();
        using var engine = new PlaybackEngine(output);
        engine.Start(project, new PlaybackOptions(), _ => { }, () => { }, startPaused: true);
        var failures = new List<string>();
        foreach (var (bar, expected) in new[] { (1, 26), (2, 24), (1, 26), (0, 24), (2, 24) })
        {
            var before = output.Messages.Length;
            engine.Seek(project, bar, 0);
            WaitForScheduleSeek(engine, bar);
            WaitForScheduleCondition(() => output.Messages.Skip(before).Any(m => m.Status == (0x90 | channel) && m.Note is 24 or 26), 2000);
            var last = output.Messages.Skip(before).LastOrDefault(m => m.Status == (0x90 | channel) && m.Note is 24 or 26);
            if (last.Note != expected) failures.Add($"bar {bar}: keyswitch {(last.Status == 0 ? "none" : last.Note.ToString())}, expected {expected}");
        }
        Check("articulations: a seek chases the keyswitch in force at the new position (palm mute inside bar 1, normal elsewhere)", failures.Count == 0, string.Join("; ", failures));
    }
}
