# Debugging by symptom

Start here when you know what went wrong for the user but not where it lives. Each row gives the first file or folder to read and the first test, command or log to run. Command syntax and the full command list are in `docs/DEBUGGING.md`. Use `--profile <scratch folder>` for every run, and `--only <TestName>` in a full-suite build (see `docs/feature-map/tests.md` for the names).

## Symptom table

| Symptom | First file or folder | First test, command or log |
| --- | --- | --- |
| Playback timing wrong (notes late or early) | `src/TabForge/Playback/PlaybackEngine.cs`, `src/TabForge/Playback/README.md` | `--audit <song> <out>` (exit 1 when notes are late); `--only TestPlaybackScheduleReuse` |
| Playback jump loses an instrument, volume or pan | `src/TabForge/Playback/RestoreTrace.cs`, `src/TabForge/Playback/PlaybackEngine.Resume.cs` | `TABFORGE_TRACE=playback`, then the `restore@` lines in `trace-playback.log` |
| No sound at all | `src/TabForge/Audio/AudioEngineClient.cs`, `src/TabForge.AudioEngine/Output/README.md` | Engine log (below); `--probe-gm <report>` (headless synth check, no device); `--only TestEngineLivenessAndSlowLoad` |
| Crackle, hiss or dropouts | `src/TabForge.AudioEngine/Mixing/README.md` (callback, master tap, limiter) | `--only TestLiveAudioFidelity,TestLiveAudioNoHiss,TestLiveAudioContinuity` (the Null driver feeds the mix the drivers' byte[]-as-float[] buffer: copy from it with spans, never `Array.Copy`); engine log; `errors.log` |
| MIDI recording wrong or missing | `src/TabForge/Audio/MidiInputCapture.cs` | `--probe-record <report>`; `--only TestRecordingPipeline` |
| Video recording or export wrong (black, out of sync) | `src/TabForge/Services/Video/README.md`, `src/TabForge/Services/Video/LiveVideoRecordController.cs`, `src/TabForge/Views/Video/README.md` | `--only TestLiveVideoRecord,TestVideoAudioPitch`; `errors.log` |
| Score engraving wrong (symbols, spacing, collisions) | `src/TabForge/Views/Score/README.md`, `src/TabForge/Views/Score/StaffNotationLayoutBuilder.cs` | `--layout-audit <song> <report>`; `--render-bars <song> <outdir>`; `--only TestLayoutAuditTechniqueSong` |
| Settings not saved | `src/TabForge/Services/AppSettingsStore.cs`, `src/TabForge/Services/SettingsFileService.cs`, `src/TabForge/Services/SettingsValidator.cs` | the settings file in the profile folder (below); `--only TestSettingsFileSplitSnapshots,TestSettingsStoreSharedAcrossWindows`; `errors.log` |
| Setting saved but nothing changes | `src/TabForge/Views/Preferences/README.md`, the consumer named in `docs/feature-map/settings-and-preferences.md` | `--only TestEverySettingIsWired`; `--probe-settings <report>` |
| Crash on start | `src/TabForge/App.xaml.cs` (`LogCrash`) | `crash-<yyyyMMdd-HHmmss>.log` in the diagnostics folder; `--only TestEssentialStartupFiles`; black window: add `--software-render` |
| Work lost after a crash | `src/TabForge/Services/AutosaveService.cs`, `src/TabForge/Documents/DocumentSaveFlow.cs` | The `Recovery` folder (below); `--only TestEssentialAutosaveAndRecovery`; `errors.log` |
| Blurry or soft moving content (playhead, scrolling, a dragged row) | `src/TabForge/Views/Rendering/PixelSnap.cs`, `src/TabForge/Views/PlayheadOverlay.cs` | `--only TestMotionSharpnessScore,TestMotionSharpnessBand,TestMotionSharpnessPlayhead,TestMotionSharpnessScoreCaret,TestMotionSharpnessTimeline,TestMotionSharpnessRowDrag` (their MOTION lines in the log give sharpness at 100% to 175%) |
| Window or dock layout wrong | `src/TabForge/Docking/README.md`, `src/TabForge/Views/DockLayoutController.cs`, `src/TabForge/Shell/README.md` | `--screenshots <folder>`; `--size WxH`; `--only TestDockDefaultsAndFretboardPosition,TestDockRatioNotRewrittenByAutoFit` |
| Import fails or a file opens damaged | `src/TabForge/Services/ImportWorker.cs`, `src/TabForge/Services/ImportPlausibility.cs` | `--plausibility <file> <out.txt>` (exit 1 when it would warn); `--import-measure <song> <report>`; `TABFORGE_TRACE=import`; `--only TestImportPlausibility` |
| Plug-in missing, untrusted or crashing | `src/TabForge/Plugins/PluginTrust.cs`, `src/TabForge/Plugins/README.md`, `src/TabForge.AudioEngine/Isolation/README.md` | Engine log (plug-in host lines); `--plugin-info <path> <sha256>`; `--only TestPluginRightsNotice,TestPluginStateCollection` |
| Engine stops answering or restarts | `src/TabForge/Audio/AudioEngineClient.cs`, `src/TabForge/Controllers/EngineSyncController.cs` | Engine log; `--only TestEngineLivenessAndSlowLoad`; `errors.log` |

## Where the logs are

| Log | Location | Written by | Switch |
| --- | --- | --- | --- |
| `errors.log` | the diagnostics folder, `%LOCALAPPDATA%\TabForge\Diagnostics\`; 256 KB, then `errors.1.log` | `src/TabForge/Services/Log/ErrorLog.cs`, through `Trace.Error` | always on; identical lines within 1 s are counted, not repeated |
| `trace-<area>.log` | the diagnostics folder | `src/TabForge/Services/Log/Trace.cs` | `TABFORGE_TRACE=area,area`; areas `playback`, `engine`, `layout`, `import`, `ui`, or `all`; read once at start-up |
| `crash-<time>.log` | the diagnostics folder | `src/TabForge/App.xaml.cs` | always on, written at a crash |
| Engine log `tabforge-audioengine.log` | `%TEMP%\tabforge-audioengine.log` (rotates to `tabforge-audioengine.1.log` at about 1 MB) | `src/TabForge.AudioEngine/EngineThreads.cs` (`EngineLog`), shared by the engine, plug-in hosts and TabForge | always on |
| Settings file | `%APPDATA%\TabForge\settings.json` | `src/TabForge/Services/UserPaths.cs` | none; a saved value is in this file |
| Recovery copies | `%LOCALAPPDATA%\TabForge\Recovery\` | `src/TabForge/Services/AutosaveService.cs` | none |

With `--profile <folder>`, the settings, the diagnostics folder and the recovery folder move under that folder. The engine log stays in `%TEMP%` either way, so check it for every run.

The `TABFORGE_TRACE` areas are opt-in and cost nothing when unset. `errors.log` is the first place to look for a silent failure: every swallowed error in a `catch` that goes through `Trace.Error` is written there. Render and pointer-move paths write nothing to it.

## Choosing the first step

1. Reproduce with the smallest song that shows the symptom, through the headless command in the table when one exists. A command that exits 1 names the problem.
2. Read `errors.log` and the engine log from the same run before touching code.
3. Turn on one `TABFORGE_TRACE` area, not all of them, so the trace stays small.
4. Run the first `--only` test that matches the symptom. If none exists, add one (see `docs/RECIPES.md`) before fixing.
