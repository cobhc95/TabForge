# Video

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `VideoExportFlow` (File > Export > Video, offline); `LiveVideoRecordController` (Record video, live); `VideoEncoder` (the MP4 writer for both)
- **Owner folders:** `src/TabForge/Views/Video/` (the WPF side, with its own README) and `src/TabForge/Services/Video/` (no WPF: pixels, encoder, live session, README). The window hooks outside them are `src/TabForge/MainWindow.VideoRecord.cs` and `src/TabForge/Views/VideoExportWindow.cs`.
- **Central tables:** the settings rows, the two hotkeys, their commands, the File menu entry and the settings bounds come from `src/TabForge/Services/Video/VideoFeatureModule.cs` (merged by `src/TabForge/Services/Features/FeatureRegistry.cs`).
- **Pathway to use:** Export renders the full mix first, then draws frame i at time i / fps off-screen with `VideoFrameSource` and encodes it. Record grabs the chosen screen region on each tick while the engine's master output feeds the audio; the session opens the encoder when the first audio chunk gives the sample rate. Settings are read from the shared store; a new option is added as in "Add a setting" in `docs/RECIPES.md`.
- **Tests:** `--areas recording`; `TestVideoExport`, `TestVideoFrameGolden`, `TestLiveVideoRecord`, `TestVideoEncoderMp4`, `TestVideoEncoder4k60`, `TestVideoAudioPitch`, `TestMasterTapProtocol`. All are full-suite tests (`tests/full-suite/Recording/`).

## Classes

| File | Purpose |
| --- | --- |
| `src/TabForge/Views/Video/VideoExportFlow.cs` | The export steps in order: offline mix, frame clock, hand-off to the encoder, cleanup of temporary and half-written files |
| `src/TabForge/Views/Video/VideoFrameSource.cs` | Draws one frame off-screen (score with page turns, instrument, Band view) in the chosen theme |
| `src/TabForge/Views/Video/VideoExportProgress.cs` | Maps audio and frame progress to one bar from 0 to 100% with the phase and time left |
| `src/TabForge/Views/Video/VideoRecordUi.cs` | The WPF helpers of live recording: the region rectangle, the UI-thread timer, the capture adapter |
| `src/TabForge/Services/Video/VideoViewSpec.cs` | What a frame shows (`VideoLayout`, tracks, theme, size) and the remembered export options (`VideoSettings`) |
| `src/TabForge/Services/Video/VideoEncoder.cs` | MP4 writer (H.264 video, AAC audio) through Media Foundation, on a worker thread with a bounded frame queue |
| `src/TabForge/Services/Video/LiveVideoRecordController.cs` | The recording state, and what starts and stops it with playback (`IVideoCapture`, `ILiveVideoRecordHost`) |
| `src/TabForge/Services/Video/LiveVideoSession.cs` | One live recording: the encoder and the audio clock built from the master output |
| `src/TabForge/Services/Video/LiveVideoSettings.cs` | The live recording preferences and the names of their choices (`LiveVideoChoices`) |
| `src/TabForge/Services/Video/DesktopRegionGrabber.cs` | Picks the pixel source: Desktop Duplication, or GDI after the first DXGI failure |
| `src/TabForge/Services/Video/DxgiRegionGrabber.cs` | Desktop Duplication copy of the region's rectangle |
| `src/TabForge/Services/Video/DxgiCom.cs` | The raw COM calls for Desktop Duplication |
| `src/TabForge/Services/Video/ScreenRegionGrabber.cs` | `ScreenRect` and the GDI copy of a screen rectangle into a top-down buffer |
| `src/TabForge/Services/Video/MediaFoundationInterop.cs` | The Media Foundation COM declarations for the encoder and the self-test read-back |
| `src/TabForge/Views/VideoExportWindow.cs` | The File > Export > Video window: range, layout, tracks, theme, size, frame rate, Cancel |
| `src/TabForge/MainWindow.VideoRecord.cs` | The Record video button and its toggle (a thin partial) |

## Debugging

- A bad export frame or layout: open the export window, or capture it with a `--capture` script (the `videoexport` case in `src/TabForge/Diagnostics/WindowProbes.CaptureShots.cs`).
- Slow UI while recording: `--perf-follow <report>` (`src/TabForge/Diagnostics/WindowProbes.PerfRecording.cs`) prints the render rate with playback alone and with Record video running (4K, 60 fps, Score).
- Skipped frames: the message after a live recording says how many were skipped. Only live mode drops frames; the export waits for room in the queue.
- Swallowed errors go to `errors.log` in the diagnostics folder. Audio-side problems are in `%TEMP%\tabforge-audioengine.log`.
- `TABFORGE_TRACE=ui` adds the slow-funnel lines for the UI.

## Known limits

- Windows only: Desktop Duplication, the GDI fallback and Media Foundation.
- After the first DXGI failure the session uses GDI for the rest of the run.
- Live recording drops frames when the encoder queue is full; 60 and 120 fps at 4K can drop frames, and the saved message reports the count.
- Output is MP4 with H.264 video and AAC audio; no other container is written.
- Sizes are 1080p and 4K only; no custom sizes.
