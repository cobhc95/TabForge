# Semantic round-trip results (external review item 7)

> **Status 2026-10-22:** a historical snapshot. The suite it cites was removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note)), and a grep of the code found no `--roundtrip-semantics` option. The last-run date and commit below are not recorded; status not verified 2026-10-22.

Local document. Source of truth: former GuitarPro round-trip-semantics suite, removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note)) (runs inside `--selftest`, or alone with
`TabForge.exe --roundtrip-semantics <report>`). Fixtures are redistributable only: in-code synthetic songs and TabForge's own
technique song (`GmSongAudit.TechniqueSong`, rewritten into the model's own conventions). `Tabs/` is never read except an optional
local extra that SKIPs when the folder is absent. The comparer compares musical meaning, not bytes: per note pitch, string, fret,
onset, duration, dynamic, ties, grace, bends, harmonics, slides, techniques; per beat tuplet/accent/staccato/fermata/whammy/lyrics;
per bar time signature, tempo (also mid-bar), repeat start/end/count, alternate endings, key, markers; per track name, tuning, capo,
program, level, pan. Any difference not on a format's allow-list (with its reason) fails.

## Result of the last run (Release, this worktree)

Suite alone: 56 checks, 0 failed, 1 skipped (local `Tabs/` extra: folder absent). Full `--selftest`: 1818 passed, 0 failed, 3 skipped.
22 scenario x format comparisons, about 39,800 notes / 31,900 beats compared, 0 unexpected differences. The number of checks is not
the coverage: the list below is.

| Format / path | Scenarios | Result |
|---|---|---|
| `.tforge` (gzip) | technique song, showcase, dense, import-edit-save-reopen, audio/routing, reorder+rename, large (10 tracks x 180 bars), 24 mixed reopens, 5 save/load cycles | pass, empty allow-list (fully lossless) |
| `.gp` with embedded project | technique, showcase, dense, audio/routing, reorder+rename | pass, empty allow-list |
| clean `.gp` (alphaTab .gp writer + importer) | technique, showcase, dense, large, 4 export/import cycles (idempotent after pass 1) | pass with the allow-list below |
| clean `.gp` + `.tfaudio` (DocumentController) | import-edit-save-reopen, audio/routing, reorder+rename with duplicate names | pass with the allow-list below |
| MusicXML export read back by an independent reader | technique, showcase, dense | pass: notes, strings, frets, onsets, durations, ties, tuplets, grace, dead/ghost, hammer/slide/bend/harmonic/tap/palm-mute presence, dynamics, time signature, tempo, repeats, endings, key, rehearsal marks |
| playback compile vs offline-render compile | all three songs + large | pass: same note events (track, pitch, onset, duration, velocity) with the count-in removed; render tempo map reproduces every bar start and, with mid-bar tempo changes/ramps, every slot position (extra scenario: ramp, mid-bar step, whole-bar ramp) |
| reopened timeline vs original | `.tforge` exact; clean `.gp` pitch/onset within 1 ms | pass |
| negative controls | 21 deliberate musical changes | all detected |

## Expected conversion losses (allow-lists)

Each is in the code with the same reason.

Clean `.gp` (.gp via alphaTab):
- Loudness: 8 dynamics steps; velocity quantised, and one dynamic per beat (other chord/drum notes take the first note's; a drum kick accent under a hi-hat is flattened). The first note's dynamic is compared strictly.
- Track volume/pan quantised to the format's 0..16 scale (compared quantised); mixer-group levels are baked into the `.gp` values (the `.tfaudio`, FormatVersion 2, stores each track's own volume/pan and restores them, so the group counts once after reopening; FormatVersion 1 pairs are un-baked on open, approximately).
- Bend curves reduced to origin/middle/destination; whammy curves to at most 4 points (first/peak/last compared strictly).
- Tenuto cannot be written; left/right-hand fingering and .gp5 mix-table changes are not written.
- Not written by alphaTab's .gp writer: pick slides, rasgueado, generic "Legato" tag, bend-grace tag.
- Importer-side naming: whammy sub-type re-derived; Dead/Ghost/Grace/Harmonic/Brush/HOPO tags mirrored or split; slide/trill targets and tremolo speed filled in when the source had none (conditional allowance: only when the source value was empty).
- Grace beat length 1 slot comes back as 2 (playback does not use it). Fermata is a bar-position attribute in the file format, so it also appears on the other tracks. Palm mute is per beat. MIDI channels are re-assigned on import.
- `.tfaudio` pair only: MIDI device numbers, record input, monitor, tint, reverb/chorus sends, transpose, performer, notes, drum-map preset (view/machine settings not in either file). Plug-in state, sidechains, MIDI forwarding, buses, master chain, clips and lanes DO survive (routing re-pointed by track, duplicate names handled).

## Bugs found

Fixed (small, in my area, covered by the suite):
1. `.gp` export crashed (`ArgumentOutOfRangeException` inside alphaTab `Voice.Finish`) when a track had a second voice in only some bars. The exporter now writes a second voice in every bar of such a track.
2. .gp import lost every minor key: it read `KeySignatureMode`/`KeyMode`, which do not exist in alphaTab 1.8 (`KeySignatureType` does). Now read.
3. Fixture: the showcase song's drum track had 5 tunings (the validator rejects a kick on line 5), so it could not be saved as `.tforge`; now the importer's 6 lines.

Fixed after the first run, and still open:
- Mixer-group level counted twice after reopening a clean `.gp` + `.tfaudio` (fixed): the `.tfaudio` (FormatVersion 2) stores each track's own volume/pan; older pairs are un-baked on open (approximate).
- Capo (fixed): as in other tab editors, frets are relative to the capo and sounding pitch = tuning + capo + fret, one rule (`TrackModel.PitchOf`) used by editor entry, playback, import (harmonics too) and export; round trip of a capo-2 track is fret- and pitch-identical in `.gp`, embedded `.gp` and `.tforge` (checked in the suite). Notes typed on a capo track in older builds are stored without the capo and keep sounding as before; no automatic migration (it would change how old songs sound).

## Not covered

- Other programs reading our `.gp` or MusicXML: only alphaTab (our own writer and reader) and our MusicXML reader were used. .gp3/.gp4/.gp5/GPX sources are not exercised (no redistributable fixtures); the local `Tabs/` extra skips on a clean checkout.
- MusicXML: drum pitch identity is read from the instrument id only; lyrics, chord symbols, octave shifts, brush/arpeggio, whammy PIs and fingering are not compared. No MusicXML import exists.
- Engraving-only data (beams, stems, line breaks, clefs, fonts) is not compared. Rests and annotation-only beats are not compared directly (silence is checked through note onsets and durations).
- MIDI export is not read back. The offline render's `Events()` (needs a running engine) is not run; only its compile step and tempo map.
- Plug-ins are never loaded: state, sidechain, MIDI forward, buses and clips are checked as model data; a real missing plug-in or device is only "compiles and keeps the ids".
- Document switching is model level (24 reopens); no UI, undo history or autosave interplay. Crash recovery of interrupted pair saves is covered by `SelfTestPersistence`, not here.
- Large project is 10 tracks x 180 bars (17,000 notes: .tforge 0.6-1 s, clean .gp 3 s); nothing near the 1,000-bar limit or 128 MiB.
