# Advanced MIDI-to-tab conversion — plan (not built yet)

> **Status 2026-10-22:** the clip menu entry *Advanced notation conversion…* exists (`Source/src/TabForge.Core/Menus/TimelineMenus.cs`). Whether the conversion itself is built was not verified; the text below is the original plan.

Status: **planned only**. The clip context menu already has *Write into the track's notation* (basic) and an
*Advanced notation conversion…* entry that only explains this is coming. Nothing below is implemented.

## What exists today

`Services/MidiClipToTab.cs`: notes are quantised to the 16th grid, notes starting together become one beat, a beat lasts
until the next beat (longest plain duration that fits), and each pitch goes to the free string with the lowest fret.
Drum tracks use the note number as the drum. It overwrites the cells it lands on. It has no idea of hand position, key,
tuplets, voices, ties across bars, or rests.

## Goal

A window, opened from the clip's context menu, where the user sees the converted tab **live**, picks rules and presets,
and applies it. It should read like something a guitarist would play: sensible positions, no jumping around the neck,
ties across bars, clean rests, and no bars overfilled.

## Research: what already exists

**Tab / fingering assignment (which string and fret):**
- Optimal-path methods (Sayegh 1989; Radicioni et al.; the KTH thesis "An Algorithm for Optimal Guitar Fingering"): every note has
  several candidate (string, fret) positions; a graph of candidates is searched by dynamic programming with a cost for hand movement.
  Costs: fret distance between consecutive positions, position shifts, stretch (span of a chord), open strings preferred, high frets penalised.
- HMM / Viterbi (Hori & Sagayama, ISMIR 2016 "Minimax Viterbi", SMC 2017 "Lp-Viterbi"): hidden state = left-hand form; the minimax
  variant minimises the *hardest* move instead of the total, which suits playability. Lp-Viterbi blends both.
- Genetic algorithms (Tuohy & Potter): playable tab as an optimisation problem; slower, less predictable.
- Learning-based (2024-25): masked-language-model tab inference trained on the GuitarSet-like data, Fretting-Transformer (encoder-decoder),
  "Tablature generation from lead sheets". Better musicality on real data, but need a model file and inference time; poor fit for a small local app.
- MIDI import in other tab editors: essentially "lowest fret, free string" plus a track tuning. This is what TabForge does today.

**Rhythm (quantising, tuplets, voices):**
- A notation editor's MIDI import: quantise to a chosen maximum resolution (quarter to 128th) with an *adaptive* grid (smaller for short notes);
  a *search tuplets* option that fits 3, 5, 7-tuplets with their own grid; a *max voices* option; duration split across bar lines with ties;
  rest filling. Known weak spots: non-aligned triplets and large quantise values confusing tuplet detection.
- Standard ideas to reuse: inter-onset-interval clustering, tempo-relative grid, note-length snapping to the notated duration,
  legato/overlap resolution (a note held over the next one: tie vs new beat), voice separation by pitch contour (melody vs bass/chords).

## Proposed design

**Two stages, separable and testable without any UI** (`Services/MidiTab/*`):

1. **Rhythm stage** (notes in seconds -> notated beats): tempo/time-signature from the song's bars (the clip is placed in the song already);
   grid choice (*Off / 1/8 / 1/16 / 1/32 / Adaptive*), tuplet search (on/off), swing/triplet-feel option, minimum note length,
   overlap handling (*cut / tie / new beat*), chord-onset window (notes within N ms form a chord), rest rules, ties across bars,
   humanise-fix ("straighten") strength. Output: beats with duration, dots, tuplet ratio, tie flags, per bar, guaranteed to fill the bar.
2. **Fingering stage** (pitches -> strings/frets): candidates per pitch from the track's tuning, capo and fret count; scoring = Viterbi over
   hand positions with these knobs: *preferred position* (open / low / mid / high / follow the melody), *max fret*, *max stretch* (frets across a chord),
   *prefer open strings*, *avoid string skipping*, *keep a chord on adjacent strings*, *penalty per position shift*, *key-aware* (prefer the scale box of the
   song's key), *bass on the low strings, melody on the high ones*. Modes: *Playable (minimax)*, *Smooth (total cost)*, *Lowest fret* (today's behaviour).
   Techniques it may add on request: hammer/pull for fast stepwise notes on one string, slides for same-string jumps, palm mute is not guessed.

**Presets** (each is just a bundle of the knobs above): *Rhythm guitar (chords, low positions)*, *Lead (single notes, follow the melody)*,
*Bass*, *Fingerstyle (chords on adjacent strings, bass on low strings)*, *Drums (GM map)*, *Keep it simple (lowest fret)*, *Custom* (user presets saved).

**The window** (WPF, theme colours, nothing per-frame):
- Left: the presets list and the knobs, grouped *Rhythm* / *Fingering* / *Result*, with a short explanation of each.
- Centre: a live preview of the converted bars using the existing tab renderer on a temporary track (read-only), with the changed notes highlighted;
  above it the original notes as a piano-roll strip aligned bar by bar, so the user sees what became what.
- Right / bottom: warnings (bars that don't fill, notes with no playable position, big position jumps), and per-bar or per-note overrides
  (click a note to choose another string/fret from its candidates; the neighbours re-optimise).
- Buttons: *Apply*, *Apply to selection only*, *Cancel*, *Save preset*. Apply is one undo step and can *replace* or *merge* into existing bars
  (today it overwrites silently). An *Auto-convert new MIDI recordings* check box (also in Settings) runs the last used preset when a take finishes.
- Optional snapping is the rhythm grid; *Off* keeps the raw timing (the bar may then be marked as a free/incomplete bar).

**Where it plugs in:** `MainWindow.Clips.cs` (the menu entry already exists), `MidiClipToTab` becomes the "Lowest fret + 16th grid" preset,
`ProjectValidator` unchanged (it only produces normal notation), settings under *Editing* for the auto-convert option and the default preset.

## Suggested build order

1. Rhythm stage with tests (grid, chords, ties across bars, bars always fill, tuplet search) - biggest quality gain.
2. Fingering stage: candidates + Viterbi with the position-shift cost; minimax variant; tests on known passages (open chords, a scale, a riff).
3. Presets, then the window with the live preview; per-note overrides last.
4. Auto-convert on record, merge mode, and hotkeys/docs (every new command bindable and in `TOOLS_AND_HOTKEYS.md`).

## Open questions for the user (answer before building)

- Default behaviour when converting into bars that already have notes: replace, merge, or ask?
- Should tuplet detection be on by default, or opt-in (it can misread swung playing)?
- Is a guitar-only first version fine (bass next, drums use the GM map already), or must all instruments be in the first version?
- Machine learning (a model file shipped with the app) is out of scope for the first version unless you want it.

## Sources (for the research)

- Hori & Sagayama, *Minimax Viterbi algorithm for HMM-based guitar fingering decision*, ISMIR 2016: https://archives.ismir.net/ismir2016/paper/000285.pdf
- Hori, *Lp-Viterbi algorithm for automatic fingering decision*, SMC 2017: http://smc2017.aalto.fi/media/materials/proceedings/SMC17_p386.pdf
- *An Algorithm for Optimal Guitar Fingering* (KTH): https://www.diva-portal.org/smash/get/diva2:668903/FULLTEXT01.pdf
- Riley et al., *MIDI-to-Tab: guitar tablature inference via masked language modeling* (2024): https://qmro.qmul.ac.uk/xmlui/bitstream/handle/123456789/97939/Riley%20GAPS%20A%20Large%202024%20Accepted.pdf?sequence=2
- *Fretting-Transformer: encoder-decoder model for MIDI to tablature*: https://arxiv.org/html/2506.14223
- Tuohy & Potter, *A genetic algorithm for the automatic generation of playable guitar tablature*: https://www.researchgate.net/publication/251423049
