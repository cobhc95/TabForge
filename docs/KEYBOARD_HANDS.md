# Keyboard mode: which hand plays each note

`Source/src/TabForge.Core/KeyboardMode/Hands` decides, for any part shown as keyboard notes (piano, guitar, bass, strings; sounding pitches with start and length), which notes the left hand plays and which the right, with no per-song settings. Entry point: `HandAssigner.Assign(IReadOnlyList<HandNote>)` (or with `HandAssignerOptions`), result index-aligned with the input, input in any order.

## Model

A shortest-path (Viterbi) pass over time, the way an HMM decoder works, with a hand-built cost model whose weights were fitted to labelled music.

- **Chords.** Notes are sorted by start; notes starting within 30 ms of a group's first note are one chord, sorted by pitch.
- **States.** A chord of m notes has m + 1 states: split point k gives the lowest k notes to the left hand and the rest to the right. Inside a chord the hands never cross, and the state space stays tiny (a five-note chord has six states).
- **Context.** Each state keeps the best path into it and both hands' context along that path: centre, last onset, when the last chord ends, the last two chords' lowest and highest notes. This is the usual "Viterbi with path-carried features" approximation: exact for terms between neighbouring chords, close for the rest.
- **Cost.** O(chords x splits^2). Measured: 5000 notes in about 2 ms (Release, one core); a few arrays per call, no LINQ in the loop, no statics.

## Cost terms (defaults in `HandAssignerOptions`)

| Term | Cost | Why |
|---|---|---|
| Chord span | a hand's chord wider than 12 semitones: 4 per semitone squared + 0.5 per semitone; more than 5 notes: 100 each | an octave plus a little is a hand; wider chords are split |
| Held notes | a hand still holding its last chord: held + new notes wider than 2 semitones cost 2 per semitone squared | a sustained note occupies the hand, so new notes go to the free hand |
| Reach | the new notes and the hand's last two chords that started within 400 ms, wider than 12 semitones: 2 per semitone | fast figures (arpeggios, Alberti bass) stay within one hand's reach |
| Jump | a move of the hand's centre beyond 12 semitones: 0.1 per semitone x 100 / (100 + ms since that hand's last onset) | big leaps need time; rests and long notes allow repositioning |
| Register | left above E4 (64) or right below C4 (60): 1 per octave; a 0.1-per-octave slope from middle C breaks ties | a soft prior, not a split at middle C |
| Crossing | left centre above right centre: 3 per semitone | hands cross only when it pays |
| Line | a note within 3 semitones of the previous chord's nearest note (less than 1.6 s earlier) but in the other hand: up to 2 | a melody or bass line stays in one hand, repeated notes too |

One-hand passages (all high, all low, or a tune around middle C) stay in one hand: the line term keeps the line together and the idle hand gains nothing by taking over.

## Ground truth and fitting

The repository has no piano parts with hand information: the bundled demo song has no piano, and the songs in the local `Tabs` folder (not tracked, third-party) store piano covers as single tracks. The one file with "Keyboard LH" / "Keyboard RH" tracks is a two-layer synth arrangement whose "left" track sits above the "right" one in 121 of 241 shared onsets, so it is not a hand split and was left out.

So the evaluation set is written in code: `HandEvalSet` in the former full-suite keyboard-hands test (removed in Batch 2 ([canonical note](TESTING.md#removed-in-batch-2-canonical-note))), 17 short pieces, 1182 notes, each note labelled with its grand-staff hand (upper staff right, lower staff left). Textures are modelled on public-domain openings (a C major prelude in broken chords where the left hand plays above middle C, a 3/8 bagatelle with hands handing over in middle C, an Alberti-bass sonatina, an E minor prelude with left-hand chords up to E4, a slow 3/4 piece with bass and chords, a chorale, a waltz, a two-part invention starting on middle C, a minuet, a nocturne with wide left-hand arpeggios), plus drills (scales in octaves, a right-hand-only tune, a left-hand-only bass line, walking bass, wide chords) and the demo song's piano part from `FullDemoSongFactory.LeadKeys.cs` (voice 1 right, voice 2 left).

Fitting: coordinate descent over the 20 constants (each tried at x0, 0.5, 0.7, 1.4, 2 or +-1, +-2 for integers), maximising the share of notes with their written hand, from hand-chosen starting values, until no step improves. Rounded afterwards with no loss. A prototype of the same model (Python) did the search; the C# port gives the identical result.

The "learned component" is this fit: about 20 weights against 1182 labelled notes. A neural network would need far more labelled two-hand music than exists here and would add a dependency, so it was not used.

## Measured accuracy (share of notes with the written hand)

| Model | Evaluation set |
|---|---|
| "Below middle C is left" | 85.4 % |
| Cost model, hand-chosen starting weights | 84.7 % |
| Cost model, fitted weights (shipped) | **97.8 %** |
| Fitted, each piece left out of its own fit (17 folds) | 95.9 % |

The accuracy test (`TestKeyboardHandsAccuracy`) requires at least 97 % and at least 10 points over the middle-C rule.

Remaining errors (26 notes) are real alternatives a pianist could choose: the prelude's second note taken by the right hand in its first bar, a slow piece's off-beat chords taken by the idle right hand before the melody enters, chorale tenor notes a tenth above the bass given to the right hand, two middle-C notes of the bagatelle, and four notes where the Alberti bass or the minuet bass meets the melody.

## Known limits

- Deliberate hand crossings (left hand over the right for a melody note) are not predicted: the crossing cost makes them lose unless the right hand is busy.
- A hand's context is its last two chords; a long-held bass under a passage in the same hand is seen only while it is the hand's last chord.
- The evaluation set is small and written from memory, so 97.8 % is an estimate; the leave-one-out 95.9 % is the fairer number for unseen music.
- Parts that are really two instruments (synth layers, two guitars merged) have no "right" answer; the engine gives a playable split.
- Notes are chords only when they start within 30 ms; a strongly spread (rolled) chord is a run of single notes.

## Tuning

Change a constant with `new HandAssignerOptions { ... }` and run `--only TestKeyboardHands,TestKeyboardHandsAccuracy` (full-suite build). To refit, add labelled pieces to `HandEvalSet` (format in its header), repeat the coordinate descent, and update the defaults, this page and the accuracy threshold together.
