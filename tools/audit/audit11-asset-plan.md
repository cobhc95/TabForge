# Audit-11 Asset Plan — DONE 2026-10-19 (lead, atomic)

## Executed
- Deleted `src/TabForge.Avalonia/licenses/MIT_PDFsharp_MigraDoc.txt` (1143 B, 0 code
  refs, shipped-only-via-glob) + removed it from the `Package-Release.ps1:173`
  required-licences gate in the same change (gate would otherwise fail release).
- Hygiene safe: `SelfTestSourceHygiene.TestLooseSoundTouchAndLicenseTexts` checks
  only the 4 required texts (SoundTouch/Bravura/NAudio/MeltySynth) — untouched.

## Deferred with reason (needs runtime probe + owner confirm — NO deletes)
- Docs orphans 174/285 = 3643930 B (Hero 891813 + cover 411088 + chapters/callouts/
  keys/diagrams). Zero static refs incl. dynamic-pattern grep — but keys are tutorial
  key-cap renders and chapters may wire later (ULTRA_LEAN item 8 probe required).
- Brand↔canonical pairs (36844+35200+43987 = 116031 B): touch brand, deferred.
- Screenshots pairs (fretboard 37844, main-window 821630, prefs-audio 615432):
  out of task-5 scope, pick canonicals later with UI owner.
- Icons: 4 stop_button orphans (subfolder NOT preloaded — TransportIconArt is
  top-level-only — zero refs, strongest) + 2 rhythm_beam waste (recursively
  preloaded, never drawn). 140 instrument SVGs are dynamic-by-design
  (InstrumentCatalog + manifest.json by program number — static grep blind).
  Screenshots/ binary content not checked. Asset-owner confirm before any delete.

## Verdict
- Docs dupes: task-5 pair-deletes hold (285 images, no resurrections); used 111/111
  (screenshots 98/98, animations 6/6, .gp 7/7, tutorial canonicals 2+4).
- Core+Engine carry ~zero content weight (theme JSON ~40KB only; fonts/DLS/demo
  are package/system/loose). Pins: NAudio 2.2.1, AlphaTab 1.8.4-tabforge.4,
  MeltySynth 2.4.1, SoundTouch.Net 2.3.2.
