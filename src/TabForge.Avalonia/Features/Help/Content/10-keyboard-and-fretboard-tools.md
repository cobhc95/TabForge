# Keyboard and fretboard tools

TabForge shows your instrument under the score. For a guitar, bass or other stringed instrument this is a fretboard. For a piano, a synth or a horn it is a keyboard. For drums it is a row of drum pads. This chapter explains that panel, the colour legend beside it, the windows that change how it looks, and the small tools that help you with notes and chords: the Scale finder, the Chord finder, the Tuner, Global tuning and Song stats.

If you are new to this, read the first section, the instrument panel, and then try each tool once with a demo song open.

## What this chapter covers

- The instrument panel: fretboard, keyboard and drum pads.
- Choosing which view each track shows.
- The colour legend: Now, Next, Upcoming and Recent.
- The Scales and Modify buttons.
- The Fretboard settings window and its five sections.
- The right-click menu of the fretboard.
- Scale highlight: showing the notes of a scale on the board.
- The keyboard view and its key colours.
- The Scale finder, Chord finder, Tuner, Global tuning and Song stats.

## The instrument panel

The instrument panel is the strip under the score that shows the notes of the track you are on. It usually matches the track automatically.

- A track with stringed-instrument strings, such as guitar, bass, oud, cello, violin, ukulele, mandolin or banjo, shows a fretboard with that track's own strings.
- A drum track shows drum pads.
- Piano, winds, brass, synths and most other tracks show a keyboard with 88 keys.

You can change this choice:

- Open **Options > Preferences > Fretboard & Keyboard** and choose **Default instrument view**. You can make the panel always show a fretboard, a keyboard or drums.
- Choose **Keyboard size** on the same page to pick 88, 76, 61, 49, 37 or 25 keys. Smaller keyboards follow the notes, so they show only the keys the track needs.
- Right-click the instrument panel and choose **Show this track as** to change this track only.
- Right-click and choose **Show all tracks as** to change every track for the rest of this session.
- The command **Switch instrument view** cycles the current track through fretboard, keyboard and drum pads. It has no default key. Give it one in Options > Preferences > Shortcuts if you use it often.

To hide the panel for more room for the score:

1. Choose **View > Instrument view** to turn the panel off. The menu row is ticked while the panel shows.
2. Choose it again to bring the panel back.
3. The same command has no default key. You can give it one in Options > Preferences > Shortcuts.

Position controls where the panel sits:

- By default, the fretboard or keyboard sits above the score.
- The command **Fretboard position: toggle top / bottom** moves it below the score, above the timeline. The right-click menu has a Position choice too. It has no default key.

Size controls how tall the panel is:

- Drag the edge of the panel to resize it. The drawing scales to fit.
- The command **Lock fretboard size** keeps the height fixed, so dragging the edge does nothing. The panel is unlocked by default. The right-click menu also has this setting.

Tip: If the panel is too tall, lock it at a smaller height and then turn on Compact mode from the View menu. Compact mode hides the track rows so the score has more room.

## The colour legend

The legend is a small list of colours beside the fretboard. Each colour is a moment in time, so you can see where the notes you are playing sit relative to the notes that are coming.

- **Now** is the notes at the current position of the playhead.
- **Next** is the notes that come right after Now.
- **Upcoming** is the notes further ahead, in the order the song reaches them.
- **Recent** is the notes you have just played, shown as a faint trail. It appears when the trail option is on in the Fretboard settings.

The legend changes with the settings. Depending on the options, you may see:

- Only Now.
- Now and Recent.
- Now, Next and Upcoming.
- All four: Now, Next, Upcoming and Recent.

To change a colour:

1. Click the row of the colour you want to change in the legend. A colour picker opens under the row.
2. Pick a colour. The panel shows it at once.
3. Close the picker. The colour is saved, and the legend and the Fretboard settings window both show it.

To go back to the default colours, open the Fretboard settings window and use **Reset note colours** in the Notes and markers section.

Tip: You do not need to memorise the colours. Watch which colour the note at the playhead has, and it will become familiar after a few songs.

## The Scales and Modify buttons

Two buttons sit under the colour legend, beside the fretboard. In keyboard and drum view they sit at the top right.

- **Scales** opens the scale popup. It shows the scales that fit the notes of your song, lets you pick one, and lights its notes on the board. Use it to find out which scale a riff is built on.
- **Modify** is the quick way to change how the fretboard looks. It opens the Fretboard settings window described below.

## The Fretboard settings window

The Fretboard settings window is a small tool window with a list of sections on the left and the cards of the chosen section on the right. The five sections are:

- **Look** changes the overall style of the board: the wood (dark rosewood in the dark theme, birdseye maple in the light one), the binding rails, the inlay dots and their material and shape, and whether a sounding string vibrates. Vibration speed is here too.
- **Notes and markers** changes the note dots and their labels, note brightness, the colours of the note dots, the fret marker colour and brightness, and the number size. Reset note colours is in this section.
- **Layout and size** changes the fret count shown, the dot and number size, the board position and whether the board is left-handed.
- **Strings and capo** changes the string spacing (Compact, Natural or Wide), the string numbers, the string finish and the capo look.
- **Scale highlight** changes how the highlighted scale is drawn. It is covered in the next section.

The footer has **More settings** on the left, which opens the wider settings page, and **Reset all** to reset everything. On the right are a status line, **Cancel**, **Apply** and **OK**.

How the buttons behave:

- Every pick is written to the settings and appears on the board at once.
- **Apply** and **OK** keep your picks.
- **Cancel**, or closing the window, puts back the values from the last Apply, or from when you opened the window.
- **Reset section** on a section writes that section's defaults back, and leaves the other sections alone.

Step by step to change the look:

1. Click **Modify** under the legend.
2. Choose **Look** in the list on the left.
3. Click a wood or a binding you like. The board changes at once.
4. Choose **Notes and markers** and adjust the note brightness if the dots are hard to see.
5. Click **OK** to keep the changes, or **Cancel** to go back.

Note: The fretboard look is saved for every song and window. The same choices are also in Preferences > Fretboard & Keyboard.

## The right-click menu of the fretboard

Right-click the instrument panel to open its menu. The main choices are:

- **Show this track as** and **Show all tracks as**, described above.
- **Keyboard size**, when a keyboard is shown.
- **Scale**, with **Select scale** (a quick list), **Find scale…** (the full scale finder), and **Clear selection** to remove the highlighted scale.
- **Appearance**, which covers the key colours on the keyboard, the scale highlight style and colour, and the fret marker colour and brightness. These choices are saved with each song and window.
- **Position** and **Lock fretboard size**.

You can open the same menu from the keyboard. Click the panel so it has the focus, then press `Shift+F10` or the Menu key. Use the arrow keys to move through the items, `Enter` to choose one, and `Esc` to close it.

## Scale highlight

Scale highlight shades the notes of one scale on the fretboard or keyboard. It helps you see which notes belong to a key, a riff or a mode.

- Choose a scale from **Scale > Select scale** in the right-click menu for a quick pick.
- Choose **Find scale…** for the full scale popup, or use **Tools > Scale finder…**.
- The status bar shows a readout of the scale. Click it to open the same popup.

The scale popup has these parts:

- The detected scales, with a badge that says Auto or Manual, for example Manual: D minor. **Back to Auto** returns to the detected scale.
- **Read from** chooses where the scale is found: this section, the whole song, the song key, or the selected bars when the song has a selection.
- **Listen to** chooses which tracks count: the selected track, or all tracks with pitch.
- Scale families you want the finder to consider.
- A pick sticks to the section and track you chose it on.
- **More scales…** lists every scale in all twelve keys, grouped by family, with a search box.

The **Fretboard look** part of the popup changes how the highlight is drawn:

- Show the highlight on or off.
- Five styles: Dots, Rings, Cell tint, Note names and Degrees.
- Colour swatches and a custom colour, with an opacity slider.
- Emphasise the root, which draws the root note more strongly.
- Dim notes outside the scale.
- Only on the frets in view, so the highlight follows the part of the neck on screen.

The keyboard view shows the scale too. It highlights the scale's keys the same way, with the root stronger, and it uses the same right-click scale tools.

Commands for scale highlight:

- **Clear scale highlight** removes the highlight. It has no default key.
- **Scale highlight brighter** makes the highlight 10% stronger, up to 150%.
- **Scale highlight dimmer** makes it 10% weaker, down to 10%.
- The same strength setting is in Options > Preferences > Fretboard & Keyboard.

Step by step to find the scale of a riff:

1. Select the notes of the riff on the timeline, or leave nothing selected to use the whole song.
2. Click **Scales** under the legend, or choose **Tools > Scale finder…**.
3. Set **Read from** to the selected bars, or to the song key.
4. Read the detected scale at the top of the list.
5. Click the scale you want. It lights up on the board.
6. Choose **Back to Auto** when you want the finder to choose again.

## The keyboard view

The keyboard view draws keys with their notes. Use it for piano, synth and other keyboard tracks.

- **Key colours** on the Appearance menu controls the look of the keys. Choose to match the theme (grey in the dark theme, white in the light one), or always grey, or always white.
- The keyboard shows the notes of the track as they play, and it follows the scale highlight, as described above.
- For practice with falling notes and a wait-for-me option, use Keyboard mode instead. It is described in the Playback and practice chapter.

## Scale finder, Chord finder, Tuner, Global tuning and Song stats

These tools open from the **Tools** menu. Several of them have no default key, but you can give them one in Options > Preferences > Shortcuts.

The Scale finder is described above.

**Chord finder** (Tools > Chord finder…) shows the notes of a chord.

1. Choose a root note, such as C or D.
2. Choose a chord type, such as major or minor.
3. The window lists the notes of the chord.
4. Click **Insert name** to put the chord name on the beat under the score cursor.

Note: Insert name writes the name onto the beat under the cursor. Click the beat you want first, then open the Chord finder.

**Tuner** (Tools > Tuner…) helps you tune your instrument.

- It shows the nearest note, its frequency in hertz, and how many cents it is sharp or flat.
- A needle shows the cents. The middle means in tune.
- It shows the string tunings of the selected track, and lights the string closest to the sound.
- It listens to the audio input you have chosen, and it follows the selected track.
- The tuner only listens while its window is open.

Step by step to tune a string:

1. Plug your instrument into the input you use for recording.
2. Choose **Tools > Tuner…**.
3. Pluck one string and watch the note name and the needle.
4. Turn the tuning peg until the needle sits in the middle.
5. Repeat for each string.

**Global tuning** (Tools > Global tuning…) changes the tuning of every pitched track in the song by the same amount. You can pick a tuning preset or set each string.

- The same window opens from the tuning fork button on the timeline.
- Double-click the number on that button to type a semitone shift.
- Right-click the button for quick options: up or down a semitone, or back to the original tuning.
- The change is one undo step.

**Song stats** (Tools > Song stats…) shows the number of tracks, bars, notes and sections in the song. Use it to get a quick picture of a song before you edit it.

Tip: If the tuner does not respond, check the audio input in Preferences > Audio & Plug-ins. The tuner uses the audio you have armed for recording.

## Troubleshooting

Problem: The fretboard is not showing at all.
Fix: Choose View > Instrument view to turn the panel on. If it is on but empty, the track may be a drum or keyboard track; check the Default instrument view in Preferences > Fretboard & Keyboard.

Problem: The fretboard shows a keyboard when I wanted strings.
Fix: Right-click the panel, choose Show this track as, and pick the fretboard. This changes only the current track.

Problem: The scale highlight is on the wrong notes.
Fix: Open the Scale finder and check the Read from choice. If it is set to the whole song, the detected scale may be different from the phrase you are looking at. Choose the selected bars instead, or pick the scale manually.

Problem: The scale highlight is too faint or too strong.
Fix: Change the Scale highlight strength in Options > Preferences > Fretboard & Keyboard. The brighter and dimmer commands also change it, in steps of 10%; give them keys in Options > Preferences > Shortcuts if you want them.

Problem: The colour legend has the wrong colours.
Fix: Open the Fretboard settings window and choose Reset note colours in the Notes and markers section. The defaults come back.

Problem: My changes to the fretboard look disappear when I reopen the song.
Fix: Click Apply or OK in the Fretboard settings window before you close it. Cancel and closing without OK put back the values from before.

Problem: The tuner shows nothing, or only a dash.
Fix: Play a steady note on the string and keep the instrument close to the input. Check that the audio input you use is selected in Preferences > Audio & Plug-ins, and that the input is not muted.

Problem: The Chord finder inserts the chord in the wrong place.
Fix: Click the beat where you want the name, so the cursor is on it, and then open the Chord finder and press Insert name.

Problem: The panel is too tall and hides the score.
Fix: Drag its lower edge up, or turn on Lock fretboard size and then choose a smaller height. You can also hide the panel with View > Instrument view.

## Common questions

Question: Why does my bass track show four strings instead of six?
Answer: The panel matches the strings of each track. Check the track's tuning and string count in Track properties. The fretboard follows what the track has.

Question: What is the difference between Scales and the Scale finder?
Answer: They open the same window. The Scales button under the legend is a shortcut to the Scale finder.

Question: Can the fretboard show a piano keyboard instead?
Answer: Yes. Right-click the panel and choose Show this track as, then pick the keyboard. Keyboard size lets you choose how many keys it shows.

Question: Does Insert name change the notes of my song?
Answer: No. It only writes the chord name as text on the beat under the cursor.

Question: Why is the tuner quiet when I play?
Answer: The tuner listens to the input you have chosen for recording. Play a little louder, move the instrument closer to the input, or choose the correct input in Preferences.

Question: Where do I change the default fretboard or keyboard view?
Answer: Open Preferences > Fretboard & Keyboard and choose Default instrument view.

Related: Playback and practice, Composer and left strip, Settings, themes and hotkeys, Troubleshooting and FAQ
