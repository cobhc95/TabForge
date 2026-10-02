# Architecture

TabForge is a notation-first composition workstation (C# / .NET 8 / WPF, Windows x64) with a real audio path:
built-in General MIDI synth, VST2/VST3 plug-in hosting, audio recording, clips, mixing and offline render.
It is three projects and up to three kinds of process. Read this before changing anything that crosses a
process, thread or file-format boundary; the rules below are not optional.

Status of the statements: "by design" means the code is written to that rule and reviewed against it; it is not
proven by a benchmark or an allocation test unless a self-test is named. Section 12 lists known limits.

## 1. Projects and dependencies

| Project | Contents | Depends on |
| --- | --- | --- |
| `src/TabForge` | WPF application: score model, editor, playback compiler, MIDI scheduler, documents, settings, render orchestration, engine client | TabForge.AlphaTab 1.8.4-tabforge.3 (alphaTab 1.8.4 plus three patches: a per-import .gp3/.gp4/.gp5 bar limit, exact gpif mixer volume/balance and the gpif trill speed; vendor/alphatab; score reading and .gp writing); references the two projects below (so NAudio, MeltySynth and SoundTouch.Net flow in transitively) |
| `src/TabForge.Audio.Contracts` | The wire contracts shared by both sides: `EngineProtocol` (commands/events, framing, records), `RenderProtocol`, `SharedBlock` (shared-memory layout). No dependencies. | none |
| `src/TabForge.AudioEngine` | Audio device output, mixer, track chains, plug-in hosting (VST2/VST3), MIDI processors, recorder, clip player, GM synth, offline renderer, isolated plug-in host, native bridge | NAudio 2.2.1 (WASAPI/ASIO/DirectSound, WAV), MeltySynth 2.4.1 (GM synth), SoundTouch.Net 2.3.2 (clip pitch/speed), native `tfvst3.dll` |

Native component: `native/tfvst3` is a C++ VST3 host bridge built against the Steinberg VST3 SDK, which is pinned by
`native/fetch-vst3sdk.ps1` (tag and commit). `native/build-tfvst3.ps1` writes `native/BUILD_PROVENANCE.md` (SDK
commit, compiler, SHA-256 of the DLL); `tools/Package-Release.ps1` checks the shipped DLL against it. Exact versions and
hashes of every managed package and native component are in `docs/SBOM.md` (regenerate with `tools/Write-Sbom.ps1`).
Restore is locked (`packages.lock.json`, `--locked-mode`).

Dependency direction inside `src/TabForge` (unchanged and still enforced):

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

Rules: `Models` and `Playback` do not reference WPF. The playback timeline is authoritative; visualisation reads it
and adds no animation that lags the sound. External formats are converted only at the import/export boundary. The
scheduler owns the compiled timeline; the UI observes it (`PlaybackEngine.TimelineChanged`).

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

- **UI process.** Owns the song, documents, undo, settings and the desired audio configuration. Never runs plug-ins
  and never sits on the audio callback path.
- **Audio engine process.** Started by `AudioEngineClient` as `TabForge.exe --audio-engine <session> <parentPid>`
  (`Program.Main` dispatches before any WPF object exists). Exits when the parent dies. The session id is 8-64
  alphanumeric characters and names the pipe and the shared block. If it dies, the client restarts it (a plug-in blamed by the
  breadcrumb is quarantined so the restart does not load it again).
- **Plug-in host process (optional).** `--plugin-host` runs one plug-in for `RemotePlugin`. Also short-lived helper
  modes: `--plugin-info` (plug-in metadata probe), `--probe-gm`.
- Headless modes (`--selftest`, `--playtest`, `--audit`, `--render`, ... in `Diagnostics/DiagnosticCommands.cs`)
  run in the UI executable without a main window and return exit codes 0 / 1 / 2.

## 3. IPC and limits

- **Control channel:** one duplex named pipe per engine session, `TabForge.AudioEngine.<session>`, created with
  `PipeOptions.CurrentUserOnly | Asynchronous`. Framing is `[int32 length][byte type][payload]` (`Frames`), payloads are
  `BinaryWriter` primitives, every read is bounded. **Frame limit: 32 MiB** (`Frames.MaxFrameBytes`); a bad length is
  a protocol error. Strings and collections read from the peer have explicit bounds (`ReadBoundedString`, ...).
- **Plug-in host control pipe:** `TabForge.PluginHost.<guid>`, also `CurrentUserOnly`.
- **MIDI ring (shared memory):** `SharedBlock` `TabForge.AudioEngine.<session>.shm`: lock-free single-producer /
  single-consumer ring of 16-byte `TimedMidi` (capacity 8192 messages, power of two); the UI writes, the engine's audio
  thread reads. Timestamps are `Stopwatch` (QPC) ticks, the same clock in both processes; a message stamped for
  t is heard at t + `MixEngine.DelayTicks` and the UI delays its winmm output by the same amount. The block also
  holds the crash breadcrumb (see 8), latency/CPU status and 256 x 2 meter slots. Layout is fixed and versioned by the
  code on both sides (same executable).
- **Plug-in host audio path:** `PluginHostBlock` (`Local\TabForge.PluginHost.<guid>`): block audio in/out, up to 512 MIDI
  events per block, transport; a request/done pair of events with a generation number, so a late answer is never taken for
  the current block. A block that is not answered within the callback budget marks that plug-in lost for the session.
- **Slots:** 256 track slots, 32 group buses, one master (`MixEngine.MaxSlots/MaxBuses/MasterSlot`).
- **Plug-in state:** at most 16 MiB raw (`PluginStateLimits.MaxRawBytes`, 4/3 as base64) per plug-in state, one plug-in per
  frame. The limit is unified: `PluginStateLimits` (`Audio.Contracts/EngineProtocol.cs`) is the one contract used by the
  validator, chain/preset load, live SetState, the native getters and the transfer.
- **File limits (`Services/InputLimits.cs`):** `.tforge` and score files 128 MiB, settings JSON 2 MiB, JSON depth 64, at most
  256 tracks, 20,000 bars per track, 100,000 bars and 2,000,000 cells per project, `.tfaudio` 64 MiB. Embedded `.gp`
  project entries are inflated with a hard cap.
- Shared-memory sections use default security descriptors; only the pipes are `CurrentUserOnly`. The IPC is a same-user,
  same-machine transport, not an authentication boundary.

## 4. Real-time rules (audio callback)

The callback is `MixEngine.Read` (NAudio `ISampleProvider`) on the driver's thread (for ASIO the driver's real-time
thread); `Recorder.Enqueue` and `InputCapture` block delivery run on capture threads with the same restrictions.

On these threads, **by design**:
1. No allocation after construction: buffers, pending-event arrays, render graph and chains are preallocated and swapped
   in whole (`Volatile.Write`), never mutated in place.
2. No locks, no blocking waits, no I/O (no disk, no pipe writes, no logging that touches a file, no `Task`/`await`).
3. No disposal. An object the callback may still use (replaced chain, plug-in, clip player) is unpublished on the engine's
   main thread and disposed there by `RetireQueue.Collect` once the callback epoch (`CallbackEpoch`, odd while a
   callback runs) has moved past it. Never on a fixed delay.
4. Communication into the callback is lock-free: the MIDI ring (SPSC), `Volatile`/`Interlocked` published references,
   `Interlocked` counters. Communication out is the same (meters, heartbeat, latency).
5. Plug-in calls are the exception that cannot be made real-time safe: a plug-in's `process` runs on the audio thread
   and is native code. Every plug-in call sets a breadcrumb first so a hang can be attributed (section 8). Isolated
   plug-ins are called through `PluginHostLink` with a time budget.
6. The callback publishes a heartbeat timestamp each time it finishes; the watchdog reads it (section 8).

Nothing in the repository proves rules 1-2 by measurement (no allocated-bytes-per-callback or p99 callback test); treat
that as an open verification item, not a guarantee.

UI side: no per-frame or layout-driven work for playback visuals; the UI never controls musical time.

## 5. State ownership

| State | Owner | Notes |
| --- | --- | --- |
| Song / tracks / bars / notes | UI process, `DocumentSession.Project` | Edits end in `CommitEdit(EditRefresh...)`; undo is a bounded chain of `ProjectState`s (JSON song/track headers plus one binary chunk per bar, unchanged chunks shared between levels; `UndoController`, `UndoHistory`) |
| Compiled timeline | `PlaybackEngine` | Rebuilt from the project; UI observes `TimelineChanged` |
| Audio configuration, chain *requests* (plug-in list, state, order, routing) | UI process (project + settings) | Sent as commands; the UI re-sends the full desired state after an engine restart |
| Live plug-in instances, chains, routing graph | Engine process | Keyed by slot and stable `PluginSpec.Id`; a chain change keeps the live instance with the same id |
| Plug-in internal state | The plug-in | Read back with `GetStates` when saving; a failed getter is reported per plug-in and never replaced by an older stored state |
| Isolated plug-in instance | Plug-in host process | The engine holds a `RemotePlugin` proxy |
| Recorded takes / clips | Files on disk; `AudioClip`/`ClipLane` metadata in the project | |
| Settings | `AppSettingsStore.Shared` (created by `App`), persisted to `%APPDATA%\TabForge\settings.json` | One `AppSettings` object for all windows (R-09): windows read and write it through the store, which raises `Changed`, saves debounced (400 ms) and atomically, flushes on exit and holds the load-failure state (an unreadable file is not overwritten until Preferences are applied). Normalised and clamped on load; never wiped by rebuilds |
| The audio engine | `AudioEngineClient.Instance`, owned by the active document | See "Engine ownership" below |
| Crash recovery copies | `%LOCALAPPDATA%\TabForge\Recovery` | Written by the last-resort handler in `App.xaml.cs` (bounded wait for the UI thread) |

Rule: one owner per piece of state; other processes hold requests or copies, never a second source of truth.

**Engine ownership (R-10, option a).** The engine is app-wide and has one explicit owner: the document whose window last synced it
(`AudioRouting.Apply(..., owner: DocumentSession)`, i.e. the active tab of the focused window). Other documents play through Windows
MIDI or are silent in the engine; there is no multi-document mixing. Switching documents does not unload anything: the previous owner's
track chains are *parked* (still loaded with their editors, level 0, no clips, not armed) on their own slots, so the documents never
share a slot (slots are keyed by track and record their owning document). Switching back within the warm period
(`AudioEngineClient.DefaultWarmIdleMinutes`, 5 minutes, `WarmIdle`) re-activates them without a LoadChain. Group buses and the master
use fixed slots every document shares, so they are never parked (they reload on an owner switch). Parked chains are removed after the
warm period; a `Sync` without engine tracks keeps the engine running (warm, like a recording host keeps its device open) and it stops after the
warm period or at app exit. Headless probes set `WarmIdle = 0` for the old stop-at-once behaviour.

**Window lifetime (R1).** A main window attaches to objects that outlive it (static events such as `MediaAccess.Changed` and
`PlaybackEngine.LoopCompleted`, the shared `AudioEngineClient` and `AppSettingsStore`, the media-provider registration, the recorder's
engine and MIDI-input hooks). Every such attachment is made through `MainWindow.Subscribe` / `_lifetime.Add` (`OwnedSubscriptions`), which
records the detach next to the attach; `ReleaseWindowResources` runs them once from `Closed`. `Closing` only decides (a cancelled close
changes nothing). Work queued for a window by another thread or object goes through `PostIfOpen`, which drops it once the window has closed.
The Settings dialogs call back into the window that opened them (`SettingsWindowActions` through `WpfSettingsWindowHost`, the discard
question through the owner chain), never through static actions. A document owns its own song clock (`DocumentPlaybackState.Clock`), so a tab
moved to another window keeps playing and reporting without the old window being kept alive. App-wide caches hold songs and windows
weakly. The `window-lifetime` self-test group builds, uses and closes real `MainWindow`s and asserts all of this.

**Document media context (R2).** Everything that reads a song's linked audio is given that song's `MediaContext` (`DocumentSession.Media`)
explicitly: `MediaAccess.Evaluate/Approve/Revoke`, `WaveformCache.Get/StatusOf/LengthOf/Cancel`, `AudioEngineClient.Sync` /
`AudioRouting.Apply` (with the document's skipped plug-ins), the drop session, the timeline and the render request. There are no ambient
providers and no "current document". The context holds the identity (a run-time session id, never stored in a file), the **media base
directory** (the saved file's folder, else the folder an imported song came from; separate from the save path), the **approval scope**
(a saved song: its canonical path, stored in settings as before; an unsaved song: its own session, memory only; approvals stored with an
empty path match nothing) and a **revision** that changes on Save As and on close. Save As does not copy approvals to the new path; the
one transfer is an unsaved song's own session approvals to the path it is first saved to. The waveform cache keeps one slot per document
and file (permission, state and cancellation are the document's) and shares the decoded peaks by canonical file only after the asking
document's own permission was checked; the permission is re-checked by the worker before it opens anything and after the read, and on
every draw from the verdict the worker obtained (`MediaAccess.Decide`: memory only, so a link into an unreachable share can never stall
the UI thread or the cache lock). A read for a closed document or an older revision is dropped, and a closed document's slots are evicted.
An approval change re-judges the clips of every live engine slot with its own document's context (`AudioEngineClient.RefreshClips`,
which waits while a render runs). Window close asks about every dirty document before doing anything. Preferences merges the approvals
with the live list instead of overwriting them (`MediaApprovalMerge`). The `document-context` self-test group covers the matrix.

**Document operations (R3).** The model half of an edit, a save, a close and an open is a function of an explicit `DocumentSession`;
`MainWindow` keeps dialogs, input gating, status text and the visual refresh. `DocumentEdits.Run` is one logical edit: one undo
transaction (nothing stored when nothing changed), one dirty change, one timeline invalidation (`SongProject.BeginTimelineBatch` makes
the model's own marks and the edit's mark one), whichever entry point started it; `ArrangementController` / `TrackController` expose the
bar, section and track edits for a document and Undo / Redo restore through the same service. `DocumentSaveFlow` is the save and
export sequence (questions only when something would be lost, plug-in states awaited, the write), `DocumentCloseFlow` the close plan
(every unsaved tab is asked before anything happens) and save-all, `DocumentPlacement` where an opened song goes. The tab an open
replaces is chosen when the open starts, so a completion never retargets whichever tab is displayed. The `document-operations`
self-test group runs these without a window and checks keyboard / menu / context-menu parity on real windows; the architecture
self-test checks the boundary (no WPF, view or window references; no static "current document"; the window's save sequence takes
its document).

## 6. Persistence formats

- **`.tforge`** (`ProjectService`): the complete project as UTF-8 **JSON** (`SongProject.FormatVersion` = 2), written atomically
  (temp file + replace) with a size-checked, hashed stream. On load: bounded read, JSON shape and depth validation, then
  `ProjectValidator`. Clean-`.gp` embedding uses the same JSON, gzip-compressed; undo keeps per-bar states (`ProjectState`);
  the `.tforge` file itself is not gzip.
- **`.gp` written by TabForge** (`GuitarProExporter.Save`): a .gp file written by alphaTab plus one extra zip entry,
  `TabForge/project.tforge.gz` (gzip of the project JSON). Other readers ignore the entry; TabForge reads it back for a
  lossless round trip (`TryReadEmbedded`, capped inflate). `embedProject:false` writes a clean file with nothing TabForge-specific.
- **Clean `.gp` + `.tfaudio` sidecar** (`AudioDataFile`): `<song>.gp` stays clean and audio data (per-track sound source, plug-in rig
  and state, mixer, clips and lanes) lives next to it in `<song>.tfaudio`, JSON, `FormatVersion` 1, at most 64 MiB. An invalid
  sidecar is ignored entirely, never half-applied.
- **Import:** .gp3-7 through alphaTab (`GuitarProImporter`, bounded read); a `.gp` with an embedded project loads that project.
  The alphaTab dependency is reached through one boundary, `AlphaTabBoundary`: it checks that the reader component is the patched
  `TabForge.AlphaTab` build (name, version, the per-import bar limit, the percussion table) and sets that import's bar limit
  (`InputLimits.MaxMeasuresPerTrack`) on its own `Settings` object; there is no global parser state and no private reflection on
  the import path (the one remaining private access only words an error message after a failed read). Cumulative track, bar, cell and
  note limits live in `GuitarProImportBudget`, memory/time limits and cancellation in `ImportGuard` and `ImportWorker`.
- **Export:** .gp (`GuitarProExporter`), Standard MIDI (`MidiExportService`), ASCII tab, WAV/MP3 render.
- **Settings/presets:** JSON, bounded and clamped.

## 7. Recording pipeline

```
input device (WASAPI, or ASIO driver callback via InputCapture.Feed)
   |  capture callback (real-time rules apply)
   +--> InputCapture ring (lock-free, ~0.7 s) --> InputBlock (inputs 1+2 at engine rate, filled once per mixer block,
   |                                              shared read-only by every armed chain for monitoring through its chain)
   +--> Recorder.Enqueue: copy into a preallocated lock-free SPSC queue (~4 s of stereo)
                              |
                       dedicated disk thread --> one 32-bit float WAV per armed track (chunked writes)
```

If the disk falls behind and the queue is full, the missing frames are written as silence so the take stays in time,
and the loss is reported once. Disk-full or a removed drive stops recording cleanly, keeps finalised files and shows
"Recording problem". A failed start disposes per-writer files and releases the input device. Capture opens only
while a track is armed.

## 8. Plug-in lifetime, isolation and the watchdog

- **Loading** is on the engine's main thread (`LoadChain`), keyed by `PluginSpec.Id`; a quarantined plug-in
  (`Skip`) is not loaded at all. VST2 (`Vst2Plugin`) and VST3 (native `tfvst3.dll` bridge) implement `IPluginInstance`.
- **In-process** (default): the plug-in runs inside the engine process. A native crash kills the engine; the UI restarts it
  with the breadcrumb-blamed plug-in switched off.
- **Separate process per plug-in** (`RemotePlugin` / `--plugin-host`): only that plug-in's process dies on a crash or hang; effects pass the
  sound through, instruments go silent, `Crashed` is raised once on the engine's main thread, and the plug-in is lost for the session.
  **This is crash isolation, not a security sandbox.** The plug-in process runs as the same user with the same rights as TabForge
  and can do anything TabForge can. Load only plug-ins you trust; this mode contains crashes and hangs, nothing more.
- **Plug-in trust boundary** (`Plugins/PluginTrust.cs`): songs, embedded/sidecar rigs, presets and auto-load chains only *name* plug-in
  paths. `AudioEngineClient.Sync` builds specs through `PluginTrust.BuildSpecs`; a path is loaded only if it is in the user's scan
  (this session's scan or the remembered list), inside a scanned folder on a local fixed drive, or in `PluginSettings.ApprovedPluginPaths`
  (full normalised path, case-insensitive). UNC and removable/optical locations always need explicit approval. **An approval covers this
  binary, not the path, folder or publisher** (Program Files / Common Files location trust excepted): the SHA-256, size, time and signing
  key are recorded, `PluginSpec.ExpectedSha256` carries the hash to the engine, and `PluginIdentity.Hold` (engine main thread, never the
  audio thread) hashes the exact file before every real load through a deny-write handle held until the plug-in is created; a mismatch is
  `PluginLoadStatus.BlockedChanged` and needs a new approval. Routine UI checks compare size and last-write time and hash only on a difference
  (cached). A new build signed with the same key (SHA-256 of the certificate's public key, WinVerifyTrust-validated) is accepted as a
  publisher update; the subject text is display only. Everything else is sent as
  `Skip` (like a quarantined plug-in), so no native code runs before the user decides: the main window shows a "Review…" bar and the FX
  window shows "Blocked: not approved" with an Allow button. Adding a plug-in from the FX window's browser approves it.
- **Breadcrumb** (`SharedBlock`): before a plug-in call the engine records slot, index and plug-in path in shared memory so the UI
  can read it after the engine dies. Offline render has one breadcrumb per worker thread.
- **Watchdog** (`EngineHost`), two independent checks:
  1. Main-thread watchdog: the engine main thread has been inside one queued command for over 10 s **and** a breadcrumb names a
     plug-in call -> `Environment.Exit(70)`; the UI blames that plug-in and restarts without it. A hang with no breadcrumb is
     not attributed (and this check does nothing for it); any path that calls into a plug-in without setting the breadcrumb is not covered.
  2. Audio-thread heartbeat: while playing, no completed callback for over 4 s -> exit 70.
  Coverage is therefore breadcrumb-dependent. Verify a new plug-in call site sets the breadcrumb before relying on the watchdog.
- **Retirement:** replaced or removed plug-ins go through `RetireQueue` (section 4) and are disposed at shutdown as well.
- **Shutdown:** each step is guarded so one failure cannot skip ASIO release; the isolated host process is killed on dispose.

## 9. Render pipeline

`File > Render` (`Rendering/RenderJob`, `RenderSpecBuilder`): the UI forces engine routing for the render, builds a render
spec (`RenderProtocol`), and the engine's `OfflineRenderer` runs faster than real time. Chains are independent offline, so
worker threads render whole chains ahead into bounded per-chain block queues (a chain always stays on one worker; one
breadcrumb per worker); one mixer thread pops in lockstep, sums the master in a fixed order (bit-identical whatever the thread
count), writes master and stems in the same pass and reports progress. Plug-in latency is compensated per chain (fed
L frames ahead, first L output frames dropped) so stems line up. MP3 is encoded from a temporary WAV through Media
Foundation (initialisation is locked). Cleanup order: routing is restored first, then temp files are removed; a cancelled or failed render
deletes partial output. Names that Windows treats as devices (CON, NUL, ...) are rejected.
Not covered: delay compensation for sidechain and buses.

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

Events are absolute milliseconds from per-bar tempo (`MusicTime.BarMs`); onsets follow a duration cursor, not the raw 16-slot
index, so tuplets and dotted values do not drift. The scheduler reads a monotonic clock and dispatches, so playback is
deterministic for the same score/settings/start. `PlaybackEngine` raises about 60 Hz position callbacks; `MainWindow` coalesces them
onto a `DispatcherTimer`.

## 11. Subsystems and their files

| Subsystem | Files | Responsibility |
| --- | --- | --- |
| Score model | `Models/SongProject.cs`, `TechniqueNames.cs`, `TripletFeels.cs`, `NotationEnums.cs` | Song/track/measure/cell/note data; persisted technique ids (use the constants) |
| Section bounds | `Services/SectionLayout.cs` | where a section starts/ends; used by timeline, copy/delete/move/loop |
| Bar-range edits | `Services/BarRangeEditor.cs` | all-track copy/remove/insert/move of bar ranges |
| Musical time | `Services/MusicTime.cs` | slots, durations, beats, bar length, arrangement X-axis |
| Playback | `Playback/*` | options, timeline, compiler, sustain, order, channels, playhead, note queries, scheduler, diagnostics |
| Import/export | `Services/GuitarProImporter.cs`, `GuitarProExporter.cs`, `MidiExportService.cs`, `ProjectService.cs`, `AudioDataFile.cs` | formats (section 6) |
| Input limits | `Services/InputLimits.cs`, `FilePathPolicy.cs`, `ProjectValidator` | bounded reads, path checks, validation |
| Documents | `Documents/*` | session, controller, undo history, tab transfer, playback-on-switch policy |
| Notation / fretboard / arrangement views | `Views/TabEditorControl.cs`, `StaffNotationRenderer.cs`, `Visualization/*`, `Views/ArrangementPanel.cs` | editing and read-only views of the timeline |
| Tab/score editor (partials) | `Views/TabEditorControl.cs` (state, properties) + `.Geometry`, `.Playback`, `.Navigation`, `.Editing`, `.Selection`, `.Keyboard`, `.Mouse`, `.Accessibility` (incl. `EditorPeer`), `.Layout`, `.Rendering` (OnRender, drawing helpers), `.Marks` (technique engraving); `Views/EditorEvents.cs` (event args) | one owner per file; members moved verbatim along the old section comments (Audit 4) |
| Arrangement panel (partials) | `Views/ArrangementPanel.cs` (fields, constructor, header) + `.RowGeometry`, `.Timeline` (zoom, playhead, selection, area move), `.Interaction` (track-row drag, rename), `.TrackRows`, `.Groups`, `.TrackColumns`; `Views/ArrangementFollowGeometry.cs`, `SectionDragOverlay.cs`, `SectionInsertionIndicator.cs` | bottom arrangement overview and its track controls |
| Arrangement timeline (partials) | `Views/TrackTimeline.cs` (state, geometry, activity cache) + `.Render` (ruler, sections, rows, overlays, playhead), `.Interaction` (section hits/resize/drag, mouse), `.Clips` (clip lanes), `.MediaDrop` (audio / MIDI file drops: plan + preview; `Services/MediaDrop`, `Services/MidiFileImport`, `Views/MediaDropSession`, `Views/VirtualFileDrop`, ghost `Views/MediaDropGhost`), `.SongTime` | custom-drawn timeline surface |
| MIDI compiler (partials) | `Playback/ScoreToMidiCompiler.cs` (build, bars/beats, swing, mix, event add) + `.Techniques` (note emission, dead/ghost/palm mute, grace, slides, trills, tremolo, bends, vibrato, whammy, fade-in), `.Clips` (recorded MIDI clips), `.Metronome` (channel setup, count-in, click) | score -> absolute-time MIDI timeline |
| UI host | `MainWindow.xaml(.cs)` + `MainWindow.<Area>.cs` partials | composition root; model edits end in `CommitEdit`; no musical logic |
| Audio client | `Audio/AudioEngineClient.cs`, `AudioRouting.cs`, `AudioDevices.cs`, `RoutedMidiOutput.cs`, `SongClock.cs`, `WaveformCache.cs` | engine lifecycle, routing, device lists |
| Plug-in catalog/rig | `Plugins/*` | scan (`VstScannerService`, isolated probe), rig per track, drum maps, MIDI processor catalog |
| Rendering | `Rendering/RenderJob.cs`, `RenderSpecBuilder.cs` | File > Render orchestration |
| Contracts | `TabForge.Audio.Contracts/*` | protocol and shared-memory layout |
| Engine | `TabForge.AudioEngine/EngineHost.cs`, `EngineThreads.cs`, `EngineRender.cs`, `Mixing/*`, `Audio/*`, `Plugins/*`, `Isolation/*`, `Midi/*`, `Synth/*`, `Output/*`, `Editors/*` | section 2-9 |
| Window shell | `Views/BrowserTabBar.xaml(.cs)`, `Shell/` | title-bar document tabs, drag policy, Win32 caption hit tests |
| Headless tools | `Diagnostics/DiagnosticCommands.cs` | `--selftest`, `--playtest`, `--audit`, `--render`, ... |
| Debug switch | `Services/Log/Trace.cs` | `TABFORGE_TRACE=area,...` (playback, engine, layout, import, ui, all) -> `Diagnostics/trace-<area>.log`; `TABFORGE_MIDI_LOG`, `TABFORGE_LAYOUT_LOG`, `TABFORGE_CAPTION_LOG` still work |
| Crash safety | `App.xaml.cs` | last-resort handler: crash log, unsaved songs to Recovery |
| Tests | `SelfTest*.cs` | headless deterministic suite (partial class); groups are guarded; skips are reported as SKIP |
| Shortcuts | `TOOLS_AND_HOTKEYS.md` | key map and presets |

## 12. Where to look, guarantees and limits

Where to look: note length/attack -> `Playback/ScoreToMidiCompiler.cs` (+`SustainResolver`); ties -> `TabNote.Tied` then the compiler;
timing jitter -> `Playback/PlaybackEngine.cs`; program/volume/pan after loop/seek -> `ScoreTimeline.ChannelSetup`; two tracks interfering ->
`ChannelAllocator`; loops/repeats -> `PlaybackOrder`; playhead disagreement -> `PlayheadMapper`; sounding-note queries -> `NoteTimeline`;
File beat placement -> `GuitarProImporter.ConvertBar`; audio dropouts, plug-in hangs -> `Mixing/MixEngine.cs`, `EngineHost.cs` watchdog;
recording problems -> `Audio/Recorder.cs`, `InputCapture.cs`; plug-in crash handling -> `Isolation/RemotePlugin.cs`.

Guarantees (by design, with the tests that cover them where they exist):
- The headless suite (`--selftest`, gated in CI by `.github/workflows/windows-ci.yml`) covers the score/timeline/playback/file-format/security
  logic. It includes a synthetic .gp export-import fixture; it does not exercise real devices, real plug-ins, or the running UI.
- Untrusted inputs are bounded (files, JSON depth, IPC frames, zip inflate); a malformed peer or file is rejected, not trusted.
- Only the pipes are user-restricted; shared memory uses default ACLs.
- A crashed isolated plug-in cannot take down the engine; an unisolated one can, and the UI recovers by restarting the engine without it.
- Data on disk is written atomically for projects; recordings are chunked and finalised; partial renders are deleted.

Known limits:
- The watchdog only attributes hangs that have a breadcrumb; other hangs end at the 4 s heartbeat check only while playing.
- No measured real-time guarantees (callback p99, allocations) exist yet; CPU/latency figures are not benchmarked here.
- Plug-in state size limit is 16 MiB, unified across paths (`PluginStateLimits.MaxRawBytes`); plug-in isolation is not a sandbox.
- Sidechain works only for VST2 plug-ins with four or more inputs; VST3 and isolated plug-ins have no MIDI output; render has no delay
  compensation for sidechain or buses; vendor drum maps are unverified.
- Rendering, device, DPI and native-window behaviour are confirmed by the live desktop pass, not by the headless suite.
- Executables are not code-signed yet; see `docs/SBOM.md` and `native/BUILD_PROVENANCE.md` for supply-chain records.

## Dependency rules

The full list of which folders and projects may depend on which, and how each rule is enforced, is being written. Until then the rules in section 1 apply.
