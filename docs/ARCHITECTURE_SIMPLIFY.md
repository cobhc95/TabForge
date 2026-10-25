# Architecture simplification proposal (task-10, no code changes)

Date: 2026-10-19. Owner: arch-tidy. Status: proposal only — nothing moved, renamed, or edited.
Reads: `Source/docs/ARCHITECTURE.md` (357 lines), `Source/docs/ARCHITECTURE_TIDY.md` (task-5 report),
`Source/src/TabForge.Avalonia/AREA-MAP.md` (53 lines), `Source/docs/COMMANDS.md` (271 ids / 62 files),
4 src csproj + test csproj, `ArchitectureBudget.json`, task-9 `Docs/Archive/Source-reports/DEPENDENCY_LEAN_REPORT.md`.
Live counts: Core 578 / Avalonia 479 / AudioEngine 66 / Contracts 38 (total 1161).

## 1. Module map v2 (merge / split / rename — all rename-only, zero behavior change)

Rule of thumb: no new abstractions; only move files into folders that already exist,
rename misleading names, and split flat folders. Every item below is a file move or rename.

### A. Core — drain the tiny misleading folders (all moves within Core)

| Now | v2 | Why |
|---|---|---|
| `Core/Views/EffectEditors/OrnamentEdits.cs` (1 file) | \u2192 `Core/Score/Editing/` | Only "Views" file in the UI-free project; it is score-edit logic |
| `Core/Controllers/SelectionLoopController.cs`, `SelectionSync.cs` (2) | \u2192 `Core/Documents/` | Both are document-operation state; Documents/ already owns that |
| `Core/Diagnostics/` (2) | \u2192 `Core/Services/Log/` | Trace already lives there; one log home |
| `Core/Shell/ChromeHitRules.cs`, `NativeHitCodes.cs`, `TabDragRules.cs` (3) | \u2192 `Core/Platform/Chrome/` | Pure rules with no UI types — they are ports, file them as ports |
| `Core/Lyrics/LyricsController.cs` (1) | keep, document | Legit boundary: logic in Core, pane in Avalonia — add one README line saying so |
| `Core/Services/` (159 files — the junk drawer) | promote two subfolders to top level: `Core/Export/` (MusicXmlExportService, GuitarProExporter, MidiExportService, AudioDataFile) and `Core/Video/` (Services/Video/* mirrors Avalonia/Video/) | 159 files is the largest folder in the repo; export + video are the two clean seams (both already subfolders) |
| `Core/Presenters/` (104 files, flat) | three subfolders: `Presenters/Dialogs/`, `Presenters/Panels/`, `Presenters/Notices/` | Flat 104-file folder is the worst navigation hotspot in Core; grouping is mechanical (name sort) |
| `Core/Menus/` (7) + `Core/Docking/` (10) | keep | Right-sized; central-table homes per ARCHITECTURE \u00a713 |

### B. Avalonia — fix the top-level sprawl (100 files at root, 86 of them MainWindow*)

| Now | v2 | Why |
|---|---|---|
| `Autosave/MainWindowAutosave.cs` + `Autosave/MainWindowAutosaveHost.cs` | rename \u2192 `Autosave/AutosaveController.cs` + `Autosave/AutosaveHost.cs` | They are **not partials** (standalone `sealed class` + `IHost` impl); the MainWindow prefix lies about what they are |
| `Video/MainWindowLiveVideo.cs` | rename \u2192 `Video/LiveVideoWiring.cs` | Same: standalone `sealed class`, owns Record-video wiring, not a window partial |
| `Playback/` (3 files) vs `Transport/` (8 files) | merge \u2192 `Transport/` with `Transport/Engine/` subfolder for the 3 process-wide files | AREA-MAP lines 45–47 admit the pair is "most often confused"; one folder ends the confusion (READMEs merge too) |
| `MainWindowEdit.cs` (**68** of 271 command ids — 25% of all commands in one file) | split Commands arrays per area (\u2192 `MainWindowEdit.{Text,Cells,Notes}.cs` partials) | Largest command concentration; every edit-area search lands here first |
| 25 single-command files (e.g. Tuner, GlobalTuning, ScaleFinder, SongStats) | fold into sibling area files (tuning trio \u2192 one `MainWindowInstrumentTools.cs`) | 25 files each holding one id inflate the 100-file root listing |
| `Views/` (166), `Platform/` (126) | keep | Right-sized for a UI project; AREA-MAP already indexes them |
| `SelfTests/` (45, inside the shipped exe) | keep, document | Headless suite must ship in-app (\u200b--selftest\u200b modes); add one Compose.cs comment pointing at it |

### C. Engine + Contracts — file the strays

| Now | v2 | Why |
|---|---|---|
| AudioEngine root (8 files) incl. 965-line `EngineSession.cs` | move session/chain files into existing `Mixing/`, `Audio/`, `Output/` | Root should hold only `EngineHost/Threads/Render`; subfolders already exist |
| Contracts root (23 files, flat, zero deps — correctly) | subfolders `Contracts/Messages/`, `Contracts/Render/`, `Contracts/Shared/` | Matches \u00a713's own grouping (EngineMessages / EngineMessageLists / SharedBlock); pure moves |

### D. Cross-cutting (owned elsewhere — referenced, not duplicated)

- PDFsharp licence orphan + release-gate entry (task-9 \u00a72/\u00a79.1) — delete file + gate line together with the hygiene-test list.
- `Directory.Build.props` central versions (task-9 \u00a78) — ends NAudio \u00d72 / Avalonia \u00d75 copy-paste.
- ParityRecorder lockfile+NuGetAudit back on (task-9 \u00a79.3); AudioEngine redundant TWAE re-declarations \u2192 inherit + comment (task-9 \u00a79.4).

## 2. Onboarding path (5 files, in order — ~45 minutes to oriented)

1. `Source/src/TabForge.Core/README.md` — the one rule everything else hangs off (Core never references the app; ports in `Platform/`). 2 min.
2. `Source/docs/ARCHITECTURE.md` \u00a71 + \u00a75 — projects/dependencies table, then the state-ownership table (one owner per state). 15 min.
3. `Source/src/TabForge.Avalonia/AREA-MAP.md` — which files own each area + the one test class per area. 5 min.
4. `Source/src/TabForge.Avalonia/MainWindow.Compose.cs` (461 lines) — the composition root; every \u200bMainWindow*\u200b partial registers here. 15 min.
5. `Source/src/TabForge.Avalonia/Transport/README.md` **then** `Playback/README.md` (counted as one step — the pair AREA-MAP says to read before moving anything). 10 min.

After these five: `COMMANDS.md` (generated index, never read cover-to-cover) + the folder READMEs under `Views/`, `Platform/`, `Transport/`.

## 3. Ten layering rules (one line each; \u00a71–4 + \u00a76–7 + \u00a710 already self-tested)

1. **Core imports no UI framework.** No `using Avalonia` / `Avalonia.(Controls|Media|Input|…)` in Core — verified 0 hits (task-5); self-test `--areas architecture` enforces.
2. **Core never references the app.** Only outward allowance is `InternalsVisibleTo` (Core \u2192 Avalonia/Tests), which is intentional and documented.
3. **The app reaches Core only through ports + presenters.** UI code uses `Core/Platform/` abstractions and shows `Core/Presenters/` state; views keep layout + wiring only (Compose.cs is the composition root).
4. **The engine references Contracts only** — one ProjectReference; Core mentions in engine docs are prose pointers (trust lives in Core), never code refs.
5. **Contracts reference nothing.** Zero PackageReferences by design; net8.0/net8.0-windows split stays Inside the contracts project (per-TFM Compile Remove).
6. **State transitions live in Core document flows; the window only drives.** `DocumentEdits/DocumentSaveFlow/DocumentCloseFlow/DocumentPlacement` take an explicit `SongDocument` — no ambient "current document" (\u00a75).
7. **The playback timeline is authoritative; visuals are read-only consumers** (\u00a71, \u00a75, \u00a710). No animation/logic that leads the sound.
8. **External formats convert only at the boundary.** Import/export through `AlphaTabBoundary` / exporter services; bounded reads, capped inflate (\u00a76). No format logic in views or the engine.
9. **Audio-callback rules are review rules.** No alloc/locks/IO/disposal on the callback path; breadcrumb before every plug-in call; retire via `RetireQueue`, never inline (\u00a74) — nothing in the repo measures them, so reviewers check them.
10. **One owner per state; processes exchange commands, never shared mutables** (\u00a73 + \u00a75 table). Session ids name pipes/blocks; approvals carry hashes, not paths (PluginTrust).

## 4. Simplification checklist (God-file folds first — proposals, none applied)

Split along method groups already visible in each file (outlines verified 2026-10-19):

- [ ] **EngineSession.cs (965 lines, 58 methods)** \u2192 5 partials: \u200b.Chains\u200b (CommitChain/SetTrackMix/SetPluginGain/Bypass/SetSynth/SetMidiRoute/SetPluginWiring/SetMidiProcessors/SetGraph/SetClips/StoreClips/SetArm), \u200b.Recording\u200b (Record/StartRecording/StopRecording/ReportTakes), \u200b.Plugins\u200b (LoadChain/BuildChain/Create/RetirePlugin/OpenEditor/CloseEditor/SendPrograms/SetProgram/SetState/SendStates), \u200b.Devices\u200b (ReconfigureLive/ASIO scratch/FollowWindowsVolume/gains/StopOutput), \u200b.Graph\u200b (ApplyGraph/ApplyMidiProcs/ApplyRoute/ApplyClips/ApplyArms). Biggest win in the repo.
- [ ] **AudioEngineClient.cs (623) + .Sync.cs (490) ≈ 1113 logical lines** \u2192 add \u200b.Lifecycle\u200b (Start/ConnectAsync/CheckLiveness/KillQuietly/Stop/WaitForExitOrKill/OnEngineEnded/Cleanup/Dispose) + \u200b.Meters\u200b (PeakOf/InputPeakOf/RefreshClips/OnMediaResolved/OnPluginEdited) partials; leave Send/ReadEvents in place.
- [ ] **MusicXmlExportService.cs (864, 30 methods)** \u2192 \u200b.Parts\u200b (WritePart/WriteVoice/WriteNote/WriteTechniques/LinkNote/Bend/Release/Harmony), \u200b.Drums\u200b (IsDrums/DrumMidi/DrumName/DrumDisplay), \u200b.Layout\u200b (WriteBackup/WriteTempo/WriteClef/WriteTabClef/WriteWholeBarRest/Pitch/TypeName/Navigation).
- [ ] **GuitarProExporter.cs (710, 23 methods)** \u2192 \u200b.Model\u200b (Build/BeatsFor/BeatFor/DurationOf/RestBeat/FillRests/Clef/Tuning/Transpose/Finger) + \u200b.Container\u200b (Save/ToBytes/EmbeddedProjectBytes/ZipPart/ReadZip/HasEmbeddedEntry/TryReadEmbedded/RejectedNotice); AddTempoAutomation/GpDirections stay.
- [ ] **FullDemoSongFactory.Helpers (1033)** — fixture data; extract tables to a data file/partial, keep builders. No logic split needed.
- [ ] **MainWindowEdit.cs (68 command ids)** — split per \u00a71B; pairs with the 25-file single-command fold.
- [ ] **FilePathPolicy.cs (577 > budget 543)** — the one file over its ArchitectureBudget ceiling; split policy groups or raise ceiling deliberately (budget re-baseline, \u00a71D context).
- [ ] **Budget re-baseline**: most ceilings now have headroom (actuals fell below); shrink them in the same pass so the budget bites again. Owned as a docs-only follow-up.
- [ ] **No \u200b.cs edits, moves, or deletions performed in this round** — report only. Suggested order: renames (\u00a71A/B, trivial) → God splits (\u00a74) → budget re-baseline → cross-cutting D-items with their owners.
