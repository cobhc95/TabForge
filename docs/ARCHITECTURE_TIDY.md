# Architecture tidy — verification report (no behavior changes)

Date: 2026-10-19. Scope: task-5. Read-only audit; this file is the only write.
Sources checked: `Source/docs/ARCHITECTURE.md` (357 lines), `Source/src/TabForge.Avalonia/AREA-MAP.md` (53 lines),
`Source/docs/COMMANDS.md` (284 lines, 271 ids in 62 files), 4 src \u200b.csproj + 1 test csproj, `ArchitectureBudget.json`,
live \u200b.cs counts via glob. Total \u200b.cs under `Source/src`: **1161** (matches stale-code baseline).

## 1. Layout verification (claimed vs actual)

| Claim (ARCHITECTURE.md \u00a71) | Actual (glob, 2026-10-19) | Verdict |
|---|---|---|
| Core: 569 \u200b.cs | **578** \u200b.cs (`Source/src/TabForge.Core/**/*.cs`) | STALE (\u0394 +9). Fix owned by task-4 |
| Avalonia: 426 \u200b.cs + 5 \u200b.axaml | **479** \u200b.cs + **5** \u200b.axaml | STALE (\u0394 +53). Fix owned by task-4 |
| Presenters: 104 files in Core/Presenters/ | **104** \u200b.cs, all under `TabForge.Core/Presenters/`, 0 under Avalonia | OK |
| MainWindow: 86 partials + Compose | **86** at top level incl. `MainWindow.Compose.cs` + `MainWindow.cs` | OK at top level (see \u00a72) |
| 4 content projects (Core, Avalonia, AudioEngine, Audio.Contracts) | 4 src csproj confirmed; +1 test csproj (`Source/tests/TabForge.Avalonia.Tests/`) = "five projects" reading OK | OK |
| \u00a713 central tables: MainMenuTable, MainWindowMenu, DockPaneTable, TechniqueInfo | All four paths exist | OK |
| \u00a711 subsystem file pointers (Models/, Playback/, Plugins/, Rendering/, Shell/, Program.cs, Trace) | Spot-checked: exist | OK |
| COMMANDS.md: 271 ids / 62 files, regen via `Source/tools/command-map.py` | Script exists; header accurate | OK |
| AREA-MAP: "Every path below exists" (77/77, checked 2026-10-18) | Sample of 11 code/README paths: all OK | OK except one stale link (\u00a74) |

Count math: 578 + 479 + 66 (AudioEngine) + 38 (Contracts) = 1161. \u2713 consistent.

## 2. Misplaced / naming-convention leaks (move or rename only — not done here)

1. **3 \u200bMainWindow*\u200b partials outside the composition root** (recursive glob = 89 vs documented 86 top-level):
   - `Source/src/TabForge.Avalonia/Autosave/MainWindowAutosave.cs`
   - `Source/src/TabForge.Avalonia/Autosave/MainWindowAutosaveHost.cs`
   - `Source/src/TabForge.Avalonia/Video/MainWindowLiveVideo.cs`
   Either relocate into the top-level partial set or rename to their folder's convention (e.g. `AutosaveHost.cs`). Doc count stays 86 either way once scoped to top level.
2. **UI-sounding folders inside the "UI-framework-free" Core** (no framework refs found — names only, but they mislead):
   - `Source/src/TabForge.Core/Views/EffectEditors/OrnamentEdits.cs` (1 file)
   - `Source/src/TabForge.Core/Shell/` (3 files: ChromeHitRules, NativeHitCodes, TabDragRules — pure rules, correctly placed logically)
   - `Source/src/TabForge.Core/Controllers/` (2 files: SelectionLoopController, SelectionSync)
   Suggestion: rename `Core/Views/` \u2192 `Core/Score/…` or `Core/Editing/`; keep Shell/Controllers but note in Core README they are UI-free rules/state.
3. **AREA-MAP Playback/ vs Transport/ pair** is already documented in-file (lines 45–47) — no action, cited here so future movers read those READMEs first.
4. `Core/Presenters/` (104 files) is correctly in Core per the ports rule (app shows presenter state through views); do NOT move to Avalonia.

## 3. Long / God-class candidates (split or budget-exception only — not done here)

Budget: `Source/src/TabForge.Avalonia/ArchitectureBudget.json` (goal under 500 lines, hard max 800; files over 800 need a reviewed budget entry that only shrinks; 25 exceptions). Top 12 by lines:

| Lines | File | Note |
|---|---|---|
| 1033 | Core/Presets/FullDemoSong/FullDemoSongFactory.Helpers.cs | budget 1107; fixture data — shrink only by extracting data tables |
| 965 | AudioEngine/EngineSession.cs | budget 1041; session orchestration — prime God candidate |
| 864 | Core/Services/MusicXmlExportService.cs | budget 920 |
| 727 | AudioEngine/Synth/DlsToSoundFont.cs | budget 806; format conversion, cohesive |
| 710 | Core/Services/GuitarProExporter.cs | budget 744 |
| 633 | Core/Score/StaffNotationLayoutBuilder.cs | budget 672 |
| 629 | Core/Score/Editing/ScoreEditCommands.cs | budget 675; split along command groups (9 partials already exist for ScoreEditCommands family) |
| 623+490 | Core/Audio/AudioEngineClient.cs + .Sync.cs | budgets 680/521; together ~1113 logical lines — the client/session God candidate |
| 594 | AudioEngine/Mixing/TrackChain.cs | budget 652 |
| 584 | Avalonia/Views/KeyboardLearnEngine.cs | budget 647 |
| 577 | Core/Services/FilePathPolicy.cs | budget 543 — **actual exceeds budget** (drift, see below) |
| 572 | Avalonia/Views/PreferencesWindow.cs | budget 628 |

Budget drift (actual vs ceiling, ceilings worth re-baselining, not violations to fix here):
FilePathPolicy 577>543, BandView 477<543 (under), OfflineRenderer 503<530, BarGrid 501<547, TutorialMarkdown 465<503,
SelfTestRoundTrip 487<524, SelfTestSyntheticFixtures 461<505. Only FilePathPolicy currently exceeds its exception ceiling.
MainWindow partials are healthy: largest is Compose (461), next TransportRecord (369); none over 400 besides Compose.

## 4. Layering check (csproj + grep)

- \u2713 Zero `using Avalonia` in Core; zero `Avalonia.(Controls|Media|Input|…)` refs in Core (0 hits). The 110 \u200bAvalonia.\u200b hits in Core \u200b.cs are \u200bTabForge.Avalonia.Tests\u200b test-class names in comments only.
- \u2713 Zero `using Avalonia` in Audio.Contracts. Contracts targets net8.0 + net8.0-windows with per-TFM Compile Remove — as documented.
- \u2713 AudioEngine references only Contracts (csproj); the 4 \u200bTabForge.Core\u200b hits there are README cross-references (trust ownership pointers), not code refs.
- \u2713 Reference direction holds: Avalonia \u2192 Core \u2192 Contracts; Avalonia \u2192 AudioEngine (Windows-only, conditional); Engine \u2192 Contracts only. Core never references the app (only documented InternalsVisibleTo + prose mentions).
- \u2713 \u200bCore/Platform/\u200b ports rule intact (IUiThread, IAppServices, Theme/Palette JSON, IPathResolver).
- **No layering violations found.** No moves proposed.

## 5. Doc-link staleness spotted (for task-4 / docs owners — not fixed here, out of scope)

1. AREA-MAP line 49: `../../COMMANDS.md (repo root…)` — from `Source/src/TabForge.Avalonia/`, that resolves to `Source/COMMANDS.md`, which does **not** exist. Actual file is `Source/docs/COMMANDS.md` (\u200b../../docs/COMMANDS.md\u200b). Also "repo root" is really `Source/`, not the workspace root.
2. ARCHITECTURE.md \u00a71 counts (569/426) stale vs 578/479 — already owned by task-4. Presenters (104), MainWindow top-level (86), axaml (5) all verified correct.
3. ArchitectureBudget.json ceilings vs actuals have drifted (mostly actuals now lower — good — except FilePathPolicy, \u00a73). Suggest a budget re-baseline pass as a separate docs-only task.

## 6. Tidy checklist (no behavior changes — proposed, none applied)

- [ ] Task-4: update ARCHITECTURE.md \u00a71 counts 569\u2192700? no — 569\u2192578, 426\u2192479 (its scope).
- [ ] Decide home for the 3 subfolder \u200bMainWindow*\u200b files (move to root partials or rename to folder convention).
- [ ] Rename \u200bCore/Views/\u200b (1 file) to a non-UI name; annotate \u200bCore/Shell/\u200b + \u200bCore/Controllers/\u200b as UI-free in Core README.
- [ ] Fix AREA-MAP line 49 link to \u200b../../docs/COMMANDS.md\u200b.
- [ ] Re-baseline ArchitectureBudget.json (FilePathPolicy ceiling; lower ceilings that now have headroom).
- [ ] God candidates for future splits (no action now): EngineSession, AudioEngineClient(+.Sync), MusicXmlExportService, GuitarProExporter, ScoreEditCommands family.
- [ ] No \u200b.cs edits, no moves, no deletions performed in this round — report only.
