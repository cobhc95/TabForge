# Pixel parity

A safety net for UI ports: the headless `--capture` run renders every shot in `parity.capture.json` (dark and light), and `ParityCheck` compares each PNG with `baseline/` pixel for pixel, with no tolerance.

Build TabForge first (Release, into a temp folder):

    dotnet build src/TabForge.Avalonia/TabForge.Avalonia.csproj -c Release -o %TEMP%\tf-j1

Check (prints `PARITY: N shots, M differences`, exit code 1 on any difference or missing shot):

    dotnet run --project tests/parity/ParityCheck -c Release -- %TEMP%\tf-j1\TabForge.exe

Accept an intended change (copies the current shots over `baseline/`; review the diff images first):

    dotnet run --project tests/parity/ParityCheck -c Release -- %TEMP%\tf-j1\TabForge.exe --accept

`TestParityCaptureCoverage` (architecture area) fails for any Window class or dock panel that no shot in `parity.capture.json` photographs; its exemptions, each with a reason, are in `src/TabForge.Avalonia/SelfTests/Architecture/SelfTestParityCoverage.cs`.

A difference writes `<id>-diff.png` (changed pixels in red), `<id>-actual.png` and `<id>-baseline.png` (cropped to the changed box plus 8 px) to `%TEMP%\tf-parity-out\diff`. A run takes about three minutes.

The ten note-effect editor shots (`effect-<kind>-<theme>`: bend, tremolobar, trill, grace, harmonic, dark and light) were recorded in the test VM (`Source/tests/parity/vm`) from a build of current main plus this change, not on the PC: the VM may render them differently from the PC (its virtual GPU). Only those ten baselines were recorded there; every other baseline is unchanged. Two VM runs of the same build gave byte-identical pictures. They come from the effect block at the end of `parity.capture.json` run on its own (it opens the demo song again first, so the block should not depend on the steps before it; the full parity run was not repeated in the VM).

The four instrument-view shots (`instrument-keyboard-<theme>` and `instrument-drums-<theme>`, dark and light) come from the block at the end of `parity.capture.json` (it opens the demo song again first, track 0 for the keyboard, track 3 for the drum track). Their baselines were recorded in the test VM (`TabForge-VM2`) from the build before the drum-kit colour rename (`071cd41c`); the same block on a build of current main gave 4 shots, 4 identical, 0 different. The drum shots show the drum-view list above the score, not the kit drawing (the kit is not in the panel at this size), so they do not yet prove the drum-kit colours.

The data A/B run (`Source/tests/parity/vm/Run-DataAB.ps1`) compares what a build writes, not pixels. In one test VM it runs the same headless commands (`--save-copy`, `--exportgp`, `--musicxml-export`, `--render-probe` and `--settings-roundtrip`, through `Data-Steps.ps1`) on the demo song from `samples/` as `.gp` and as `.gp5`, once with an OLD and once with a NEW build (`-Old C:\TF\<name> -New C:\TF\<name>`, after `Publish-ToVm.ps1`; `-VmName` is `TabForge-VM` or `TabForge-VM2`, since `TabForge-VM3` is the behaviour recorder's). It compares every output byte for byte, after three normalisations that each name the files they touch: the random track ids the importers give a project (GUIDs, numbered by first appearance), the `ProjectSha256` field that hashes that project, and the wall-clock render speed in the probe report (its own check still compares the WAVs bit for bit). It prints `DATA: N files, M differences`, lists each difference and exits 1 on any; a step that exits non-zero on either side is a difference too, so equal failures do not pass. The save and settings steps write under a `--profile` folder per run (their scratch root), and `-CleanupInVm` removes the run's VM copies afterwards. The save step reads no plug-in state (no engine runs in it), so plug-in settings are not part of the comparison.

## Corpus counting probe (opt-in)

`CorpusProbeTests` (TabForge.Avalonia.Tests) counts written attacks against compiled NoteEvents per bar/track/voice for every Guitar Pro file and groups the deltas by cause.
Run with `TABFORGE_CORPUS_PROBE=1`, `TABFORGE_SAMPLES_DIR` (default `Samples\corpus`) and `TABFORGE_CORPUS_PROBE_OUT` (default `%TEMP%\opencode\gp-parity`); it writes attacks.csv, causes.csv and summary.txt.
The full corpus takes about 10 minutes, so use a `--blame-hang-timeout` of at least 14m.
