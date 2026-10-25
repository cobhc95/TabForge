# Residual Video-Deny Map (post-fix) — task-67, Core lane

Evidence rule first: Trace.Error → errors.log ALWAYS. Passing stages are SILENT,
so absence of a stage line = stage passed.

## R1 — Validate refuse (unchanged by fix, correct)
User: `The video folder could not be used: {link|device|invalid}` (never a hint).
Log: `video folder: validate: {msg} hresult=0x…`.
Links/devices stay refused by design.

## R2 — Create-deny + fallback NULL
User: `The video folder could not be used: {msg}[+hint iff access-class]`.
Log: `video folder: create: …` and NOTHING after.
Fallback null when: LocalApplicationData "" (service/session-0, unloaded profile);
LocalAppData blocked (offline redirected profile, quota, enterprise CFA covering
AppData); or non-access class (Argument/NotSupported → no fallback attempted).
Discriminator: hint present = access-class, hint absent = non-access
(too-long/unready/DirectoryNotFound).

## R3 — Probe-deny non-access on original folder
User: same shape as R2, no hint, no fallback.
Log: `video folder: probe: … denied=False …`.
Classes: DirectoryNotFound (TOCTOU delete, 0x80070003), disk-full (0x80070027),
too-long, drive-not-ready, NotSupported. No fallback BY DESIGN.

## R4 — Probe-deny access + fallback ALSO denied
User: `…: {probe msg}{hint}`.
Log: `probe:` line, NO `probe-fallback:` line.
Means BOTH Videos and LocalAppData blocked — profile/machine policy, not TabForge.

## R5 — TOCTOU after fallback-probe
Revocation/link-swap between probe-pass and MP4 CreateNew (encoder worker) or
staging `.name.tfvideo-job.tmp.mp4` CreateNew.
User: `The recording could not start: …` with probe-fallback line present but NO
later folder line. Signature: fallback line + start-failure = window between
probe and open. (Create-fallback path double-probes, so its window is short;
probe-path fallback single-probes.)
Note: create-fallback path double-probes, so its window is short.

## R6 — Downstream mimics (same "could not start" UX, NO folder stage lines)
Absence of folder lines IS the evidence. Candidates: engine rate text (pre-folder
check, L72); `Video size WxH is not supported` (check Sizes for portrait
1080x1920/2160x3840); `Windows Media Foundation is not available` (N-edition —
biggest mimic); H.264 start COM `0x…` (often GPU/DWM E_ACCESSDENIED — real
permission error OUTSIDE folder policy); BeginCapture DXGI-duplication deny;
frame-buffer exhaustion (4K120); mid-run rate-change/audio-refuse surfacing at
STOP not start.

## R7 — Perceived deny (records fine)
User: `Saved to {LocalAppData…} because the usual folder was blocked by Windows`
+ `…-fallback:` log line. User looking in Videos\TabForge finds nothing.
Support script: ask for this notice first.
