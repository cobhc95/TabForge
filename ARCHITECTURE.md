# Architecture

TabForge is a notation-first composition workstation (C# / .NET 8 / WPF, Windows x64) with an audio path: built-in General
MIDI synth, VST2/VST3 hosting, recording, clips, mixing and offline render. Three projects, up to three kinds of process.

"By design" means the code is written to that rule and reviewed against it. It is proven only where a self-test is named.

## 1. Projects and dependencies

| Project | Contents | Depends on |
| --- | --- | --- |
| `src/TabForge` | WPF app: score model, editor, playback compiler, MIDI scheduler, documents, settings, render orchestration, engine client | TabForge.AlphaTab 1.8.4-tabforge.4 (alphaTab 1.8.4 with four patches: per-import .gp3/.gp4/.gp5 bar limit, exact gpif mixer volume/balance, gpif trill speed, .gp3-.gp5 link marks without a partner note; source in `vendor/alphatab`); the two projects below (NAudio, MeltySynth, SoundTouch.Net come in transitively) |
| `src/TabForge.Audio.Contracts` | Wire contracts: `EngineProtocol` (commands, events, framing, records), `RenderProtocol`, `SharedBlock` (shared-memory layout) | none |
| `src/TabForge.AudioEngine` | Audio output, mixer, track chains, VST2/VST3 hosting, MIDI processors, recorder, clip player, GM synth, offline renderer, isolated plug-in host, native bridge | NAudio 2.2.1, MeltySynth 2.4.1, SoundTouch.Net 2.3.2, native `tfvst3.dll` |

`native/tfvst3` is a C++ VST3 host bridge built against the Steinberg VST3 SDK (pinned by `native/fetch-vst3sdk.ps1`).
`native/build-tfvst3.ps1` writes `native/BUILD_PROVENANCE.md`; `tools/Package-Release.ps1` checks the shipped DLL against it.
Versions and hashes of all components: `docs/SBOM.md`. Restore is locked (`packages.lock.json`, `--locked-mode`).

Dependency direction inside `src/TabForge`:

```
Models/            (pure data: SongProject, TrackModel, MeasureModel, TabCell, TabNote)
   ^
Services/  MusicTime, GuitarProImporter/Exporter, ProjectService, MusicTheory, exports, limits
   ^
Playback/  PlaybackCompiler -> ScoreTimeline ; PlaybackEngine (scheduler) ; PlaybackOrder
   ^
Audio/ Rendering/ Plugins/   (engine client, routing, render orchestration, plug-in catalog/rig)
   ^
Visualization/ + Views/ + MainWindow  (read-only consumers of the timeline)
```

Rules for this layering:
- `Models` and `Playback` do not reference WPF.
- The playback timeline is authoritative. Visualisation reads it and adds no animation that lags the sound.
- External formats are converted only at the import and export boundary.
- The scheduler owns the compiled timeline; the UI observes it (`PlaybackEngine.TimelineChanged`).

The architecture self-tests (`--areas architecture`) check the boundaries.

## 2. Processes

```
+----------------------------------------------+
| TabForge.exe  (UI process, WPF, STA)          |
|  documents, editor, score model, undo         |
|  PlaybackEngine  (MIDI scheduler thread)      |   winmm MIDI out (plain MIDI tracks / external ports)
|  AudioEngineClient  (engine lifecycle)        |
+---------+------------------------+-----------+
          | named pipe (frames)    | shared memory: MIDI ring, breadcrumb, meters
          |  CurrentUserOnly       | (SharedBlock, one per engine session)
+---------v------------------------v-----------+
| TabForge.exe --audio-engine <session> <pid>   |   same executable, no WPF started
|  EngineHost: command reader, main thread,     |
|  watchdog thread                              |
|  MixEngine (audio callback, device thread)    |
|  track chains: VST2 / VST3 / GM synth / MIDI  |
|  processors, InputCapture, Recorder, Clips    |
|  OfflineRenderer (render only)                |
+---------+------------------------------------+
          | per isolated plug-in: pipe (control) + shared block + two events (audio)
+---------v-------------------------------------+
| TabForge.exe --plugin-host <id> <pid> ...     |   optional, one process per plug-in
|  one plug-in instance, its editor window      |   (Preferences > Audio & Plug-ins > separate process)
+-----------------------------------------------+
```

- **UI process.** Owns the song, documents, undo, settings and the desired audio configuration. Never runs plug-ins and never
  sits on the audio callback path.
- **Audio engine process.** Started by `AudioEngineClient` as `--audio-engine <session> <parentPid>`; `Program.Main` dispatches
  before any WPF object exists. Exits when the parent dies. The session id is 8-64 alphanumeric characters and names the pipe
  and the shared block. If the engine dies, the client restarts it, and a plug-in blamed by the breadcrumb is quarantined.
- **Plug-in host process (optional).** `--plugin-host` runs one plug-in for `RemotePlugin`. Helpers: `--plugin-info` (metadata
  probe) and `--probe-gm`.
- **Headless modes** (`--selftest`, `--playtest`, `--audit`, `--render`, ... in `Diagnostics/DiagnosticCommands.cs`) run without a
  main window and return exit codes 0 / 1 / 2.

## 3. IPC and limits

- **Control channel:** one duplex named pipe per session, `TabForge.AudioEngine.<session>`, `CurrentUserOnly | Asynchronous`.
  Framing `[int32 length][byte type][payload]` (`Frames`); every read is bounded (`ReadBoundedString`, ...). **Frame limit
  32 MiB** (`Frames.MaxFrameBytes`); a bad length is a protocol error. The plug-in host control pipe is `TabForge.PluginHost.<guid>`.
- **MIDI ring (shared memory):** `TabForge.AudioEngine.<session>.shm`, a lock-free SPSC ring of 16-byte `TimedMidi` (capacity
  8192, power of two). The UI writes; the engine's audio thread reads. Timestamps are `Stopwatch` (QPC) ticks in both processes.
  A message stamped for t is heard at t + `MixEngine.DelayTicks`; the UI delays its winmm output by the same amount. The block
  also holds the crash breadcrumb (section 8), latency and CPU status, and 256 x 2 meter slots. Layout is fixed and versioned by
  the code on both sides.
- **Plug-in host audio path:** `PluginHostBlock` (`Local\TabForge.PluginHost.<guid>`): block audio in and out, up to 512 MIDI
  events per block, transport. A request/done event pair with a generation number means a late answer is never taken for the
  current block. A block not answered within the callback budget marks that plug-in lost for the session.
- **Slots:** 256 track slots, 32 group buses, one master (`MixEngine.MaxSlots/MaxBuses/MasterSlot`).
- **Plug-in state:** at most 16 MiB raw (`PluginStateLimits.MaxRawBytes`, 4/3 as base64), one plug-in per frame. One contract
  (`PluginStateLimits`, `Audio.Contracts/EngineProtocol.cs`) for the validator, load, live SetState, native getters and transfer.
- **File limits (`Services/InputLimits.cs`):** `.tforge` and score files 128 MiB, settings JSON 2 MiB, JSON depth 64, at most 256
  tracks, 20,000 bars per track, 100,000 bars and 2,000,000 cells per project, `.tfaudio` 64 MiB. Embedded project entries in a
  `.gp` are inflated with a hard cap.
- Shared memory uses default security descriptors; only the pipes are `CurrentUserOnly`. The IPC is a same-user, same-machine
  transport, not an authentication boundary.

## 4. Real-time rules (audio callback)

The callback is `MixEngine.Read` (NAudio `ISampleProvider`) on the driver's thread (ASIO: its real-time thread).
`Recorder.Enqueue` and `InputCapture` block delivery run on capture threads under the same rules. On these threads, **by design**:

1. No allocation after construction. Buffers, pending-event arrays, render graph and chains are preallocated and swapped in whole
   (`Volatile.Write`), never mutated in place.
2. No locks, blocking waits or I/O (no disk, pipe writes, file logging, `Task`/`await`).
3. No disposal on the callback. An object the callback may still use (replaced chain, plug-in, clip player) is unpublished on the
   engine's main thread and disposed by `RetireQueue.Collect` once the callback epoch (`CallbackEpoch`, odd while a callback runs)
   has moved past it. Never on a fixed delay.
4. Communication in and out is lock-free: the MIDI ring (SPSC), `Volatile`/`Interlocked` published references, `Interlocked`
   counters (meters, heartbeat, latency).
5. Plug-in calls cannot be made real-time safe: a plug-in's `process` is native code on the audio thread. Every call sets a
   breadcrumb first (section 8). Isolated plug-ins are called through `PluginHostLink` with a time budget.
6. The callback publishes a heartbeat timestamp each time it finishes; the watchdog reads it.

Nothing in the repository measures rules 1-2 (no allocated-bytes-per-callback or p99 test). Treat them as rules to review.

UI side: no per-frame or layout-driven work for playback visuals; the UI never controls musical time.

## 5. State ownership

| State | Owner | Notes |
| --- | --- | --- |
| Song, tracks, bars, notes | UI process, `DocumentSession.Project` | Edits end in `CommitEdit(EditRefresh...)`. Undo is a bounded chain of `ProjectState`s (`UndoController`, `UndoHistory`); unchanged bar chunks are shared |
| Compiled timeline | `PlaybackEngine` | Rebuilt from the project; the UI observes `TimelineChanged` |
| Audio configuration, chain *requests* | UI process (project and settings) | Sent as commands; the full desired state is re-sent after an engine restart |
| Live plug-in instances, chains, routing | Engine process | Keyed by slot and stable `PluginSpec.Id`; a chain change keeps the instance with the same id |
| Plug-in internal state | The plug-in | Read back with `GetStates` on save; a failed getter is reported per plug-in, never replaced by an older stored state |
| Isolated plug-in instance | Plug-in host process | The engine holds a `RemotePlugin` proxy |
| Recorded takes, clips | Files on disk; `AudioClip` / `ClipLane` metadata in the project | |
| Settings | `AppSettingsStore.Shared` (created by `App`), `%APPDATA%\TabForge\settings.json` | One `AppSettings` for all windows. Changes raise `Changed`; saves are debounced (400 ms) and atomic, and flush on exit. An unreadable file is not overwritten until Preferences are applied. Normalised and clamped on load; never wiped by rebuilds |
| The audio engine | `AudioEngineClient.Instance`, owned by the active document | Engine ownership below |
| Crash recovery copies | `%LOCALAPPDATA%\TabForge\Recovery` | Written by the last-resort handler in `App.xaml.cs` |

Rule: one owner per piece of state. Other processes hold requests or copies, never a second source of truth.

**Engine ownership.** The engine is app-wide with one owner: the document whose window last synced it (`AudioRouting.Apply(...,
owner: DocumentSession)`). Other documents play through Windows MIDI or are silent; there is no multi-document mixing. On a switch,
the previous owner's chains are *parked* (loaded, level 0, no clips, not armed) on their own slots, so documents never share a slot.
Switching back within the warm period (`AudioEngineClient.DefaultWarmIdleMinutes`, 5 minutes, `WarmIdle`) re-activates them without
a LoadChain. Group buses and the master use fixed shared slots, never parked; they reload on an owner switch. Parked chains are removed
after the warm period. A `Sync` without engine tracks keeps the engine warm until the warm period ends or the app exits. Headless
probes set `WarmIdle = 0`.

**Window lifetime.** A main window attaches to objects that outlive it: static events (`MediaAccess.Changed`,
`PlaybackEngine.LoopCompleted`), the shared `AudioEngineClient` and `AppSettingsStore`, the media-provider registration, and the
recorder's engine and MIDI-input hooks. Each attachment goes through `MainWindow.Subscribe` / `_lifetime.Add` (`OwnedSubscriptions`),
which records the detach beside the attach; `ReleaseWindowResources` runs them once from `Closed`. `Closing` only decides. Work queued
from another thread goes through `PostIfOpen`, which drops it after close. Settings dialogs call back into their opener through
`SettingsWindowActions` (via `WpfSettingsWindowHost`), never through statics. A document owns its clock (`DocumentPlaybackState.Clock`),
so a tab moved to another window keeps playing. App-wide caches hold songs and windows weakly. The `window-lifetime` group builds,
uses and closes real `MainWindow`s and asserts this.

**Document media context.** Everything that reads a song's linked audio receives its `MediaContext` (`DocumentSession.Media`)
explicitly: `MediaAccess.Evaluate/Approve/Revoke`, `WaveformCache.Get/StatusOf/LengthOf/Cancel`, `AudioEngineClient.Sync`,
`AudioRouting.Apply` (with the document's skipped plug-ins), the drop session, the timeline and render requests. There are no ambient
providers and no "current document". The context holds:
- the identity: a run-time session id, never stored in a file;
- the **media base directory**: the saved file's folder, else the folder an imported song came from (separate from the save path);
- the **approval scope**: a saved song's canonical path (in settings), or an unsaved song's own session (memory only); an empty
  path matches nothing;
- a **revision** that changes on Save As and on close.

Save As copies no approvals; an unsaved song's session approvals transfer only to the path it is first saved to. The waveform cache
keeps one slot per document and file and shares decoded peaks by canonical file only after the asking document's permission is
checked. The worker re-checks permission before opening anything, after the read and on every draw (`MediaAccess.Decide`, memory only,
so an unreachable share never stalls the UI thread or the cache lock). A read for a closed document or an older revision is dropped.
An approval change re-judges the clips of every live engine slot (`AudioEngineClient.RefreshClips`, which waits while a render runs).
Window close asks about every dirty document first. Preferences merges approvals with the live list (`MediaApprovalMerge`). The
`document-context` group covers the matrix.

**Document operations.** The model half of an edit, save, close or open is a function of an explicit `DocumentSession`; `MainWindow`
keeps dialogs, input gating, status text and visual refresh.
- `DocumentEdits.Run` is one logical edit: one undo transaction (nothing stored when nothing changed), one dirty change, one timeline
  invalidation (`SongProject.BeginTimelineBatch`). `ArrangementController` and `TrackController` expose the bar, section and track
  edits; Undo and Redo use the same service.
- `DocumentSaveFlow`: the save and export sequence. Questions only when data would be lost; plug-in states awaited; then the write.
- `DocumentCloseFlow`: every unsaved tab is asked before anything happens; also save-all.
- `DocumentPlacement` decides where an opened song goes; the tab an open replaces is chosen when the open starts.

The `document-operations` group runs these without a window and checks keyboard, menu and context-menu parity on real windows.

## 6. Persistence formats

- **`.tforge`** (`ProjectService`): the complete project as UTF-8 **JSON** (`SongProject.FormatVersion` = 2), written atomically
  (temp file, then replace) through a size-checked, hashed stream. On load: bounded read, shape and depth validation, then
  `ProjectValidator`. Not gzip.
- **`.gp` written by TabForge** (`GuitarProExporter.Save`): an alphaTab-written file plus one zip entry,
  `TabForge/project.tforge.gz` (gzip of the project JSON). Other readers ignore it; TabForge reads it back for a lossless round trip
  (`TryReadEmbedded`, capped inflate). `embedProject:false` writes a clean file.
- **Clean `.gp` with a `.tfaudio` sidecar** (`AudioDataFile`): `<song>.gp` stays clean; audio data (sound source, plug-in rig and
  state, mixer, clips and lanes) lives in `<song>.tfaudio`, JSON, `FormatVersion` 1, at most 64 MiB. An invalid sidecar is ignored
  entirely, never half-applied.
- **Import:** .gp3 to .gp7 through alphaTab (`GuitarProImporter`, bounded read); a `.gp` with an embedded project loads that project.
  `AlphaTabBoundary` is the only route to alphaTab: it checks the patched `TabForge.AlphaTab` build (name, version, bar limit,
  percussion table) and sets that import's bar limit (`InputLimits.MaxMeasuresPerTrack`) on its own `Settings`. No global parser
  state and no private reflection on the import path (one private access only words an error after a failed read). Cumulative limits:
  `GuitarProImportBudget`; memory, time and cancellation: `ImportGuard` and `ImportWorker`.
- **Export:** .gp (`GuitarProExporter`), Standard MIDI (`MidiExportService`), ASCII tab, WAV and MP3 render.
- **Settings and presets:** JSON, bounded and clamped.

## 7. Recording pipeline

```
input device (WASAPI, or ASIO driver callback via InputCapture.Feed)
   |  capture callback (real-time rules apply)
   +--> InputCapture ring (lock-free, ~0.7 s) --> InputBlock (inputs 1+2 at engine rate, filled once per mixer block,
   |                                              shared read-only by every armed chain for monitoring)
   +--> Recorder.Enqueue: copy into a preallocated lock-free SPSC queue (~4 s of stereo)
                              |
                       dedicated disk thread --> one 32-bit float WAV per armed track (chunked writes)
```

If the disk falls behind and the queue is full, missing frames are written as silence so the take stays in time, and the loss is
reported once. Disk full or a removed drive stops recording cleanly, keeps finalised files and shows "Recording problem". A failed
start disposes per-writer files and releases the input device. Capture opens only while a track is armed.

## 8. Plug-in lifetime, isolation and the watchdog

- **Loading** runs on the engine's main thread (`LoadChain`), keyed by `PluginSpec.Id`. A quarantined plug-in (`Skip`) is not loaded.
  VST2 (`Vst2Plugin`) and VST3 (native `tfvst3.dll`) implement `IPluginInstance`.
- **In-process** (default): a native crash kills the engine; the UI restarts it with the breadcrumb-blamed plug-in switched off.
- **Separate process per plug-in** (`RemotePlugin` / `--plugin-host`): only that process dies on a crash or hang. Effects pass the sound
  through, instruments go silent, `Crashed` is raised once on the engine's main thread, and the plug-in is lost for the session.
  **This is crash isolation, not a security sandbox.** The process runs as the same user with the same rights as TabForge.
- **Plug-in trust boundary** (`Plugins/PluginTrust.cs`): songs, rigs, presets and auto-load chains only *name* plug-in paths.
  `AudioEngineClient.Sync` builds specs through `PluginTrust.BuildSpecs`. A path loads only if it is in the user's scan (this
  session's or the remembered list), inside a scanned folder on a local fixed drive, or in `PluginSettings.ApprovedPluginPaths`
  (full normalised path, case-insensitive). UNC and removable or optical locations always need explicit approval.
- **An approval covers this binary, not the path, folder or publisher** (Program Files and Common Files location trust excepted). The
  SHA-256, size, time and signing key are recorded; `PluginSpec.ExpectedSha256` carries the hash to the engine. `PluginIdentity.Hold`
  (engine main thread, never the audio thread) hashes the exact file before every real load, through a deny-write handle held until the
  plug-in is created. A mismatch is `PluginLoadStatus.BlockedChanged` and needs a new approval. Routine UI checks compare size and
  last-write time, and hash only on a difference (cached). A new build signed with the same key (SHA-256 of the certificate's public
  key, WinVerifyTrust-validated) is accepted as a publisher update; the subject text is display only.
- Everything not approved is sent as `Skip`, so no native code runs before the user decides. The main window shows a "Review..." bar;
  the FX window shows "Blocked: not approved" with an Allow button. Adding a plug-in from the FX window's browser approves it.
- **Breadcrumb** (`SharedBlock`): before a plug-in call the engine records slot, index and plug-in path in shared memory, so the UI can
  read it after the engine dies. Offline render has one breadcrumb per worker thread.
- **Watchdog** (`EngineHost`), two independent checks:
  1. Main thread: one queued command running for over 10 s **and** a breadcrumb naming a plug-in call -> `Environment.Exit(70)`; the
     UI blames that plug-in and restarts without it. A hang with no breadcrumb is not attributed.
  2. Audio-thread heartbeat: while playing, no completed callback for over 4 s -> exit 70.
  Coverage is breadcrumb-dependent: a new plug-in call site must set the breadcrumb before the watchdog can be relied on.
- **Retirement:** replaced or removed plug-ins go through `RetireQueue` (section 4) and are disposed at shutdown too.
- **Shutdown:** each step is guarded so one failure cannot skip ASIO release. The isolated host process is killed on dispose.

## 9. Render pipeline

`File > Render` (`Rendering/RenderJob`, `RenderSpecBuilder`): the UI forces engine routing, builds a render spec (`RenderProtocol`),
and the engine's `OfflineRenderer` runs faster than real time. Worker threads render whole chains ahead into bounded per-chain block
queues (a chain stays on one worker; one breadcrumb per worker). One mixer thread pops in lockstep, sums the master in a fixed order
(bit-identical whatever the thread count), writes master and stems in one pass, and reports progress. Plug-in latency is compensated
per chain (fed L frames ahead, first L output frames dropped) so stems line up. MP3 is encoded from a temporary WAV through Media
Foundation (initialisation locked). Cleanup restores routing first, then removes temp files; a cancelled or failed render deletes
partial output. Names Windows treats as devices (CON, NUL, ...) are rejected. Not covered: delay compensation for sidechain and buses.

## 10. The MIDI playback pipeline (UI process)

```
SongProject
  |  PlaybackOrder.Build        repeats / alternate endings -> bar indices in performance order
  v
ScoreToMidiCompiler             tempo/time-sig map, channel allocation, ties, techniques, metronome, count-in, start-cell seek
  v
ScoreTimeline                   Events[] (absolute ms), Bars[], Notes[], ChannelSetup[], PlayFromMs
  |  SustainResolver            let-ring tails, same-pitch release gap, deterministic sort
  v
PlaybackEngine (scheduler)      monotonic clock -> dispatch due events -> routed output
  +--> RoutedMidiOutput         per track: winmm port, or timed messages into the engine's shared ring (plug-in / GM synth tracks)
  +--> PlayheadMapper           ms -> bar/cell/fraction (score, arrangement, fretboard)
  +--> MidiExportService        the same timeline -> Standard MIDI File
```

Events are absolute milliseconds from per-bar tempo (`MusicTime.BarMs`). Onsets follow a duration cursor, not the raw 16-slot index,
so tuplets and dotted values do not drift. The scheduler is deterministic for the same score, settings and start. `PlaybackEngine`
raises about 60 Hz position callbacks; `MainWindow` coalesces them onto a `DispatcherTimer`.

## 11. Subsystems and their files

| Subsystem | Files |
| --- | --- |
| Score model | `Models/SongProject.cs`, `TechniqueNames.cs` (persisted ids: use the constants), `TripletFeels.cs`, `NotationEnums.cs` |
| Section bounds, bar ranges, musical time | `Services/SectionLayout.cs`, `Services/BarRangeEditor.cs` (all-track copy, remove, insert, move), `Services/MusicTime.cs` |
| Playback | `Playback/*` (options, timeline, compiler, sustain, order, channels, playhead, note queries, scheduler, diagnostics) |
| Import and export | `Services/GuitarProImporter.cs`, `GuitarProExporter.cs`, `MidiExportService.cs`, `ProjectService.cs`, `AudioDataFile.cs` |
| Input limits | `Services/InputLimits.cs`, `FilePathPolicy.cs`, `ProjectValidator` |
| Documents | `Documents/*` (session, controller, undo history, tab transfer, playback-on-switch policy) |
| Notation view | `Views/TabEditorControl.cs` (state, properties) with partials `.Geometry`, `.Playback`, `.Navigation`, `.Editing`, `.Selection`, `.Keyboard`, `.Mouse`, `.Accessibility` (incl. `EditorPeer`), `.Layout`, `.Rendering`, `.Marks`; `Views/StaffNotationRenderer.cs`; `Views/EditorEvents.cs`. One owner per partial file |
| Arrangement panel | `Views/ArrangementPanel.cs` with partials `.RowGeometry`, `.Timeline`, `.Interaction`, `.TrackRows`, `.Groups`, `.TrackColumns`; `ArrangementFollowGeometry.cs`, `SectionDragOverlay.cs`, `SectionInsertionIndicator.cs` |
| Arrangement timeline | `Views/TrackTimeline.cs` (state, geometry, activity cache) with partials `.Render`, `.Interaction`, `.Clips`, `.MediaDrop`, `.SongTime`; `Services/MediaDrop`, `Services/MidiFileImport`, `Views/MediaDropSession`, `Views/VirtualFileDrop`, `Views/MediaDropGhost` |
| MIDI compiler | `Playback/ScoreToMidiCompiler.cs` with partials `.Techniques` (note emission, dead, ghost and palm mute, grace, slides, trills, tremolo, bends, vibrato, whammy, fade-in), `.Clips`, `.Metronome` |
| UI host | `MainWindow.xaml(.cs)` and `MainWindow.<Area>.cs` partials (composition root; model edits end in `CommitEdit`; no musical logic) |
| Audio client | `Audio/AudioEngineClient.cs`, `AudioRouting.cs`, `AudioDevices.cs`, `RoutedMidiOutput.cs`, `SongClock.cs`, `WaveformCache.cs` |
| Plug-in catalog and rig | `Plugins/*` (scan via `VstScannerService` and isolated probe, rig per track, drum maps, MIDI processor catalog) |
| Rendering | `Rendering/RenderJob.cs`, `RenderSpecBuilder.cs` |
| Contracts | `TabForge.Audio.Contracts/*` |
| Engine | `TabForge.AudioEngine/EngineHost.cs`, `EngineThreads.cs`, `EngineRender.cs`, `Mixing/*`, `Audio/*`, `Plugins/*`, `Isolation/*`, `Midi/*`, `Synth/*`, `Output/*`, `Editors/*` |
| Window shell | `Views/BrowserTabBar.xaml(.cs)`, `Shell/` (title-bar tabs, drag policy, Win32 caption hit tests) |
| Headless tools | `Diagnostics/DiagnosticCommands.cs` |
| Debug switch | `Services/Log/Trace.cs`: `TABFORGE_TRACE=area,...` (playback, engine, layout, import, ui, all) writes `Diagnostics/trace-<area>.log`; `TABFORGE_MIDI_LOG`, `TABFORGE_LAYOUT_LOG`, `TABFORGE_CAPTION_LOG` still work |
| Crash safety | `App.xaml.cs` (last-resort handler: crash log, unsaved songs to Recovery) |
| Tests | `SelfTest*.cs` (headless deterministic suite, partial class; groups are guarded; skips reported as SKIP) |
| Shortcuts | `TOOLS_AND_HOTKEYS.md` |

## 12. Where to look, guarantees and limits

Where to look: note length and attack -> `Playback/ScoreToMidiCompiler.cs` (+`SustainResolver`); ties -> `TabNote.Tied`, then the
compiler; timing jitter -> `Playback/PlaybackEngine.cs`; program, volume and pan after loop or seek -> `ScoreTimeline.ChannelSetup`;
two tracks interfering -> `ChannelAllocator`; loops and repeats -> `PlaybackOrder`; playhead disagreement -> `PlayheadMapper`;
sounding-note queries -> `NoteTimeline`; file beat placement -> `GuitarProImporter.ConvertBar`; audio dropouts and plug-in hangs ->
`Mixing/MixEngine.cs`, `EngineHost.cs` watchdog; recording problems -> `Audio/Recorder.cs`, `InputCapture.cs`; plug-in crash handling ->
`Isolation/RemotePlugin.cs`.

Guarantees (by design, with the tests that cover them where they exist):
- The headless suite (`--selftest`, gated in CI by `.github/workflows/windows-ci.yml`) covers score, timeline, playback, file-format and
  security logic, including a synthetic `.gp` export-import fixture. It does not exercise real devices, real plug-ins or the running UI.
- Untrusted inputs are bounded (files, JSON depth, IPC frames, zip inflate). A malformed peer or file is rejected, not trusted.
- Only the pipes are user-restricted; shared memory uses default ACLs.
- A crashed isolated plug-in cannot take down the engine. An unisolated one can, and the UI recovers by restarting the engine without it.
- Project data on disk is written atomically. Recordings are chunked and finalised. Partial renders are deleted.

Known limits:
- The watchdog only attributes hangs that have a breadcrumb. Other hangs end at the 4 s heartbeat check, and only while playing.
- No measured real-time guarantees (callback p99, allocations) exist. CPU and latency figures are not benchmarked here.
- The plug-in state limit is 16 MiB, unified across paths. Plug-in isolation is not a sandbox.
- Sidechain works only for VST2 plug-ins with four or more inputs. VST3 and isolated plug-ins have no MIDI output. Render has no delay
  compensation for sidechain or buses. Vendor drum maps are unverified.
- Rendering, device, DPI and native-window behaviour are confirmed by the live desktop pass, not by the headless suite.
- Executables are not code-signed yet; see `docs/SBOM.md` and `native/BUILD_PROVENANCE.md` for supply-chain records.

## Dependency rules

The rules are in section 1: which folder may use which, and `Models` and `Playback` never reference WPF. The architecture
self-tests (`--areas architecture`) check the boundaries.
