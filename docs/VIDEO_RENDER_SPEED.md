# Video export speed: where the time goes, and what to try

> **Status 2026-10-22:** the commit `38296bda` and the figures are from the earlier read-only investigation. Status not verified 2026-10-22 (this checkout has no git metadata).

Read-only investigation of File > Export > Video (offline). Branch main at 38296bda. Nothing was built, run or changed.
Figures marked "owner-measured" are the owner's and were not re-measured here. Figures marked "guess" are reasoning, not measurements.

## Short answer

- An export runs in two steps. First the full mix is rendered by the audio engine (a separate process), and the UI waits for it. Then the frames are drawn and encoded. Nothing starts on the frames until the audio file is complete.
- Frames are pipelined: the UI thread draws frame i while the encoder thread writes frame i-1, with up to 6 frames queued. So the frame loop costs about N x max(draw, encode), plus the audio step and the encoder drain.
- Two different ceilings exist. At 1080p the drawing (UI thread) is the limit, since Band, Focus and Score+Band take 21-37 ms per frame (owner-measured) against a budget of 16.7 ms at 60 fps and 8.3 ms at 120 fps. At 4K the encoder is probably the limit (owner-measured live numbers: 35-44 fps, so about 23-28 ms per frame). Speeding up drawing does little at 4K until the encoder gets faster.
- The most obvious waste: the score page is cached only for the score-only layout. `VideoFrameSource.cs:225` skips the cache whenever a live row (instrument or Band) exists, so the whole score page is re-drawn every frame for three of the four layouts.
- Every frame's pixels are copied three times on the way to the encoder, and a fresh native buffer is allocated per frame. These costs do not depend on the layout.

## Per-stage breakdown (from the code)

| # | Stage | Where | What it does | Serial? | Notes |
|---|---|---|---|---|---|
| 1 | Audio render | `VideoExportFlow.cs:73` (`RenderJob.RunAsync`) | Offline mix to a float WAV, in the audio engine process (`AudioEngineClient` starts `--audio-engine`) | Yes, before any frame | Progress code assumes 10% of the total (`VideoExportProgress.AudioShare`). Real share not measured. |
| 2 | Playback mapping, page turn, playhead | `VideoFrameSource.cs:213-226` | Maps time to bar and cell, moves the score playhead, turns the page | UI thread | Cheap. |
| 3 | Score bake (score-only only) | `VideoFrameSource.cs:257-276` | RenderTargetBitmap of the score canvas plus CopyPixels, only when page, bar, cell or a note boundary changes | UI thread | This is the cache that works today. |
| 4 | Band tick and instrument | `:228-234`, `ShowInstrument` `:355-362` | Band view tick, `HideBandControls` walk of the whole Band tree, instrument state rebuilt every frame | UI thread | Walk and rebuild run every frame, not only on change. |
| 5 | Layout | `:109-111`, `:232`, `:235`, `:265` | Up to 3-4 `UpdateLayout` calls per frame over the whole tree | UI thread | Cost not measured. Some calls may be redundant. |
| 6 | Rasterise | `:243-244` `_bitmap.Render(_root)` | Draws the whole tree, including the score page, into a full-frame RenderTargetBitmap for every layout except score-only | UI thread | RenderTargetBitmap is software-rendered in WPF (known WPF behaviour, not measured here). This is the main cost of 21-37 ms. |
| 7 | Readback | `:246` CopyPixels | Full frame copied into `_pixels` (PBGRA32, top-down) | UI thread | Copy 1 of 3 on the way out. |
| 8 | Compose (score-only) | `:281` Array.Copy, `:282-318` playhead blend | Whole baked page copied into `_pixels`, then a managed per-pixel blend over the playhead strip | UI thread | Copy of the full frame every frame, plus a managed loop. |
| 9 | Hand-off | `VideoEncoder.cs:80-82` | Rent from ArrayPool and CopyTo the full frame; block while the queue holds 6 frames (`_room.Wait(50)`) | UI thread (blocks when the queue is full) | Copy 2 of 3. Blocking here is what makes the encoder the limit. |
| 10 | Buffer and sample | `VideoEncoder.cs:222-235` | `MFCreateMemoryBuffer` and `MFCreateSample` per frame, `Marshal.Copy` into the buffer | Worker thread | Copy 3 of 3. A native allocation of the full frame size happens every frame. |
| 11 | Colour conversion | Inside the MF sink writer | RGB32 input to the encoder's NV12 input, usually a CPU converter on the software path | Worker thread | Not visible in our code. Cost not measured. |
| 12 | H.264 encode | `VideoEncoder.cs:146-166` | Microsoft software H.264 MFT, or a hardware MFT if one is registered and `EnableHardware` is set | Worker thread | Settings set: bitrate 0.1 bit per pixel per frame (1080p60 about 12 Mbit/s, 4K60 about 50 Mbit/s, capped at 100), High profile, progressive, `DisableThrottling`. Not set: low latency (default off, good for offline), B-pictures, quality-vs-speed preset, GOP. Encoder defaults apply to those. |
| 13 | Audio into the file | `VideoExportFlow.cs:117-120`, `VideoEncoder.cs:210-215` | Float chunk read from WAV, copied to a pool, converted to 16-bit in a new byte array per chunk | Worker thread | Small: about 1,470 samples per frame at 60 fps. |

Threads in use: one UI thread (the whole frame loop, with `Dispatcher.Yield` every 40 ms) and one encoder thread (video and audio written in turn). The audio engine is a separate process. No parallel frame drawing exists.

Hardware path: `Mf.HardwareH264Available()` (`MediaFoundationInterop.cs:46-53`) decides `EnableHardware`. No D3D device manager is given, so a hardware MFT may still copy through system memory. If a hardware MFT fails after the writer is created, the fallback at `VideoEncoder.cs:125-126` does not run, so the export would fail. This looks possible from the code; not tested.

## Option 2 result (encoder copies)

Done on branch h11-vidcopies. The frame copy into a managed array and the per-frame native buffer are gone: the caller copies once into a locked native buffer from a fixed pool of queue-sized buffers, and the worker recycles each buffer once the sink writer no longer holds it. The output stays byte-identical (`TestVideoEncoderGolden`, digests recorded on the old code).

Median of 3 runs, 120 frames, headless encode at 60 fps (`WriteFrame` on the caller thread / whole encode per frame):

| Size | Before | After |
|---|---|---|
| 1080p | 4.45 / 8.06 ms | 2.9 to 3.2 / 8.4 ms |
| 4K | 22.3 / 26.5 ms | 18.5 / 25.5 to 26.5 ms |

The caller saves about 1.3 ms per frame at 1080p and about 4 ms at 4K. Wall time per frame does not move, because the encoder thread is the limit even at 1080p in this test. The remaining caller time at 4K is back-pressure from the full queue. The third copy from the source (the readback into `_pixels` in `VideoFrameSource`) is untouched.

## Known numbers

- Owner-measured: score-only about 12 ms per frame; Band, Focus and Score+Band 21-37 ms per frame. The resolution behind these numbers is not stated, and it is the first thing to pin down.
- Owner-measured: live 4K recording tops out at 35-44 fps (encoder is the limit); 1080p reaches 60 and 120.
- Export frame budget: 60 fps is 16.7 ms, 120 fps is 8.3 ms.
- Illustration (guess): a 4-minute song at 4K 60 is 14,400 frames. At 25 ms per frame that is about 6 minutes of frame work, plus the audio step.

## Ranked options

Effort: S under about 200 changed lines, M about 200-500, L more. Tokens use the formula from the brief: about 25k fixed plus about 12 tokens per line read or edited, rounded up. Speed-ups are guesses unless the basis is stated. "Identical" means the frames stay byte-identical, which is checkable with the `FrameTap` hook in the export code.

| Rank | Option | Files and classes | Expected speed-up | Changes pixels? | Risk | Effort | Tokens | Agent |
|---|---|---|---|---|---|---|---|---|
| 1 | Cache the score page under live rows and composite the live row separately (owner idea). Lit notes come from the baked page; the playhead strip and live row are drawn each frame. | `VideoFrameSource.cs` (lines 225, 228-246, 281); a new small class (for example `VideoScoreLayer`) so `VideoFrameSource.cs` stays under 500 lines | ScoreAndBand and Focus with instrument: render 1.3-2x faster (guess; the score takes 42% and 62% of the frame). Band-only: no change. 1080p Band from 21-37 ms to about 12-25 ms (guess). | No, if the composite is exact. Verify with a frame hash before and after. | Medium-low. Background under the live row must match `WorkspaceBrush`; clipping must match; playhead strip must still blend after the page is composed. | M | ~35k | Sonnet, medium. Then an Opus visual audit (AGENTS rule for visible output). |
| 2 | Remove two of the three frame copies and the per-frame native allocation. Draw into a ring of frame buffers the encoder owns, bake and compose into the ring slot, and reuse one `IMFMediaBuffer` and `IMFSample` per slot. | `VideoEncoder.cs` (`WriteFrame` 73-84, `Write` 222-235, pool and ring); `VideoFrameSource.cs` (Render, ComposeScoreFrame); `VideoExportFlow.cs` (WriteFrame 111-124, unchanged API if possible) | 4K: guess 5-15 ms per frame saved off the worker and UI thread, which matters most when the encoder is the limit. 1080p: 1-4 ms per frame (guess). | No. | Medium. Ring must be larger than the queue (6) plus one in-flight slot. A slot must not be reused until the encoder has released it. Dispose order matters. The live path shares the encoder, so it must work too. | S-M | ~35k | Sonnet, medium. tf-senior review (threading and lifetime). |
| 3 | Cheap per-frame cleanups: check whether the second `UpdateLayout` (line 232) is needed; run `HideBandControls` only after a Band rebuild, not every frame; reuse the instrument state when the track and settings have not changed. | `VideoFrameSource.cs` (lines 228-236, 355-362) | 1-5 ms per frame (guess). More in Focus and Band layouts. | No. | Low. Layout can change when a button collapses; check identity. | S | ~27k | Haiku, high. Opus visual audit still applies (AGENTS). |
| 4 | Measure first, then set encoder options through `ICodecAPI`: quality-vs-speed to speed, B-pictures off, rate control, possibly CAVLC or Main profile. | `VideoEncoder.cs` (`Begin` 146-166); `MediaFoundationInterop.cs` (new `ICodecAPI` declarations) | Guess: 10-40% of software encoder time. Unknown until measured. | Yes. Output quality and bitstream change. Quality must be checked at 4K. | Medium. COM declarations must be right. An option the encoder rejects must not fail the export; log it and carry on. | S-M | ~35k incl. measuring | Sonnet, medium. |
| 5 | Hardware H.264 with GPU-side input: a DXGI device manager and D3D11 NV12 surfaces through a hardware MFT, which removes the CPU copy and CPU colour conversion. | `VideoEncoder.cs`; `MediaFoundationInterop.cs`; new D3D11 surface class; `DxgiCom.cs` for COM patterns | If a hardware MFT really runs: 4K encoder time 2-4x lower (guess). No effect when drawing is the limit (1080p). | Yes. Different encoder and GPU colour conversion; range and matrix must match. | High. Device lifetime, driver failure mid-export, and the fallback issue above. Render still reads back to the CPU unless drawing moves to the GPU too. | L | ~40-60k | Sonnet, medium, after a design note. tf-senior review. |
| 6 | Run the audio render alongside the first frames. The engine is a separate process, so this is feasible. Frames do not need the WAV, only the audio stream does. | `VideoExportFlow.cs` (lines 73-74, 86-109); `VideoEncoder.cs` (audio ordering) | Saves most of the audio step. Guess 5-15% of wall time. | No. | Medium-high. Either the encoder holds video until audio arrives (memory), or the audio is read from a file still growing. Needs an ordering decision. | M-L | ~40k | Sonnet, medium. |
| 7 | Render frames on N worker threads, each a separate STA dispatcher with its own `TabEditorControl` and project copy. Reorder by frame index before the encoder. | `VideoFrameSource.cs` (`WithTheme` 94-99 mutates the static `VisualTheme.IsLight`; `:166` mutates `_project.BandLayout`); new worker class; `VideoExportFlow.cs` | Guess: draw stage 2-3x on 3-4 cores, but only where drawing is the limit (1080p). Little at 4K. | No, if each frame is deterministic. | High. Shared mutable statics; per-worker memory (33 MB per 4K frame plus visual trees); conflicts with the AGENTS rule against new mutable statics. | L | ~60-80k | Sonnet, medium, at a manager checkpoint. tf-senior review. |
| 8 | Per-row caching or culling inside the Band view. | `BandViewController` and the Band rendering code (not read here) | Unknown. Band-only is the only layout option 1 does not help. | No, if identical. | Medium-high; Band lit state is complex. | L | ~50k | Sonnet, medium, after profiling. |
| 9 | Skip encoding identical frames, or lower the effective frame rate with variable timestamps. | `VideoExportFlow.cs` (WriteFrame); `VideoEncoder.cs` | Low. The playhead moves with the playback fraction (`PlaybackOverlay.cs:148-160`), so consecutive frames are rarely identical. Only paused or stopped stretches repeat. Comparing 33 MB per 4K frame also costs about 3 ms (guess). | Skip-if-identical: no. Lower fps: yes, motion changes. | Low. | S | ~27k | Haiku, high, after measuring the identical-frame ratio. |
| 10 | Move BGRA to NV12 conversion to the GPU or to an own SIMD worker. | `VideoEncoder.cs`; new converter | Guess 2-5 ms per 4K frame, if the sink writer's converter is significant (not measured). | Yes, rounding and matrix. | Medium. Only worth it with option 5. | L | ~45k | Sonnet, medium. |
| 11 | Deeper encoder queue (`QueueFrames` above 6). | `VideoEncoder.cs:25` | Smooths bursts only. Guess 0-5%. Costs 33 MB per queued 4K frame. | No. | Low. | S | ~25k | Haiku, high. |
| 12 | Draw at a smaller internal size, then upscale on the GPU. | `VideoFrameSource.cs`; `VideoViewSpec` | Draw cost falls with pixel count (guess 2-4x). | Yes, visibly softer text. | High for the output's look. | M | ~35k | Not recommended. |

## What to do first

1. Measure (see below). Confirm the 1080p numbers are render-bound and the 4K numbers are encoder-bound.
2. Option 2 and option 3 first: no pixel change, small to medium, and they help every layout.
3. Option 1 next: the biggest gain on live layouts at 1080p, and it needs a frame hash check.
4. Option 4 only with measured evidence that the software encoder is the limit at 4K. Option 5 needs a design note first.
5. Options 7 and 8 only after 1 to 4, and only if the 1080p render is still the limit.

Sanity note on option 9: before building it, measure how many consecutive frames are byte-identical in a real export. If it is low, drop it.

## How to measure each stage (before changing code)

Existing probe: `TABFORGE_VIDEO_PERF=<file>` (former full-suite `SelfTestVideoExport.cs:51`, removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))). It runs the export test for four layouts and writes ms per frame for layout, render, copy and encode, plus the bake count. Build a full-suite copy into `%TEMP%`, then run `--selftest <log> --only TestVideoExport`. Limits: 30 fps only; "encode" includes time spent waiting for queue room, so it is back-pressure, not encoder cost; "render" includes the score compose copy; "copy" is zero for score-only. The resolution used by the test was not read here.

Missing counters (add behind the same environment variable, through the per-export timings object, not a new static):

- Audio render time: Stopwatch around `RenderJob.RunAsync` (`VideoExportFlow.cs:73`).
- UI blocked time: total time in `_room.Wait` (`VideoEncoder.cs:79`). This is the decisive one. If it is large, the encoder is the limit. If it is near zero and the draw time is high, drawing is the limit.
- Worker busy split: time in `WriteSample`, `Marshal.Copy` and `MFCreateMemoryBuffer` (`VideoEncoder.cs:222-235`).
- `UsesHardware` and the encoder's name in the probe output.
- Identical-frame ratio: compare each frame with the previous one (for option 9).
- A per-frame hash dump keyed by frame index, from `FrameTap` (`VideoExportFlow.cs:114`), before and after each change (for the pixel-identity check).

Reading rule: UI blocked time high (the UI waits for queue room) and worker near 100% busy means encoder-bound. UI blocked time near zero, with the UI thread busy most of the time, means draw-bound.

## Not measured, not tried

- The resolution behind the owner-measured numbers; the audio render share; hardware or software encoder on the owner's PC; the cost split inside RenderTargetBitmap; the cost of `UpdateLayout` calls; whether `WriteSample` blocks; how `BandViewController` spends its time.
- No build, no run, no export, no hash comparison.
- The sink writer's internal converter and the H.264 encoder's defaults for B-pictures and speed were not checked against a running encoder; they are from the MF documentation as remembered.
- AGENTS asks for an Opus visual audit of any change that touches visible output. That applies to options 1, 3 and 9 whatever their pixel result, because the export frames are the product.

## Option 1 tried: not adopted

`TestVideoFrameSourceGolden` hashes 24 frames per layout and theme at 640x360 (Band and Score+Band, with and without their instruments). The export's Band view runs with `UseFrameClock`, so it refreshes its instruments and rows on every frame; the live view keeps its wall-clock throttle (33 ms instruments, 250 ms rows), which is why a Band frame drawn in under 33 ms could differ from a slow one before the flag. A version that drew the baked page under a live row, the playhead box through the tree and the rest of the tree without the editor, matched those hashes exactly in all eight runs. It was slower in same-process comparisons, because the page changes about every 2.6 frames at 30 fps and each rebake costs about a frame, and the extra clears and copies cost more than the page drawing saved. Two variants that put the baked page into the tree as an image differed by one level in a few pixels where the playhead crosses text, so they were dropped. The live layouts keep drawing the whole tree each frame.
