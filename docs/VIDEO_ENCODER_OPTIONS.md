# Video encoder options (built, experimental, off by default)

The plan below was written before the build; the measured results are in the last section ("Measured options on this PC").
Tags: **[code]** read in the file (line numbers given); **[MF]** Media Foundation behaviour from memory, not checked against the SDK headers in this repo; **[guess]** reasoning; **[unknown]** not known.
Companion: `Source/docs/VIDEO_RENDER_SPEED.md` (draw-side options and the 4K encoder bound). Branch h11-vidcopies edits `VideoEncoder.cs`, so steps that touch it wait for that merge.

## 1. What the writer uses today

- Writer: `MFCreateSinkWriterFromURL` with two attributes **[code]** `VideoEncoder.cs:148-151`: `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS` (set to 1 when a hardware H.264 encoder is registered) and `MF_SINK_WRITER_DISABLE_THROTTLING`.
- So the **default already tries a hardware encoder** when one is registered. The owner's PC may already be on the hardware path. Nothing reports which encoder actually ran.
- Output type **[code] 168-180**: H.264, frame size, frame rate n/1, pixel aspect 1:1, progressive (InterlaceMode 2), AvgBitrate, MPEG-2 profile 100 (High). Set **not**: level, GOP size, rate-control mode, CABAC, B-pictures, quality-vs-speed, low latency.
- Input type **[code] 155**: RGB32, top-down stride. `SetInputMediaType(..., null)` at 155 and 160, so **no codec properties are passed** (the `encodingParameters` argument is the documented way to pass `CODECAPI_*` values **[MF]**).
- Audio **[code] 159-160, 192**: AAC at 20,000 bytes/s, AAC-LC level 0x29; PCM input 16-bit.
- Bitrate **[code] 57**: 0.1 bit per pixel per frame, capped at 100 Mbit/s. 1080p60 about 12.4 Mbit/s; 4K60 about 49.8 Mbit/s; 4K120 about 99.5 Mbit/s (near the cap).
- Hardware detection **[code] MediaFoundationInterop.cs:46-53**: `MFTEnumEx` with category `MFT_CATEGORY_VIDEO_ENCODER`, output H.264, flag 0x4 (`MFT_ENUM_FLAG_HARDWARE`). Returns a bool only; the IMFActivate objects are released unread, so the adapter name is lost.
- Fallback **[code] 125-126**: only a COM error thrown by `Begin` falls back to software. A failure while frames are being written ends the export (`_error`, line 135).
- `UsesHardware` **[code] 125** is the request, not the result. The sink writer can still choose a software MFT with the flag set **[MF]**.
- `live` **[code] 78** only changes the queue policy (drop vs wait). `Begin` never reads it, so **live and offline use identical encoder settings today.**

## 2. Hardware encoder: choose and verify

- Choose: `MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS` (already set) lets the sink writer use a hardware MFT. Forcing software means setting it to 0 (already done when none is registered).
- Verify the real encoder: `IMFSinkWriterEx::GetTransformForStream` returns the CLSID and the `IMFTransform` **[MF]**. Its declaration is missing from `MediaFoundationInterop.cs` (the interface at 104-112 has only the base methods). Check the vtable order against `mfreadwrite.h` before declaring it.
- Vendor: read `MFT_ENUM_HARDWARE_VENDOR_ID_Attribute` and `MFT_FRIENDLY_NAME_Attribute` from the IMFActivate objects **[MF]**. Needs an `IMFActivate` declaration. Simpler alternative: DXGI adapter `VendorId` (NVIDIA 0x10DE, AMD 0x1002, Intel 0x8086) **[MF/DXGI]**; `DxgiCom.cs` already has COM patterns (check whether it declares adapters).
- Enumeration flags: the code uses 0x4 only. Hardware encoders are often *asynchronous* MFTs (flag 0x2). **[unknown]** whether 0x4 alone includes them. Test counts with 0x4, 0x6 and 0x7 on this PC.
- Level and profile read-back: `GetUINT32` on the reader's current type (already declared, `MediaFoundationInterop.cs:60`) using the MF_MT_MPEG2_LEVEL key **[MF]**; the key GUID is not declared here.
- Checks on the code's GUIDs (line 19, `Mpeg2Profile`, and the low-latency key): confirm against `mfapi.h` before use. Not checked here.
- Not needed: `MF_TRANSCODE_CONTAINERTYPE` belongs to the transcode API (`MFCreateTranscodeProfile`), not the sink writer. The sink writer takes the container from the `.mp4` extension **[MF]**. `MF_LOW_LATENCY` is the sink-writer-level low-latency attribute **[MF]**; its GUID is to be confirmed.

## 3. Settings: speed against quality and size

Effect on the software H.264 MFT is **[guess]** unless marked. Values are the `CODECAPI_*` names from memory **[MF]**.

| Setting | Speed | Quality / size | Live | Offline |
|---|---|---|---|---|
| `AVLowLatencyMode` (or `MF_LOW_LATENCY`) | Faster: no B-frame reordering or lookahead | Slightly larger file | On | Off |
| `AVEncMPVDefaultBPictureCount` | 0 is faster | B-frames save bits at equal quality | 0 | 2 (typical) |
| `AVEncCommonQualityVsSpeed` (0-100) | Lower is faster | Higher is better | Low (guess 25) | 100 or unset |
| `AVEncH264CABACEnable` | CAVLC is faster to encode | CABAC is ~10% smaller **[guess]** | Off | On |
| `AVEncCommonRateControlMode` | Quality or VBR in offline; low-delay VBR or CBR live | CBR wastes bits in quiet passages | Low-delay VBR (guess) | Quality or VBR |
| `AVEncMPVGOPSize` / `MF_MT_MAX_KEYFRAME_SPACING` | Little | Shorter GOP: bigger file, faster seek | 1-2 s | 2-4 s |
| MF_MT_MPEG2_LEVEL | None | Required for 4K60 and above (see below) | Set | Set |
| Profile High (100) | Current. Main is not faster enough to matter **[guess]** | Keep for quality | Keep | Keep |

Level, from macroblock rates **[MF, H.264 Table A-1 from memory; verify]**: 4K is 32,400 macroblocks per frame. 4K30 is 972,000 MB/s (fits level 5.1, max 983,040). 4K60 is 1,944,000 MB/s and needs **level 5.2** (max 2,073,600). 4K120 needs a level above 5.2 **[unknown]**. 1080p120 is 979,200 MB/s (fits 5.1). **The code never sets a level.** Some players reject a stream whose declared level is too low **[guess]**. Check the level of an existing 4K60 output first.

Live 4K ceiling (35-44 fps, owner-measured) is an encoder-throughput limit. The settings most likely to help: low-latency mode, quality-vs-speed, B-frames off, and the hardware MFT. Each speed-up is **[guess]** until measured.

## 4. Proposal

Default output stays identical. Every new option defaults to today's behaviour.

- **Encoder choice** (live and offline): `Auto` (today: hardware if registered), `Software`, `Hardware`. Default `Auto`.
- **Fast export (experimental)** (offline only): quality-vs-speed low, B-frames 0, CAVLC, rate control VBR. Default off. Shown as a checkbox in the export window next to the existing options (file `VideoExportWindow.cs`, not read here).
- **Low latency (experimental)** (live only): low-latency mode, B-frames 0, low-delay VBR, GOP 1-2 s. Default off. Currently no live option changes the encoder at all, so off means identical to today.
- Where: a new `Services/Video/VideoEncoderTuning.cs` (owns: the choices, the mapping to `CODECAPI_*` values, the hardware probe result; does not own: writing or the UI). Rows in `VideoFeatureModule.SettingRows` and `SettingsCatalog.LiveVideoRows` (`VideoSettingsRows.cs`), tagged "(experimental)". Thin hooks in `VideoExportFlow.cs` and `LiveVideoSession.cs`.
- Hardware availability: probe once per export (no new static; the probe result is passed in an options object). Pass the real CLSID back so `UsesHardware` is true only when the sink writer really used a hardware MFT (see section 2).
- Setting a codec property the encoder rejects (`E_NOTIMPL` or similar) must not fail the export: log it and carry on. This matches the owner's note in `VIDEO_RENDER_SPEED.md`.
- Failure mid-run on hardware: offline export can restart the whole write with software (the frame source redraws; the audio WAV is kept). Live cannot restart: finalise the file with what was written and report. **[unknown]** whether `FinalizeWriter` succeeds after a hardware MFT fails. Needs a test that forces a failure.
- Visible in the UI: the export window checkbox and the Preferences row. Per AGENTS, an Opus visual audit renders the export window and the Preferences row in dark and light.

## 5. Measurement (objective, headless, no UI)

- Base: the former full-suite `SelfTestVideoEncoder.cs` (removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))) already has `EncodeTestClip` (a 440 Hz tone and gradient frames), reads `UsesHardware`, and reads back through `MFCreateSourceReaderFromURL`. Extend it; do not write a new reader.
- Source: a procedural 4K frame generator (score-like lines, moving edges and text-like blocks, plus gradient) with an index-based seed. Do not store frames: one 4K BGRA frame is 33 MB, so 300 frames would be 10 GB. Regenerate per frame index.
- Matrix: 1080p and 4K; 30, 60 and 120 fps (4K120 may be refused); presets: Auto (baseline), Software, Hardware, Fast export, Low latency. Three runs per cell, median.
- Metrics per run: encode ms per frame (time in `WriteSample` on the worker, measured per export and not as a static); UI blocked time (time in `_room.Wait`, `VideoEncoder.cs:79`) for offline; dropped frames for live; file size and bitrate; encoder CLSID and hardware flag; level and profile read back; first video timestamp minus first audio timestamp (sync).
- Quality: decode each output with the same reader path (RGB32); PSNR and SSIM (8x8 windows, about 60 lines) against the source and against the baseline output. Baseline vs baseline must be exactly 0 difference (a determinism check).
- Thresholds are proposals to confirm with the owner: e.g. PSNR against baseline above 40 dB for Fast export, and a visual check of decoded frames.
- Run: `dotnet build Source/src/TabForge.Avalonia/TabForge.Avalonia.csproj -c Release -o %TEMP%\tf-venc`, then `%TEMP%\tf-venc\TabForge.exe --selftest <log> --only TestVideoEncoderPresets` with a timeout per run (AGENTS). Register in the former full-suite runner (removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))) and `AreaOf` in `SelfTest.cs`. Results on this PC only; the owner's PC may differ.
- Pixel-identity check: `FrameTap` (`VideoExportFlow.cs:114`) with a per-frame hash, before and after the change, for the default path.

## 6. Risks

- **Pixels change** for any non-default setting, and for the hardware path (colour conversion and matrix can differ). Default off avoids this for users.
- **Playability**: wrong or too-low level; hardware encoders may emit SPS/PPS that some players reject **[guess]**; Baseline profile would drop B-frames and CABAC (not proposed).
- **B-frames and audio sync**: sample times are set by us (`SetSampleTime`, lines 231-232), so the presentation timeline stays monotonic. Reordering only shifts decode order inside the sink writer. Risk is on some hardware MFTs that report an offset **[guess]**: the harness checks the first-sample offset.
- Hardware MFTs are asynchronous; the sink writer handles this, but any future direct `IMFTransform` use needs the async unlock **[MF]**.
- Driver removal or a GPU crash mid-export; laptops with two GPUs may pick the wrong adapter **[guess]**.
- Live CBR or low-delay VBR can cost quality at the same bitrate **[guess]**.
- Memory: a larger queue costs 33 MB per 4K frame (per `VIDEO_RENDER_SPEED.md` option 11).

## 7. Step plan

Token estimate = 25k + 12 per line read or edited (rounded). Lines are estimates. Totals are about +/-50%.

| Step | Work | Files | Lines (est.) | Tokens | Agent | Notes |
|---|---|---|---|---|---|---|
| S1 | Measurement harness and PSNR/SSIM, test only | `SelfTestVideoEncoder.cs` (extend), new `SelfTestVideoEncoderPresets.cs`, `SelfTestFullSuite.cs`, `SelfTest.cs` `AreaOf` | ~620 | ~33k | Sonnet, medium | Runs now; no `VideoEncoder.cs` edits |
| S2 | Real encoder CLSID, hardware vendor, level read-back | `MediaFoundationInterop.cs` (IMFSinkWriterEx, IMFActivate), `VideoEncoder.cs` | ~305 | ~29k | Sonnet, medium | After h11 merge |
| S3 | Tuning class and `CODECAPI_*` through `SetInputMediaType`; live flag reaches `Begin` | new `VideoEncoderTuning.cs` (<=150), interop (+40), `VideoEncoder.cs` (+30), tests | ~520 | ~32k | Sonnet, medium | After S2; the harness decides defaults |
| S4 | Rows and export checkbox | `VideoFeatureModule.cs`, `VideoSettingsRows.cs`, `LiveVideoSettings.cs`, `VideoExportWindow.cs` (unread), `VideoExportFlow.cs` | ~290 | ~30k | Sonnet, medium, then Opus visual audit | Visible UI: Opus audit mandatory |
| S5 | Offline restart with software on hardware failure | `VideoExportFlow.cs`, `VideoEncoder.cs` | ~380 | ~30k | Sonnet, medium; tf-senior read-only review | Lifetime and threading |
| S6 | Live low-latency wiring | `LiveVideoSession.cs` (+10), tests | ~150 | ~27k | Haiku, high | After S3 |
| S7 | Docs, feature map, CHANGELOG line, budgets | `CHANGELOG.md`, `ArchitectureBudget.json` | ~200 | ~28k | Haiku, high | Each step |
| S8 | Opus visual audit (dark and light, contrast, reachability) | none | n/a | ~50k [guess] | Opus | Before merge of S4 |

Total: about 230-260k tokens [guess]. Manager runs REBUILD once after S3 and S5 (agents never run it).

## Unknowns

- Whether the owner's PC uses the hardware encoder today (`UsesHardware` is only the request).
- Whether `MFT_ENUM_FLAG_HARDWARE` alone lists async hardware encoders.
- The software encoder's default B-frames, GOP and rate control, and the real effect of each `CODECAPI_*` value (all [guess]).
- The current level of the output file, and what level 4K120 needs.
- Whether `FinalizeWriter` works after a hardware failure.
- Any speed-up figure in this document: none is measured.

## Baseline measured on this PC

Machine: Ryzen 7 9700X, 16 threads, 61 GB RAM. Release full-suite build, the encoder's default path (`VideoEncoder.Open(..., live: false)`, no options). Two back-to-back runs of `TabForge.exe --selftest <log> --profile <dir> --only TestVideoEncoderBenchmark`. The `bench:` lines in the log hold the numbers. Each clip is 120 frames with a 440 Hz stereo tone at 48 kHz; the clip is generated per frame index, so nothing is stored.

Timing (ms per frame): **write loop** is the Stopwatch around `WriteFrame` + `WriteAudio`; **end-to-end** is (loop + finalise) / frames, which is the throughput to compare. The loop alone understates the encoder: the queue holds 6 frames, so the loop only waits for room.

| Clip | Run | Write loop | Finalise | End-to-end | Size (bytes) | Bitrate (kbit/s) | Luma PSNR min / mean (dB) | SSIM | Decoded frames |
|---|---|---|---|---|---|---|---|---|---|
| 1080p30 | 1 | 0.49 | 29 ms | 0.73 | 3194366 | 6389 | 47.67 / 53.13 | 0.9973 | 120 |
| 1080p30 | 2 | 1.23 | 98 ms | 2.05 | 3194442 | 6389 | 47.67 / 53.13 | 0.9973 | 120 |
| 1080p60 | 1 | 0.45 | 30 ms | 0.71 | 6264151 | 25057 | 52.38 / 57.63 | 0.9988 | 120 |
| 1080p60 | 2 | 0.76 | 32 ms | 1.03 | 6264151 | 25057 | 52.38 / 57.63 | 0.9988 | 120 |
| 4K30 | 1 | 2.56 | 63 ms | 3.09 | 12525626 | 25051 | 50.23 / 53.87 | 0.9983 | 120 |
| 4K30 | 2 | 1.63 | 82 ms | 2.31 | 12525566 | 25051 | 50.23 / 53.87 | 0.9983 | 120 |
| 4K60 | 1 | 2.08 | 67 ms | 2.64 | 24926551 | 99706 | 53.46 / 60.39 | 0.9995 | 120 |
| 4K60 | 2 | 1.58 | 81 ms | 2.25 | 24926551 | 99706 | 53.46 / 60.39 | 0.9995 | 120 |

Other readings, same runs:
- Every clip: first audio sample minus first video sample is 0.0 ms, an audio stream is present, the decoded frame count is 120 (equal to the encoded count), `dropped=0`.
- Quality: PSNR and SSIM are on luma of every 10th frame (12 frames) against the regenerated source. The luma matrix is picked per clip by the first sampled frame; all four clips picked BT.709. The test's threshold is 30 dB (lowest sampled frame); the lowest measured value is 47.67 dB.
- `hardware=True` on all four clips. This is the request flag (`UsesHardware`), not proof of which MFT ran; the encoder CLSID is not exposed, so `encoder=unknown`.
- Bitrate at 60 fps is about 2x what `VideoEncoder.VideoBitrate` asks for (1080p60: 25 Mbit/s measured vs 12.4 Mbit/s requested; 4K60: 100 vs 49.8). At 30 fps the output matches the request (1080p30 6.4 vs 6.2; 4K30 25.1 vs 24.9). Cause not found and not investigated here: check it before S3 changes rate control.
- Timing noise is large on this PC: the same 1080p30 clip measured 0.49 and 1.23 ms per frame in the write loop across two runs. Use at least 3 runs and the median before any speed decision.
- Sizes differ by a few hundred bytes between runs (hardware path), while PSNR and SSIM repeat exactly.

Harness: former full-suite `SelfTestVideoEncoderBench.cs` (removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))), `TestVideoEncoderBenchmark`, area `bench` (outside `basic` and `release`; runs only with `--only`). An encoder options object is passed as a new opener next to `BenchDefaultOpen` and given to `RunVideoBench`; the default stays today's behaviour.

## Measured options on this PC

Built as three Preferences rows (General, "Video encoder (experimental)"): `Encoder (experimental)` Auto / Software / Hardware, `Fast export (experimental)` (offline), `Low latency recording (experimental)` (live). Code: `Services/Video/VideoEncoderOptions.cs`, `VideoEncoder.cs`. Default output is unchanged: `TestVideoEncoderGolden` and the 4K tests pass with default settings, and `TestVideoEncoderOptions` re-checks the golden digests with an explicit default options object.

Machine: Ryzen 7 9700X, 16 threads. `MFTEnumEx` for H.264 encoders finds, with the hardware flag: **AMDh264Encoder (vendor VEN_1002, AMD), listed three times**; synchronous (software): **H264 Encoder MFT**; asynchronous: none (the AMD encoder is found through the hardware flag only). The GPU model is unknown (the enumeration gives the vendor only).

**What ran.** The sink writer's transform chain holds a colour converter first and the encoder second. `VideoEncoder.EncoderName` / `UsesHardware` now report the encoder transform found through `IMFSinkWriterEx.GetTransformForStream` (asynchronous = hardware). The default (Auto) ran **AMDh264Encoder** in every clip: the Auto output is byte-identical to the forced Hardware output. The older `UsesHardware` value was only the request.

Method: `TestVideoEncoderBenchmark` (full-suite build, `--only`), 120 frames of the procedural clip, three runs, medians below. Times are ms per frame. Loop is the time the caller spends in `WriteFrame` + `WriteAudio`; end-to-end adds the finalise. PSNR and SSIM are luma against the regenerated source (every 10th frame), not against the default output; the default is the first row of each group, so a difference is the difference to it. Sync is first audio sample minus first video sample.

| Clip | Row | Loop | Finalise (ms) | End-to-end | Size (MB) | PSNR min / mean (dB) | SSIM | Sync (ms) | Encoder |
|---|---|---|---|---|---|---|---|---|---|
| 1080p60 | Auto (default) | 1.06 | 19 | 1.22 | 6.26 | 52.4 / 57.6 | 0.9988 | 0.0 | AMD hardware |
| 1080p60 | Hardware | 1.06 | 18 | 1.21 | 6.26 | 52.4 / 57.6 | 0.9988 | 0.0 | AMD hardware (identical file) |
| 1080p60 | Software | 1.09 | 21 | 1.27 | 2.70 | 44.7 / 47.7 | 0.9950 | -16.7 | H264 Encoder MFT |
| 1080p60 | Fast export | 1.04 | 21 | 1.21 | 6.26 | 52.4 / 57.6 | 0.9988 | 0.0 | AMD hardware (identical file) |
| 1080p60 | Fast export + Software | 1.11 | 21 | 1.29 | 3.01 | 45.3 / 47.3 | 0.9945 | 0.0 | H264 Encoder MFT |
| 1080p60 | Low latency | 1.88 | 642 | 7.64 | 3.15 | 47.7 / 53.1 | 0.9973 | 0.0 | AMD hardware |
| 4K60 | Auto (default) | 4.12 | 42 | 4.48 | 24.93 | 53.5 / 60.4 | 0.9995 | 0.0 | AMD hardware |
| 4K60 | Hardware | 4.08 | 40 | 4.42 | 24.93 | 53.5 / 60.4 | 0.9995 | 0.0 | AMD hardware (identical file) |
| 4K60 | Software | 4.42 | 77 | 5.09 | 9.60 | 47.1 / 48.0 | 0.9975 | -16.7 | H264 Encoder MFT |
| 4K60 | Fast export | 4.52 | 43 | 4.88 | 24.93 | 53.5 / 60.4 | 0.9995 | 0.0 | AMD hardware (identical file) |
| 4K60 | Fast export + Software | 4.37 | 70 | 4.92 | 12.05 | 46.1 / 47.4 | 0.9954 | 0.0 | H264 Encoder MFT |
| 4K60 | Low latency | 4.91 | 69 | 5.25 | 12.48 | 50.2 / 53.9 | 0.9983 | 0.0 | AMD hardware |

Every row decoded 120 of 120 frames with an audio stream and sync within 50 ms. Timing noise on this PC is large (a few tenths of a ms at 1080p, up to 1 ms at 4K between runs), so differences under about 10% are not real.

What the numbers say:
- **Encoder choice.** Auto already picks the hardware encoder here. Forcing Hardware changes nothing. Forcing Software is no faster on the write loop, is slower at 4K (end-to-end 5.1 against 4.5 ms), and makes a file 2.5 times smaller at lower quality (44.7 dB). Software finishes the first video sample one frame after the audio (-16.7 ms), which is within the 50 ms bound. The option is useful for a PC whose hardware encoder misbehaves; it is not a speed-up.
- **Fast export.** On the hardware encoder the file is **byte-identical** to the default and the time is the same: the properties have no effect there. On the software encoder the file is slightly bigger and the time is not lower. No measurable gain; it stays OFF, and the setting says so.
- **Low latency.** On the AMD encoder it is **slower** (1080p60 end-to-end 7.6 against 1.2 ms, finalise 0.5 to 0.9 s; 4K60 loop 4.9 against 4.1) and it makes the encoder follow the requested bitrate: sizes halve (see below). No speed gain for live recording; it stays OFF, and the setting says so.
- **Bitrate overshoot found.** The default hardware output at 60 fps is about twice the requested bitrate (1080p60: 25.1 Mbit/s against 12.4; 4K60: 99.7 against 49.8). With low-delay VBR (the low-latency set) the file is 12.6 and 49.9 Mbit/s, the requested rate, at 47.7 dB min (1080p60) and 50.2 dB (4K60): still well above the 30 dB bound. A smaller default file would need a rate-control change that alters the default output; not done here, left for the owner to decide.
- Rejected properties: an unknown codec property (a made-up key) is accepted silently by the sink writer on this encoder; nothing is logged because nothing fails. If a writer ever rejects the property set, the writer is rebuilt without it and the export goes on (logged; covered by the test through a failing-start hook, not by a real rejection).
- Hardware failure: offline export (Auto or Hardware) retries once with software and logs it; live with an explicit Hardware choice reports the error through the existing path; live Auto keeps today's retry. Tested with a simulated failing start (`SimulateHardwareFailure`); a real driver failure was not available to test.

Not measured: 4K120 and the live (paced, dropping) path at 4K; the level of the output stream; CABAC and GOP properties (not added: the GUIDs were not confirmed). The encoder name appears in the trace file (`TABFORGE_TRACE=engine`) and in `VideoEncoder.Notes`; it is not shown in the finished-video notice.
