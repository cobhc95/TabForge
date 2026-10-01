---
title: Reading and writing tab
id: tab-basics
order: 3
keywords: tab, tablature, strings, frets, fret number, open string, notes, note length, duration, rest, dot, dotted, tie, eighth note, undo, red bar, bar does not add up
summary: How tab maps to strings and frets, and how to type notes with their lengths, rests, dots and ties.
---

# Reading and writing tab

Tablature, or TAB, is the neck of your instrument written down. It tells you where to put your fingers. A few more marks tell you when to play and for how long. This chapter shows how to read those marks and how to write them yourself.

By the end you can say which string and fret a note uses, work out how long it lasts, type a short phrase, undo a mistake and check that every bar adds up.

## What you will learn

- Read strings and fret numbers in TAB.
- Tell note lengths apart, including rests, dots and ties.
- Type fret numbers to write notes.
- Choose a note length before you write.
- Undo and redo, and find a bar that does not add up.

## Strings and frets

A guitar TAB staff has six lines, one for each string. The top line is the thinnest, highest string, which is string 1. The bottom line is the thickest, lowest string. A bass has four or five lines, and a seven-string guitar has seven.

A number on a line is the fret to press on that string. A `0` means the open string, played with no finger on it. A `5` means press fret 5. You read from left to right, and numbers stacked at the same point are played together as a chord.

![A guitar neck above a TAB staff showing the same three notes: string 3 open, string 2 fret 3 and string 4 fret 5.](images/reading-tab.png)
*Figure: the line shows the string, and the number shows the fret.*

Standard notation sits beneath the TAB and shows each note's pitch. The **View** menu lets you show tablature, standard notation or both.

## How long a note lasts

TAB numbers say where to play, not for how long. TabForge draws the rhythm in the notation and beneath the TAB numbers. Music is counted in beats, the pulses you tap your foot to. In 4/4, a bar holds four quarter-note beats.

![A ladder of note lengths from whole note to 32nd note, with the number of beats each lasts and how many fit in a bar.](images/duration-ladder.png)
*Figure: each note lasts half as long as the one above it.*

Four marks change how long a note lasts.

- A **dot** after a note makes it half as long again.
- A **tie** joins two notes of the same pitch, so you play the first and hold through the second.
- A **rest** is a silence with its own length.
- A **triplet** squeezes three notes into the time of two.

## Type fret numbers

The edit cursor shows where the next note goes. It marks a beat and a string. Click the line of the string you want, then type a fret number with the digit keys `0` to `9`. The note appears, you hear it, and the cursor moves on by the length of the note, ready for the next one.

- For frets 10 and above, type both digits quickly, one after the other.
- Use the arrow keys to move the cursor. `Up` and `Down` change the string.
- To fix a wrong fret, type the right number over it.
- `Backspace` deletes the note on the cursor's string.

If a digit seems to choose a string instead of a fret, you are in the **Standard notation only** view. Choose **View > Tablature + standard**.

## Choose a note length

The **Note** menu lists **Whole**, **Half**, **Quarter**, **Eighth**, **Sixteenth**, **32nd** and **64th**. The length you choose is used for the next notes you write, until you choose another. The status bar shows the current length.

The same menu holds **Dotting**, **Triplet**, **Tie note** and **Rest**. With the cursor on an empty beat, these set up the next note. With the cursor on a written note, they change that note. Hover any of them to see its shortcut.

## Undo and redo

Every edit can be taken back. Press `Ctrl+Z` to undo the last edit and `Ctrl+Y` to redo it. Both are also in the **Edit** menu. You can undo several steps in a row, so try things freely.

### Try it: Write a short phrase

*Goal: write five notes on a new song, hear them and undo them.*

1. Choose **File > New**. A blank song opens in a new tab.
2. Click the fifth line down in the TAB staff of the first bar. That is the A string, and the cursor lands there.
3. Choose **Note > Eighth**. The status bar shows the new length.
4. Type `0`, `3`, `5`, `3`, `0`, one key at a time. Five notes appear, and the cursor moves on after each.
5. Click the **Rewind to beginning** button, then press `Space`. You hear the five notes.
6. Press `Space` to pause. Press `Ctrl+Z` five times. The notes disappear one by one.

You know it worked when the bar is empty again. Close the tab without saving.

**Stuck?** If nothing appears when you type, click a line of the TAB staff first so the cursor is on a string.

## Check that bars add up

Every bar holds a fixed amount of music. A bar in 4/4 holds four quarter notes, or eight eighth notes. TabForge lets you write a bar that is too long or too short, so you are never blocked in the middle of an idea. It shows you the problem instead: the bar turns red.

A red bar is a warning, not an error. The song still plays, but the bar does not match its time signature. The status bar shows how full the bar is, such as 16:16 for a full bar of 4/4.

Choose **Tools > Check bar duration** to list every bar that does not add up. Change a note length, add a rest or delete a note until the red goes away.

If you would rather be stopped than warned, open **Options > Preferences…**, search for `overfill` and tick **Prevent rhythms that overfill a bar**.

## Quick recap

- Each TAB line is a string, and each number is a fret. A `0` is an open string.
- Click a string, then type digits to write notes. The cursor moves on by itself.
- The **Note** menu sets the length and offers dots, triplets, ties and rests.
- `Ctrl+Z` undoes and `Ctrl+Y` redoes.
- A red bar does not add up. **Tools > Check bar duration** lists it.

## What next

Go on to **Chapter 4: Tracks and sound**. You will add a track, choose an instrument and balance the volume.
