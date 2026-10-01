---
title: Techniques and effects
id: techniques
order: 6
keywords: techniques, effects, palm mute, p.m., let ring, hammer on, pull off, slide, bend, vibrato, whammy, tremolo bar, harmonic, dead note, ghost note, accent, staccato, grace note, dynamics, chord name
summary: Add palm mutes, slides, vibrato and other marks to your riff, learn what each looks like in TAB and which ones change the sound.
---

# Techniques and effects

Your riff from Chapter 5 has the right notes, but a guitarist does more than press frets. Palm mutes tighten a chug, slides join two notes, and vibrato makes a long note sing. In TabForge each of these is a mark on a note.

In this chapter you give the riff a palm mute, a slide and vibrato. You also learn what every mark looks like in tablature (TAB) and which marks change the sound you hear.

## What you will learn

- Apply and remove a technique on one note or on a whole selection.
- Recognise the mark each technique leaves in TAB.
- Use the **Effects** menu, the tool palette, the right-click menu and the keys.
- Set loudness with dynamics, accents and fades.
- Add chord names and text to the score.
- Tell which marks change the sound and which are only marks.

## How techniques work

A technique is a toggle on a note. Put the edit cursor on a note and use the technique: it switches on. Use it again: it switches off. If you select a stretch of beats first, the technique applies to every note in the selection at once.

There are four ways to reach a technique, and all do the same thing.

- The keys. Most techniques have a single letter, such as `P` for palm mute.
- The **Effects** menu, which lists every technique.
- The **Effects** group on the **Tools** page of the side panel. Hover a button to read its name and, if it has one, its key.
- The right-click menu. Right-click a note, then choose **Effects**. From the keyboard, press `Shift+F10` or the `Menu` key to open the same menu at the cursor.

One more rule helps. With the cursor on an empty beat, a technique key writes a new note that carries the technique. The note uses the current note value and sits on the cursor's string, at fret 0 (a ghost note starts at fret 5). So you can write a muted note without typing a fret first.

> **Tip:** Select a range first and apply a technique once. Every note in the range is marked, and one more press removes the mark from all of them.

## Muting and sustain

These techniques change how long and how clean a note sounds.

- **Palm mute** (`P`): the side of your picking hand rests on the strings. The notes sound short and dull. TAB shows a **P.M.** label with a dashed line over the notes.
- **Let ring** (`I`): the notes keep sounding over the ones that follow. TAB shows a line under the notes.
- **Dead note** (`X`): a muted, percussive click, drawn as an X.
- **Ghost note** (`O`): a very soft note, drawn in brackets.
- **Staccato** (`Shift+1`): the note is played short, and a dot marks it.
- **Tenuto** (`Shift+Minus`): the note is held for its full value, and a dash marks it. It is a mark only; it does not change the sound.
- **Accent** (`;`): pressing it cycles through no accent, an accent and a heavy accent. Accents make the note louder.

`Shift+1` means hold `Shift` and press the digit `1`. `Shift+Minus` uses the minus (`-`) key, and `;` is the semicolon key.

### Try it: Palm-mute the riff

*Goal: palm-mute bars 1 and 2 of your riff and hear the difference.*

1. Open your riff from Chapter 5, or open `first-riff-05.gp` from the guide's downloads.
2. Click the first note of bar 1. The cursor sits on it.
3. Hold `Shift` and click the last note of bar 2. Bars 1 and 2 are selected.
4. Press `P`. A **P.M.** label with a dashed line appears over both bars.
5. Press `Ctrl+Home`, then press `Space`. The notes of bars 1 and 2 sound shorter and tighter than before.
6. Press `Space` again to pause. The chords in bars 3 and 4 stay unmuted.

Press `P` again with the selection in place to remove the mark. Leave it on for the next exercise. Let ring looks different: a line under the notes.

## Join notes together

Three techniques connect one note to the next.

- **Hammer-on and pull-off** (`H`): the second note sounds without being picked again. TAB draws a curved line from the first note of a run to its last, and standard notation draws a slur over the same notes. To mark a run, select the notes and press `H` once. A run is two or more notes in a row on one string.
- **Legato slide** (`S`): the finger slides along the string from this note to the next note on the same string, and the second note is not picked again. TAB draws a line between the two frets. Standard notation draws a short slanted stroke as well.
- **Shift slide**: the same slide, but the second note is picked again. It has no key; choose **Effects > Shift slide**.

Ties are a different tool. A tie joins two notes of the same pitch into one long note. It is the `L` key, from **Chapter 5: Writing your first riff**.

## Pitch and vibrato

These techniques move the pitch of a note while it sounds.

- **Bend** (`B`): the string is pushed sideways and the note rises. TAB shows a bend curve over the fret number.
- **Vibrato** (`V`): a small, steady wobble in the pitch. A wavy line marks it.
- **Wide vibrato**: a bigger wobble. Choose **Effects > Wide vibrato**.
- **Tremolo bar** (`W`): the whammy lever dips the pitch. TAB shows a whammy diagram.
- **Trill** (`N`): the note flicks quickly between itself and a note above it.

> **Note:** TabForge marks a bend or a tremolo bar with a standard shape. You cannot draw your own curve in the editor. Songs you open can carry custom bend curves, whammy curves and other detail, and TabForge shows and plays those exactly.

### Try it: A slide and a vibrato

*Goal: add a slide to bar 2 and vibrato to the chord in bar 4.*

1. In bar 2, click the first `5`. It is the sixth note of the bar. The cursor sits on it.
2. Press `S`. A slide line joins the `5` to the `7` that follows.
3. Click the E5 chord in bar 4, on the fret 2 note. The cursor sits on it.
4. Press `V`. A wavy line appears over the chord.
5. Press `Down`. The cursor moves to the open string, string 6, on the same beat. Press `V` again to mark that note too.
6. Press `Ctrl+Home`, then press `Space`. You hear the slide in bar 2 and the vibrato on the last chord.

Vibrato marks one note at a time, which is why step 5 marks the second note of the chord. Your riff now matches `first-riff-06.gp`, if you want a fresh copy.

## Harmonics

A harmonic is a bell-like ringing note made by touching the string lightly instead of pressing it down. Press `Y` to mark a natural harmonic. TAB shows a **Harm.** caption above the note.

The other kinds of harmonic, artificial, pinch, tapped and semi, come from songs you open. TabForge reads, draws and plays them. In standard notation they are written at the fretted pitch with a diamond note head.

## Strums and ornaments

Some marks have no key. Choose them from the **Effects** menu, or from the palette.

- **Brush down** and **Brush up**: a chord played as a sweep across the strings. In TAB a down stroke is an arrow that points up towards the thin strings, because the sweep starts on the bass string, and an up stroke points down.
- **Arpeggio down** and **Arpeggio up**: the notes of a chord spread out one after another, with the same arrow directions.
- **Grace before beat** (`G`): a short ornament played straight before the main note. The score shows a "gr" label.
- **Tapping**: right-hand tapping on the fretboard.
- **Slap** and **Pop**: bass techniques.
- **Tremolo picking**: very fast repeated picking, drawn as slashes through the stem.
- **Pickstroke down** and **Pickstroke up**: down and up picking marks, in the **Beat** group of the palette.

You apply each of these to a note exactly as before.

## Dynamics and fades

Dynamics say how loud a note is, from very soft to very loud: ppp, pp, p, mp, mf, f, ff and fff. Choose them from the **Dynamic** group on the **Tools** page. They have no keys.

With notes selected, a dynamic sets their loudness. With the cursor on an empty beat, it sets the loudness of the next notes you write. New notes start at f. The letter appears under the staff at the first note, and again wherever the loudness changes.

Fades change loudness gradually across a note. Press `Shift+Comma` to fade in and `Shift+Full stop` to fade out. A hairpin shape marks each one.

## Words on the score

You can add chord names and short text above a beat. Neither is played; they are labels for the reader.

- **Chord name** (`A`): a window named **Chord** opens. Type a name such as `Am` or `G7` and click **OK**. TabForge prints names only; it does not draw chord diagrams.
- **Chord finder**: in the **Chord finder** group of the side panel's **Practice** tab, pick a root and a type, click **Show** to see the notes, and click **Insert name** to put the name on the cursor beat.
- **Beat text** (`T`): a window named **Text** opens. Type a short word, such as `chorus`, and click **OK**.

The **Lyrics** box is for the song as a whole. Open the **Sections** tab in the side panel and expand **Lyrics**. Type your words there. They are one block of text that is saved with the song, not tied to individual beats.

## Marks and sound

Some marks change what you hear. Others only change what you see. The table lists the technique keys.

| Technique | Key | In TAB | Changes the sound |
|---|---|---|---|
| Palm mute | `P` | **P.M.** label and dashed line | Yes: shorter, tighter |
| Let ring | `I` | Line under the notes | Yes: the notes ring on |
| Dead note | `X` | An X | Yes: a click |
| Ghost note | `O` | Fret in brackets | Yes: very soft |
| Staccato | `Shift+1` | A dot | Yes: short |
| Tenuto | `Shift+Minus` | A dash | No |
| Accent | `;` | A sign over the note | Yes: louder |
| Fade in | `Shift+Comma` | A widening hairpin | Yes: a swell up |
| Fade out | `Shift+Full stop` | A narrowing hairpin | Yes: a swell down |

The second table covers notes that change pitch or join.

| Technique | Key | In TAB | Changes the sound |
|---|---|---|---|
| Hammer-on, pull-off | `H` | Curved line over the notes | Yes: no second pick |
| Legato slide | `S` | Line between the frets | Yes: a glide |
| Bend | `B` | Bend curve | Yes: the note rises |
| Vibrato | `V` | Wavy line | Yes: a pitch wobble |
| Tremolo bar | `W` | Whammy diagram | Yes: a dip |
| Trill | `N` | **tr** and a wave | Yes: a fast flick |
| Natural harmonic | `Y` | **Harm.** caption | Yes: a higher, ringing note |

Some marks are marks only. Tenuto, tapping, pick strokes, chord names and beat text appear in the score and do not change the playback.

How long a let-ring note may keep ringing is a setting. Open **Preferences > Playback & Practice**, open **More options** and look for **Let-ring tail limit**.

### Try it: Find the techniques in the demo

*Goal: spot the marks you have learned in the demo song.*

1. Choose **File > Open…** (`Ctrl+O`). Open `TabForge Demo - Ashen Meridian.gp` from the `Samples` folder.
2. Click **Rhythm Gtr L** in the track list.
3. Choose **Sections > Go to…** (`Ctrl+G`), type `Intro: Meridian` and press `Enter`. The score jumps to the first heavy riff.
4. Look for **P.M.** labels above the notes. These are palm mutes.
5. Click **Clean Gtr** in the track list. Press `Ctrl+G`, type `Embers` and press `Enter`. Look for let ring lines and, in the fifth bar of the song, a **Harm.** caption.
6. Click **Lead Gtr**. Press `Ctrl+G`, type `Solo` and press `Enter`. Look through the first bars of the solo for bends, slides and hammer-on curves.
7. Close the document tab (`Ctrl+W`) when you have finished looking. If TabForge asks whether to save, choose not to.

> **Tip:** Solos have the most marks. To read a crowded bar, hold `Ctrl` and scroll the mouse wheel to zoom in.

## Quick recap

- A technique is a toggle on a note. Use it once to add it and again to remove it.
- Select a stretch of beats and apply a technique once to mark every note in it.
- Reach techniques by key, the **Effects** menu, the palette, or the right-click menu.
- On an empty beat, a technique key writes a new note that carries the technique.
- Bends and the tremolo bar use a standard shape. Custom curves come only from songs you open.
- The **Dynamic** group sets loudness. Fades use `Shift+Comma` and `Shift+Full stop`.
- Most marks change the sound. Tenuto, tapping, pick strokes, chord names and text only mark the score.

## What next

Your riff now sounds like a guitar. Go on to **Chapter 7: Tracks, instruments and tunings**, where you add a bass to the riff and learn how tunings and the capo change the notes you see.
