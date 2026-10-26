using TabForge.Models;

namespace TabForge.Services;

// Owns: the Audio part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> VstOutputRows(AppSettings s)
    {
        var a = s.Appearance!;
        var pl = s.Plugins!;
        return new List<SettingDescriptor>
        {
            Choice(AudioVst, "Audio output", "vst.driver", "Audio driver", v => pl.SelectOutput(v, FirstOutputDevice(v), () => NewOutputBuffer(v, FirstOutputDevice(v))), () => pl.Driver, AudioDrivers.All,
                "How audio reaches your speakers. Exclusive WASAPI and ASIO give the least delay; DirectSound is the slow fallback.",
                "audio driver wasapi asio directsound exclusive shared latency output"),
            Choice(AudioVst, "Audio output", "vst.device", "Output device", v => { if (v != PendingChoices.CheckingText) pl.SelectOutput(pl.Driver, v == DefaultAudioDevice ? "" : v, () => NewOutputBuffer(pl.Driver, v == DefaultAudioDevice ? "" : v)); },
                () => ShownOutputDevice(pl), new PendingChoices(() => AudioDeviceChoices(pl), () => Features.CatalogComposition.Platform.DeviceNamesSettled(pl.Driver)),
                "The device audio plays on. For ASIO: the installed driver to use. Otherwise Windows default follows your Windows sound settings.",
                "audio output device speakers interface asio driver name headphones"),
            Button(AudioVst, "Audio output", "vst.asio.configure", "Configure ASIO driver",
                "Opens the chosen ASIO driver's own control panel. ASIO only.",
                "asio configure control panel driver settings buffer channels", dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio output", "vst.asio.out", "ASIO output: first channel", v => { if (v != PendingChoices.CheckingText) pl.AsioOutputChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)); },
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputChannel, 0, AsioOutputs(pl).Count - 1)], AsioChoices(pl, AsioOutputs),
                "The first ASIO output channel (the output pair starts here). Names come from the driver.", "asio output channels range outputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Choice(AudioVst, "Audio output", "vst.asio.outlast", "ASIO output: last channel", v => { if (v != PendingChoices.CheckingText) pl.AsioOutputLastChannel = Math.Max(0, AsioOutputs(pl).IndexOf(v)); },
                () => AsioOutputs(pl)[Math.Clamp(pl.AsioOutputLastChannel, 0, AsioOutputs(pl).Count - 1)], AsioChoices(pl, AsioOutputs),
                "The last ASIO output channel: the next one after the first for stereo, or the same as the first for one mono output (the mix is summed to it).", "asio output channels range outputs last mono",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.playall", "Play the whole song through the audio engine", v => pl.PlayAllThroughEngine = v, () => pl.PlayAllThroughEngine,
                "Off: tracks without plug-ins play on Windows MIDI, which never uses the audio driver above (so ASIO does not apply to them, and the Windows volume controls them). On: every track is played by TabForge's own General MIDI synth through the chosen driver (ASIO included). Uses a little more memory and CPU. Track volume, pan, mute, solo and the master volume apply to plug-in tracks either way.",
                "play all engine asio whole song general midi synth windows midi driver route"),
            Bool(AudioVst, "Audio output", "vst.autogm", "Switch to the built-in sound when no plug-in instrument plays", v => pl.AutoGmSound = v, () => pl.AutoGmSound,
                "On: when a track's VST instrument stops playing it (chain switched off, instrument bypassed or removed) its built-in (General MIDI) sound is ticked, and unticked again when the instrument plays. A built-in sound you untick yourself stays off. Same option as in the FX window.",
                "auto gm vst general midi sound switch instrument bypass chain off fallback"),
            Bool(AudioVst, "Audio output", "vst.autopitch", "Match plug-in instrument pitch automatically", v => pl.AutoPitchMatch = v, () => pl.AutoPitchMatch,
                "Measures a VST instrument's octave when it loads and transposes it to match the notes. Drums are never transposed.",
                "auto vst pitch match octave transpose instrument preset tuning measure"),
            Text(AudioVst, "Startup tracks", "vst.startuptracks", "Startup tracks", v =>
                {
                    var keep = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    pl.StartupTracks.RemoveAll(t => !keep.Contains(t.Name));
                },
                () => string.Join("; ", pl.StartupTracks.Select(t => t.Name)),
                "Chains ticked with 'Add as a track on startup' in the FX window; each is added (not armed) to every song you open or create. Delete a name from the list (separated by ';') to remove it. Added tracks are not saved with the song until you untick the option.",
                "startup track template live guitar amp sim chain add every song remove"),
            Int(AudioVst, "Audio output", "vst.winmidilatency", "Windows MIDI delay", v => pl.WindowsMidiLatencyMs = v, () => pl.WindowsMidiLatencyMs, 0, 600,
                "Delay of the Windows MIDI sound in ms (about 200). Raise it if plug-in tracks sound ahead, lower it if they lag.",
                "windows midi latency ms milliseconds delay offset sync early late plugin vst ahead lag microsoft gs synth", unit: "ms"),
            Bool(AudioVst, "Audio output", "vst.followvolume", "Follow the Windows volume", v => pl.FollowWindowsVolume = v, () => pl.FollowWindowsVolume,
                "ASIO ignores the Windows volume. On: TabForge scales its audio by the Windows master volume and mute.",
                "asio windows volume master follow keys slider mute interface knob",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio output", "vst.popguard", "Protect speakers from pops when audio settings change", v => pl.ProtectFromPops = v, () => pl.ProtectFromPops,
                "Fades the sound out and back in around device or setting changes.",
                "pop click crackle noise protect speakers fade mute device driver change switch"),
            Bool(AudioVst, "Audio output", "vst.renderlimiter", "Protect rendered files from distortion", v => s.Render.SafetyLimiter = v, () => s.Render.SafetyLimiter,
                "Catches loud peaks in the final mix of a rendered file. Stems are never touched.",
                "render export limiter dbfs lookahead release safety clip clipping peak ceiling master wav mp3 loud"),
            Bool(AudioVst, "Audio output", "vst.livelimiter", "Protect live playback from loud peaks", v => pl.LiveLimiter = v, () => pl.LiveLimiter,
                "The same protection for live playback. Off by default: it adds a tiny delay.",
                "live playback limiter safety clip clipping peak ceiling output engine loud"),
        };
    }

    /// <summary>The buffer size an output never used before starts with: an ASIO driver's own preferred size, else the usual one.</summary>
    private static int NewOutputBuffer(string driver, string device) =>
        Features.CatalogComposition.Platform.PreferredBufferSize(driver, device) is var preferred and >= AudioDrivers.MinBuffer and <= AudioDrivers.MaxBuffer ? preferred : AudioDrivers.DefaultBuffer;

    /// <summary>The rates the current driver and device support (empty = could not be asked, null = still being asked).</summary>
    private static IReadOnlyList<int>? SupportedRates(PluginSettings pl) => Features.CatalogComposition.Platform.SupportedSampleRates(pl.Driver, pl.Device);

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
            Choice(AudioVst, "Audio input", "vst.asio.infirst", "ASIO input: first channel", v => { if (v == PendingChoices.CheckingText) return; var one = pl.AsioInputLastChannel <= pl.AsioInputChannel; pl.AsioInputChannel = Math.Max(0, AsioInputs(pl).IndexOf(v)); pl.AsioInputLastChannel = one ? pl.AsioInputChannel : 63; },
                () => AsioInputs(pl)[Math.Clamp(pl.AsioInputChannel, 0, AsioInputs(pl).Count - 1)], AsioChoices(pl, AsioInputs),
                "The ASIO input channel that tracks see as Input 1. Every channel from it on can be chosen as a track's input.", "asio input channels range inputs first",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio input", "vst.asio.oneinput", "ASIO input: one channel on inputs 1 and 2", v => pl.AsioInputLastChannel = v ? pl.AsioInputChannel : 63, () => pl.AsioInputLastChannel <= pl.AsioInputChannel,
                "On: only the first channel is opened and tracks hear it on Input 1, Input 2 and the pair 1+2 (one guitar plugged into one input). Off (default): every channel from the first on can be chosen.", "asio input mono one channel single doubled both sides",
                dependsOn: "vst.driver", dependsOnValue: "ASIO"),
            Bool(AudioVst, "Audio input", "vst.allinputs", "Use all inputs of the audio device", v => pl.UseAllInputs = v, () => pl.UseAllInputs,
                "On (default): every input of the audio device can be chosen by a track. Off: only two input channels are opened (Input 1, Input 2 and the pair 1+2); turn it off if the device misbehaves with all its inputs open.",
                "all inputs channels multichannel interface two stereo only safe fallback"),
            Button(AudioVst, "Audio input", "vst.inputsshown", "Inputs shown",
                "Choose which of the audio device's inputs the track input pickers and the live input menu list. Every input shows by default; the choice is kept for each device.",
                "inputs shown hide channels recording live input list picker device"),
            Int(AudioVst, "Audio input", "vst.recordoffset", "Recording timing correction", v => pl.RecordingOffsetMs = v, () => pl.RecordingOffsetMs, -1000, 1000,
                "Shifts new recordings along the timeline, in milliseconds (1000 = one second), on top of the delay your audio device reports. If recordings sound late against the song, enter a positive number (they move earlier); if early, a negative one. 0 by default.",
                "recording offset ms milliseconds latency compensation input late early align takes record manual offset", unit: "ms"),
            Choice(AudioVst, "MIDI recording", "vst.midiover", "When recording over a MIDI clip", v => pl.MergeMidiTakes = v == TabForge.Features.Recording.Rules.MidiTakeLanding.MergeChoice,
                () => pl.MergeMidiTakes ? TabForge.Features.Recording.Rules.MidiTakeLanding.MergeChoice : TabForge.Features.Recording.Rules.MidiTakeLanding.NewLaneChoice,
                new[] { TabForge.Features.Recording.Rules.MidiTakeLanding.NewLaneChoice, TabForge.Features.Recording.Rules.MidiTakeLanding.MergeChoice },
                "New lane (default): a MIDI take gets its own lane above the clip. Merge: the notes are added into the MIDI clip underneath, as one undo step.",
                "midi record over clip merge overdub new lane take"),
            Bool(AudioVst, "MIDI recording", "vst.midiquantize", "Quantize MIDI input", v => pl.QuantizeMidiInput = v, () => pl.QuantizeMidiInput,
                "Snaps the start of each recorded MIDI note to the snap grid when the take lands. Off (default): the raw timing is kept.",
                "midi quantize input snap grid record timing triplet"),
            Choice(AudioVst, "Audio input", "vst.input", "Recording device", v => { if (v != PendingChoices.CheckingText) pl.InputDevice = v == DefaultAudioDevice ? "" : v; },
                () => pl.InputDevice.Length == 0 ? DefaultAudioDevice : pl.InputDevice, new PendingChoices(() => AudioInputChoices(pl), () => Features.CatalogComposition.Platform.DeviceNamesSettled("")),
                "The device armed tracks record from (WASAPI, DirectSound). With ASIO the recording comes through the ASIO driver instead.",
                "audio input recording device microphone interface capture"),
            Choice(AudioVst, "Audio output", "vst.buffer", "Buffer size", v => pl.BufferSize = Math.Clamp(int.Parse(v), AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer), () => pl.BufferSize.ToString(),
                AudioDrivers.BufferSizes.Select(r => r.ToString()).ToArray(),
                "Smaller = less delay, larger = fewer crackles (16 to 8192). Remembered per device.", "buffer size latency samples cpu crackle"),
            Choice(AudioVst, "Audio output", "vst.samplerate", "Sample rate", v => { if (v != PendingChoices.CheckingText) pl.SampleRate = OutputRates.Parse(v); }, () => OutputRates.Label(pl.SampleRate),
                new PendingChoices(() => SupportedRates(pl) is { } rates ? OutputRates.Choices(rates, pl.SampleRate) : null,
                    () => Features.CatalogComposition.Platform.SampleRatesSettled(pl.Driver, pl.Device)),   // asked when the list is first shown
                "Rates the device supports; higher costs more CPU. Device default lets the device decide. Remembered per device.", "sample rate khz hz 44100 48000 96000 quality device default"),
            Button(AudioVst, "Linked audio", "audio.linkedmedia", "Linked audio from network or removable drives",
                "A song can link audio files on a network location or a removable drive. They are read only from folders you approved. Review the approved folders here and revoke any you no longer want.",
                "network removable linked audio approve revoke folders drive usb unc share"),
            FolderList(AudioVst, "Plug-ins", "vst.folders", "Plug-in folders", v => pl.Folders = v, () => pl.Folders,
                "Folders that hold your VST2 (.dll) and VST3 (.vst3) plug-ins. Use Add folder… to add one and Remove to take the selected one out. Only these folders are scanned unless the option below is on.",
                "vst plugin folder directory path browse scan vst2 vst3"),
            FolderList(AudioVst, "Plug-ins", "vst.commonfolders", "Common plug-in folders (Scan common folders)", v => pl.CommonFolders = v,
                () => pl.CommonFolders.Count > 0 ? pl.CommonFolders : Features.CatalogComposition.Platform.DefaultVstFolders,
                "The usual VST locations scanned by the Scan common folders button of the Add plug-in window. Windows folder variables such as %APPDATA% work; missing folders are skipped. Use Add folder… and Remove to change the list, or Reset to defaults to go back to the built-in list.",
                "vst plugin common folders standard locations scan reset defaults"),
            Bool(AudioVst, "Plug-ins", "vst.rememberscan", PluginSettings.RememberScanLabel, v => pl.RememberScan = v, () => pl.RememberScan,
                "On (default): the folders are scanned once and the list is remembered; use Rescan in the Add plug-in window (Add… in an FX chain window) after installing new plug-ins. Off: the folders are scanned each time you add a plug-in.",
                "remember scan cache permanent folders once list plugins"),
            Bool(AudioVst, "Plug-ins", "vst.dock", "Show plug-in windows inside the FX chain window", v => pl.DockPluginWindows = v, () => pl.DockPluginWindows,
                "On (default): the selected plug-in's own controls appear in the FX chain window. Double-click a plug-in to float its window. Off: plug-in windows always float.",
                "dock plugin window embedded ui float fx chain"),
            Bool(AudioVst, "Plug-ins", "vst.ontop", "Keep floating plug-in windows on top", v => pl.PluginWindowsOnTop = v, () => pl.PluginWindowsOnTop,
                "Floating plug-in windows stay above other windows.", "plugin window always on top float"),
            Bool(AudioVst, "Plug-ins", "vst.allowinstalled", "Load installed plug-ins without asking", v => pl.AllowInstalledPlugins = v, () => pl.AllowInstalledPlugins,
                "Plug-ins installed under Program Files load without an approval step. Plug-ins anywhere else (your own folders, downloads, network drives) still need a scan or your approval. Turn off to approve every plug-in yourself.",
                "allow approve trust blocked plug-in vst automatically security ask load installed"),
            Bool(AudioVst, "Plug-ins", "vst.scanstandard", "Also scan the standard VST folders", v => pl.ScanStandardFolders = v, () => pl.ScanStandardFolders,
                "Off (default): only the folders you added are scanned. On: the usual Common Files VST3 and VstPlugins folders too.", "scan standard default folders common files vst3"),
            Button(AudioVst, "Plug-ins", "vst.quarantine", "Plug-ins switched off after a crash",
                "A plug-in that crashed is switched off so it cannot crash the next playback too. Review the list here and allow a plug-in again; it is then loaded on the next playback (the same button is in its FX chain window). Allowing again does not approve an untrusted file.",
                "quarantine crashed crash switched off plugin allow again blocked faulted"),
            Bool(AudioVst, "Safety", "vst.isolate", "Run each plug-in separately", v => pl.SeparateProcessPerPlugin = v, () => pl.SeparateProcessPerPlugin,
                "Off (default): all plug-ins run together in a helper program separate from TabForge, so a crash never closes TabForge. On: each plug-in runs on its own, so a crash stops only that plug-in (needs a faster computer and more memory). This protects TabForge from a plug-in crash, not your files from the plug-in: a plug-in runs with your Windows permissions and can read and write your files like any program, so only use plug-ins from sources you trust.",
                "isolate sandbox crash process own bridge separate safe plugin"),
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
                "How long a previewed note sounds, in milliseconds (1000 = one second).", "preview duration ms milliseconds", "ms", dependsOn: "audio.preview"),
            Int(Audio, "Note preview", "audio.letring", "Longest let-ring sustain", v => au.LetRingCapMs = v, () => au.LetRingCapMs, 400, 6000,
                "The longest a let-ring note keeps sounding, in milliseconds (1000 = one second).", "let ring tail limit ms milliseconds sustain drone cap", "ms"),
            Int(Audio, "Metronome", "audio.metroclick", "Metronome click sound", v => au.MetronomeClick = v, () => au.MetronomeClick, 0, 127,
                "Which drum sound the metronome clicks with (a General MIDI drum number from 0 to 127).", "click note gm percussion midi", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccent", "Metronome first-beat sound", v => au.MetronomeAccent = v, () => au.MetronomeAccent, 0, 127,
                "Which drum sound marks the first beat (a General MIDI drum number from 0 to 127).", "accent note gm percussion downbeat", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metrovolume", "Metronome volume", v => au.MetronomeVolume = v, () => au.MetronomeVolume, 0, 100,
                "Master metronome level.", "click master percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroaccentvolume", "First-beat volume", v => au.MetronomeAccentVolume = v, () => au.MetronomeAccentVolume, 0, 100,
                "Level of the accented first beat.", "downbeat accent percent", "%", dependsOn: "audio.metronome"),
            Int(Audio, "Metronome", "audio.metroclickvolume", "Regular-click volume", v => au.MetronomeClickVolume = v, () => au.MetronomeClickVolume, 0, 100,
                "Level of regular metronome beats.", "regular click percent", "%", dependsOn: "audio.metronome"),
            Choice(Audio, "Metronome", "audio.metrodivision", "Metronome subdivisions", v => au.MetronomeSubdivision = int.Parse(v), () => au.MetronomeSubdivision.ToString(),
                new[] { "1", "2", "3", "4" }, "Clicks per beat: 1 = quarter notes, 2 = eighths, 3 = triplets, 4 = sixteenths.", "beat subdivision triplet", dependsOn: "audio.metronome"),

            // Tombstone: "timeline.volumestyle" and "timeline.panstyle" (Slider / Knob) are hidden until knob controls are built;
            // AudioSettings.VolumeKnobs and PanKnobs are kept.

            Bool(Timeline, "Track controls", "timeline.autofit", "Auto-resize track list to fit", v => timeline.AutoFitTrackList = v, () => timeline.AutoFitTrackList,
                "Grow or shrink the track list / arrangement panel so every track and group row fits, when tracks are added or removed and when groups are shown, hidden, collapsed or expanded.", "track list height fit resize groups collapse auto"),

        };
    }
}
