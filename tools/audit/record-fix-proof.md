# Record-Denied Fix — Proof Checklist (for the proving run)

Build (needs network, once): `dotnet build TabForge.sln -c Release`
Run: `src/TabForge.Avalonia/bin/Release/net8.0-windows/TabForge.exe`

## Test A — Record video (default folder, Videos\TabForge blocked or not)
1. Press Record video (camera button / hotkey).
2. PASS if: recording starts, possibly with
   "Saved to %LocalAppData%\TabForge\Videos because the usual folder was blocked by Windows".
3. If it denies: copy the dialog text EXACTLY + the newest `errors.log` lines
   containing `video folder: ` (stage = validate/create/probe, HResult hex).

## Test B — Record audio (arm a track, press Record)
1. PASS if: recording starts, possibly with the "Saved to … blocked by Windows" status.
2. If it denies: copy the status text EXACTLY + `errors.log` lines containing
   `recording folder: ` (includes denied=true/false + HResult).

## Test C — locale (only if a non-English Windows is handy)
- Same as B. Old code could never fall back there (`"denied"` sniff); new code uses
  HResult. Report Windows display language + result.

## Send back
- For each test: PASS (what you saw) or FAIL (exact dialog/status text + errors.log lines).
- errors.log lives beside the exe (or %LocalAppData%\TabForge\logs — check both).

Team reviews all four together (implementer + Core/UI/Engine angles) before DONE.
