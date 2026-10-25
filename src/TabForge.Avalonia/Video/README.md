# Avalonia Video (Record video)

The title-bar camera button (and the Sound menu item and Ctrl+Alt+V) starts and stops a live video recording of the window, as the WPF build does. The recorder, the MP4 encoder and the recording state are shared Core code (`Services/Video`, Windows build); this folder is the Avalonia side.

- `MainWindowLiveVideo.cs`: the thin hook the window calls. Owns the `Transport.RecordVideo` command, the REC state event for the title bar, the "Video saved" notice with Open folder, and the error box.
- `AvaloniaLiveVideoHost.cs`: the `ILiveVideoRecordHost` port over the playback sessions, the engine's master tap and the timers.
- `AvaloniaWindowFrameCapture.cs`: the frame source. The UI thread renders the window content into a bitmap at the recording rate and reads it back; the capture thread crops the region and fits it into the frame.
- `LiveVideoRegions.cs`: what each region choice (score, Band view, whole window, score with instrument) means in window pixels.
- `AvaloniaVideoFrameSource.cs`: the frames of File > Export video (the shared Core `VideoExportFlow`): the first chosen track's score page through the Core score host, with its playhead, as BGRA. The Band rows and the instrument are not drawn yet.

The Render and Export video window stays under File > Export video (`MainWindowFileExports`).

Tests: `TabForge.Avalonia.Tests.LiveVideoRecordTests`.
