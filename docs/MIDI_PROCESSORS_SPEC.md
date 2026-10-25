# TabForge MIDI Processor Catalog - Functional Spec

Sources: a reference host `InstallData/Effects` JS source (read directly: desc/slider lines and @block behaviour) plus
`InstallData/Data` (ix_scales, ix_sequences, ix_keymaps). Two of its processors are binaries; their
entries come from the user's brief / general knowledge and are marked (K). Parameter ranges below are the reference host's.

Conventions: `ch` = MIDI channel (1-16, 0 = omni where noted). Processors run on the audio thread, per block, with
sample-accurate event offsets. "Pairing" = every note-off leaves with the pitch/channel its note-on actually left with.
Flags: T = needs tempo/transport, A = needs audio input, S = stateful, G = generative/random.

## 1. Reference-host plug-in -> unified processor

| Host plug-in | What it does | Parameters (compact) | TabForge processor |
|---|---|---|---|
| MIDI Control (K) | Channel filter, bank/program, transpose, snap to scale, CC sliders, sysex, all-notes-off | ch filter, bank/prog + .reabank, transpose, snap key+scale, 4+ CC sliders (vol/pan/pitch/assignable) + raw mode, sysex (+on play start), panic, log | Channel Filter/Remap, Program/Bank Select, Transpose, Scale Snap, CC Sender, Sysex Sender, Panic, Monitor |
| Channel Filter (CodeFN42) (K) | Per-channel enable/disable and remap of 16 channels | 16 in-ch -> out-ch/off | Channel Filter/Remap |
| Audio To MIDI Drum Trigger | Audio transient -> note | open/close thr dB -60..6 (-17/-18), retrigger 0-200ms (30), orig mix 0-100%, ch 1-16 (10), note 0-127 (69), peak interval 0.1-7, align -5..5ms | Audio Drum Trigger |
| Loop Sampler w/MIDI Triggers | Audio loop recorder/player, MIDI-triggered | vol -120..12dB, speed -8..8, start/end 0-30000ms, first trigger note (50), overlap 0-1000ms, silence thr, state 0-5, ch 0-16 | Audio Loop Sampler |
| MIDI Arpeggiator | Arps held notes | rate 0-16 x BPM (1), length 0.01-1, mode Down/Up/DownAlt/UpAlt, variants 0-3 with +-64 st offsets, velocity 0-127 (127; 0=played) | Arpeggiator |
| MIDI CC LFO Generator | LFO -> CC | ch, CC (1), center 0-127, range +-0-127, off value, shape sine, freq 0-32 (1), Hz/Beats, updates/beat 1-512 (64), on/off | CC LFO |
| MIDI CC Mapper | CC in -> CC out with clamp | src CC (1), dst CC (1), clamp lo 0 / hi 127, pass-through source Y/N | CC Mapper |
| MIDI Choke | Note-on in a choke set kills notes in an affected set | ch, choke start 42 + count 1-16, affected start 46 + count, action during choke (block/allow), 4 extra choke notes (-1 off) | Choke |
| MIDI Choke Group | Mutually exclusive note range (mono-per-group) | ch, start 60, count 1-128 (8) | Choke Group |
| MIDI Chord In Key | Adds 2 chord notes diatonic to a key | steps for note 2 (+2) and 3 (+4), range -24..24, key C..B, vel scale 0-1, low/high key | Chord (diatonic mode) |
| MIDI Chorderizer | Adds up to 4 fixed-interval voices | voice 1-4 offsets 1-24 st (5,0,0,0; 0=off), vel scale @1 and @4, low/high key | Chord (fixed interval mode) |
| MIDI Delay | Delays all MIDI (ms, QN, samples) | ms 0-1000, QN 0-16 (.25), samples 0-10000, ch (0=omni), bus | MIDI Delay |
| MIDI Duplicate Note Filter | Drops a note-on while the same note is already on; drops the paired extra note-off | in ch | Note Sanitizer |
| MIDI EQ Ducker | Notes trigger an EQ/gate/pump envelope on audio | note (All/0-127), attack 0-75ms, atk shape 0-4, release 0-500ms, rel shape, freq coarse+fine, width 0-2 oct, vol -32..32dB, mode Peak/LowShelf/HighShelf/Gate/Pump, vel-react | Audio Note Ducker |
| MIDI Examiner | Shows the last MIDI event decoded | offset, status, data1/2, high/low bits, type | Monitor |
| MIDI Logger | Scrolling event log with note analysis | note analysis mode 0-2, 10000-entry history | Monitor |
| MIDI Map To Key | Maps every note to nearest note of a key | key C..B, low/high key | Scale Snap (Remap) |
| MIDI Map To Key v2 | Per-pitch remap table from a keymap file | in ch, mapping file, per-note in/out editor, reload | Note Map (table remap) |
| MIDI Modal Randomness | Turns input notes into decaying random modal note clouds | 4 intervals (1,1.5,2..7) with probabilities, speed, octave/timing/velocity randomness, decay 0-600s, simultaneous notes 0-8, quantize targets/beat, quantize strength | Modal Randomizer |
| MIDI Note Filter | Passes only notes in a range | low 21 / high 108, others pass Y/N | Note Range Filter |
| MIDI Note Hold | Mono legato-hold: previous note released only when next arrives | ch (0=omni) | Note Hold |
| MIDI Note Randomize | Replaces a trigger note with random pitch in a range | in note 60, in ch, low 48/high 72, mix 0-100% | Note Randomizer |
| MIDI Note Repeater | Retriggers held notes each N beats | size 0.1-4 beats | Note Repeater |
| MIDI note sanitizer | Removes duplicate note-ons/offs; optional retrigger threshold | retrigger 0-128 1/32nds (0=never retrigger) | Note Sanitizer |
| MIDI Note-On Delay | Random delay on note-ons only | max 0-4096 samples | Note-On Jitter (part of Timing Randomizer) |
| MIDI Pattern/Scale Variation Generator | Replaces notes by scale degrees across octave range, with accent pattern | in ch, note range, root, scale file, low/high octave 0-10 (5/5), sequence file, on/off | Scale Variation |
| MIDI Pitch Wheel LFO | LFO -> pitch bend | ch, max bend 0-100%, freq 0-24 (1), Hz/Beats, updates/beat, on/off | Pitch Bend LFO (same engine as CC LFO) |
| MIDI Program/Bank Switch on Load | Sends bank MSB/LSB + program once on load | ch 1-16, MSB, LSB, program, has-sent flag | Program/Bank Select |
| MIDI Route Note To Channel | Sends one note to another channel | note (60), channel 1-16 | Channel Router (per-note rule) |
| MIDI Router/Transpose | Range-limited channel reroute + transpose | in ch, out ch, mode Off/Notes/Non-notes/Both, note range, transpose +-60 | Channel Router + Transpose |
| MIDI Sequencer Baby / v2 / Megababy | Step sequencer with 16 patterns; Megababy adds drum lanes, swing, chaining, MIDI trigger, CC lanes | pattern 0-15, start note 60 (36), length 4-128 (16), notes 1-32, rate 0.125-4, note length 1-100%, piano/drum mode, swing 0-100, steps/beat 1-16, trigger mode 0-9, trigger note start 72, chain, 4 CC lanes (1,7,10,11), start/end beat, drum-map file | Step Sequencer |
| MIDI Snap To Key | Block or remap out-of-scale notes | in ch, note range, root, scale file, Block/Remap, on/off | Scale Snap |
| MIDI Tool / v2 | Note+vel range filter, vel scale/limit/randomize, transpose, random pitch, ch out | in ch (v2: Any), note range, in-vel range + clamp/ignore (v2), vel scaling 0-1000% (v2), random vel 0-100%, out vel range, transpose +-60, random pitch 0-100%, pitch reset (v2), out ch, controller routing (v2: original/dest/both) | Note Range Filter + Velocity Shaper + Transpose + Channel Router |
| MIDI Transpose Notes | Transpose with premultiply (interval scaling), live-tracking | semitones +-64, premultiply -16..16 (1), low/high key | Transpose |
| MIDI Velocity and Timing Humanizer | Random +/- velocity and timing | baseline vel (0=orig), add 1 beat delay, timing bias +-10ms, timing level 0-10, vel level 0-10, ch 0-16 | Humanizer |
| MIDI Velocity Control | y = vel*mul + add, clamped, in key range | mul -16..16 (1), add -128..128, min/max 0-127, low/high key | Velocity Shaper |
| MIDI Velocity Scaler/Compressor | Scale + offset with dry/wet | in ch, scale 0-3 (1), offset +-127, mix 0-100% | Velocity Shaper (mix param) |
| MIDI Velocity Variation Generator | Base velocity + accent sequence + variation | in ch, note range, base 64, variation 0-100%, sequence file, on/off | Velocity Shaper (pattern mode) |
| Super8 looper | 8-channel synchronized audio looper, MIDI/CC/PC-mapped | sync off/project/playback, click count 0-64, per-channel mappings: record/length, play, monitor-select, reverse; halve/double/x-fade | Audio Looper (out of first phase) |

## 2. Unified catalog

Shared params on every note-touching processor: **Input channel** (Any/1-16, default Any), **Bypass**. Unless stated,
non-note events pass through unchanged. Universal safety: on bypass, param change that alters a held note's output,
transport stop/seek, or processor removal, emit a note-off for every currently sounding *output* note (pairing table),
then clear state. "Held table" = per (channel, input-note) record of {output note, output channel, velocity}.

### Filter / Routing
- **Channel Filter/Remap** (S): 16 rows, each: enabled + out channel (1-16, default identity). Notes and all channel messages follow the row. A note-off is routed by the channel recorded at its note-on (held table), so changing the table mid-note never strands a note. Sysex/system messages pass. Also a "solo channel" shortcut (MIDI Control filter).
- **Channel Router**: rule list; rule = in ch, out ch, scope (Off/Notes/Non-notes/Both), note range 0-127 (0..127), transpose -60..60, optional single-note match (Route Note To Channel). First matching rule wins. Note-offs use held table (fixes the source Router, which re-derives off-pitch from current settings).
- **Note Range Filter**: low 21 / high 108 (0-127), "other events pass" Yes. Out-of-range note-on dropped; its note-off dropped via held table (never orphan an off nor drop an off for a passed on).
- **Choke** (S): channel 1-16; choke set (start 42, count 1-16 + up to 4 extra notes, -1 = none); affected set (start 46, count 1-16); action during choke = Block (default) / Allow note-on. Choke-set note-on (first one, choke count 0->1) sends note-off for every sounding affected note, and while any choke note is held, affected note-ons are suppressed (and their offs) if Block. Choke note-off decrements count. Note-off of an already-choked note is swallowed.
- **Choke Group** (S): channel, start 60, count 1-128 (8). Note-on in range sends note-off for the previously active note of that range then becomes current; note-offs in range are swallowed (one-shot, mono group). Reset current on stop.
- **Note Sanitizer** (S): retrigger threshold 0-128 in 1/32 note (0 = never retrigger). Per (ch,note) on-state. Duplicate note-on while on: dropped, or if beyond threshold, emit note-off then pass (retrigger). Note-off while not on dropped. Also absorbs Duplicate Note Filter. T when threshold > 0.
- **Note Hold** (S): channel (0 omni). Note-on releases previously held note first, output note stays on; note-offs are swallowed. Only the last note remains sounding. Emit off on removal.
- **Program/Bank Select**: ch, bank MSB, LSB, program (0-127), .reabank loader for names, "send on load/play start/every start", manual Send button. Emits CC0, CC32, PC. Idempotent per session via "has sent" flag; reset flag on load.
- **Sysex Sender / Panic** (K): hex text, send now / at playback start; Panic = CC123 + CC120 + note-offs for held table on all channels.

### Pitch
- **Transpose** (S): semitones -64..64, premultiply -16..16 (default 1; out = in*mult + semis, clamp 0-127), low/high key 0-127. Held table {in note -> out note}; note-off uses the recorded out note. If params change while notes are held: note-off old out, note-on new out with same velocity at offset 0 (source behaviour, keep). Clear on seek (>1.5 blocks jump) with note-offs first.
- **Scale Snap** (S): root C..B, scale (chromatic, major, natural/harmonic/melodic minor, modes, pentatonic maj/min, whole tone + user file list of semitone steps), Mode Block (drop out-of-scale) / Remap (nearest in-scale, tie -> down), note range, on/off; "Map To Key" = Remap with major-key preset. Note-off follows held table.
- **Note Map** (table): 128-entry in->out table (default identity), load/save; in ch. Held table for offs. Absorbs Map To Key v2. Also the vehicle for drum-map conversion (section 3).
- **Chord** (S): mode Fixed (voices 1-4 offsets 1-24 st, 0=off; default 5,0,0,0) or Diatonic (key C..B, step advance for voices 2 and 3 in scale steps -24..24, default 2 and 4 = triad). Velocity scale 0-1 (Fixed: at voice 1 and 4, linearly interpolated). Low/high key limit which played notes get chords. Extra notes clamped 0-127 and dropped when out of range. Note-off releases all chord voices from the held table.
- **Arpeggiator** (S, T): rate 0-16 x (steps per beat), note length 0.01-1, mode Down/Up/Down Alt/Up Alt, variants 0-3 with offsets -64..64 st cycled per pass, velocity 0-127 (0=use played). Holds set of active notes; on each step plays next note, note-off at step + length*step. Held-set changes take effect next step. Emits off on stop or empty set.
- **Scale Variation** (G, S): note range, root, scale, low/high octave 0-10; incoming notes are replaced by a scale degree in the octave range following the chosen accent sequence file. Held table for offs.
- **Note Randomizer** (G): trigger note (0-127), in ch, output range low 48 / high 72, mix 0-100% (probability a trigger is replaced). Offs follow held table.
- **Modal Randomizer** (G, S, T): input notes seed a cloud of random output notes: four (interval, probability) pairs with intervals from {1,1.5,2,2.5,3,4,4.5,5,5.5,6,6.5,7} (0-100%), speed, octave/timing/velocity randomness 0-100, decay 0-600 s, simultaneous notes 0-8, quantize grid 1-8 per beat and strength 0-100. Each generated note gets its own timed off; all flushed on stop.

### Velocity
- **Velocity Shaper**: out = clamp(vel*mul + add, min, max), mul -16..16 (1), add -128..128, min 0-127, max 127, low/high key, in-velocity range with Clamp / Ignore, scaling 0-1000%, random +-0-100%, mix 0-100% (dry/wet against original), pattern mode (base velocity 0-127 + variation 0-100% + accent sequence list, one step per note-on, 0/1 flags). Output vel never < 1 for note-on (0 would mean note-off).

### Timing
- **MIDI Delay** (S, T): delay ms 0-1000 + QN 0-16 (step 0.25) + samples 0-10000, summed; channel 0-16; bus. Delays note-offs equally, so pairing is intrinsic. Flush pending offs on stop.
- **Timing Randomizer / Note-On Jitter** (G): max samples 0-4096 (limited to block). **Decision:** jitter both on and off with the same value per pair, never delay on only (source delays only note-ons and can reorder on/off; do not copy that).
- **Humanizer** (G): baseline velocity (0 = original) 0-127, velocity level 0-10 (random +-64 scaled), timing level 0-10 (up to +-30 ms), timing bias +-10 ms, "add 1 beat delay" (delay everything one beat so early jitter is possible), channel 0-16. Reports last applied offsets (read-only meters). Note-off gets the same time shift as its note-on plus its own length.
- **Note Repeater** (S, T): size 0.1-4 beats. While a note is held: re-send note-off then note-on with original velocity every size beats. Off on release.

### Generators
- **Step Sequencer** (S, T): patterns 0-15, sequence length 4-128 steps, notes 1-32 lanes, start note (60 piano / 36 drum), rate 0.125-4, steps/beat 1-16, note length 1-100%, swing 0-100, mode piano-roll/drum-map, trigger mode (Off / pattern change / +transpose / +resync / quantized resync / always recording), trigger note start 72 (notes 72.. select patterns 0-15), chain, hold threshold 50-1000 ms, 4 CC lanes (default 1,7,10,11), start/end beat, play-before-start, drum-map file. Every step-note off is scheduled; all flushed on stop/pattern change.
- **CC LFO** / **Pitch Bend LFO**: target CC 0-127 (or pitch bend, 14 bit; max bend 0-100%), ch, center 0-127, range +-0-127, off value (sent when disabled), shape sine (extend: tri/saw/square/S&H), freq 0-32 Hz (or synced Beats), updates/beat 1-512 (default 64), on/off. Only emits when the quantized value changes. Sends off value / bend centre on disable and stop. T when synced.
- **Modal Randomizer**, **Arpeggiator**, **Scale Variation**, **Note Randomizer**: listed above.

### Controllers
- **CC Sender** (K): rows of {CC, value 0-127 or 14-bit}; presets volume(7), pan(10), pitch bend, assignable; raw mode = literal status/data bytes. Sends on change and on playback start.
- **CC Mapper**: src CC, dst CC, clamp low 0 / high 127 (value clamped, not scaled), pass source Yes/No.

### Drums / Audio-coupled  (A = needs audio)
- **Audio Drum Trigger** (A): open thr -17 dB, close thr -18 dB (hysteresis), retrigger interval 30 ms, original signal mix 0-100% (audio pass-through), channel 10, note 69 (sic; recommend default 38), velocity from detected peak, peak-detection interval 0.1-7 ms, trigger align -5..5 ms (latency compensation). Note-off sent on close. Latency = detection window.
- **Audio Note Ducker** (A): note All/0-127, attack 0-75 ms (shape 0-4), release 0-500 ms (shape), frequency 0-15000 + fine, width 0-2 oct, gain -32..32 dB, mode Peak/LowShelf/HighShelf/Gate/Pump, react-to-velocity. Note-on starts envelope, note-off starts release. Runs on the audio path; MIDI passes through.
- **Audio Loop Sampler** (A, S): vol -120..12 dB, speed -8..8 (negative = reverse), start/end 0-30000 ms, edge overlap crossfade 0-1000 ms, silence-removal thr, state (Stopped/Rec/Play loop/Play once/+Rec overdub), first trigger note 50 (note x record, x+1 play once, etc.), channel 0=omni.
- **Audio Looper** (A, S, T): 8 synchronized channels, sync off/project/playback, click count 0-64, MIDI-learnable note/CC/PC per action (record/length/overdub, play/stop, monitor select, reverse), halve/double, cross-fade. Phase 2.

### Utility / Monitor
- **MIDI Monitor**: ring buffer of 10000 events with time, offset, bus, decoded type (note/CC/PC/AT/PB/sysex), hex bytes; note analysis mode 0-2 flags unmatched on/off (stuck or orphan notes); examine-last-event pane; pause/clear. Pass-through, zero latency.
- **Panic**: see Program/Bank Select group.

## 3. Drum map presets

Format: `name = note`. Verification: the local host install ships no drum maps (Default Kit file is empty, `MIDINoteNames` has only a sample), so "verified" means standard, spec-defined GM only. Everything else is from memory of vendor documentation and must be checked against each vendor's MIDI map before shipping.

**GM (verified, General MIDI level 1 percussion, channel 10):** 35 Acoustic Bass Drum, 36 Bass Drum 1, 37 Side Stick, 38 Acoustic Snare, 39 Hand Clap, 40 Electric Snare, 41 Low Floor Tom, 42 Closed Hi-Hat, 43 High Floor Tom, 44 Pedal Hi-Hat, 45 Low Tom, 46 Open Hi-Hat, 47 Low-Mid Tom, 48 Hi-Mid Tom, 49 Crash 1, 50 High Tom, 51 Ride 1, 52 Chinese Cymbal, 53 Ride Bell, 54 Tambourine, 55 Splash, 56 Cowbell, 57 Crash 2, 58 Vibraslap, 59 Ride 2.

**Unverified (from memory, mark "UNVERIFIED" in the shipped preset until checked):**
- EZdrummer 2/3: follows GM for main pieces (36 kick, 38 snare, 42/44/46 hats, 49/57 crashes, 51/59 rides, toms 41/43/45/47/48/50); hi-hat articulation and rim/edge notes vary per kit, e.g. snare rim/side stick 37, snare edge/rimshot variants around 22-25 and 40. UNVERIFIED.
- Drum plug-in A: kick 36, snare 38 (rimshot 40, side stick/cross-stick 37), toms 41/43/45/47/48/50, hats closed 42 / pedal 44 / open 46 (extra hi-hat states on 22 / 26 range), crashes 49/57, ride 51/59/53. Uses many extra articulation notes. UNVERIFIED.
- Addictive Drums 2: GM-like default (36, 38, 42/44/46, toms, 49/57, 51/53/59) with extra articulations in low octaves via its own map. UNVERIFIED.
- GetGood Drums (GGD): kick 36, snare 38, rim/side 37 or 40, toms 41-50, hats 42/44/46, crashes 49/57, rides 51/59. Per-pack maps differ. UNVERIFIED.
- Steven Slate Drums 5: GM-style core (36, 38, 42/44/46, toms, crashes/rides) with extra notes for hat-pedal and sample-variant switches. UNVERIFIED.
Recommendation: ship GM as the one authoritative map; store the rest as editable `.tfmap` presets flagged unverified; Note Map + Scale Snap tables serve as GM -> vendor conversion. Keep the Sequencer's drum-map file format as `note<TAB>name` lines with `//` comments (as in REAPER's `_Default Kit.txt`).

## 4. Overlaps removed
- Duplicate Note Filter + Note Sanitizer -> one Note Sanitizer (retrigger threshold 0 = pure duplicate filter).
- Map To Key + Snap To Key + MIDI Control snap -> Scale Snap (Remap/Block, key list); Map To Key v2 -> Note Map (table).
- Transpose Notes + Router/Transpose + MIDI Tool transpose + MIDI Control transpose -> Transpose; the channel part -> Channel Router / Channel Filter/Remap (with Channel Filter).
- MIDI Tool v1/v2, Velocity Control, Velocity Scaler/Compressor, Velocity Variation Generator -> one Velocity Shaper (v1 is a strict subset of v2).
- Chorderizer + Chord In Key -> Chord (Fixed/Diatonic).
- CC LFO + Pitch Wheel LFO -> one LFO engine with a CC or pitch-bend target.
- Sequencer Baby, v2, Megababy -> one Step Sequencer (Baby = fixed 16-step, single lane, subset).
- Program/Bank Switch on Load + MIDI Control bank/program -> Program/Bank Select.
- Examiner + Logger -> Monitor. Choke and Choke Group kept separate (different semantics).
- Note-On Delay + Delay + Humanizer timing: Delay stays deterministic; Note-On Jitter/Humanizer own randomness.
- Deliberate behaviour changes vs source: pairing via held tables everywhere (source Router, Filter, Note-On Delay can strand notes), jitter applies to on and off together, drum trigger default note changed to 38.
