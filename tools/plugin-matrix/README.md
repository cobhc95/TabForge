# TabForge Stage 10 plug-in matrix

Test-only harness for the plan's 50-plug-in matrix. The real manifest is the private file
`tools/plugin-matrix/manifest.private.json` **on branch `wine-bridge` only**. It is never merged to `main`,
never put in a package and never published in public docs; `tools/package-linux.sh`, `tools/package-mac.sh`
and `tools/Publish.ps1` refuse to package a `manifest.private*.json`. The committed `manifest.json` is an anonymised copy
(opaque entry ids, no maker pages or download URLs) so the structure and counts are reviewable without
shipping the test list.

## Files

- `run.py` — download/extract with SHA-256 checks, one app process per plug-in with a hard timeout,
  editor screenshots, per-plug-in `result.json` and `run.log`, summary table and failure clusters.
- `manifest.private.json` — the real test manifest; exists only on `wine-bridge`, never on `main`.
- `manifest.json` — anonymised copy of the real manifest (counts and hashes only).
- `manifest.schema.json` — the manifest shape.

CI checks out `wine-bridge` and reads `tools/plugin-matrix/manifest.private.json` directly; no manifest
is passed through a workflow input, gist or URL. Plug-ins are downloaded to the runner cache, never
committed and never uploaded with the results.

## Manifest rules

- At least 50 distinct builds: 15 VST2 32-bit, 16 VST2 64-bit, 15 VST3 64-bit, 6 VST3 32-bit
  (52 total, including one 16-output DrumGizmo build).
- Maker page or maker release page only; direct archive; no sign-ups, accounts or GUI installers.
- `sha256` is the loadable binary; `download_sha256` is the archive. The runner verifies both.
- Free builds only; `licence` records the maker terms and `automated_testing` says whether ordinary
  automated compatibility testing is allowed. The JUCE 8 group is marked `risk_group: "juce8"`.
- Duplicate binary hashes are rejected: one build cannot be padded into several entries.

## Run

```bash
# validate the private manifest (also writes the anonymised tools copy)
python tools/plugin-matrix/run.py --manifest tools/plugin-matrix/manifest.private.json \
  --validate --write-public tools/plugin-matrix/manifest.json

# Windows native, first 10
python tools/plugin-matrix/run.py --manifest tools/plugin-matrix/manifest.private.json \
  --platform windows --app build\bin\TabForge.Avalonia\release\TabForge.exe --limit 10 \
  --out C:\tfrel-tmp\matrix\results-win --cache C:\tfrel-tmp\matrix\cache

# Linux Wine, one shard (the app is the extracted TabForge.AppDir/AppRun)
python tools/plugin-matrix/run.py --manifest tools/plugin-matrix/manifest.private.json \
  --platform linux-wine --app /tmp/TabForge.AppDir/AppRun --shard 0/6 \
  --out /tmp/matrix-results --cache /tmp/matrix-cache

# Summary and failure clusters
python tools/plugin-matrix/run.py --summarize C:\tfrel-tmp\matrix\results-win
```

Linux needs `Xvfb`, `openbox`, `xdotool`, `wmctrl`, ImageMagick (`import`, `convert`) and `xauth`.
Windows uses Pillow (`ImageGrab`) on the interactive desktop. The app creates a visible owner window
titled `TabForge Matrix - <id>` and, for a native plug-in, a child editor window. The runner finds
only windows whose process is this run's app or the helper PID the app wrote in `editor-open.txt`,
raises that window with `SWP_NOACTIVATE`, and copies exactly its own rect. It never enumerates or
captures another application's window, and it never uses the desktop as a fallback. Under Wine the
helper-owned top-level window is found by its `TabForge - ` title and closed through `wmctrl` (a real
WM close).

## Checks

The app runs checks 1–10 from the plan in one process: approve/identify; five load/unload cycles;
audio, bypass and silence with finite samples; state save/restore and a reopened instance; parameters
set/read; preset or program change while the editor is open; editor open/screenshot/close/reopen
(the runner supplies the screenshot and pixel verdict); 44.1/48/96 kHz and 128/256/1024 frames;
offline versus realtime; and a deliberate helper kill. On Windows 64-bit the product's native
in-process route is used, so check 10 is recorded as skipped (there is no helper to kill); 32-bit
and Wine runs use the helper and run it.

A failure is recorded, never fixed here. The summary groups failures by check and cause. The process
exit code is 0 when the runner completed; a CI gate can read `summary.json` for thresholds.

Check 4 compares a restore into the live instance with a fresh instance from the same saved state. If
the two disagree and the fresh instance matches the original value, the check is recorded as **fail**:
the state is good and only the live-instance restore failed. `nondeterministic` is kept only when the
two restores disagree with each other **and** neither matches the original.

Check 5 uses a 0.1 tolerance for a continuous parameter. A stepped VST3 parameter is exposed as
`0..steps` in whole steps, so its tolerance is half a step (0.5 in plain units) and only whole-step
read-backs are accepted; this replaces the old flat 0.26. VST2 has no stepped flag, so a failed VST2
pair still gets the stable-quantised fallback. If no settable parameter is found among the first 8
writable parameters, or the plug-in lists none, check 5 is a skip and is listed under
`Check 5 parameter coverage skipped` in the summary; it is not counted as a pass.

Check 9 compares two fresh instances from the same saved state, then repeats the offline render on
two more fresh instances. If the offline/realtime pair differs but the two identical-mode renders
also differ, the check is recorded as `nondeterministic` with both max-diff values, not as pass or
fail; that is the evidence for free-running modulation or randomness. No tolerance is widened for
these cases.

An entry may carry `skip_audio_reason` when the audio needs data the runner does not supply. The
multi-output DrumGizmo entry uses this: its load, state, editor and sample-rate checks run, while
checks 3 and 9 are marked skipped with that reason, and the entry is counted as skipped rather than
passed or failed.

## Cache and CI

`--cache` holds downloaded archives and extracted fixtures. It is outside the repo and is safe to
cache in Actions by the manifest hash. `wine-matrix.yml` is `workflow_dispatch` only, shards the list,
caches downloads, uploads only results/logs/screenshots, and never commits or uploads a plug-in.
