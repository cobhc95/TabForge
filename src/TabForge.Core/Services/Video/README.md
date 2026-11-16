# Services/Video

Live video recording and offline video export: the pixel sources (Desktop Duplication with a GDI fallback), the MP4 encoders (Media Foundation, or an external ffmpeg) and the recording state.

## Files
| File | Owns |
| --- | --- |
| `DesktopRegionGrabber.cs` | choosing the pixel source for a recording: Desktop Duplication when it works, otherwise the GDI grabber, switching once for good on the first DXGI failure. |
| `DxgiRegionGrabber.cs` | Desktop Duplication of the output under the region: acquiring a frame, copying only the region's rectangle on the GPU into a staging texture (halved on the GPU first, as mip levels, when the frame is at most half the region's size, so less crosses the bus), mapping it, turning it upright on a rotated (portrait) display and scaling it (bilinear, parallel rows) into the caller's top-down BGRA buffer with the same fit-and-black-bars layout as ScreenRegionGrabber. |
| `FfmpegArguments.cs` | the ffmpeg command lines (raw frames in on stdin, then a stream-copy mux with the spooled audio) and the choice of H.264 encoder from ffmpeg's own encoder list. |
| `FfmpegLocator.cs` | finding an external ffmpeg executable (TABFORGE_FFMPEG, next to the app, then PATH). Nothing is bundled; a missing ffmpeg only switches the FFmpeg encoder off. |
| `FfmpegProbe.cs` | asking an ffmpeg which encoders it has. |
| `FfmpegVideoEncoder.cs` | writing an H.264/AAC MP4 through an external ffmpeg: raw BGRA frames piped to its stdin, audio spooled to a raw file and muxed in at the end; the bounded frame queue, the dropped-frame count, repeating the last frame over gaps so picture and sound stay in step. |
| `IVideoEncoder.cs` | the port every MP4 encoder implements (Media Foundation on Windows, an ffmpeg child process anywhere), the size, rate and bitrate rules they   share, and their shared frame-queue mechanics (VideoEncoderQueue). |
| `LiveVideoFocus.cs` | the pure rules of the record-video focus: whether the main window must be restored and brought forward before a recording   (or when a recording is found black), and the reason text for a black picture. |
| `LiveVideoRecordController.cs` | the state of live video recording (idle or recording), the REC indicator ticks and the hand-over of a finished MP4 to the user. |
| `LiveVideoSession.cs` | one live recording: the encoder (opened when the first audio chunk tells the sample rate), the audio clock built from the master tap's chunks, the capture thread that grabs a frame per tick and stamps it from that clock, and the gap-filling of lost audio. |
| `LiveVideoSettings.cs` | the live video recording preferences and the names of their choices. |
| `ScreenRegionGrabber.cs` | copying a screen rectangle into a top-down BGRA buffer of a fixed size (scaled to fit, black bars for the aspect difference), through IScreenCapture. |
| `VideoDisposeHelper.cs` | the one swallow-finish shared by the video encoders' Dispose (finishing an already-finished encoder throws; Dispose must not). |
| `VideoEncoder.Finalize.cs` | the one end-of-file sequence of the Media Foundation encoder, shared by live recording and offline export (the queue drained, then the sink writer finalised), and its diagnostics: the threads and COM apartments involved, the per-stream sample counts and last timestamps, the sink writer's own statistics and the encoder transform's name and vendor, all in errors.log; pacing an offline hardware encoder so it is never far behind (as live recording feeds it). |
| `VideoEncoder.Writer.cs` | sink-writer setup, media types and pumping. |
| `VideoEncoder.cs` | writing an MP4 (H.264 video, AAC audio) through Media Foundation's sink writer; the bounded frame queue and its fixed pool of native frame buffers (one per queue slot); the dropped-frame count; the bitrate rule. A worker thread owns every COM object, so callers may be on any thread; a caller only copies pixels into a buffer the worker has locked for it. |
| `VideoEncoderFactory.cs` | choosing the MP4 encoder for this OS and setting: Media Foundation (Windows, today's behaviour) or an external ffmpeg; the live-recording availability per OS. |
| `VideoEncoderFinisher.cs` | finishing an encoder for an offline export: Finish runs on a worker thread (never the UI thread), the status says "Finishing video…", and the encoder is given up on only when its heartbeat has not moved for the stall allowance (a hardware stall is marked so the export can redo the video in software). A given-up encoder keeps its unfinished file. A cancel abandons the encoder at once and never waits for it. |
| `VideoEncoderOptions.cs` | the experimental encoder choices (encoder kind, fast export, low latency) and their mapping to codec property values. |
| `VideoExportFlow.Encode.cs` | choosing the export's encoder (live recording's rule: one hardware self-test, Auto uses hardware only after it passed), opening it off the UI thread, waiting for queue room without blocking, redoing the video with the software encoder when a hardware encoder stalls, and the export's working files (the app temp folder, deleted once no encoder holds them). |
| `VideoExportFlow.cs` | the order of the steps, the frame clock, the audio hand-off, the staging files, the hardware-stall fallback and the "video export" log lines. |
| `VideoExportProgress.cs` | video export progress reports (audio share plus frame counts and ETA text). |
| `VideoFrameSlots.cs` | the constant-frame-rate slot maths of a live recording and the "capture cannot keep up" decision. Pure: the clock is passed in. |
| `VideoSettingsRows.cs` | the Video recording rows of the settings catalogue (General page); VideoFeatureSettings (Services/Features/FeatureSettingsRows.cs) hands them to the catalogue. |
| `VideoViewSpec.cs` | the choices only. |

Tests: see the `// Tests:` line at the top of each file.
