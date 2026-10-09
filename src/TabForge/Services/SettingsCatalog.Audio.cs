using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Audio part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> VstOutputRows(AppSettings s)
    {
        var a = s.Appearance!;
        var pl = s.Plugins!;
        return new List<SettingDescriptor>
        {
            Choice(AudioVst, "Audio output", "vst.driver", "Audio driver", v => { if (pl.Driver != v) { pl.Driver = v; pl.Device = ""; } }, () => pl.Driver, AudioDrivers.All,
                "How plug-in audio reaches your speakers. WASAPI (shared) works everywhere; exclusive and ASIO give the lowest latency. ASIO also carries the recording input. DirectSound is the legacy fallback (larger buffer, more latency); WASAPI or ASIO is recommended. Tracks on Windows MIDI are not affected.",
                "audio driver wasapi asio directsound exclusive shared latency output"),
            Choice(AudioVst, "Audio output", "vst.device", "Output device", v => pl.Device = v == DefaultAudioDevice ? "" : v,
                () => pl.Device.Length == 0 ? DefaultAudioDevice : pl.Device, Lazy(() => AudioDeviceChoices(pl)),
                "The device plug-in audio plays on (for ASIO: the ASIO driver; use Configure… for its buffer size and routing). Windows default follows the device chosen in Windows sound settings. The list follows the driver above.",
                "audio output device speakers interface asio driver name headphones"),
            Choice(AudioVst, "Audio output", "vst.asio.out", "ASIO output: first channel", v => pl.AsioOutputChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)),
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputChannel, 0, AsioOutputs(pl).Count - 1)], Lazy(() => AsioOutputs(pl)),
                "The first ASIO output channel (the output pair starts here). Names come from the driver.", "asio output channels range outputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio output", "vst.asio.outlast", "ASIO output: last channel", v => pl.AsioOutputLastChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)),
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputLastChannel, 0, AsioOutputs(pl).Count - 1)], Lazy(() => AsioOutputs(pl)),
                "The last ASIO output channel: the next one after the first for stereo, or the same as the first for one mono output (the mix is summed to it).", "asio output channels range outputs last mono",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.playall", "Play the whole song through the audio engine", v => pl.PlayAllThroughEngine = v, () => pl.PlayAllThroughEngine,
                "Off: tracks without plug-ins play on Windows MIDI, which never uses the audio driver above (so ASIO does not apply to them, and the Windows volume controls them). On: every track is played by TabForge's own General MIDI synth through the chosen driver (ASIO included). Uses a little more memory and CPU. Track volume, pan, mute, solo and the master volume apply to plug-in tracks either way.",
                "play all engine asio whole song general midi synth windows midi driver route"),
            Bool(AudioVst, "Audio output", "vst.autogm", "Auto-switch to GM sound when no VST instrument plays", v => pl.AutoGmSound = v, () => pl.AutoGmSound,
                "On: when a track's VST instrument stops playing it (chain switched off, instrument bypassed or removed) its GM sound is ticked, and unticked again when the instrument plays. A GM sound you untick yourself stays off. Same option as in the FX window.",
                "auto gm general midi sound switch instrument bypass chain off fallback"),
            Bool(AudioVst, "Audio output", "vst.autopitch", "Match VST instrument pitch automatically", v => pl.AutoPitchMatch = v, () => pl.AutoPitchMatch,
                "On (default): when a VST instrument loads or its preset changes, TabForge measures silently which octave it sounds at and transposes it to match the notes (e.g. a bass preset two octaves low gets +24). Each FX window can override this per chain. Drum tracks are never transposed.",
                "auto pitch match octave transpose instrument preset tuning measure"),
            Text(AudioVst, "Startup tracks", "vst.startuptracks", "Startup tracks", v =>
                {
                    var keep = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    pl.StartupTracks.RemoveAll(t => !keep.Contains(t.Name));
                },
                () => string.Join("; ", pl.StartupTracks.Select(t => t.Name)),
                "Chains ticked with 'Add as a track on startup' in the FX window; each is added (not armed) to every song you open or create. Delete a name from the list (separated by ';') to remove it. Added tracks are not saved with the song until you untick the option.",
                "startup track template live guitar amp sim chain add every song remove"),
            Int(AudioVst, "Audio output", "vst.winmidilatency", "Windows MIDI latency (ms)", v => pl.WindowsMidiLatencyMs = v, () => pl.WindowsMidiLatencyMs, 0, 600,
                "Output latency of the Windows MIDI synth (about 200 ms measured on a typical PC). Raise it if plug-in tracks such as drums sound ahead of the others, lower it if they lag. Measure plays one very quiet hit through the Windows synth and reads it back (only when nothing else plays).",
                "windows midi latency delay offset sync early late plugin vst ahead lag microsoft gs synth", unit: "ms"),
            Bool(AudioVst, "Audio output", "vst.followvolume", "Follow the Windows volume", v => pl.FollowWindowsVolume = v, () => pl.FollowWindowsVolume,
                "ASIO bypasses the Windows mixer, so the Windows volume keys and slider do nothing to it. On: TabForge scales its audio by the Windows master volume and mute (a software level; the interface's own knob is not touched). On by default.",
                "asio windows volume master follow keys slider mute interface knob",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.renderlimiter", "Safety limiter on rendered audio", v => s.Render.SafetyLimiter = v, () => s.Render.SafetyLimiter,
                "File > Render: a transparent limiter on the master mix (ceiling -0.3 dBFS, 1.5 ms lookahead, about 80 ms release) catches peaks that would otherwise clip the file. Material already below the ceiling is unchanged. Stems are never limited. On by default.",
                "render export limiter safety clip clipping peak ceiling master wav mp3 loud"),
            Bool(AudioVst, "Audio output", "vst.livelimiter", "Safety limiter on live playback", v => pl.LiveLimiter = v, () => pl.LiveLimiter,
                "The same limiter on the live output of the audio engine, after the master chain and Monitor FX. Off by default: it adds about 1.5 ms of delay and normal playback should not need it.",
                "live playback limiter safety clip clipping peak ceiling output engine loud"),
        };
    }

    private static List<SettingDescriptor> VstInputRows(AppSettings s)
    {
        var a = s.Appearance!;
        var pl = s.Plugins!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            Bool(AudioVst, "Audio input", "vst.asio.inputs", "Enable ASIO inputs", v => pl.AsioInputsEnabled = v, () => pl.AsioInputsEnabled,
                "Use the ASIO driver's inputs for recording and monitoring. Off: no input through ASIO.", "asio enable inputs recording monitor",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio input", "vst.asio.infirst", "ASIO input: first channel", v => pl.AsioInputChannel = Math.Max(0, AsioInputs(pl).IndexOf(v)),
                () => AsioInputs(pl)[Math.Clamp(pl.AsioInputChannel, 0, AsioInputs(pl).Count - 1)], Lazy(() => AsioInputs(pl)),
                "The first ASIO input channel recorded. Pick the same channel for first and last for one mono input (a guitar plugged into input 2, say).", "asio input channels range inputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio input", "vst.asio.inlast", "ASIO input: last channel", v => pl.AsioInputLastChannel = Math.Max(0, AsioInputs(pl).IndexOf(v)),
                () => AsioInputs(pl)[Math.Clamp(pl.AsioInputLastChannel, 0, AsioInputs(pl).Count - 1)], Lazy(() => AsioInputs(pl)),
                "The last ASIO input channel: the same as the first for mono, or the next one for a stereo pair (at most two channels).", "asio input channels range inputs last",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Int(AudioVst, "Audio input", "vst.recordoffset", "Recording offset (ms)", v => pl.RecordingOffsetMs = v, () => pl.RecordingOffsetMs, -1000, 1000,
                "Shifts new recordings on the timeline, on top of the input latency the device reports. If takes sound late against the song, enter a positive value (they move earlier); if early, a negative one. 0 by default.",
                "recording offset latency compensation input late early align takes record manual offset", unit: "ms"),
            Choice(AudioVst, "Audio input", "vst.input", "Recording device", v => pl.InputDevice = v == DefaultAudioDevice ? "" : v,
                () => pl.InputDevice.Length == 0 ? DefaultAudioDevice : pl.InputDevice, Lazy(() => AudioInputChoices(pl)),
                "The device armed tracks record from (WASAPI, DirectSound). With ASIO the recording comes through the ASIO driver instead.",
                "audio input recording device microphone interface capture"),
            Choice(AudioVst, "Audio output", "vst.samplerate", "Sample rate", v => pl.SampleRate = int.Parse(v), () => pl.SampleRate.ToString(),
                AudioDrivers.SampleRates.Select(r => r.ToString()).ToArray(), "Plug-in processing sample rate in Hz.", "sample rate hz 44100 48000 96000"),
            Choice(AudioVst, "Audio output", "vst.buffer", "Buffer size", v => pl.BufferSize = Math.Clamp(int.Parse(v), AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer), () => pl.BufferSize.ToString(),
                AudioDrivers.BufferSizes.Select(r => r.ToString()).ToArray(),
                "Samples per block (type any value from 16 to 8192, or pick one). Smaller is lower latency but uses more CPU; raise it if you hear crackles. ASIO: this size is requested from the driver (kept inside what it allows); the status bar shows the size the driver really uses.", "buffer size latency samples crackle"),
            Button(AudioVst, "Linked audio", "audio.linkedmedia", "Linked audio from network or removable drives",
                "A song can link audio files on a network location or a removable drive. They are read only from folders you approved. Review the approved folders here and revoke any you no longer want.",
                "network removable linked audio approve revoke folders drive usb unc share"),
            Text(AudioVst, "Plug-ins", "vst.folders", "Plug-in folders", v => pl.Folders = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                () => string.Join("; ", pl.Folders),
                "Folders that hold your VST2 (.dll) and VST3 (.vst3) plug-ins, separated by ';'. Use Browse to add one. Only these folders are scanned unless the option below is on.",
                "vst plugin folder directory path browse scan vst2 vst3"),
            Text(AudioVst, "Plug-ins", "vst.commonfolders", "Common plug-in folders (Scan common folders)", v => pl.CommonFolders = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                () => string.Join("; ", pl.CommonFolders.Count > 0 ? pl.CommonFolders : TabForge.Plugins.VstScannerService.DefaultCommonFolders),
                "The usual VST locations scanned by the Add plug-in window's Scan common folders button, separated by ';'. %ENV% variables work; missing folders are skipped. Clear the box to go back to the built-in list (the Add plug-in window's Edit list button also has Reset to defaults).",
                "vst plugin common folders standard locations scan reset defaults"),
            Bool(AudioVst, "Plug-ins", "vst.rememberscan", "Remember the plug-in list", v => pl.RememberScan = v, () => pl.RememberScan,
                "Off (default): the folders are scanned each time you add a plug-in. On: they are scanned once and the list is remembered; use Rescan in the Add plug-in window after installing new plug-ins.",
                "remember scan cache permanent folders once list plugins"),
            Bool(AudioVst, "Plug-ins", "vst.dock", "Show plug-in windows inside the FX chain window", v => pl.DockPluginWindows = v, () => pl.DockPluginWindows,
                "On (default): the selected plug-in's own controls appear in the FX chain window. Double-click a plug-in to float its window. Off: plug-in windows always float.",
                "dock plugin window embedded ui float fx chain"),
            Bool(AudioVst, "Plug-ins", "vst.ontop", "Keep floating plug-in windows on top", v => pl.PluginWindowsOnTop = v, () => pl.PluginWindowsOnTop,
                "Floating plug-in windows stay above other windows.", "plugin window always on top float"),
            Bool(AudioVst, "Plug-ins", "vst.scanstandard", "Also scan the standard VST folders", v => pl.ScanStandardFolders = v, () => pl.ScanStandardFolders,
                "Off (default): only the folders you added are scanned. On: the usual Common Files VST3 and VstPlugins folders too.", "scan standard default folders common files vst3"),
            Button(AudioVst, "Plug-ins", "vst.quarantine", "Plug-ins switched off after a crash",
                "A plug-in that crashed is switched off so it cannot crash the next playback too. Review the list here and allow a plug-in again; it is then loaded on the next playback (the same button is in its FX chain window). Allowing again does not approve an untrusted file.",
                "quarantine crashed crash switched off plugin allow again blocked faulted"),
            Bool(AudioVst, "Safety", "vst.isolate", "Run each plug-in in its own process", v => pl.SeparateProcessPerPlugin = v, () => pl.SeparateProcessPerPlugin,
                "Off (default): all plug-ins share one audio engine process, separate from TabForge, so a crash never closes TabForge. On: each plug-in gets its own process, so a crash stops only that plug-in (uses more CPU and memory). This protects TabForge from a plug-in crash, not your files from the plug-in: a plug-in runs with your Windows permissions and can read and write your files like any program, so only use plug-ins from sources you trust.",
                "isolate sandbox crash process bridge separate safe plugin"),
        };
    }

    private static List<SettingDescriptor> AudioRows(AppSettings s)
    {
        var a = s.Appearance!;
        var au = s.Audio!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            Bool(Audio, "Transport", "audio.metronome", "Metronome", v => au.Metronome = v, () => au.Metronome,
                "Play clicks during playback.", "click tempo count"),
            Bool(Audio, "Transport", "audio.countin", "Count-in before playback", v => au.CountIn = v, () => au.CountIn,
                "Play a count-in before the score starts.", "count in lead"),
            Int(Audio, "Transport", "audio.countinbars", "Count-in bars", v => au.CountInBars = v, () => au.CountInBars, 1, 4,
                "Number of count-in bars.", "count in bars length", "bars", dependsOn: "audio.countin"),
            Number(Audio, "Transport", "audio.speed", "Playback speed", v => au.Speed = v, () => au.Speed, 0.25, 2,
                "Practice at a fraction of the written tempo.", "speed trainer slow down tempo", "x", 0.05, 2),
            Bool(Audio, "Note preview", "audio.preview", "Preview notes", v => au.PreviewNotes = v, () => au.PreviewNotes,
                "Audition notes when clicking or entering them.", "preview audition click"),
            Int(Audio, "Note preview", "audio.previewlen", "Preview length", v => au.PreviewLengthMs = v, () => au.PreviewLengthMs, 60, 1200,
                "Duration of a previewed note.", "preview duration ms", "ms", dependsOn: "audio.preview"),
            Int(Audio, "Note preview", "audio.letring", "Let-ring tail limit", v => au.LetRingCapMs = v, () => au.LetRingCapMs, 400, 6000,
                "Maximum sustain for let-ring notes.", "let ring sustain drone cap", "ms"),
            Int(Audio, "Metronome", "audio.metroclick", "Metronome click note", v => au.MetronomeClick = v, () => au.MetronomeClick, 0, 127,
                "General MIDI percussion note used for regular clicks.", "gm percussion midi", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccent", "Metronome accent note", v => au.MetronomeAccent = v, () => au.MetronomeAccent, 0, 127,
                "General MIDI percussion note used for the first-beat accent.", "gm percussion downbeat", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metrovolume", "Metronome volume", v => au.MetronomeVolume = v, () => au.MetronomeVolume, 0, 100,
                "Master metronome level.", "click master percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccentvolume", "First-beat volume", v => au.MetronomeAccentVolume = v, () => au.MetronomeAccentVolume, 0, 100,
                "Level of the accented first beat.", "downbeat accent percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroclickvolume", "Regular-click volume", v => au.MetronomeClickVolume = v, () => au.MetronomeClickVolume, 0, 100,
                "Level of regular metronome beats.", "regular click percent", "%", dependsOn: "audio.metronome"),
            Choice(Audio, "Metronome", "audio.metrodivision", "Metronome subdivisions", v => au.MetronomeSubdivision = int.Parse(v), () => au.MetronomeSubdivision.ToString(),
                new[] { "1", "2", "3", "4" }, "Clicks per beat: quarter, eighth, triplet or sixteenth.", "beat subdivision triplet", dependsOn: "audio.metronome"),

            Choice(Timeline, "Track controls", "timeline.volumestyle", "Track volume control", v => au.VolumeKnobs = v == "Knob", () => au.VolumeKnobs ? "Knob" : "Slider",
                new[] { "Slider", "Knob" }, "Slider with the value on its handle, or a rotary knob.", "volume slider knob circle mixer"),
            Choice(Timeline, "Track controls", "timeline.panstyle", "Track pan control", v => au.PanKnobs = v == "Knob", () => au.PanKnobs ? "Knob" : "Slider",
                new[] { "Slider", "Knob" }, "Slider with the value on its handle, or a rotary knob.", "pan slider knob circle mixer balance"),

            Bool(Timeline, "Track controls", "timeline.autofit", "Auto-resize track list to fit", v => timeline.AutoFitTrackList = v, () => timeline.AutoFitTrackList,
                "Grow or shrink the track list / arrangement panel so every track and group row fits, when tracks are added or removed and when groups are shown, hidden, collapsed or expanded.", "track list height fit resize groups collapse auto"),

        };
    }
}
