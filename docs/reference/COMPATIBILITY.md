# Compatible .gp files: what is kept, what changes, what cannot be held

<!-- Generated from TabForge's compatibility record by `TabForge.exe --gp-compat-doc`; a self-test regenerates it and compares, so do not edit it by hand. -->

TabForge can write a compatible .gp file that other programs open. A .gp file holds the music and the common marks, but it cannot hold everything TabForge knows. This page says exactly what a compatible file keeps, what it changes and what it cannot hold. When you save or export a song that uses something in the changed or left out lists below, TabForge names it in a question before anything is written and offers to keep a full TabForge copy as well, so nothing is lost without you knowing.

## The short version

- **Kept:** the notes (pitch, string and fret), their rhythm, rests, ties, tuplets, grace notes and voices; drums; tunings and capo; time signatures, key changes and the tempo (steps and ramps); repeats, endings and jumps; section markers; the eight dynamic marks; track volume, pan, mute and solo; and the common playing marks (bends, slides, hammer-ons and pull-offs, harmonics, vibrato, palm mute, whammy bar, brush strokes and more). Each of the 54 single-feature cases and the 15 test songs behind this page is exported, reopened and compared, note by note.
- **Changed or left out:** the items listed below.
- **Cannot be held at all:** plug-ins, FX chains, audio and MIDI clips and mixer groups (see "TabForge audio settings" below).
- **To keep everything:** save a full TabForge copy (a .tforge project). It is the lossless option, and it is offered every time the question appears.

## Kept exactly

These are written to the .gp file and read back unchanged:

- Track volume, exactly.
- Track pan (left and right balance), exactly.
- Left-hand fingering.
- Right-hand fingering.
- Tenuto marks.
- Palm mute on one note of a chord.
- Trill speed.
- Legato slurs.
- Rasgueado patterns.
- Pick slide up.
- Pick slide down.
- Left-hand tap.
- Track colours.

## Kept, spelled the way other programs spell it

These come back as the same music; only the internal label or name differs. The save question does not mention them:

- An old per-note Tenuto tag is written as the beat's tenuto mark.
- MIDI channel numbers are handed out again in track order; drums stay on the drum channel.
- The target note of a slide is worked out from the note that follows it.
- A trill with no target set gets a target two semitones up.
- Navigation marks (Segno, Coda, D.S., D.C. and so on) are kept under the standard names.
- A tempo marking that repeats the tempo already playing is not kept as a change; the tempo you hear is the same.
- Whammy-bar types (dip, dive and so on) are worked out again from the curve; the curve itself is kept.
- Every kind of harmonic also carries the general harmonic label.
- A dead note also carries a dead-note label.
- A ghost note also carries a ghost-note label.
- A hammer-on or pull-off is stored as an origin and a destination.
- The destination of a hammer-on or pull-off carries its own label.
- A grace note before the beat also carries a grace label.
- A grace note on the beat also carries a grace label.
- An arpeggio stroke down also carries a brush-stroke label.
- An arpeggio stroke up also carries a brush-stroke label.
- A hammer-on that ends a bar marks the first note of the next bar as its destination.
- A track's playback transpose is written into the tuning (or into string and fret), so every note sounds the same; the setting itself reads back as 0.
- A track's instrument name is named again from its sound.

## Changed or left out, and named in the save question

When your song uses any of these, the question lists it with the bars where it occurs:

- Different loudness inside one chord or drum beat. A beat holds one dynamic mark, so every note of the beat takes the first note's.
- Bend curves with more turns than the format holds. A .gp file keeps where a bend starts, one flat stretch and where it ends. A bend that rises and then holds, or releases and then holds, is written as drawn; a curve with more turns is reduced to the closest such shape.
- Whammy-bar curves with more than four points are reduced to four.
- A fermata placed on one track only. A .gp file stores a fermata on the bar, so it shows on every track.
- Tremolo picking at 1/64 is written as 1/32 (a .gp file has three speeds: 1/8, 1/16 and 1/32).
- A fade-in on one note of a chord applies to the whole beat.
- A fade-out on one note of a chord applies to the whole beat.
- A beat made only of ghost notes loses its accent, tenuto or staccato mark (in a .gp file a ghost mark and those marks exclude each other). With a plain note in the chord, the mark stays on that note.
- Volume, pan and sound changes placed on a single beat, including on a rest. The track keeps its starting mix. Tempo changes are kept.
- The reverb send of a track. It reopens at the default.
- The chorus send of a track. It reopens at the default.

## Changed or left out, not named in the save question

These are small, or belong to your machine or to TabForge's own screens. The question stays quiet about them; a full TabForge copy keeps all of them:

- A note's exact loudness. Loudness is kept as the nearest of the eight dynamic marks (ppp to fff), so a value between two marks moves to the nearer one; the note keeps its mark.
- The length of a grace note is normalised when the file is reopened; playback does not use it.
- A bend on a grace note is kept; its separate "bend grace" label is not.
- The MIDI output device of a track (a device number belongs to your machine).
- The record input choice of a track.
- The input monitoring switch of a track.
- The tint of a track row in the track list.
- The performer name of a track.
- The notes written on a track.
- The drum-map preset of a drum track.

## TabForge audio settings: plug-ins, FX chains, clips and mixer groups

A .gp file has nowhere to keep these. What happens depends on how you save:

- **Save as .gp:** TabForge asks how to keep them: the whole project embedded in the .gp, a .gp with a .tfaudio file beside it (keep the two together), or a .tforge project. Nothing is lost and the save question does not repeat the list.
- **Export compatible .gp file:** writes the .gp alone, so these settings are left out. TabForge asks first and offers to keep a full copy; exporting never changes your song or its file.

## How to keep everything

Choose "Keep a full TabForge copy" in the question, or save a .tforge project. The full copy holds every setting of the song, including everything on this page.

## How this page stays true

TabForge's own tests export a song for every row, reopen the file and compare it with the original, and they fail if a loss is found that the save question does not list or that this page does not describe. This page is written from the same rows, and a test regenerates it and compares, so it cannot fall behind the program.
