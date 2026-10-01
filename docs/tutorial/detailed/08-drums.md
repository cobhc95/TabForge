---
title: Drums
id: drums
order: 8
keywords: drums, drum kit, drum map, kick, bass drum, snare, hi-hat, hihat, cymbal, crash, ride, toms, drum notation, drum beat, rock beat, percussion, add drums, drum track, repeat bar, midi note numbers
summary: Add a drum track, read drum notation, write a basic rock beat with the drum map and choose how the TAB shows the kit.
est-minutes: 35
---

# Drums

A drum kit is not tuned and has no frets, so a drum track works differently from a guitar track. Each line or position in the notation stands for one drum or cymbal, and a drum map takes the place of the fretboard. Once you know where each sound sits, a drum part is quick to read.

In this chapter you add a drum track to "My first riff" and write a basic rock beat by clicking pads. Then you read the real drum part in the demo song.

## What you will learn

- Add a drum track and choose a drum kit.
- Read drum notation, with the kick, snare, hi-hat, cymbals and toms in their places.
- Write drum hits by clicking the pads of the drum map.
- Write a basic rock beat and repeat it.
- Choose how the kit is written on the TAB.

## A drum track is different

A guitar track needs strings, frets and a tuning. A drum track needs none of them. Its sound is a kit, a set of drums and cymbals, and every sound in the kit has its own General MIDI note number. Drum tracks always use MIDI channel 10, and TabForge sets that for you.

Select a drum track and the instrument panel above the score becomes the drum map, a grid of labelled pads. The score shows the same sounds as drum notation, and the TAB shows one line for each group of sounds. A drum track has no **Tuning** card, so there is no tuning or capo to set.

## Add a drum track

Choose **Track > Add Drums**. A track called **Drums** appears at the end of the track list and is selected. It has as many bars as the song, and its TAB starts with six lines.

The new track uses the standard kit. To pick a different one, open **Track properties** (`F6`), click **Change…** on the **Instrument** card, and look in the **Drum Kits** family of the **Choose instrument** window. There are nine kits, including Standard, Room, Power, Electronic, Jazz and Brush. Typing `drum` in the search box shows them all.

> **Tip:** The **+ Track** button opens the **Add track** window, where a drum kit gives you a drum track too. **Track > Add Drums** is the faster route.

## Read drum notation

Drum notation uses the standard five-line staff, but each position means a drum rather than a pitch. Learn six places and you can read most rock drumming.

- The kick (bass drum) is a round note in the bottom space of the staff.
- The snare is a round note in the second space from the top.
- The closed hi-hat is an **x** directly above the top line. An open hi-hat is a small circle in the same place.
- The ride cymbal is an **x** on the top line, and the ride bell is a diamond there.
- The crash cymbal is an **x** above the staff.
- The toms are round notes in the upper half of the staff. High toms sit at the top, and floor toms sit lower down, close to the kick.

Cymbals use an **x** and drums use round notes. That one habit helps you spot a hi-hat pattern at a glance.

## Use the drum map

The drum map is the panel above the score when a drum track is selected. It has 61 pads, one for each General MIDI percussion sound from 27 to 87. Each pad shows its number, its name and the short label the TAB uses.

During playback the pads for the sounds being played light up, so you can watch the kit as the song plays.

To write a hit, click its pad. TabForge writes that sound at the edit cursor, using the current note value. Every pad writes, from the lowest to the highest, and the hit lands on the TAB line that the track's drum notation preset gives that sound (see the end of this chapter). Click the same pad again to remove the hit. The cursor stays on the same beat, so you can click several pads to stack a kick and a hi-hat together. Press `Right` to move on.

You can also type a number. With the cursor on a drum line of the TAB, typing `38` writes note 38, and any other number from 27 to 87 works the same way.

> **Tip:** You do not need the note numbers at first. Click the pads and let the notation draw itself.

## Read the numbers on the TAB

With the default drum notation, the TAB shows a General MIDI note number instead of a fret number, and each group of sounds has its own line. The numbers are the same ones printed on the pads. These are the ones you will meet most.

| Sound | Number | TAB line, counting from the top |
|---|---|---|
| Crash, ride and other cymbals | `49`, `51`, `53`, `57` | 1 |
| Closed hi-hat | `42` | 2 |
| Open hi-hat | `46` | 2 |
| High toms | `48`, `50` | 3 |
| Snare | `38` | 4 |
| Low and floor toms | `41`, `43`, `45`, `47` | 5 |
| Kick | `35`, `36` | 6 |

Line 1 is the top line of the TAB. A snare on beat 2 shows as `38` on line 4, and a kick on beat 1 shows as `36` on line 6.

## Write a basic rock beat

A basic rock beat has three parts. The hi-hat plays every eighth note, the kick plays on beats 1 and 3, and the snare plays on beats 2 and 4. Together they make eight steps in a bar of 4/4.

### Try it: A basic rock beat

*Goal: write one bar of rock beat on a new drum track.*

Starter file: `first-riff-07.gp`, the riff and bass from Chapter 7. It comes with the guide as an optional download. You can also carry on with your own song.

The bar has eight steps. Use this table as your guide, and read the count aloud: 1 and 2 and 3 and 4 and.

| Step | Count | Pads to click |
|---|---|---|
| 1 | 1 | **Bass Drum 1**, **Closed Hi-Hat** |
| 2 | and | **Closed Hi-Hat** |
| 3 | 2 | **Acoustic Snare**, **Closed Hi-Hat** |
| 4 | and | **Closed Hi-Hat** |
| 5 | 3 | **Bass Drum 1**, **Closed Hi-Hat** |
| 6 | and | **Closed Hi-Hat** |
| 7 | 4 | **Acoustic Snare**, **Closed Hi-Hat** |
| 8 | and | **Closed Hi-Hat** |

1. Choose **File > Open…** (`Ctrl+O`), then open `first-riff-07.gp`. The riff opens with a guitar and a bass.
2. Choose **Track > Add Drums**. A **Drums** track is added and selected, and the drum map appears above the score.
3. Choose **Note > Eighth**. Each hit you write will be an eighth note.
4. Click the first beat of bar 1 in the drum score. The edit cursor lands there.
5. Click the pads listed for step 1. A kick and a hi-hat appear on the first beat.
6. Press `Right`, then click the pads listed for step 2. The cursor moves to the next step, and a hi-hat appears.
7. Repeat for steps 3 to 8. Press `Right` before each step, and click its pads.

The TAB now shows eight hi-hat numbers on line 2, a kick on steps 1 and 5, and a snare on steps 3 and 7. Those are beats 1 and 3 for the kick, and beats 2 and 4 for the snare. Press `Ctrl+Z` if you need to take back a hit.

**Stuck?** If a click does nothing, check that the drum track is selected in the track list. See **Chapter 14: Troubleshooting and FAQ**.

## Repeat the beat

You have one bar. Copy it across the song so the beat plays under every bar of the riff.

### Try it: Copy the beat across the riff

*Goal: fill bars 2 to 4 of the drum track with the bar you wrote.*

1. Click the first beat of bar 1 in the drum score, then hold `Shift` and click the last beat of the bar. The whole bar is selected.
2. Press `Ctrl+C`. The bar is copied.
3. Click the first beat of bar 2, then press `Ctrl+V`. The beat is pasted into bar 2.
4. Click the first beat of bar 3, then press `Ctrl+V`. Bar 3 now has the beat.
5. Click the first beat of bar 4, then press `Ctrl+V`. Bar 4 now has the beat.
6. Press `Ctrl+Home`, then press `Space`. You hear the guitar, the bass and the drums play the riff together. Press `Space` again to stop.

You now have the same song as `first-riff-08.gp`.

Two other tools repeat bars. **Bar > Repeat selection as bars** adds copies of the selected bars as new bars after them, and it does this on every track. Use it to build a longer song, not to fill a drum part. **Bar > Repeat one bar** marks a bar as a repeat of the one before, and the score shows the one-bar repeat sign.

## Choose a drum notation preset

The same drum part can be written on the TAB in more than one way. The choice is yours and nothing about the sound changes. Open **Track properties** (`F6`) on a drum track and look at the **Drum notation** card.

The preset list offers four choices.

- The number preset prints the General MIDI note number on each line. This is the default.
- The name preset prints short names instead, such as BD for the kick, SD for the snare and HH for the hi-hat.
- The line-per-instrument preset gives each drum its own named line, in a style like a classic text drum tab. It has seven lines, named CC, HH, SD, T1, T2, FT and BD, with an **x** or an **o** for each hit.
- The custom map lets you decide for every sound.

Choose a preset and click **OK**. TabForge moves every drum note on the track to the line the preset gives it. The staff always uses the same standard positions, so only the TAB changes.

To make your own map, click **Edit custom map…**. The **Custom drum map** window lists the sounds from 27 to 87. For each one you set the **TAB line**, the **TAB text** (up to four characters), the **Staff position** and the **Notehead**, which can be normal, x, circle or diamond. Use **Reset from** to start every row from one of the presets.

The demo song uses a custom map, which is why its ride bell is drawn as a diamond.

## Volume and feel

Every drum hit has a strength. The **Dynamic** icons in the tool palette set how hard the selected hits play, from very soft to very loud. Select some hits and click one. The hits get louder or softer.

For a shuffle, choose **Bar > Triplet feel** on a bar. It changes how pairs of eighth notes are played in that bar. To balance the whole kit against the other tracks, see **Chapter 9: Shaping the sound**.

> **Note:** A drum track always shows its drum map. It has no tuning or capo, and it is not transposed.

### Try it: Read the demo's drums

*Goal: match the lit pads to the notes as the demo plays.*

1. Choose **File > Open…** (`Ctrl+O`), go to the `Samples` folder beside `TabForge.exe`, and open `TabForge Demo - Ashen Meridian.gp`. The song opens as an unsaved song.
2. Click the **Drums** track in the track list. The drum map appears above the score.
3. Press `Ctrl+G`, type `40`, and press `Enter`. The score jumps to the start of **Chorus 1**.
4. Lower the playback speed before you listen, as shown in **Chapter 3: Practice tools**. The song is fast.
5. Press `Space`. The pads for the sounds being played light up, and the notes in the score pass the playback line.
6. Watch the hi-hat and snare pads. They light up on the same beats as the notes in the score. Press `Space` to stop.

You can close the demo tab without saving.

## Quick recap

- A drum track has no frets or tuning. Its sounds are a kit, and every sound has a General MIDI note number.
- **Track > Add Drums** adds a drum track. The **Drum Kits** family in the instrument catalogue offers nine kits.
- Cymbals are written with an **x** and drums with round notes. The kick sits in the bottom space and the snare in the second space from the top.
- Click a pad in the drum map to write that sound at the edit cursor. Press `Right` to move to the next beat.
- Copy a bar with `Ctrl+C` and paste it with `Ctrl+V` to repeat a beat.
- **Track properties** has a **Drum notation** card for choosing how the TAB shows the kit.

## What next

Your riff now has a guitar, a bass and drums, and the next job is to make them sound right together. Go on to **Chapter 9: Shaping the sound** to balance the volumes, place each track in the stereo picture and listen to the whole band.
