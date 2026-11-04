# Plug-in compatibility — draft for owner review

Status: **INTERNAL DRAFT, NOT FOR PUBLICATION.** This file is not in the public-export allowlist and must not be
copied into a release package. The appendix names the test fixtures; those names need the owner's explicit approval
before any of them appears in a shipped document, the public repository, a release note or the store page.

## What was tested

The final Stage 10 matrix ran 52 free Windows VST2/VST3 fixtures (15 VST2 32-bit, 16 VST2 64-bit, 6 VST3 32-bit,
15 VST3 64-bit; 20 instruments and 32 effects) through the same nine checks: load/scan, role and vendor, instance,
state, parameters, audio, editor, audio-accuracy comparison and offline render. It ran on:

- **Windows 11 x64**, native in-process hosting for 64-bit VST2, the 32-bit helper for 32-bit VST2 and both VST3
  routes. Summary: `C:\tfrel-tmp\matrix\stage12b-win\summary.md`.
- **Linux x64 under the bundled Wine 9.21**, the packaged app in a clean runner. Summary:
  `C:\tfrel-tmp\matrix\ci-final-41273209288\wine-matrix-summary\summary.txt` (run `41273209288`).

These numbers are compatibility results, not a support promise. A "pass" means the checks completed with the
fixture's own output; it does not mean every control, preset or host feature of that plug-in was exercised.

## Summary (public-safe wording, no third-party names)

| Platform | Fixtures | Pass | Fail one check | Nondeterministic | Refused / skipped / crash |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows 11 x64 | 52 | 44 | 5 | 1 | 1 skipped, 1 crash |
| Linux + bundled Wine 9.21 | 52 | 44 | 4 | 3 | 1 refused |

| Format | Windows pass | Linux/Wine pass |
| --- | ---: | ---: |
| VST2 32-bit | 14 / 15 | 14 / 15 |
| VST2 64-bit | 13 / 16 (1 skipped, 1 crash) | 14 / 16 (1 refused) |
| VST3 32-bit | 5 / 6 | 5 / 6 |
| VST3 64-bit | 12 / 15 | 11 / 15 |

The two platforms agree on the majority of results. The differences are named in the limits below.

## Limits to state in the release notes

1. **JUCE 8 editors are black under Wine.** A VST3 built with JUCE 8's Direct2D UI creates its window but paints
   nothing under Wine's Direct2D 1.2. Audio, state and parameters still work; only the editor is unusable. The CI
   editor fixture is an older software-rendered VST3 so the editor path itself is still proven. This is a Wine
   limitation and must be stated as such, not as a TabForge fix.
2. **Eight bridged plug-ins need about 1024 frames.** Stage 15 measured a 256-frame floor for one bridged plug-in
   and 1024 frames for eight under Wine (0 late blocks in 375 at 1024; the 256/512 cases fall behind). At 48 kHz
   1024 frames is about 21.3 ms; at 44.1 kHz about 23.2 ms. A project with eight bridged plug-ins at a lower buffer
   is not real-time safe. This is a measured limit, not a crash.
3. **One 64-bit VST2 instrument crashes on Windows.** The 64-bit Windows VST2 route is still native in-process
   (0.8.7 isolates 32-bit VST2 and the Wine routes, not native 64-bit VST2), so that plug-in's access violation ends
   the audio engine. The same instrument's 32-bit build and its Linux/Wine 64-bit path pass. The crash is in the
   plug-in, but the host has no isolation for that route yet.
4. **No real Linux hardware has been tested.** Every Linux result comes from CI runners or containers: no real GPU,
   audio interface, MIDI keyboard, desktop session, suspend/resume or device change. The packaged tar.xz starts
   headless and the AppImage matches it; that is not a desktop test. Real hardware remains the owner's test.
5. **A bridged plug-in's own preset menu has ended its helper.** The cause is still unknown (the earlier external-kill
   theory did not hold). The helper is isolated, so the engine survives and reports it, but the user loses that
   plug-in instance until it is reloaded. Do not describe the bridge as crash-free.
6. **The owner has not tested the 0.8.7 bridge in the real app.** The changelog marks those features "(untested in
   app)". The matrix proves the packaged host paths, not the everyday UI flows.

## What the pass does not cover

- Only free fixtures were used; no commercial plug-ins, iLok/Challenge-Response products, sidechains, multi-out
  routing beyond one fixture, MIDI-out, program changes or VST2 shell bundles.
- Editors are opened, captured and closed; no pixel comparison of every control, no preset browsing inside the
  plug-in, no drag-and-drop, no resize stress.
- Windows results are from the CI runner, not the owner's audio interface or ASIO driver.
- The matrix is a point-in-time result for the pinned fixture archives; a newer build of a plug-in may behave
  differently.

## Appendix: named results (owner approval required before publication)

This appendix is the evidence behind the public-safe summary. Keep it internal unless the owner approves the exact
fixture names for the public page.

### Linux + Wine 9.21 (run 41273209288)

Pass (44): Acceleration, ADClip9, Console4Channel, Density, PurestGain (VST2 32); TAL-BassLine-101, Dexed 0.9.3,
mdaDelay, mdaMultiBand, mdaJX10, mdaDX10, Synth1, TDR Nova, TDR Kotelnikov (VST2 32); Dexed 0.9.3, Synth1,
TAL-BassLine-101, TAL-Chorus-LX, TDR Nova, TDR Kotelnikov, TDR VOS SlickEQ, TDR Molotok, TDR Prism, Acceleration,
ADClip9, Console4Channel, Density, PurestGain (VST2 64); Dexed 1.0.1, TDR Nova, TDR Kotelnikov, TDR Molotok,
TDR Prism, FB3, Fury-800, Grain Strain, Surge XT Effects, TAL-NoiseMaker, TAL-BassLine-101 (VST3 64); TDR Nova,
TDR Kotelnikov, TDR Molotok, TDR Prism, TAL-BassLine-101 (VST3 32).

Failed one check (4):
- Dexed 1.1.0 VST3 64 — editor (JUCE 8 Direct2D black window).
- TDR VOS SlickEQ VST3 64 and VST3 32 — offline check (EQ engaged renders differently offline; report-only probe).
- FB-7999 VST3 64 — state.

Nondeterministic (3): TAL-NoiseMaker VST2 32, TAL-NoiseMaker VST2 64, Surge XT VST3 64.

Refused (1): DrumGizmo VST2 64 (copy protection/driver refusal).

### Windows 11 x64 (stage12b final)

Pass (44): Acceleration, ADClip9, Console4Channel, Density, PurestGain (VST2 32); TAL-BassLine-101, Dexed 0.9.3,
mdaDelay, mdaMultiBand, mdaJX10, mdaDX10, Synth1, TDR Nova, TDR Kotelnikov (VST2 32); Dexed 0.9.3, TAL-BassLine-101,
TAL-Chorus-LX, TDR Nova, TDR Kotelnikov, TDR VOS SlickEQ, TDR Molotok, TDR Prism, Acceleration, ADClip9,
Console4Channel, Density, PurestGain (VST2 64); Dexed 1.0.1, Dexed 1.1.0, TDR Nova, TDR Kotelnikov, TDR Molotok,
TDR Prism, FB3, Fury-800, Grain Strain, Surge XT Effects, TAL-NoiseMaker, TAL-BassLine-101 (VST3 64); TDR Nova,
TDR Kotelnikov, TDR Molotok, TDR Prism, TAL-BassLine-101 (VST3 32).

Failed one check (5): TAL-NoiseMaker VST2 32 and VST2 64 — state; TDR VOS SlickEQ VST3 64 and VST3 32 — offline;
FB-7999 VST3 64 — state.

Nondeterministic (1): Surge XT VST3 64.

Skipped (1): DrumGizmo VST2 64 — missing test data.

Crash (1): Synth1 VST2 64 — access violation in the plug-in's process path (the plug-in's own crash; the 32-bit build
and the Wine 64-bit build pass).

## Publication checklist before this becomes a public page

- [ ] Owner approves or removes every fixture name in the appendix.
- [ ] Re-run the matrix after the release candidate is frozen (the counts above are the Stage 10 final, not the
      release binary).
- [ ] Add the real-hardware result from the owner's machine, or keep the "no real hardware" limit.
- [ ] Re-check the eight-plug-in / 1024-frame limit against the final package.
- [ ] State the date, app version, Wine version, Windows version and the exact fixture manifest hash.
- [ ] Owner real-hardware test of the primary Linux AppImage (still unproven): AppImage size, first-start time, second-start time, plug-in scan time on a Wine-bridged plug-in, FUSE-less run with `--appimage-extract-and-run`, one desktop-menu/icon check, and AppRun Wine path resolution from an AppImage mount (see `native/wine/BUILD_PROVENANCE.md:60`).

## Stage 18 status (1 Nov 2026, Job A — no packaging, no release)

Licence/SBOM/Wine notices: consistent after one fix. `docs/THIRD_PARTY.md` now references
`licenses/MIT_DotNet_WindowsDesktop_runtime.txt` (shipped; required by both packagers but
previously unmentioned in the text). All other shipped licence files are referenced in
`THIRD_PARTY.md` and required by `tools/Package-Release.ps1` and `tools/package-linux.sh`;
`native/wine/{BUILD_PROVENANCE.md,COPYING.LIB,LICENSE,AUTHORS}` are present and all five
notices are copied/checked by the Linux packager; `docs/reference/SBOM.md` records both the
prebuilt and source Wine tarball SHA-256 values. Not verified: byte-for-byte package output
(no packaging per this job).

Compatibility docs: fixture names appear only in this internal draft's appendix (owner
approval still pending); no public doc names a commercial plug-in fixture. The only
commercial product name in public docs is the "Superior Drummer 3" drum-map label
(`docs/CHANGELOG.md:197`), which is a map name, not a plug-in fixture — left as is.

Linux-facing text: no passing claims for the own Wine build, distro legs, or real hardware.
`docs/CHANGELOG.md:40` records the distro legs as cancelled/failed and unproven;
`docs/README.md:398` and this draft state no real Linux hardware tested; WSL is absent on
this machine (`wsl --version` reports not installed), so nothing Wine/Linux was executed
here beyond Windows-hosted bridged tests.

Engine memory-growth (no plug-in links): `DockBandPerfE2bTests.ForcedGcAfterDialogCloseReturnsHeap`
passed — forced-GC heap mid 53027952, after 62825424, growth 9568 KB over the second
3×(Mixer+Settings) open/close batch (gate < 32 MB). This is a no-link UI soak, not the
bridged engine soak; it does not resolve the bridged memory question.

Bridged load short diagnostic (needs links): `Stage15StressTests` first run skipped (helpers
not deployed under the job's custom ArtifactsPath directory — harness artifact); re-run with
the `TABFORGE_TFVST32_EXE/_TFVST64_EXE/_TEST_DLL/_TEST64_DLL` environment variables pointing at the repo binaries
passed at 1024 frames, 1 chain × 8 plug-ins, 1 s warm + 3 s measure on 12 logical
processors: single 0/141 missed, baseline 6/141 missed + 11 budget-skipped, all-cores
12/141 missed + 8 skipped, 0 late, link_failed False. Short-window data only; the earlier
high missed-block rates have no proven cause and this does not resolve them.

Theme tests by class (31 classes, Release, `--no-build`): 27 pass alone; 4 fail alone:
`tests/TabForge.Avalonia.Tests/Theme/AccentRepaintTests.cs:90` (1/5; 50 ms repaint budget
exceeded on this machine — timing, not proven a product defect);
`ColourButtonTests` (1/12), `ColourPickerPopupTests` (1/16), and `PaletteThemeTokenTests`
(1/26) each fail with `tests/TabForge.Avalonia.Tests/Infrastructure/TestRepo.cs:29`
"checkout root (Source/TabForge.sln) was not found" — the helper only understands the
private Source layout, so these fail in this public-layout checkout regardless of
`TABFORGE_REPO` (test-harness bug, not verified as product failures). No broad theme run
was attempted (per-job rule: no full suite); no test-host abort was reproduced.

Unproven / not verified: own Wine build from pinned source; distro-container startup,
XWayland editor, virtual-audio path, settings/prefix upgrade; Debian 11 glibc refusal;
real Linux hardware/GPU/audio/MIDI/desktop/suspend/device changes; owner app testing and
sign-off; release packaging.
