---
title: Tracks, instruments and tunings
id: tracks
order: 7
keywords: track, add track, delete track, move track, reorder, track properties, instrument, tuning, drop d, retune, capo, transpose, global tuning, tuner, seven string, 8 string, bass, five string, frets, duplicate track
summary: Add a bass to your riff, choose instruments, change a tuning, set a capo and keep three kinds of transposing apart.
est-minutes: 40
---

# Tracks, instruments and tunings

A song is a band, and each player in the band is a track. So far your riff has one track, a guitar. In this chapter you add a bass, choose instruments and change how a track is tuned.

A fret number only means something once TabForge knows which note each string plays. By the end you will have added a bass to "My first riff", tried a drop tuning and read the capo in the demo song.

## What you will learn

- Read the track list and select, rename, recolour, reorder and delete tracks.
- Add a track from the **Track** menu or with the **Add track** window.
- Choose an instrument from the instrument catalogue.
- Change a tuning, the number of strings and frets, and set a capo.
- Tell apart **Transpose**, the mixer's **Pitch** and **Global tuning**.
- Open the tuner.

## Read the track list

The track list sits at the left of the timeline, one row per track. Each row has a few small controls.

- The cog button opens **Track properties**.
- The red dot arms the track for recording (see **Chapter 11: Arranging and recording**).
- **#** is the track number, and **TRACK** is its name.
- **FX** opens the track's plug-in chain (see **Chapter 10: Plug-ins, effects and audio devices**).
- **M** mutes the track, and **S** plays it alone (solo).
- **VOLUME** and **PAN** set how loud the track is and where it sits between the speakers.
- **INSTRUMENT** shows the sound and changes it.

Right-click a control on a row and you reach that control: the knob type-in, the pan menu, the sliders, the buttons and the colour menu on the track number. Only a right-click on the row's own background opens **Track properties**.

To work on a track, click an empty part of its row. The row lights up, and the score, the fretboard and the status bar all switch to that track. The score always shows the selected track.

> **Tip:** Hover over any button in the track list to read its tooltip. When the command has a key, the tooltip shows it in brackets.

## Add a track

TabForge gives you two ways to add a track. The quick way makes a ready-made track at once. The careful way opens a window where you choose everything first.

For the quick way, open the **Track** menu and choose one of **Track > Add Guitar**, **Track > Add Bass**, **Track > Add Drums** or **Track > Add Keys**. The new track goes to the end of the list and is selected, and it has as many bars as the song. A drum track starts with six TAB lines, the same as one made in the **Add track** window. **Add Bass** gives you a four-string bass with the strings tuned E A D G, which is the one to use in this chapter.

For the careful way, click **+ Track** at the top left of the track list. Hover over it and its tooltip says what it does. Right-click it to see a small menu with **Add track…** and **Mixer…**.

The **Add track** window is the same window as **Track properties**, with a few extras. It has a **Details** card for the name and colour, an **Instrument** card, a **Mixer** card, and a **Tuning** card. At the bottom is a **Position** list, and the **Add track** and **Cancel** buttons.

The **Position** list decides where the new track lands in the list. You can choose **Last**, **First**, **After the selected track**, or **As track number…**, which lets you type a number.

> **Note:** For a bass, **Track > Add Bass** is the quickest route because it sets up four bass strings for you.

### Try it: Add a bass

*Goal: add a bass track that plays the riff one octave below the guitar.*

Starter file: `first-riff-06.gp`, the riff as it was at the end of Chapter 6. It comes with the guide as an optional download. You can also carry on with your own riff.

1. Choose **File > Open…** (`Ctrl+O`), then open `first-riff-06.gp`. The riff opens with one guitar track.
2. Choose **Track > Add Bass**. A track called **Bass** appears below the guitar and is selected.
3. Choose **Note > Eighth**. The next notes you type will be eighth notes (quavers).
4. Click the third line from the top of the TAB staff, at the start of bar 1. This is the A string, and the edit cursor lands there.
5. Type `0` `0` `3` `0` `0` `5` `3` `0`. Each digit writes a fret and moves the cursor on. Bar 1 fills with eight eighth notes.
6. Keep typing `0` `0` `3` `0` `0` `5` `7` `5`. The cursor carries on into bar 2, and the notes match the guitar fret for fret.

The guitar and bass now show the same numbers on the A string, but the bass sounds an octave lower. The bass A string is an octave below the guitar A string, so the same fret gives a lower pitch. Bars 3 and 4 use the lowest string.

7. Choose **Note > Quarter**, then press `Down` once. The edit cursor drops to the lowest string, the E string.
8. Type `5` and then `3`. Two quarter notes fill the first half of bar 3.
9. Choose **Note > Half**, then type `1`. A half note finishes the bar.
10. Choose **Note > Whole**, then type `0`. An open E whole note fills bar 4.
11. Press `Ctrl+Home` to go back to bar 1, then press `Space`. You hear the guitar and the bass play together, and the bass sits an octave below. Press `Space` again to stop.

You now have the same song as `first-riff-07.gp`. Keep it open for the next exercise.

**Stuck?** If a note lands on the wrong line, press `Backspace`, press `Up` or `Down` to change string, and type the fret again.

## Choose an instrument

An instrument is the sound a track makes. Pick a different one and the same notes sound like a violin, a piano or a drum kit. TabForge uses the General MIDI list of 128 sounds, plus nine drum kits.

The quick way is the **INSTRUMENT** button on a track row. Click it and a menu shows the sounds in the track's current family, followed by all the families. Choosing one changes the sound and nothing else.

For the full catalogue, open **Track properties** (the next section) and click **Change…** on the **Instrument** card. The **Choose instrument** window opens with the sounds grouped by family and shown as pictures.

Type a word in the search box, such as `bass` or `piano`, and the tiles filter as you type. Press `Enter` to take the first match, or click a tile and click **Select**. Double-click a tile to choose it at once.

> **Tip:** Searching is quicker than scrolling. Try `bass`, `strings` or `drum`.

The instrument sets the sound. On a track with no notes yet, choosing a bass sound also gives it bass strings, and choosing a piano, organ or strings sound makes it a keys track with a keyboard. In the **Add track** window, a drum kit makes a drum track with a drum map. A track that already has notes keeps its strings and its kind, so no note moves; change the strings on the **Tuning** card. **Track properties** tells you so when you pick a different kind of instrument for such a track.

## Open Track properties

**Track properties** holds every setting of one track in one window. Select the track, then press `F6`, click the cog button on its row, or choose **Track > Properties…**. Double-clicking or right-clicking an empty part of a row works too.

The window has four cards.

- **Details** holds the **Track name**, **Played by**, **Colour**, a tick box to tint the track's row and lane with its colour, and a **Notes** box for your own reminders.
- **Instrument** has the **Change…** button, plus **MIDI program** and **Channel**. Leave those two alone unless you know you need them.
- **Mixer** has the **Volume** and **Pan** knobs and a button for the track's plug-ins.
- **Tuning** is for guitars and basses. It is where strings, frets and the capo live.

The window for a bass looks the same, with bass tuning.

Nothing changes until you click **OK**. If you close the window after making changes, TabForge asks whether to discard them and lists what changed, so you can go back.

To set a knob exactly, double-click it, or right-click it. A small box opens. Type a value such as `80%` for volume or `L 20` for pan, then press `Enter`. You can also focus a knob and press `F2` or `Enter`. Hold `Ctrl` and click a knob to reset it.

## Set a tuning

A tuning is the note each open string plays. Standard guitar tuning, from the lowest string to the highest, is E A D G B E. If you tune the lowest string down to D, you have Drop D, and a fret number on that string now means a different note.

Open **Track properties** and look at the **Tuning** card. At the top is the **Preset** list. It offers the common tunings for the number of strings the track has. For a six-string guitar that includes standard, a half step down, Drop D, Drop C and Open G. Choose one and the strings below follow.

Each string has its own row. The row shows the string's note, a **-** button and a **+** button. Click **-** to tune that string down by a semitone, and **+** to tune it up. You can also double-click the note and type one, such as `Eb3`, then press `Enter`.

Below the strings are **All -1** and **All +1**, which move every string by a semitone. If you type a tuning that matches no preset, the list shows **Custom**.

The tick box **Keep fret numbers** is ticked by default. Ticked, every note keeps its fret number and changes pitch with its string, as it does when you retune a real guitar. Unticked, notes keep their pitch and TabForge works out new fret numbers.

> **Note:** **Keep fret numbers** changes how existing notes sound when you retune. Untick it to keep the sound the same and let the numbers change.

### Try it: Drop D

*Goal: hear what a retune does to the notes already written, with and without **Keep fret numbers**.*

1. Click the guitar track in the track list. The score shows the guitar part.
2. Press `F6`. **Track properties** opens.
3. In the **Preset** list on the **Tuning** card, choose **Drop D**. The lowest string changes from E to D.
4. Check that **Keep fret numbers** is ticked, then click **OK**.
5. Press `Ctrl+Home`, then press `Space` and listen to bars 3 and 4. The chords on the lowest string now sound a whole tone lower.
6. Press `Space` to stop, then press `Ctrl+Z`. The tuning goes back to standard.
7. Press `F6`, choose **Drop D** again, untick **Keep fret numbers**, then click **OK**. The fret numbers in bars 3 and 4 change, and the chords sound as before.
8. Press `Ctrl+Z` once more. The guitar is back in standard tuning, and the bass is still there.

The numbers on the lowest string went up by two when you unticked the box, because the notes stayed the same.

### Seven strings, eight strings and a five-string bass

There is no box for the number of strings. Click **+ Low string** on the **Tuning** card to add a string a fourth below the lowest, and **- Low string** to remove the lowest one. The label beside the **Preset** list shows how many strings you have, such as "7 strings".

Add the string first, then choose a preset. The list changes to match the new count, so a seven-string guitar offers presets such as standard B and Drop A.

Ashen Meridian has all three. **Rhythm Gtr L** has seven strings, **Rhythm Gtr R** has eight, and **Bass** has five. Select one and the TAB gains a line for each string, and the fretboard grows with it.

## Use a capo

A capo is a clamp across the neck that raises every open string by the same number of semitones. It lets a player use open-string shapes in a higher key. In TAB, fret numbers are counted from the capo, so fret `0` means the open string at the capo.

The **Tuning** card has two boxes at the bottom. **Frets** sets how many frets the neck has, from 12 to 36. **Capo** sets the capo fret, from 0 to 12. Type a number and click **OK**.

You can change the capo at any time. The fret numbers you have already written stay as they are, and the notes sound higher or lower to match, as they would if you moved a real capo. Fret numbers always count from the capo.

### Try it: Meet the capo

*Goal: find the capo and the track note in the demo song.*

1. Choose **File > Open…** (`Ctrl+O`), go to the `Samples` folder beside `TabForge.exe`, and open `TabForge Demo - Ashen Meridian.gp`. The song opens as an unsaved song.
2. Click the **Clean Gtr** track in the track list.
3. Press `F6`. **Track properties** opens.
4. Look at the **Tuning** card. The **Capo** box shows `5`.
5. Look at the **Notes** box on the **Details** card. It says that frets are relative to the capo.
6. Click **Cancel**. Nothing changes.

Close the demo tab without saving. The file on disk was never touched.

## Three kinds of transpose

Three different things shift pitch, and beginners often mix them up.

- **Tools > Transpose…** rewrites the notes of the selected track, or of the selected bars, by a number of semitones. It asks for a number from -12 to 12. The notes in the score change, so the TAB shows new frets. Drum tracks are refused, because their numbers name instruments, not pitches. The change covers both voices of each bar and moves slide and trill targets with the notes. A note that would fall below fret 0 or past the last fret moves to a free string of its beat where it fits, and the status bar counts any note that fits nowhere. The command has no default key, and one undo step takes it back.
- The **Pitch** column in the mixer changes only the sound. The written notes stay as they are. Ashen Meridian uses it on **Sub Drop**, which is written an octave up and sounds an octave down. See **Chapter 9: Shaping the sound**.
- **Global tuning** retunes every instrument track at once and keeps the fret numbers. Drum tracks are not affected.

**Global tuning** is the tuning-fork button in the header of the timeline. Hover over it to read its tooltip. Click it to open the **Global tuning (all instruments)** window, where you can pick a preset or shift every string, then click **Apply**.

Double-click the number beside the fork to type a shift in semitones. Right-click the fork for a quick menu with **Tune up a semitone (+1)**, **Tune down a semitone (-1)**, **Global tuning window…** and **Back to original tuning**. A global retune is one undo step.

> **Tip:** To play along with a recording tuned a half step down, use **Tune down a semitone (-1)** instead of editing every track.

## Rename, recolour, reorder and delete

- **Rename.** Double-click the track name, type a new one and press `Enter`. Press `Esc` to cancel.
- **Recolour.** Right-click the track number. A menu lists 16 colours. You can also pick a colour in **Track properties**.
- **Reorder.** Drag a row up or down by an empty part of it. You can also choose **Track > Move up** or **Track > Move down**, or press `Alt+Shift+Up` or `Alt+Shift+Down`. Playback carries on while you move a track.
- **Delete.** Choose **Track > Delete track** (`Ctrl+Shift+Delete`). TabForge does not ask first, so press `Ctrl+Z` to bring the track back. The last track cannot be deleted.

`Ctrl+Shift+Up` and `Ctrl+Shift+Down` select the previous and next track, and `Ctrl+Shift+Insert` adds a track.

There is no Duplicate track command. To copy a track, select it, press `Ctrl+A` to select the whole track, and press `Ctrl+C`. Add a track of the same kind, click its first beat, and press `Ctrl+V`.

### Copy a part to another track

You can also move a part to a different instrument, such as guitar bars to a bass. Select the bars, press `Ctrl+C`, select the other track, click where the part should start, and press `Ctrl+V`.

Because the instruments differ, a **Paste** window opens with the question **Pasting between instruments of different range**. **Keep the exact pitch** gives the notes the same sound as the guitar, so TabForge moves them to whichever strings and frets of the bass give those pitches. **Shift by an octave automatically** moves them into the bass's own range instead.

In both cases TabForge chooses the strings and frets for you. A note that cannot fit on the bass is left out, and the status bar reports how many. This is why the "Add a bass" exercise typed the bars by hand: typing puts each note on exactly the string you pick. Tick **Remember my choice** to stop being asked.

## Tune a real instrument

The tuner tells you how close a real string is to the note it should be. Choose **Tools > Tuner…**. It listens to the audio input of an armed track, so arm a track first by clicking the red dot on its row. The audio engine must also be running (see **Chapter 14: Troubleshooting and FAQ**).

Play one string. The window shows the note name, how many cents sharp or flat you are, and the frequency. A cent is one hundredth of a semitone. Below, the strings of the selected track are listed from low to high, and the one nearest your sound lights up. The reference pitch is fixed at A = 440 Hz, and drum tracks show no strings.

## Quick recap

- Each track is one player, with its own notes, sound, volume and pan. The score shows the selected track.
- **Track > Add Bass**, **Add Guitar**, **Add Drums** and **Add Keys** add ready-made tracks. **+ Track** opens the **Add track** window with a **Position** list.
- The instrument sets the sound. **Track properties** (`F6`) holds the name, colour, mixer knobs and tuning, and changes apply when you click **OK**.
- Choose a tuning from the **Preset** list or edit each string. **Keep fret numbers** decides whether existing notes change pitch.
- Fret numbers are counted from the capo. Changing the capo keeps the numbers and moves the sound.
- **Transpose** rewrites notes, **Pitch** changes the sound only, and **Global tuning** retunes every instrument track.

## What next

Your riff now has a guitar and a bass. Go on to **Chapter 8: Drums** to add a drum track and write a basic rock beat that carries the riff along.
