# Writing notes

This chapter shows you how to put notes into a song, change them, move them, and take them away again. You do not need to know music theory to follow it. You need a song open, a string line to click, and a few keys on your keyboard.

Most of the writing happens on the TAB lines, where each number is a fret. The standard notation shows the same notes as dots on a staff, and it updates as you type. If something you type does not look right on the staff, check the TAB first, because the TAB is where you enter notes.

Take your time. Every change can be undone, so it is safe to try things.

## Before you start

You need a song open in its own document tab. A document tab is the strip at the top that shows the name of each song you have open.

1. Choose **File > New** (`Ctrl+N`) to start a blank song. Or open a song you already have with **File > Open…** (`Ctrl+O`).
2. Make sure the view shows both tab and notation. Choose **View > Tablature + standard**. You need this view to type frets. The other two views, **Tablature only** and **Standard notation only**, change how digits behave, and you may get confused if you are in the wrong one.
3. Click one of the string lines in the first bar. A highlight appears on that line. This highlight is the edit cursor.

The edit cursor marks one beat on one string. Everything you type or press goes to the place the cursor is. If you want to write in a different place, move the cursor first.

Note: A blank song has one guitar track and 4 empty bars. You can start writing in bar 1 straight away.

## Know where the cursor is

The cursor is easy to lose, especially in a long song. These are the signs to look for.

- The highlight sits on one string and one beat.
- The status bar at the bottom shows how full the current bar is, as two numbers. A full bar of 4/4 reads 16:16.
- When you press `Space` to play, a line moves across the score as the music plays.

If you cannot see the cursor at all, click an empty bar in the score. The cursor appears where you clicked.

## Move the edit cursor

The arrow keys move the cursor. Once you are in the score, you do not need the mouse.

- `Left` and `Right` move the cursor one beat. Use these to go along a bar.
- `Up` and `Down` move the cursor one string. Use these to choose a string before you type a fret.
- `Ctrl+Right` and `Ctrl+Left` move the cursor a whole bar. Use these to skip to the next bar quickly.
- `Home` jumps to the first beat of the bar you are in.
- `Ctrl+Home` jumps to the first bar of the song.
- `Ctrl+End` jumps to the last written beat of the song.

Tip: If the arrow keys seem to do nothing, click an empty part of the score. The keys go to whatever has the focus, so the score must be the one that has it.

## Enter notes on the tab

To write a note, type its fret number.

1. Click a string in an empty bar.
2. Type a fret number with the digit keys `0` to `9`. The note appears, and you hear it.
3. Press `Right` to move to the next beat.
4. Type the next fret, and repeat.

A `0` is an open string, played with no finger on it. A `5` means press fret 5. For frets 10 and above, type both digits one after the other, quickly. If you type `1` and then `2` slowly, you get fret 1 and then fret 2, not fret 12.

To change a note you already wrote, type the right fret over it. The note keeps its length, so you do not need to delete it first.

To delete the note on the cursor's string, press `Backspace`. To clear the whole beat, press `Delete`. A `Delete` on an empty beat removes the beat instead, which pulls the rest of the bar left.

Note: By default the cursor stays on the note you just wrote, so you press `Right` yourself. Turn on **Move to the next note after writing** in **Options > Preferences > Editing** if you want the cursor to move on by itself after each note.

## Choose a note length

The note length is the length the next note you write will have. There are three ways to set it.

- Click a button in the **Duration** group of the **Tools** panel. The buttons are **Whole**, **Half**, **Quarter**, **Eighth**, **Sixteenth**, **32nd** and **64th**. The button you pick lights up.
- Choose a length from the **Note** menu. The menu lists the same seven lengths.
- Press `-` to make the note one step longer, or `+` to make it one step shorter. For example, a quarter note becomes a half note with `-`.

With the cursor on an empty beat, the length sets the next note. With the cursor on a note that is already written, the length changes that note. With several beats selected, the length changes all of them.

Note: The seven note lengths have no default keys, so use the menu or the buttons. Hover a button to see its key if one has been set.

Tip: Check the lit button in the **Duration** group before you type. It shows the length that the beat under the cursor has.

## Dotted notes

A dot after a note makes it half as long again. A dotted quarter note lasts as long as a quarter and an eighth together.

- Press `.` to add a dot to the note on the cursor.
- Press `.` again to add a second dot. Two dots make the note last a little longer still.
- Press `.` a third time to remove the dots.

You can also use the **Dotting** and **Double dotting** entries in the **Note** menu, and the **Dotted** and **Double dotted** buttons in the **Duration** group. Use the buttons if you do not want to remember the key.

## Triplets

A triplet fits three notes into the time of two. Three eighth notes in a triplet take the place of two regular eighth notes.

- Press `/` to make the note on the cursor a triplet. Press it again to turn the triplet off.
- Choose **Triplet** from the **Duration** group or the **Note** menu.
- Choose **Choose tuplet ratio** from the **Duration** group if you want a different group size. This is a button with no default key.

Triplets work best when you enter the notes one after another, with `Right` between them.

## Rests

A rest is a silence. It takes up time in the bar but makes no sound.

- Press `R` to turn the beat under the cursor into a rest.
- Press `R` again on the same beat to turn it back into a note.
- Type a fret on a rest to write a note with the same length as the rest.

Rests are useful for keeping the bar full. If a bar turns red because it is too short, add a rest where the music should pause.

## Ties

A tie joins two notes of the same pitch so that the sound continues. You play the first note and let it ring through the second, without picking again.

1. Write the first note on a string, for example fret 3 on string 5.
2. Move the cursor to the next beat, with `Right`.
3. Write a note of the same pitch on the same string, fret 3 again.
4. Press `L`. The second note is tied to the one before it, so the two notes are joined.

Use a tie when a note needs to last longer than a bar line, or longer than one beat. A tie is not the same as a hammer-on, which you add with `H` as an effect.

## Write chords

A chord is two or more notes that sound on the same beat. There are two ways to write one.

- **By typing.** Type the first note on a string. Then press `Up` or `Down` to move to another string, and type its fret. The cursor stays on the beat, so both notes sit together on that beat. Repeat for each note in the chord.
- **By clicking the fretboard.** The fretboard is the picture of the neck above the score. Click a string at a fret, and a note is written on the cursor's beat. Click the same place again to remove that note. Each click adds or removes one note, so you can build a chord with several clicks.

On the fretboard, a faded note shows where a click would write before you click. This is a useful check before you commit. The fretboard works for guitar and bass tracks.

Tip: Choose the note length first. A click on an empty beat uses the current length, just as typing does.

Note: If the cursor jumps to the next beat after every fret you type, the setting **Move to the next note after writing** is turned on. Turn it off in **Options > Preferences > Editing** so that a chord stacks on one beat.

## Click the fretboard to write notes

The fretboard entry is a quick way to put notes where you can see them.

1. Move the cursor to the beat where the note goes.
2. Look at the fretboard. The faded note shows the spot under the mouse.
3. Click the string and fret you want. The note is written, and the score updates.
4. Click the same spot again to remove that note.

A click on the fretboard works while the song plays, so you can add notes as you listen.

## Select notes and bars

A selection is a stretch of beats that you act on together. You make a selection before you copy, cut, change a length, or apply an effect to many notes.

- Hold `Shift` and press `Left` or `Right`. The selection grows one beat at a time.
- Click the first beat, then hold `Shift` and click the last beat. Everything between the two clicks is selected.
- Press and hold on a beat for a moment, then drag sideways across the beats. A quick click stays a click and does not select.
- Press `Ctrl+A` to select every bar of the track.
- Press `Esc` to clear the selection.

When beats are selected, the edit you make applies to all of them. Check the selection before you press a key that changes many notes.

## Copy, cut and paste

Copy and paste work on the selection. If nothing is selected, they work on the beat under the cursor.

1. Select the beats you want, or click one beat.
2. Press `Ctrl+C` to copy. The notes stay where they are.
3. Or press `Ctrl+X` to cut. The cut beats become rests, and the notes move to the clipboard.
4. Click where the copy should go.
5. Press `Ctrl+V` to paste.

If the paste needs a choice, such as replace or insert, a small window asks you. Pick the one you want. The window only appears when it needs an answer.

Press `Ctrl+Shift+V` for **Paste special**. It lets you paste with a repeat count, replace or insert, shift the octave, keep the string and fret, and copy the bar settings too.

Tip: To copy a whole bar, click its first beat, hold `Shift`, and click its last beat. Then press `Ctrl+C`.

Warning: A note that does not fit in its bar stays in that bar, and the bar shows red. Press `F4` to check the bars after a paste, so you know where the problem is.

## Undo and redo

Every edit can be taken back. This is the safest way to learn the program.

- Press `Ctrl+Z` to undo the last edit.
- Press `Ctrl+Y` to redo an edit you undid.
- Press `Ctrl+Z` several times to step back through a series of edits.

Each edit is one undo step. If you undo too far, press `Ctrl+Y` to come back.

## Step through the beats

Two buttons in the **Structure** panel move the cursor one beat at a time.

- **Step back one beat** moves the cursor one beat to the left.
- **Step forward one beat** moves the cursor one beat to the right.

Use them when you want to move slowly along a bar and watch each beat. These buttons have no default keys, so you can set your own keys in **Options > Preferences > Shortcuts** if you want them.

## Notes on the standard staff

The standard notation above or below the tab shows each note as a dot on the staff, with the correct pitch and length. Use it to check how the music reads.

Each note is entered on the TAB. The staff follows what you write on the tab. If you need to change a note's pitch, change its fret or string on the TAB.

## Keyboard entry and the fretboard

This chapter covers entering notes two ways: typing frets on the keyboard, and clicking the fretboard.

## Common questions

Question: Why does my note appear on the wrong string?
Answer: The cursor was on another string when you typed. Press `Up` or `Down` until the highlight is on the string you want, then type the fret.

Question: Why does the cursor not move after I type a note?
Answer: By default the cursor stays on the note you wrote. Press `Right` to move on. If you want it to move on by itself, turn on **Move to the next note after writing** in **Options > Preferences > Editing**.

Question: How do I write a chord?
Answer: Type one note, press `Up` or `Down` to a new string, and type that fret while the cursor is still on the same beat. Or click the fretboard once for each note.

Question: What is the difference between a tie and a hammer-on?
Answer: A tie joins two notes of the same pitch into one long sound. A hammer-on is an effect on a note that makes the second note sound without picking. You add a hammer-on with `H`.

Question: How do I undo a whole group of edits?
Answer: Press `Ctrl+Z` once for each edit. Each edit is one step, so a paste or a bar insert needs one press to undo.

Question: Can I type notes directly onto the standard staff?
Answer: Enter notes on the TAB. The staff shows what you wrote and updates with it.

## Troubleshooting

Problem: I can't hear anything when I type a fret.
Fix: Check that the track is not muted. Its **M** button should not be lit. Also check that the song's volume is not turned down in the mixer. Press `Space` to hear the notes you wrote.

Problem: My note jumped to the wrong string.
Fix: The cursor was on a different string when you typed. Press `Up` or `Down` until the highlight sits on the string you want, then type the fret again.

Problem: I typed 12 but got a 1 and then a 2 on different beats.
Fix: Type both digits quickly, one after the other. If you pause between them, the program reads the first digit as a fret on its own.

Problem: I pressed a digit and a string was chosen, not a fret.
Fix: You are in the standard notation only view. Choose **View > Tablature + standard** from the **View** menu.

Problem: The bar turned red after I wrote a note, and I don't know why.
Fix: The bar holds more or less music than its time signature asks for. Press `F4` or choose **Bar > Check bar duration** to list the bar. Then undo with `Ctrl+Z`, or add or remove a rest to fix the length.

Problem: I clicked the fretboard, but nothing was written.
Fix: Check that the cursor is on a beat you can write to, and that you clicked a string and fret on the fretboard itself, not the space around it. The fretboard works for guitar and bass tracks, so a drum track will not take a fretboard click.

Problem: My chord turned into separate notes, one on each beat.
Fix: **Move to the next note after writing** is turned on. Turn it off in **Options > Preferences > Editing**, then write the chord again: type a note, press `Up` or `Down`, and type the next fret.

Problem: I pressed `Ctrl+V` and nothing happened.
Fix: The clipboard may be empty. Copy the beats first with `Ctrl+C`, then click the place where you want the copy, and press `Ctrl+V` again.

Problem: A note turned into a rest by itself.
Fix: You may have pressed `R` on that beat, or cut the beat with `Ctrl+X`. Press `R` again on the same beat to turn it back, or press `Ctrl+Z` to undo.

Problem: The `+` and `-` keys do the opposite of what I expect.
Fix: The setting **Plus key lengthens notes** in **Options > Preferences > Editing** swaps them. Turn it on or off there.

Problem: My notes went in when I wanted them in the next bar.
Fix: The cursor was at the end of the bar you were in. Press `Ctrl+Right` to move to the next bar, then type the notes again.

Problem: I pressed a key, and a window opened that I did not expect.
Fix: The key is probably a command with its own window, such as the chord or text window. Close that window, then press `Ctrl+Z` if the song changed.

Related: Effects and expression, Bars and structure, Rhythm and layout.
