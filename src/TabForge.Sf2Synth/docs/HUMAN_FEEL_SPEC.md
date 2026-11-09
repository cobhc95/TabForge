# Human feel spec (humanizer v2)

Numbers that drive the synth humanizer (`TabForge.Sf2Synth/Humanize`, values in `HumanTable`). Levels are given in dB first:
the SF2 default velocity curve is a 40·log10 law (attenuation = 40·log10(127/v) dB), so near v = 90 one dB is about 5 velocity
steps and near v = 50 about 3. The model works in dB and converts at the end. "Uncertain" marks a number with weak support.

## 1. Measured facts

### Timing
| Quantity | Value | Source |
|---|---|---|
| Interval std, virtuoso hi-hat 16ths (IOI 157 ms) | 8.7 ms | https://pmc.ncbi.nlm.nih.gov/articles/PMC4454559 |
| Interval std, drummer tapping to a metronome at 180 BPM | 15.6 ms | same (cites Hennig 2011) |
| Short-range DFA exponent of intervals (anticorrelated) | α = 0.31 | same |
| Long-range DFA exponent of intervals (persistent) | α = 0.72, crossover 5-8 s (~0.1 Hz) | same |
| Lag-1 correlation of consecutive intervals | -0.48 | same |
| Bar-to-bar correlation of the timing pattern (2-bar phrases) | 0.48 | same |
| Listeners prefer long-range-correlated deviations over white noise | significant, 39 listeners | https://pmc.ncbi.nlm.nih.gov/articles/PMC3202537 |
| Onset deviation std, professional jazz pianist, 12 pieces | 15-22 ms (mean 18.5) | https://pmc.ncbi.nlm.nih.gov/articles/PMC6934603 |
| Mean offset ahead/behind the beat per piece | at most about ±10 ms | same |
| Microtiming variability: jazz larger than rock | qualitative | same |
| Share of drum microtiming that is a recurring pattern (systematic) | up to 65 % | https://labs.sonicfield.org/library/quantifying-microtiming-patterning-and-variability-in-drum-kit-recordings (Hellmer et al. 2015) |
| Swing ratio, pianist | 1.57-2.11, std within piece 0.33-0.58; falls with tempo | https://pmc.ncbi.nlm.nih.gov/articles/PMC6934603 |
| Swing ratio, ride cymbal | up to 3.5:1 slow, 1:1 fast; 2:1 only at one tempo | https://acoustics.org/pressroom/httpdocs/137th/friberg.html |
| Laid-back guitar strokes: softer, longer movement, slower attack | qualitative, 21 experts | https://doaj.org/article/29bdc0cd13114e52ae261ea3945c3236 |
| Pushed bass strokes: louder | qualitative | https://www.uio.no/ritmo/english/projects/time/ |
| Two fast attacks ≥ 40 ms apart are heard as two events | threshold | https://www.uio.no/ritmo/english/projects/time/ (Danielsen et al.; uncertain citation path) |

### Strumming and picking
| Quantity | Value | Source |
|---|---|---|
| Block chords are almost never simultaneous ("spread interval") | qualitative, 1 ms hexaphonic analysis | https://independent.academia.edu/AndersonReis (SMC 2014) |
| Downward strum gestures faster than upward | qualitative | https://pmc.ncbi.nlm.nih.gov/articles/PMC7583031 |
| Down/up pick-speed symmetry tracks skill; beginners lose speed on upstrokes | qualitative | https://openaccess-api.cms-conferences.org/articles/download/978-1-964867-99-1_5 |
| Engineering convention: delay per string | ~20 ms (example) | https://sfzformat.com/tutorials/strum (convention, not measurement) |
| Engineering convention: delay per string | 2.2 % of the strum period | https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/7420114 (convention) |
| Measured inter-string delay per direction in ms | not found in literature | uncertain: use owner data below |
| Down vs up loudness asymmetry in dB | not found (forum reports only) | uncertain |

### Dynamics
| Quantity | Value | Source |
|---|---|---|
| Long-range DFA exponent of hit amplitudes | α = 0.63 | https://pmc.ncbi.nlm.nih.gov/articles/PMC4454559 |
| Bar-to-bar correlation of the accent pattern | 0.88 (much more stable than timing) | same |
| Pitch/loudness fluctuate as 1/f (Voss) | qualitative | https://pmc.ncbi.nlm.nih.gov/articles/PMC4454559 (cited) |

### Owner DI measurement (8.5 s, single guitar, clean DI, mono)
Onsets by log-spectral flux (2.5 ms hop), level = peak RMS (5 ms) in the 40 ms after each onset. 68 detections, 47 confident
(≥ 6 dB rise). Pitch (autocorrelation f0) explains almost none of the level spread (4.34 → 4.21 dB), so it is touch.
| Quantity | Value | Source |
|---|---|---|
| Attack level std | 4.3 dB confident, 6.1 dB all | owner DI measurement |
| Attack level range | 19 dB confident, 23 dB all | owner DI measurement |
| Same as SF2 velocity (loudest = 120) | std 19-25; p10/50/90 = 66/95/116 (all: 45/85/113) | owner DI measurement |
| Distribution shape | skewed to soft side, skew -0.8 to -1.6 (ceiling + soft tail) | owner DI measurement |
| Lag-1 / lag-2 level correlation | ~0 (0.08 / -0.29) / +0.28: a repeating pattern, not a smooth drift | owner DI measurement |
| Pulse | ~175 ms with an 88 ms subdivision (meter unknown, no score) | owner DI measurement |
| Onsets within 25 ms of the 88 ms grid | 65 %; their std 11.7 ms, mean -1.4 ms | owner DI measurement |
| All onsets vs the grid | std 22 ms (includes ornaments / off-grid notes) | owner DI measurement |
| Accent by pulse position | the two positions differ by 5-7 dB | owner DI measurement |
| Level vs preceding gap | r = +0.31 (longer gap → louder) | owner DI measurement |
| Decay from attack to just before the next onset | 15 dB (std 6): notes ring into each other | owner DI measurement |
| Attack rise to -3 dB of peak | median 25 ms, p10 16, p90 51 (multi-string spread) | owner DI measurement |
| Onsets with 1-3 extra flux peaks within 60 ms | 66 % | owner DI measurement |
Cannot tell from 8.5 s mono: strum direction and string order, down/up asymmetry, true meter and beat phase, long-range (1/f)
behaviour (needs minutes; crossover is 5-8 s), tempo drift, fatigue. Some off-grid detections may be slides or false onsets.

## 2. Recommended generative model

Per channel, per note: level (dB) and onset offset (ms) are sums of independent named parts, each with its own stated
amplitude; parts are deterministic from the seed. All amplitudes scale with the preset factor k (tight 0.6, natural 1, loose 1.5).

### Velocity (dB domain, then back through the SF2 curve, clamp 1-127)
| Part | Natural value | Notes |
|---|---|---|
| Base | input velocity | the written dynamic stays the centre |
| Metric accent | downbeat +2.5, beat +1, 8th off -1.5, 16th off -3 dB | rock/pop strum: beats 2 and 4 +1.5 extra when the style is backbeat |
| Bar pattern memory | per-position offset drawn once per section (σ 1.5 dB), reused each bar with correlation 0.85 | the main source of "same player" feel (0.88 measured) |
| Phrase arch | ±1.5-2 dB over 2-4 bars: rise into the phrase peak, fall over the last bar | restart after a rest ≥ 0.6 s |
| 1/f noise | pink (α ≈ 0.65), σ 1.5 dB | e.g. sum of 5-6 octave-spaced random walks with reversion |
| Per-note white noise | σ 1.2 dB | |
| Down/up asymmetry | up -1.0 dB (tight -0.5, loose -2) | uncertain; pros are symmetric, amateurs not |
| Speed damping | IOI < 90 ms: -2 dB; 90-250 ms: linear to 0; gap > 400 ms: +1 dB | owner r = +0.31 |
| Ceiling | soft-limit the top: above base +4 dB compress 3:1 | gives the measured soft-side skew |
| Ghost notes | 16th offbeats in strum patterns: 8 % chance (loose 15 %) at -12 dB | never on beats, never on the first note of a phrase |
| Palm-mute chugs | white σ 0.8 dB, accent pattern only; down-picked: no asymmetry | |
Target totals: level std 2.5 / 4 / 5.5 dB (tight/natural/loose) = about 12 / 19 / 25 velocity steps at v ≈ 90.
Remove the ±12 velocity cap (MaxDelta); cap at ±10 dB instead.

### Timing (ms; positive = late)
| Part | Natural value | Notes |
|---|---|---|
| Feel bias | 0 straight; laid-back +8 (range +5..+15); pushed -5 | laid-back also -1 dB and strum ×1.2 |
| Bar pattern memory | per-position mean offset σ 4 ms, reused each bar, correlation 0.5 | ~half of the variance is systematic |
| Long-range part | pink, σ 5 ms, DFA α ≈ 0.7 beyond ~16 notes | |
| Short-range jitter | σ 5 ms, made anticorrelated: e_n = w_n - 0.5·w_(n-1) | lag-1 interval correlation ≈ -0.45 |
| Tempo drift | slow walk of local tempo, ±1 % over 8 bars, reverting; cumulative error bounded ±20 ms | the grid stays the reference |
| Fast notes | IOI < 120 ms: jitter ×0.7 | fast playing is motor-locked |
| Swing (only if the score asks) | ratio = clamp(2.8 - 0.009·(BPM-60), 1.2, 3.0); per-note σ 0.3 | falls with tempo |
Target onset std vs grid: tight 6-8, natural 11-14 (owner 11.7), loose 18-24 ms.
Since v3 the humanizer is a pass over the written notes before playback, so early notes are simply scheduled earlier (no look-ahead,
no latency); v3's structural model and numbers are in `MEASUREMENTS.md` and the library README.

### Strum (chords of ≥ 3 notes arriving together)
| Part | Natural value | Notes |
|---|---|---|
| Total spread, 6 strings, downstroke | 25 ms (tight 14, loose 45); owner median 25, p10 16, p90 51 | |
| Dynamics | spread × 1.4 at pp, × 0.7 at ff (interpolate in dB) | faster hand = louder |
| Tempo | spread ≤ 30 % of the IOI to the next stroke | fast 16th strumming tightens |
| Upstroke | spread × 0.8, high strings first; lowest 2 strings -6 dB, 30 % chance the lowest is skipped | |
| Gaps | first gap longest (×1.3), last shortest (×0.8), each gap jitter 20 %; no gap ≥ 40 ms | perceptual fusion limit |
| Per-string level | first-hit string +1 dB, last -1 dB, each string σ 1 dB | uncertain |
| Direction | from the score if marked; else alternate on the subdivision grid (on-grid = down) | |
| Thumb / fingers (nylon, bass) | spread × 1.5, level σ ×1.2 | uncertain |
Bass (GM 32-39): no strum; timing σ ×0.8; feel bias +3 ms behind; finger alternation ±0.5 dB.

### Voice variation (keep v1 parts)
Per-note detune σ 3 cents falling to 0 in 150 ms; brightness tied to the final level (+1 dB → +4 % brightness); pick noise
level follows the level; no vibrato on short notes.

### Leave human input alone
Measure a window of 16 notes per channel. Human if two of: velocity std ≥ 6 steps, ≥ 7 distinct velocities, longest run of
equal velocities ≤ 3, < 30 % of intervals within 0.5 ms of a simple ratio to a pulse. Flat if velocities sit on ≤ 4 levels
(typical of imported tab files, which store dynamic marks) or intervals are exact. Blend each part by a 0..1 smoothstep
between these limits, separately for velocity and timing; a human part gets 0, never "added on top".

### What to test objectively (on a 64-bar flat 8th strum and a flat 16th single-note line, fixed seed)
Status (`HumanizerTests`, Nov 2026; numbers in `MEASUREMENTS.md`):
- [x] level std in dB and in velocity per preset hits the targets ±15 %; tight < natural < loose.
- [x] onset std vs grid per preset; lag-1 interval correlation between -0.6 and -0.3; DFA α of offsets over 512+ notes: short < 0.5, long 0.6-0.9.
- [x] bar-to-bar correlation of per-position level 0.7-0.95, of timing 0.3-0.7.
- [x] strum spread per direction (median), no inter-string gap ≥ 40 ms, upstroke spread < downstroke. [ ] percentiles per dynamic.
- [x] ghost rate within ±3 % of the setting; no ghost on beats (beats no lower than 8th offbeats in the output).
- [x] no note-on before allowed, delays ≤ budget, note-offs never before their note-on (6000-event fuzz), determinism across Reset.
- [x] a human-played file (velocity std ≥ 15, jittered timing) passes through unchanged (0 steps, 0 ms).
- [ ] render-level check: run the onset/level analysis above on rendered audio; level std 3-5 dB at natural.

## 3. What the first version did wrong
- Velocity std 4-5 steps (≈ 1 dB): about a quarter of the measured 19-25 steps (4-6 dB); the ±12 cap made it impossible to reach.
- Worked in velocity steps, not dB, so the same change is uneven across the range.
- Timing could only be late (0-21 ms), so no early notes, no centred jitter and no drift; the feel was a constant lag plus small noise.
- Jitter was white, small (2-5 ms) and uniform; humans show 9-15 ms with short-term anticorrelation and long-range persistence.
- No bar pattern memory, so accents changed bar to bar instead of repeating (measured 0.88 correlation).
- Strum spread 4-15 ms: below the owner's 16-51 ms; same for both directions and dynamics; all strings always hit.
- No ghost notes, no ceiling/soft tail in the level distribution, no speed damping, no laid-back/pushed coupling with level.
