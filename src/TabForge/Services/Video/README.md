# Services/Video

Live video recording of the screen with the audio: the pixel sources (Desktop Duplication with a GDI fallback), the Media Foundation MP4 encoder and the recording state. No WPF. The window side is the thin hooks in `MainWindow.VideoRecord.cs`.

## Files
| File | Purpose |
| --- | --- |
| `DesktopRegionGrabber.cs` | Chooses the pixel source: Desktop Duplication when it works, otherwise GDI, switched for good on the first DXGI failure |
| `DxgiCom.cs` | Raw COM plumbing for Desktop Duplication: DXGI and Direct3D 11 calls and interface ids |
| `DxgiRegionGrabber.cs` | Desktop Duplication of the output under the region: copies only the region's rectangle, halves a large one on the GPU, turns a rotated (portrait) display upright |
| `LiveVideoRecordController.cs` | `IVideoCapture` and `ILiveVideoRecordHost`; the recording state (idle or recording) and what starts and stops it with playback |
| `LiveVideoSession.cs` | One live recording: the encoder (opened when the first audio chunk gives the sample rate) and the audio clock |
| `VideoSettingsRows.cs` | The four "Video recording" Preferences rows (a `SettingsCatalog` partial) |
| `LiveVideoSettings.cs` | The live video preferences and the names of their choices |
| `MediaFoundationInterop.cs` | Hand-written Media Foundation COM declarations for the encoder and the self-test read-back |
| `ScreenRegionGrabber.cs` | `ScreenRect`, `Fit` (where the whole region goes in the frame, for every grabber), and copying a screen rectangle into a top-down BGRA buffer of fixed size with GDI |
| `VideoEncoderOptions.cs` | The experimental encoder choices (Auto, Software or Hardware; fast export; low latency) and the codec properties they ask for; all off by default |
| `VideoEncoder.cs` | MP4 writer (H.264 video, AAC audio) through the Media Foundation sink writer, with a bounded frame queue and a fixed pool of native frame buffers (the caller copies a frame once, straight into a buffer) |
| `VideoFeatureModule.cs` | `IVideoCommandHost` and the module: what Video adds to the settings, hotkey, command and menu tables (merged by `Services/Features/FeatureRegistry.cs`) |
| `VideoViewSpec.cs` | What a video frame shows: `VideoLayout`, the tracks, the theme and the size |

## Pathway
`START_HERE.md`, Find a feature (`docs/feature-map/recording.md`).

## Tests
`docs/feature-map/recording.md`.
