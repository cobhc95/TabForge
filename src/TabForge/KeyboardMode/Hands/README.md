# KeyboardMode/Hands (hand assignment)

Decides, for any part shown as keyboard notes (piano, guitar, bass, strings, at sounding pitches), which notes the left hand plays and which the right, with no per-song settings. Pure and deterministic: no WPF, no statics that change. Model, cost terms, fitting and measured accuracy are in the design note KEYBOARD_HANDS.md (docs folder, not in the public tree); the cost terms are summarised on `HandAssignerOptions`.

Does not own the notes (`KeyboardNoteSource`), the hand colours or any drawing (the Keyboard mode view calls `HandAssigner.Assign`).

## Files
| File | Purpose |
| --- | --- |
| `HandTypes.cs` | `Hand`, `HandNote` (pitch, start, length in ms) and `HandAssignerOptions` (the cost model's fitted constants) |
| `HandAssigner.cs` | `HandAssigner.Assign(notes[, options])`: a Viterbi pass over the chords in time order; a state is a chord's split point (lowest k notes left), carrying both hands' context along its best path. O(chords x splits^2), allocation of a few arrays per call |

## Use
`Hand[] hands = HandAssigner.Assign(notes);` the result is index-aligned with the input, which may be in any order. Notes starting within 30 ms are one chord.

## Tests
`TestKeyboardHands` (one-hand passages, melody over bass, wide chord, arpeggio, contrary motion, repeated notes, strum, unsorted input, determinism, 5000 notes under 50 ms) and `TestKeyboardHandsAccuracy` (the evaluation set in `HandEvalSet` in `tests/full-suite/KeyboardMode/SelfTestKeyboardHands.cs`, at least 97 %).
