using System.IO;
using EM = TabForge.AudioEngine.Midi;
using EP = TabForge.AudioEngine.Plugins;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge;

/// <summary>Mixer groups, sound source, plug-in data and plug-in discovery (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>
    /// Writes a minimal valid x64 PE (PE32+) file with one section holding an export directory that names <paramref name="exports"/>
    /// (none: no export directory). It cannot run; it only has the headers and the export table that <see cref="PeExports"/> reads.
    /// </summary>
    private static void WriteSyntheticPe(string path, params string[] exports)
    {
        const int raw = 0x400, rva = 0x1000, total = 0x800;
        var file = new byte[total];
        void U16(int at, int v) { file[at] = (byte)v; file[at + 1] = (byte)(v >> 8); }
        void U32(int at, long v) { for (var i = 0; i < 4; i++) file[at + i] = (byte)(v >> (8 * i)); }
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        U32(0x3C, 0x80);                                   // e_lfanew
        U32(0x80, 0x00004550);                             // "PE\0\0"
        U16(0x84, 0x8664);                                 // machine: x64
        U16(0x86, 1);                                      // one section
        U16(0x94, 240);                                    // optional header size (PE32+)
        const int opt = 0x98;
        U16(opt, 0x20B);                                   // PE32+
        U32(opt + 112, exports.Length == 0 ? 0 : rva);     // data directory 0: export table
        U32(opt + 116, exports.Length == 0 ? 0 : 0x300);
        const int section = opt + 240;
        "TEXT".Select((c, i) => (c, i)).ToList().ForEach(x => file[section + x.i] = (byte)x.c);
        U32(section + 8, 0x400);                           // virtual size
        U32(section + 12, rva);                            // virtual address
        U32(section + 16, 0x400);                          // raw size
        U32(section + 20, raw);                            // raw pointer
        if (exports.Length > 0)
        {
            int At(int r) => raw + (r - rva);
            const int functions = rva + 0x40, names = rva + 0x80, strings = rva + 0x100;
            U32(At(rva) + 24, exports.Length);             // number of names
            U32(At(rva) + 28, functions);
            U32(At(rva) + 32, names);
            var next = strings;
            for (var i = 0; i < exports.Length; i++)
            {
                U32(At(names) + 4 * i, next);
                foreach (var c in exports[i]) file[At(next++)] = (byte)c;
                file[At(next++)] = 0;
            }
        }
        File.WriteAllBytes(path, file);
    }

    private static void TestMixer()
    {
        TrackModel Track(string name, TrackKind kind, int program, int channel = 0) =>
            new() { Name = name, InstrumentName = name, Kind = kind, MidiProgram = program, MidiChannel = channel };
        var song = SingleTrack(1);
        song.Tracks.Clear();
        var lead = Track("Distortion Guitar", TrackKind.Guitar, 30);
        var bass = Track("Electric Bass (finger)", TrackKind.Bass, 33);
        var piano = Track("Acoustic Grand Piano", TrackKind.Keys, 0);
        var organ = Track("Drawbar Organ", TrackKind.Keys, 16);
        var drums = Track("Drums", TrackKind.Drums, 0, 9);
        var trumpet = Track("Trumpet", TrackKind.Keys, 56);
        song.Tracks.AddRange(new[] { lead, bass, piano, organ, drums, trumpet });

        Check("tracks are grouped by instrument family (guitars, basses, drums; everything else together)",
            MixerGroups.GroupOf(song, lead) == MixerGroups.Guitars && MixerGroups.GroupOf(song, bass) == MixerGroups.Basses
            && MixerGroups.GroupOf(song, piano) == MixerGroups.AllButGuitarsAndBass && MixerGroups.GroupOf(song, organ) == MixerGroups.AllButGuitarsAndBass
            && MixerGroups.GroupOf(song, drums) == MixerGroups.Drums);

        song.Mixer.Grouping = MixerGrouping.Compact;
        Check("the older compact grouping is the same as by instrument",
            MixerGroups.GroupOf(song, piano) == MixerGroups.AllButGuitarsAndBass && MixerGroups.GroupOf(song, lead) == MixerGroups.Guitars);
        song.Mixer.Grouping = MixerGrouping.ByInstrument;

        lead.Volume = 100; lead.Pan = 64;
        var guitars = song.Mixer.Edit(MixerGroups.Guitars);
        guitars.Volume = 50; guitars.Pan = -20; guitars.Pitch = 2;
        Check("a group's level, pan and pitch apply to its tracks",
            MixerGroups.Volume(song, lead) == 50 && MixerGroups.Pan(song, lead) == 44 && MixerGroups.Transpose(song, lead) == 2,
            $"{MixerGroups.Volume(song, lead)}/{MixerGroups.Pan(song, lead)}/{MixerGroups.Transpose(song, lead)}");
        Check("other groups are unaffected", MixerGroups.Volume(song, bass) == bass.Volume && MixerGroups.Transpose(song, bass) == 0);

        lead.MixerGroup = MixerGroups.Drums;
        Check("a track moved to another group follows that group", MixerGroups.GroupOf(song, lead) == MixerGroups.Drums && MixerGroups.Volume(song, lead) == 100);
        lead.MixerGroup = null;

        song.Mixer.Edit(MixerGroups.Basses).Solo = true;
        Check("soloing a group silences the other groups", MixerGroups.GroupSilences(song, lead) && !MixerGroups.GroupSilences(song, bass));
        song.Mixer.Edit(MixerGroups.Basses).Solo = false;

        // Playback: the group pitch moves the notes, the group level scales the channel volume.
        var play = SingleTrack(1);
        play.Tracks[0].Kind = TrackKind.Guitar; play.Tracks[0].MidiProgram = 29; play.Tracks[0].Volume = 100;
        play.Tracks[0].Measures[0].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 1, MidiValue = 65 });
        var plain = MidiTimelineBuilder.Build(play, new PlaybackOptions());
        var firstPlain = plain.Events.First(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0).Data1;
        var group = play.Mixer.Edit(MixerGroups.GroupOf(play, play.Tracks[0]));
        group.Pitch = 12; group.Volume = 50;
        var shifted = MidiTimelineBuilder.Build(play, new PlaybackOptions());
        var firstShifted = shifted.Events.First(e => (e.Status & 0xF0) == 0x90 && e.Data2 > 0).Data1;
        var volume = shifted.ChannelSetup.First(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 7).Data2;
        Check("playback follows the group's pitch and level", firstShifted == firstPlain + 12 && volume == 50, $"{firstPlain}->{firstShifted}, CC7 {volume}");

        // Saved data is validated.
        var bad = SingleTrack(1);
        bad.Mixer.Edit("Guitars").Volume = 900;
        Check("out-of-range mixer values are refused when loading", Throws(() => ProjectValidator.Validate(bad)));
        var badSource = SingleTrack(1);
        badSource.Tracks[0].SoundSource = "Theremin";
        Check("an unknown sound source is refused when loading", Throws(() => ProjectValidator.Validate(badSource)));
        var chain = SingleTrack(1);
        chain.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "ReaEQ", Path = @"C:\x\reaeq.dll", Format = "VST2", Wet = 101 });
        Check("an out-of-range wet/dry value is refused when loading", Throws(() => ProjectValidator.Validate(chain)));
        chain.Tracks[0].Rig.Plugins[0].Wet = 80;
        chain.Tracks[0].SoundSource = SoundSources.Plugins;
        Check("a plug-in chain is valid and marks the song as needing TabForge to play it",
            !Throws(() => ProjectValidator.Validate(chain)) && MixerGroups.UsesPlugins(chain) && !MixerGroups.UsesPlugins(SingleTrack(1)));

        // Clean .gp + .tfaudio: nothing TabForge-specific inside the .gp; the audio data comes back on open.
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tf-audio-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            var gp = System.IO.Path.Combine(folder, "song.gp");
            var withAudio = SingleTrack(1);
            withAudio.Tracks[0].Name = "Lead";
            withAudio.Tracks[0].SoundSource = SoundSources.Plugins;
            withAudio.Tracks[0].Rig.Plugins.Add(new PluginSlot { Name = "ReaSynth", Path = @"C:\x\reasynth.dll", Format = "VST2", Type = PluginSlotType.Instrument });
            withAudio.Mixer.Edit(MixerGroups.GroupOf(withAudio, withAudio.Tracks[0])).Volume = 70;
            Check("a song with plug-ins or mixer groups has audio data; a plain song does not",
                MixerGroups.HasAudioData(withAudio) && !MixerGroups.HasAudioData(SingleTrack(1)));
            var doc = Documents.DocumentSession.Blank();
            doc.Project = withAudio;
            new Documents.DocumentController().SaveCleanGuitarProWithAudioData(doc, gp, "");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(gp))
                Check("the clean Guitar Pro file contains nothing TabForge-specific", zip.GetEntry(GuitarProExporter.EmbeddedProjectEntry) is null);
            var reopened = new Documents.DocumentController().Open(gp).Project;
            Check("opening the .gp brings back the plug-in chain and mixer from the .tfaudio file",
                reopened.Tracks[0].SoundSource == SoundSources.Plugins && reopened.Tracks[0].Rig.Plugins.Count == 1
                && MixerGroups.LevelsFor(reopened, reopened.Tracks[0]).Volume == 70);
            System.IO.File.WriteAllText(AudioDataFile.PathFor(gp), """{"Tracks":[{"Index":0,"Name":"Lead","SoundSource":"Nope"}],"Mixer":{}}""");
            var rejected = new Documents.DocumentController().Open(gp).Project;
            Check("an invalid .tfaudio file is ignored, never half-applied", rejected.Tracks[0].SoundSource == SoundSources.Midi);
        }
        finally { try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { } }

        // How a track sounds: the built-in General MIDI synth runs ONLY for chain on + effects + no instrument + MIDI sound on.
        var previousPlayAll = TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine;
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = false;   // these routes describe the engine-off case (the engine is the default)
        var route = SingleTrack(1).Tracks[0];
        Check("a track without a chain plays on Windows MIDI", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.WindowsMidi);
        route.SoundSource = SoundSources.Plugins;
        Check("chain on but empty still plays on Windows MIDI (nothing extra runs)", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.WindowsMidi);
        route.Rig.Plugins.Add(new PluginSlot { Name = "ReaEQ", Path = @"C:\x\reaeq.dll", Type = PluginSlotType.Effect });
        Check("effects without an instrument shape the track's MIDI sound", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.MidiThroughEffects);
        route.Rig.Plugins[0].Enabled = false;
        Check("bypassed effects leave the track on Windows MIDI", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.WindowsMidi);
        route.Rig.Plugins[0].Enabled = true;
        route.MidiSound = false;
        Check("MIDI sound switched off with no instrument silences the track", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.Silent);
        route.Rig.Plugins.Insert(0, new PluginSlot { Name = "Synth", Path = @"C:\x\synth.dll", Type = PluginSlotType.Instrument });
        Check("a VST instrument makes the sound", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.Instrument);
        route.SoundSource = SoundSources.Midi;
        Check("the power switch off returns the track to Windows MIDI", MixerGroups.RouteOf(route, TabForge.Audio.AudioEngineClient.Instance.Mixer) == TrackRoute.WindowsMidi);
        TabForge.Audio.AudioEngineClient.Instance.Mixer.PlayAllThroughEngine = previousPlayAll;

        Check("every open song routes plug-in tracks to the audio engine (not plain Windows MIDI)",
            new Documents.DocumentSession().Playback.Routing is not null);

        // Plug-in discovery reads DLL exports without loading them.
        // A TabForge-generated x64 PE file with an export table stands in for a third-party plug-in (no plug-in needs installing).
        var peFolder = Path.Combine(Path.GetTempPath(), "tabforge-pe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(peFolder);
        try
        {
            var vst2 = Path.Combine(peFolder, "synthetic-vst2.dll"); WriteSyntheticPe(vst2, "VSTPluginMain", "other");
            var vst2Main = Path.Combine(peFolder, "synthetic-main.dll"); WriteSyntheticPe(vst2Main, "main");
            var noPlugin = Path.Combine(peFolder, "synthetic-plain.dll"); WriteSyntheticPe(noPlugin, "Initialize", "Shutdown");
            var noExports = Path.Combine(peFolder, "synthetic-none.dll"); WriteSyntheticPe(noExports);
            Check("a VST2 plug-in is recognised from its export table (VSTPluginMain)", PeExports.IsVst2Plugin(vst2));
            Check("a VST2 plug-in is recognised from its export table (main)", PeExports.IsVst2Plugin(vst2Main));
            Check("the export names of a generated file are read back", PeExports.ExportNames(vst2).SetEquals(new[] { "VSTPluginMain", "other" }));
            Check("a DLL with other exports is not taken for a plug-in", !PeExports.IsVst2Plugin(noPlugin));
            Check("a DLL without an export table is not taken for a plug-in", !PeExports.IsVst2Plugin(noExports));
        }
        finally { try { Directory.Delete(peFolder, true); } catch (IOException) { } }
        var system = System.IO.Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        Check("an ordinary DLL is not taken for a plug-in", !PeExports.IsVst2Plugin(system));
        var settings = new PluginSettings();
        Check("plug-in folders: only the user's own folders are scanned by default",
            !VstScannerService.RootsFor(settings).Any() && VstScannerService.RootsFor(new PluginSettings { ScanStandardFolders = true }).Any());
        // The Add plug-in window's empty-state button runs this same method: the default stays off, one click turns the standard folders on.
        VstScannerService.EnableStandardFolders(settings);
        Check("plug-in folders: the empty-state button's action turns the standard folders on (scan roots appear)",
            settings.ScanStandardFolders && VstScannerService.RootsFor(settings).Any());
        Check("plug-in folders: the button tooltip lists the folders by environment name, never a user path",
            VstScannerService.StandardFolderLabels.Length > 0 && VstScannerService.StandardFolderLabels.All(l => l.StartsWith('%') && !l.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase)));
        Check("mixer and FX chain can be bound to keys",
            HotkeyCatalog.All.Any(a => a.Id == "View.Mixer") && HotkeyCatalog.All.Any(a => a.Id == "Track.FxChain"));
        Check("wiring and MIDI processing can be bound to keys",
            HotkeyCatalog.All.Any(a => a.Id == "Track.Wiring") && HotkeyCatalog.All.Any(a => a.Id == "Track.MidiProcessing"));
        var midiIn = new PluginSlot().MidiIn;
        Check("a plug-in's MIDI input defaults to the track's own MIDI on all channels (old files load unchanged)",
            midiIn.Source == PluginMidiIn.Own && midiIn.Channel == 0 && System.Text.Json.JsonSerializer.Deserialize<PluginSlot>("{\"Name\":\"x\"}")!.MidiIn.Source == PluginMidiIn.Own);
    }

    // ---------- MIDI processors (events in, events out) ----------
    private static EP.BlockMidi Ev(int frame, int status, int d1, int d2) => new() { Frame = frame, Status = (byte)status, Data1 = (byte)d1, Data2 = (byte)d2 };

    private static EM.MidiProcessorChain? Chain(params (string Type, string Json)[] specs) =>
        EM.MidiProcessorChain.Create(specs.Select(s => new Audio.Contracts.MidiProcSpec(s.Type, true, s.Json)).ToList(), 48000);

    /// <summary>Runs one block through a chain; returns what came out as "frame:status:d1:d2" strings.</summary>
    private static List<string> RunBlock(EM.MidiProcessorChain chain, int frames, bool playing, params EP.BlockMidi[] input)
    {
        var buffer = new EM.MidiBuffer();
        chain.Process(input, buffer, frames, new EP.TransportInfo { Tempo = 120, Playing = playing }, null);
        return buffer.Span.ToArray().Select(e => $"{e.Frame}:{e.Status:X2}:{e.Data1}:{e.Data2}").ToList();
    }

    private static void TestMidiProcessors()
    {
        // Note selector ("Applies to"): velocity only for 36/38; others unchanged. Excluded notes keep their own pairing under transpose.
        var sel = Chain(("velocity", "{\"Mode\":1,\"Fixed\":50,\"NoteMode\":1,\"NoteSet\":\"36,38\"}"))!;
        var selOut = RunBlock(sel, 256, true, Ev(0, 0x90, 36, 100), Ev(1, 0x90, 40, 100), Ev(2, 0x90, 38, 90), Ev(3, 0x80, 36, 0), Ev(4, 0x80, 40, 0));
        Check("velocity 'only these notes' changes 36 and 38 and leaves others unchanged",
            selOut.SequenceEqual(new[] { "0:90:36:50", "1:90:40:100", "2:90:38:50", "3:80:36:0", "4:80:40:0" }), string.Join(",", selOut));
        var exc = Chain(("transpose", "{\"Semitones\":2,\"NoteMode\":2,\"NoteSet\":\"36-38\"}"))!;
        var excOn = RunBlock(exc, 256, true, Ev(0, 0x90, 36, 100), Ev(1, 0x90, 60, 100));
        var exc2 = Chain(("transpose", "{\"Semitones\":5,\"NoteMode\":2,\"NoteSet\":\"60\"}"))!;
        exc2.TakeOver(exc, new EM.MidiBuffer());
        var excOff = RunBlock(exc2, 256, true, Ev(5, 0x80, 36, 0), Ev(6, 0x80, 60, 0));
        Check("transpose with an exclusion set keeps note-off pairing (excluded note unchanged, others follow their note-on)",
            excOn.SequenceEqual(new[] { "0:90:36:100", "1:90:62:100" }) && excOff.SequenceEqual(new[] { "5:80:36:0", "6:80:62:0" }), $"{string.Join(",", excOn)} / {string.Join(",", excOff)}");
        var legacy = Chain(("velocity", "{\"Mode\":1,\"Fixed\":50}"))!;
        Check("without a note set every note is processed (old files)", RunBlock(legacy, 256, true, Ev(0, 0x90, 40, 100)).SequenceEqual(new[] { "0:90:40:50" }));
        Check("note set text parses numbers, ranges and names", EM.NoteSetText.Format(Enumerable.Range(0, 128).Where(n => EM.NoteSetText.Parse("36, 38, 40-45, C2, D#3")[n])) == "36,38,40-45,51");

        // Transpose: the note-off follows the pitch its note-on left with, even when the setting changed in between (list edited while held).
        var a = Chain(("transpose", "{\"Semitones\":5}"))!;
        var on = RunBlock(a, 256, true, Ev(0, 0x90, 60, 100));
        var b = Chain(("transpose", "{\"Semitones\":7}"))!;
        b.TakeOver(a, new EM.MidiBuffer());
        var off = RunBlock(b, 256, true, Ev(10, 0x80, 60, 0));
        Check("transpose sends the note-on up and the note-off to the same pitch after an edit",
            on.SequenceEqual(new[] { "0:90:65:100" }) && off.SequenceEqual(new[] { "10:80:65:0" }), $"{string.Join(",", on)} / {string.Join(",", off)}");
        var next = RunBlock(b, 256, true, Ev(0, 0x90, 60, 90));
        Check("the edited transpose applies to the next note", next.SequenceEqual(new[] { "0:90:67:90" }), string.Join(",", next));

        // Note range: a dropped note-on drops its note-off too.
        var range = Chain(("noteRange", "{\"Low\":40,\"High\":80}"))!;
        var dropped = RunBlock(range, 256, true, Ev(0, 0x90, 20, 100), Ev(5, 0x80, 20, 0), Ev(6, 0x90, 60, 100), Ev(9, 0x80, 60, 0));
        Check("note range drops out-of-range notes with their note-offs", dropped.SequenceEqual(new[] { "6:90:60:100", "9:80:60:0" }), string.Join(",", dropped));

        // Delay: events past the block wait in the carry-over ring; the note-off keeps behind its note-on.
        var delay = Chain(("delay", "{\"Ms\":30}"))!;   // 1440 samples at 48 kHz
        var blocks = new List<List<string>>
        {
            RunBlock(delay, 512, true, Ev(100, 0x90, 64, 100)),
            RunBlock(delay, 512, true, Ev(0, 0x80, 64, 0)),
            RunBlock(delay, 512, true), RunBlock(delay, 512, true), RunBlock(delay, 512, true),
        };
        Check("delay holds events across blocks and keeps the note-on before its note-off",
            blocks[0].Count == 0 && blocks[1].Count == 0 && blocks[2].Count == 0
            && blocks[3].SequenceEqual(new[] { "4:90:64:100", "416:80:64:0" }) && blocks[4].Count == 0,
            string.Join(" | ", blocks.Select(x => string.Join(",", x))));
        var notesOnly = Chain(("delay", "{\"Ms\":30,\"NotesOnly\":true}"))!;
        var mixed = RunBlock(notesOnly, 512, true, Ev(0, 0xB0, 1, 50), Ev(1, 0x90, 60, 100));
        Check("delay 'notes only' lets controllers through at once", mixed.SequenceEqual(new[] { "0:B0:1:50" }), string.Join(",", mixed));

        // Transport stop releases every note the chain let through; so does a change of the chain's shape.
        var held = Chain(("transpose", "{\"Semitones\":2}"))!;
        RunBlock(held, 256, true, Ev(0, 0x90, 60, 100), Ev(1, 0x91, 40, 100));
        var stopped = RunBlock(held, 256, false);
        Check("stopping the transport releases sounding notes (no stuck notes)",
            stopped.OrderBy(x => x).SequenceEqual(new[] { "0:80:62:0", "0:81:42:0" }), string.Join(",", stopped));
        var before = Chain(("transpose", "{\"Semitones\":2}"))!;
        RunBlock(before, 256, true, Ev(0, 0x90, 60, 100));
        var after = Chain(("velocity", "{}"))!;
        var released = new EM.MidiBuffer();
        after.TakeOver(before, released);
        Check("a chain of a different shape releases the old one's notes", released.Count == 1 && released.Items[0].Status == 0x80 && released.Items[0].Data1 == 62);

        // Channel map: note-off follows the channel of its note-on; disabled rows drop notes.
        var map = Chain(("channelMap", "{\"Ch1\":3,\"Ch2\":0}"))!;
        var mapped = RunBlock(map, 256, true, Ev(0, 0x90, 60, 100), Ev(1, 0x91, 61, 100), Ev(2, 0x80, 60, 0));
        Check("channel map moves notes to the mapped channel, blocks off rows, and pairs the note-off", mapped.SequenceEqual(new[] { "0:92:60:100", "2:82:60:0" }), string.Join(",", mapped));

        // Velocity: never 0 for a note-on; fixed mode; compressor.
        var quiet = RunBlock(Chain(("velocity", "{\"Mul\":0,\"Add\":0,\"Min\":0}"))!, 256, true, Ev(0, 0x90, 60, 100));
        var fixedVel = RunBlock(Chain(("velocity", "{\"Mode\":1,\"Fixed\":90}"))!, 256, true, Ev(0, 0x90, 60, 20));
        var compressed = RunBlock(Chain(("velocity", "{\"Mode\":2,\"Threshold\":80,\"Ratio\":2}"))!, 256, true, Ev(0, 0x90, 60, 120));
        Check("velocity keeps note-ons at 1 or more, fixes and compresses",
            quiet.SequenceEqual(new[] { "0:90:60:1" }) && fixedVel.SequenceEqual(new[] { "0:90:60:90" }) && compressed.SequenceEqual(new[] { "0:90:60:100" }),
            $"{string.Join(",", quiet)} / {string.Join(",", fixedVel)} / {string.Join(",", compressed)}");

        // CC mapper, note map table, program / bank on load.
        var cc = RunBlock(Chain(("ccMapper", "{\"Source\":1,\"Target\":11,\"ClampLow\":10,\"ClampHigh\":100}"))!, 256, true, Ev(0, 0xB0, 1, 127));
        Check("CC mapper retargets and clamps the value", cc.SequenceEqual(new[] { "0:B0:11:100" }), string.Join(",", cc));
        var table = string.Join(",", Enumerable.Range(0, 128).Select(i => i == 36 ? 38 : i));
        var noteMap = RunBlock(Chain(("noteMap", $"{{\"Map\":[{table}]}}"))!, 256, true, Ev(0, 0x99, 36, 100), Ev(4, 0x89, 36, 0));
        Check("note map converts the pitch and pairs the note-off", noteMap.SequenceEqual(new[] { "0:99:38:100", "4:89:38:0" }), string.Join(",", noteMap));
        var program = RunBlock(Chain(("programBank", "{\"Channel\":2,\"Msb\":1,\"Lsb\":2,\"Program\":5}"))!, 256, false);
        Check("program / bank select sends bank and program at the first block", program.SequenceEqual(new[] { "0:B1:0:1", "0:B1:32:2", "0:C1:5:0" }), string.Join(",", program));

        // Catalog: every entry has an engine processor and a search index; JSON protocol and model round-trip.
        var allBuild = EM.MidiProcessorChain.Create(MidiProcessorCatalog.All.Select(i => new Audio.Contracts.MidiProcSpec(i.Type, true, MidiProcessorCatalog.EngineParams(MidiProcessorCatalog.Create(i)))).ToList(), 48000);
        Check("every catalog processor builds in the engine with its default parameters", allBuild is not null && allBuild.Count == MidiProcessorCatalog.All.Count, $"{allBuild?.Count}/{MidiProcessorCatalog.All.Count}");
        Check("catalog search filters by name, description and keyword",
            MidiProcessorCatalog.Search("drum").Any(p => p.Type == "noteMap") && MidiProcessorCatalog.Search("humanise").Any(p => p.Type == "humanize") && !MidiProcessorCatalog.Search("zzzz").Any());
        var wire = new MemoryStream();
        using (var w = new BinaryWriter(wire, System.Text.Encoding.UTF8, leaveOpen: true)) Audio.Contracts.Frames.Write(w, new List<Audio.Contracts.MidiProcSpec> { new("delay", false, "{\"Ms\":5}") });
        wire.Position = 0;
        var back = Audio.Contracts.Frames.ReadMidiProcs(new BinaryReader(wire));
        Check("MIDI processor lists survive the engine protocol", back.Count == 1 && back[0].Type == "delay" && !back[0].Enabled && back[0].ParamsJson == "{\"Ms\":5}");
        var slot = new PluginSlot { Name = "x" };
        slot.MidiProcessors.Add(MidiProcessorCatalog.Create(MidiProcessorCatalog.Find("transpose")!));
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<PluginSlot>(System.Text.Json.JsonSerializer.Serialize(slot))!;
        Check("a plug-in's MIDI processors are saved with the song and default to none",
            reloaded.MidiProcessors.Count == 1 && reloaded.MidiProcessors[0].Type == "transpose"
            && System.Text.Json.JsonSerializer.Deserialize<PluginSlot>("{\"Name\":\"x\"}")!.MidiProcessors.Count == 0);
        var oldMixer = System.Text.Json.JsonSerializer.Deserialize<MixerSettings>("{\"Grouping\":\"Compact\"}")!;
        Check("old songs load with no bus or master effects and a centred master", oldMixer.Buses.Count == 0 && oldMixer.Master.IsDefault && oldMixer.MasterPan == 0 && oldMixer.IsDefault);
        var ta = new TrackModel(); var tb = new TrackModel(); var tc = new TrackModel();
        string Id(TrackModel t) => t.Id.ToString("N");
        ta.Rig.Plugins.Add(new PluginSlot { SidechainTrackId = Id(tb) });   // tb -> ta
        tb.Rig.Plugins.Add(new PluginSlot { MidiOutTrackId = Id(tc) });     // tb -> tc
        var linked = new[] { ta, tb, tc };
        Check("routing links reject cycles and allow chains",
            RoutingLinks.WouldCycle(linked, Id(ta), Id(tb)) && !RoutingLinks.WouldCycle(linked, Id(tc), Id(ta)) && RoutingLinks.WouldCycle(linked, Id(ta), Id(ta)));
        var busSong = new SongProject();
        busSong.Tracks.Add(new TrackModel { Kind = TrackKind.Guitar });
        var guitarBus = busSong.Mixer.Bus(MixerGroups.GroupOf(busSong, busSong.Tracks[0]));
        guitarBus.Rig.Plugins.Add(new PluginSlot { Name = "verb" });
        var busTrack = MixerBuses.Active(busSong).SingleOrDefault();
        MixerBuses.SetOn(guitarBus, false);
        Check("a group bus with plug-ins runs in its fixed engine slot; power off bypasses it",
            busTrack is { IsBus: true, BusSlot: >= MixerBuses.BusBase and < MixerBuses.MasterSlot } && MixerBuses.Active(busSong).Count == 0 && busTrack.SoundSource == SoundSources.Midi);
        Check("drum maps: the acoustic kit map ships under its neutral name, and chains saved with its earlier name still find it",
            DrumMapLibrary.Find("Acoustic kit map (unverified)") is { } kit && DrumMapLibrary.Find("Superior Drummer 3 (unverified)") == kit
            && DrumMapLibrary.All().All(m => !m.Name.Contains("Superior", StringComparison.OrdinalIgnoreCase)));
        var overrideTable = DrumMapLibrary.BuildTable("", "", "36>38, 40>x");
        Check("drum map overrides change or drop notes", overrideTable[36] == 38 && overrideTable[40] == -1 && overrideTable[41] == 41);
        if (DrumMapLibrary.Find("General MIDI") is { } gm) Check("the General MIDI drum map ships and is verified", gm.Verified && gm.Notes[38] == "Acoustic Snare" && DrumMapLibrary.All().Where(m => m.Name.Contains("unverified")).All(m => !m.Verified));
        else Skip("shipped drum maps", "Resources/DrumMaps not next to the test executable");
        CheckMidiGenerators();
        CheckMidiAudioProcessors();
    }

    private static void CheckMidiGenerators()
    {
        static List<string> Ons(List<string> o) => o.Where(s => s.Split(':')[1] == "90").ToList();

        var arp = Chain(("arp", "{\"Rate\":1,\"Length\":0.5,\"Mode\":1}"))!;
        var arpOut = RunBlock(arp, 60000, true, Ev(100, 0x90, 60, 100), Ev(101, 0x90, 64, 100));
        Check("arpeggiator plays held notes on the beat grid (up)", Ons(arpOut).SequenceEqual(new[] { "24000:90:60:100", "48000:90:64:100" }), string.Join(",", arpOut));

        var choke = Chain(("choke", "{}"))!;
        var chokeOut = RunBlock(choke, 256, true, Ev(0, 0x90, 46, 100), Ev(10, 0x90, 42, 100), Ev(20, 0x90, 46, 100), Ev(30, 0x80, 46, 0), Ev(40, 0x80, 42, 0), Ev(50, 0x90, 46, 100));
        Check("choke ends affected notes, blocks them while held, then lets them play again",
            chokeOut.SequenceEqual(new[] { "0:90:46:100", "10:80:46:0", "10:90:42:100", "40:80:42:0", "50:90:46:100" }), string.Join(",", chokeOut));
        var group = Chain(("chokeGroup", "{\"Start\":60,\"Count\":8}"))!;
        var groupOut = RunBlock(group, 256, true, Ev(0, 0x90, 60, 100), Ev(5, 0x80, 60, 0), Ev(10, 0x90, 62, 100));
        Check("choke group ends the previous note and swallows note-offs", groupOut.SequenceEqual(new[] { "0:90:60:100", "10:80:60:0", "10:90:62:100" }), string.Join(",", groupOut));

        var hold = Chain(("noteHold", "{}"))!;
        var holdOut = RunBlock(hold, 256, true, Ev(0, 0x90, 60, 100), Ev(10, 0x90, 64, 100), Ev(20, 0x80, 64, 0));
        var holdStop = RunBlock(hold, 256, false);
        Check("note hold releases the previous note on the next one and the last on stop",
            holdOut.SequenceEqual(new[] { "0:90:60:100", "10:80:60:0", "10:90:64:100" }) && holdStop.SequenceEqual(new[] { "0:80:64:0" }), $"{string.Join(",", holdOut)} / {string.Join(",", holdStop)}");

        var rep = Chain(("repeater", "{\"Size\":0.25}"))!;
        var repOut = RunBlock(rep, 24000, true, Ev(0, 0x90, 60, 100));
        Check("note repeater retriggers a held note every size beats (1 + 3 note-ons per beat at 1/4)", Ons(repOut).Count == 4, string.Join(",", repOut));

        var san = Chain(("sanitizer", "{}"))!;
        var sanOut = RunBlock(san, 256, true, Ev(0, 0x90, 60, 100), Ev(5, 0x90, 60, 100), Ev(10, 0x80, 60, 0), Ev(15, 0x80, 60, 0), Ev(20, 0x80, 60, 0));
        Check("sanitizer drops duplicate note-ons with their extra note-offs", sanOut.SequenceEqual(new[] { "0:90:60:100", "10:80:60:0" }) || sanOut.SequenceEqual(new[] { "0:90:60:100", "15:80:60:0" }), string.Join(",", sanOut));

        var snap = Chain(("scaleSnap", "{\"Root\":0,\"Scale\":1}"))!;
        var snapOut = RunBlock(snap, 256, true, Ev(0, 0x90, 61, 100), Ev(1, 0x90, 64, 100));
        Check("scale snap moves C# to C (ties down) and keeps E", snapOut.SequenceEqual(new[] { "0:90:60:100", "1:90:64:100" }), string.Join(",", snapOut));

        var chord = Chain(("chord", "{\"Mode\":1,\"Root\":0,\"Scale\":1,\"Step2\":2,\"Step3\":4}"))!;
        var chordOut = RunBlock(chord, 256, true, Ev(0, 0x90, 60, 100), Ev(5, 0x80, 60, 0));
        Check("chord in key builds a diatonic triad and releases all voices", chordOut.Count == 6 && chordOut[1].StartsWith("0:90:64") && chordOut[2].StartsWith("0:90:67") && chordOut[5].StartsWith("5:80:67"), string.Join(",", chordOut));

        var lfo = Chain(("lfo", "{\"Cc\":1,\"Shape\":0,\"Freq\":1,\"Sync\":1,\"Updates\":8}"))!;
        var lfoOut = RunBlock(lfo, 48000, true);
        Check("CC LFO emits changing controller values", lfoOut.Count >= 8 && lfoOut.All(s => s.Split(':')[1] == "B0") && lfoOut.Distinct().Count() > 4, lfoOut.Count.ToString());

        var seq = Chain(("stepSeq", "{\"Pattern1\":\"0 . 7 .\",\"Steps\":4,\"StepsPerBeat\":4,\"Gate\":50}"))!;
        var seqOut = RunBlock(seq, 24000, true);
        Check("step sequencer plays its pattern in time (steps of a 16th)", Ons(seqOut).SequenceEqual(new[] { "0:90:60:100", "12000:90:67:100" }), string.Join(",", seqOut));

        var rnd = Chain(("noteRandom", "{\"Trigger\":60,\"Low\":50,\"High\":52}"), ("velVariation", "{\"Base\":90,\"Variation\":0,\"Accent\":\"0\"}"))!;
        var rndOut = RunBlock(rnd, 256, true, Ev(0, 0x90, 60, 100), Ev(5, 0x80, 60, 0));
        var rp = rndOut[0].Split(':');
        Check("note randomizer stays in range, velocity variation sets the base, the note-off follows", int.Parse(rp[2]) is >= 50 and <= 52 && rp[3] == "90" && rndOut[1] == $"5:80:{rp[2]}:0", string.Join(",", rndOut));
    }

    private static void CheckMidiAudioProcessors()
    {
        static List<string> Run(EM.MidiProcessorChain c, float[] l, float[] r, params EP.BlockMidi[] input)
        {
            var b = new EM.MidiBuffer();
            c.Process(input, b, l.Length, new EP.TransportInfo { Tempo = 120, Playing = true }, null, l, r);
            return b.Span.ToArray().Select(e => $"{e.Frame}:{e.Status:X2}:{e.Data1}:{e.Data2}").ToList();
        }

        var drum = Chain(("audioDrum", "{}"))!;
        var l = new float[4800]; var r = new float[4800]; l[100] = 1; r[100] = 1;
        var drumOut = Run(drum, l, r);
        Check("audio drum trigger fires note 38 on channel 10 at an impulse with full velocity, then ends it", drumOut.Count == 2 && drumOut[0] == "100:99:38:127" && drumOut[1].Contains(":89:38"), string.Join(",", drumOut));

        var duck = Chain(("audioDucker", "{\"Mode\":0,\"GainDb\":-20,\"AttackMs\":0,\"ReleaseMs\":0}"))!;
        var dl = Enumerable.Repeat(1f, 480).ToArray(); var dr = Enumerable.Repeat(1f, 480).ToArray();
        Run(duck, dl, dr, Ev(240, 0x90, 36, 100));
        Check("MIDI EQ ducker leaves audio before the note and ducks it after", Math.Abs(dl[100] - 1) < 1e-4 && Math.Abs(dl[400] - 0.1f) < 0.01f, $"{dl[100]} {dl[400]}");

        var samp = Chain(("loopSampler", "{\"MaxSec\":1,\"Loop\":true}"))!;
        var sl = new float[200]; var sr = new float[200]; for (var i = 0; i < 100; i++) { sl[i] = 0.5f; sr[i] = 0.5f; }
        Run(samp, sl, sr, Ev(0, 0x90, 50, 100), Ev(100, 0x80, 50, 0));
        var pl = new float[100]; var pr = new float[100];
        Run(samp, pl, pr, Ev(0, 0x90, 60, 127));
        Check("loop sampler plays back what the record note recorded", Math.Abs(pl[10] - 0.5f) < 1e-3f, pl[10].ToString());

        var loop = Chain(("looper", "{\"Tracks\":2,\"MaxSec\":2,\"Quantize\":false}"))!;
        var ll = new float[24000]; var lr = new float[24000]; Array.Fill(ll, 0.25f); Array.Fill(lr, 0.25f);
        Run(loop, ll, lr, Ev(0, 0x90, 36, 100));
        var stop = new float[24000]; var stopR = new float[24000];
        Run(loop, stop, stopR, Ev(0, 0x90, 36, 100));   // ends the first loop (0.5 s = 24000 frames)
        var play = new float[100]; var playR = new float[100];
        Run(loop, play, playR);
        Check("looper records the first loop, then plays it back on the loop grid", Math.Abs(play[10] - 0.25f) < 1e-3f, play[10].ToString());
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (Exception) { return true; }
    }
}
