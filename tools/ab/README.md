# A/B parity loop: Avalonia vs WPF

Compares the Avalonia renders with the WPF reference shots of the same `Source/tests/parity/parity.capture.json` entries.

## Run

    Source\tools\ab\run.ps1 -Wpf "<folder of the WPF PNGs>"

or set `TF_AB_WPF` to that folder. The script makes a new folder `%TEMP%\tf-ab-<guid>` for the run and sets `TF_AB_OUT` to it,
so earlier runs are never clobbered. It then runs `dotnet test ... --filter ParityShotsTests` (which only renders), then
`python -I Source\tools\ab\compare.py`. Without `TF_AB_OUT`, the test writes under its own process folder and compare.py falls back to `%TEMP%\tf-ab`.

## Files

- `Source/tests/TabForge.Avalonia.Tests/Capture/ParityShotMap.cs`: one entry per shot base: the Avalonia window or pane, its DIP size (from the WPF
  report), the state it reproduces, or the reason it has no twin.
- `Source/tests/TabForge.Avalonia.Tests/Capture/ParityShotsTests.cs`: renders each mapped shot to `%TEMP%\tf-ab\avalonia\<shot>.png` in the shot's theme,
  and writes `%TEMP%\tf-ab\missing.txt` (`shot<TAB>reason`). `EveryManifestShotHasAMappingAndMissingShotsAreListed` fails if a manifest shot has no entry.
- `Source/tools/ab/compare.py`: outputs below.

## Outputs (the run folder, `TF_AB_OUT`)

- `pairs\<shot>.png`: WPF reference, Avalonia, and a diff heat map (red where the blurred images differ).
- `report.csv`: every shot with status, diff %, worst region, worst region mean delta, sizes, note.
- `report.md`: summary counts, compared shots worst first, then the missing list with reasons.

## Method

- The WPF shot is taken at 200 %, so it is divided by `--wpf-scale` (2) to DIP and resized to the Avalonia render size.
- Both images get a Gaussian blur of radius 2 px (`--blur`), which forgives edge shifts of about two pixels.
- A pixel counts as different when its largest channel delta exceeds 32 (`--pixel`).
- Status: pass when at most 2.0 % of pixels differ (`--threshold`). This is a starting bar: anti-aliased text alone rarely gives
  more than a fraction of a percent on the same layout, so anything above 2 % means a visible layout, colour or text difference.
  Tune it after the first real distribution.
- Region deltas: the image is split into a 3 x 3 grid; the worst region's mean channel delta is reported.

## Not covered

Shots listed in `missing.txt` have no Avalonia twin yet (context menus, playback states, several panes, and windows that do not exist
in Avalonia). Main-menu shots are captured by `MenuPopupCapture`: the open popup alone (or a submenu's popup for the View > Layouts and View > Panels shots), cropped as the WPF shot is. They are compared like the other shots; the rows the Avalonia menu does not have yet (View's Instrument view, Arrangement overview, Band view and the other toggles, Metronome, Count-in, Record video) are the main remaining difference.
