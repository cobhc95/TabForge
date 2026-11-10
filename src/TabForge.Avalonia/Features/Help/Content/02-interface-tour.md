# Interface tour

This chapter walks you around the TabForge window, from the top edge to the bottom. You do not need to learn every part at once. Use it as a map, and come back to a section whenever you want to know what a part is for or where a button lives.

The window has these main parts, from top to bottom: the menu bar, the tab strip, the left icon strip and fretboard, the score in the middle, the right panel, the track list and timeline, the transport bar, and the status bar at the very bottom.

## The menu bar

The menu bar runs along the top of the window. Each menu groups related commands together. Click a menu name to open it, and click a command to run it. A command that has a shortcut shows it beside its name.

- **File** opens, saves, imports and exports songs, and it closes tabs. It also holds the printing items, which are not available yet in this version.
- **Edit** holds **Undo**, **Redo**, **Copy**, **Cut**, **Paste**, **Paste special**, **Select all** and the repeat and delete commands for beats and bars.
- **Insert** adds things to the song: a track, a bar, a beat, a section, a chord, text, lyrics, and audio or MIDI files.
- **Bar** changes whole bars. It holds the time signature, key signature, repeats, double bars, clefs, triplet feel and the commands for deleting, duplicating and clearing a bar. **Go to** jumps to a bar number.
- **Track** adds, deletes and moves tracks, and opens the properties of the selected track.
- **Note** changes the selected notes. It holds the note values (whole, half, quarter and so on), ties, rests, and an **Effects** submenu with playing techniques such as bends, slides, vibrato, harmonics and tremolo picking.
- **View** shows or hides panels, switches the score between its views, sets the zoom, and turns on the full-window modes.
- **Tools** holds helpers such as **Transpose**, **Scale finder**, **Chord finder**, **Song stats**, **Tuner**, **Global tuning** and **Preset manager**.
- **Options** holds **Preferences** and **MIDI / Audio setup**.
- **Help** holds **Help** (`F1`, this window), **Command palette**, **Check for updates** and **About**.

Tip: If you cannot find something, do not hunt through every menu. Press `Ctrl+Shift+A` and type a word from the command instead.

Note: The **Insert** menu contains a **Track** submenu. Choose **Insert > Track** to add a Bass, Drums, Keys, or a new tab track or audio track quickly.

## The tab strip

Every song you have open has its own tab. The tabs run along the top of the window, just below the menu bar.

- Click a tab to switch to that song.
- Click the **+** button in the tab strip to start a new song in a new tab. Its tooltip reads **New tab**. You can also press `Ctrl+N`.
- Drag a tab left or right to change the order of the tabs.
- Press `Ctrl+W` to close the current tab. The same command is **Close tab** in the **File** menu.
- Press `Ctrl+Shift+D` to duplicate the current tab. The copy is a new, unsaved song, so your original file is safe.

A small dot on a tab means the song has changes that you have not saved yet. A small speaker on a tab means that song is playing right now.

Note: Each tab is a separate song. Changes in one tab never affect another, and each tab keeps its own undo steps.

Tip: Work on a copy when you try something risky. Duplicate the tab with `Ctrl+Shift+D`, try the change in the copy, and close the copy if you do not like the result.

## The left icon strip

The left icon strip is a slim column of buttons along the left edge of the window. Each button runs one command, so common jobs are one click away.

By default the strip starts with **New**, **Open**, **Save**, **Save as** and **Render**. Next comes a divider, then **Undo** and **Redo**, then another divider, then the **Composer** toggle.

Here is what the buttons do.

- **New** starts a new song in a new tab.
- **Open** opens a song file.
- **Save** saves the current song.
- **Save as** saves the current song under a new name or in a new place.
- **Render** turns the song into an audio file.
- **Undo** and **Redo** step back and forward through your edits.
- **Composer** opens or closes the Composer panel beside the strip.

Other buttons can be added to the strip. The list includes **Add track**, **Mixer**, **Tuner**, **Zoom in** and **Zoom out**, **Band view**, **Compact mode**, **Fretboard**, **Chord finder**, **Scale finder** and **Preferences**. Their groups are named File, Edit, Playback, Song, View and App in the list.

You can change the strip in these ways.

- Move the strip to the right side of the window.
- Place the strip beside the menu bar instead of down the side.
- Hide the strip altogether with **View > Left strip**.

To bring a hidden strip back, choose **View > Left strip** again. The **View** menu also has **Show strip button** and **Show strip on start**, which control whether the strip's button is shown and whether the strip opens when TabForge starts.

Note: The Composer panel is an early preview for now. It shows a few placeholder rows and a small preview box, and it does not yet have a riff library.

## The fretboard

The fretboard shows the instrument for the selected track. For a guitar or bass it draws the neck, so you can see which string and fret each note uses. For a drum track it shows a drum map, and for a keys track it shows a keyboard.

- It follows playback, so you can see the notes light up as they sound.
- It shows the notes of the track you have selected.
- To hide the fretboard for more room for the score, choose **View > Instrument view**. Choose it again to bring the fretboard back.

Note: There is no shortcut for **Instrument view** by default, so use the menu or the command palette.

Tip: Use the fretboard while you are learning a fingering. Each note shows where it sits on the neck, which is quicker to read than the numbers alone.

## The score area

The score area is the large space in the middle of the window. It shows the selected track as tablature, standard notation, or both. You choose the look from the **View** menu.

- **Tab and staff** shows both at once.
- **Tab only** shows the string and fret numbers.
- **Staff only** shows the standard notation.

You can also choose a dark or light score page from the **View** menu, depending on what is easier on your eyes.

The score is where you do most of your work.

- Click a beat to place the edit cursor there.
- Roll the mouse wheel over the score to scroll it.
- Press `Ctrl+=` to zoom in, and `Ctrl+-` to zoom out.
- Right-click the score for a menu of the things you can do at that point.

The score always shows the track that you have selected in the track list. If the score seems to show the wrong part, click the right track in the track list.

Tip: Right-click the score and look at **Score scrolling** to choose how the score moves. Vertical scrolling wraps the bars into lines and scrolls down the page. Horizontal scrolling puts the whole score on one line that scrolls to the right.

Tip: Turn on **Follow playback** with `Ctrl+Shift+F` if you want the score to keep up with the music. Turn it off if you want to read a part without the score moving.

## The right panel

The right side of the window holds panels. Each panel is a box with its own tab at the top, and you click a tab to bring that panel forward.

- **Tools, Structure, Rhythm and Layout** are four pages in one panel.
- **Sections** lists the sections of the song, so you can move between them.
- **Lyrics** holds the words of the song.
- **View and tempo** holds the zoom controls and the tempo of the song.

Tools has a palette of editing tools. Its groups include Edit, where the selection pointer and the erase tool live, and Composition, which holds time signature, tempo and repeats. Structure holds bar editing, such as inserting, duplicating and deleting a bar, and it also has markers. Rhythm holds tuplets, ties and octave signs. Layout holds voices, line breaks, beaming and stem direction.

Panels can be moved about the window. Drag a panel's tab to a new spot, and it docks there. To put every panel back to where it started, choose **View > Reset all panels to default positions**. It never changes the song.

Tip: If a panel is missing, open the **View** menu and look in the list of panels. Choose the one you want to bring it back.

## The track list

The track list sits beside the timeline, at the bottom of the window. It has one row for each track in the song.

Each row shows the track's name and a few small controls.

- **M** mutes the track, so you hear nothing from it.
- **S** solos the track, so you hear only it.
- **FX** opens the track's effects chain.

The track list also has an **Add track** button at its end. It adds a new track to the bottom of the list.

- Click an empty part of a row to select that track. The score and the fretboard then show it.
- Use `Ctrl+Shift+Up` and `Ctrl+Shift+Down` to select the previous or next track without the mouse.
- Use `Alt+Shift+Up` and `Alt+Shift+Down` to move the selected track up or down the list.
- Press `F6` to open the track properties window for the selected track.
- Press `Ctrl+T` to open the **Add track** window.

The track list also has a **Show master track** option that shows or hides the master track row at the top of the list. The master track row is an overview lane for the whole song.

Tip: Hover over any control in a track row to read its name before you click it.

## The timeline

The timeline is the strip of bars under the score. It shows the whole song from left to right, so you can see the shape of the music at a glance.

- Each box in the timeline is one bar.
- Click a bar to jump to that part of the song.
- Choose **View > Arrangement overview** or press `F8` to show or hide the timeline as its own panel.
- The timeline has its own zoom, separate from the score zoom. Press `Ctrl+Alt+Shift+=` to make the bars wider, and `Ctrl+Alt+Shift+-` to make them narrower.
- Press `Esc` to clear a selected area of the timeline.

Note: The timeline zoom changes only how wide the bars look on screen. The score zoom is a separate setting, found under **View > Zoom**.

## The transport bar

The transport bar sits at the bottom of the window, above the status bar. It holds the playback controls.

- **Play / pause** starts the song or pauses it. The shortcut is `Space`.
- **Stop** stops playback and returns to the edit cursor. The shortcut is `Ctrl+.`.
- **Play from beginning** restarts playback from the first bar. The shortcut is `Shift+Space`.
- **Rewind to beginning** jumps back to the first bar without playing.
- **Loop** plays a part of the song again and again. The shortcut is `F9`. Loop plays the selected area, or the whole song when no area is chosen.
- **Metronome** turns the click on or off, so you can keep time.
- **Count-in** plays a few clicks before playback begins, so you have a beat to join in on.
- **Record** records every armed track while the song plays. The shortcut is `Ctrl+R`. Press it again to stop.
- **Mixer** opens the mixer window.

Hover over any button to read its name and shortcut.

Tip: The speed of playback can be changed with `Ctrl+Alt+Up` and `Ctrl+Alt+Down`. It moves through 50, 75, 100, 125, 150 and 200 percent. Press `Ctrl+Alt+0` to go back to the song's own speed, which is 100 percent.

## The status bar

The status bar runs along the very bottom of the window. It shows where you are in the song.

- The bar number and the time of the edit cursor.
- The name of the track and the cell where the cursor sits.
- Whether the song is playing.
- Short messages about what TabForge has just done, such as an export that has finished or a file that could not be opened.

For a screen reader, press `Ctrl+Alt+B` to hear the current bar, or `Ctrl+Alt+P` to hear the full position of the cursor.

## Band view

Band view shows every track in the song together. Each track's instrument and its tab sit in rows, with the timeline below. It is useful when you want to see the whole band at once.

- Choose **View > Band view** to switch to it.
- Choose it again to go back to the layout you left.
- To add or remove the selected track's row from the Band view, use the command palette and search for `Band view`.

## Compact mode

Compact mode hides every track row and shows the master track lane at twice its normal height. This gives the score and the other panels more room, which helps on a small screen or when you only want to write.

- Choose **View > Compact mode**, or press `Ctrl+Alt+C`.
- Press the same keys again to bring the track rows back.

## Keyboard mode

Keyboard mode shows the notes of the selected track falling onto a strip of keys, in time with playback. It is marked experimental.

- Press `Ctrl+Alt+L` to show or hide it.
- Press `Ctrl+Alt+O` to open it in its own window.
- Press `Ctrl+Alt+W` to make the playback wait for the right notes at each chord.

## Common questions

Question: I can't see the score at all. Where did it go?
Answer: The panel may have been hidden or moved. Choose **View > Reset all panels to default positions**. The score is the large central area and always stays put.

Question: Why does the score show a different track from the one I expected?
Answer: The score shows the track you have selected. Click the right row in the track list, or press `Ctrl+Shift+Up` and `Ctrl+Shift+Down` to step through the tracks.

Question: What is the difference between the score zoom and the timeline zoom?
Answer: The score zoom changes how big the notes look in the score, found under **View > Zoom**. The timeline zoom changes how wide the bars are in the timeline, using `Ctrl+Alt+Shift+=` and `Ctrl+Alt+Shift+-`.

Question: How do I get more room to write?
Answer: Turn on compact mode with `Ctrl+Alt+C`, or hide the fretboard with **View > Instrument view**. Either one gives the score more space. Turn them back on the same way when you need them.

Question: Does the left strip save its buttons?
Answer: Yes. Your choice of buttons and the strip's position are kept, so the strip looks the same next time you open TabForge.

Question: Do I need the Composer panel?
Answer: No. It is an early preview with no riff library yet, so you can ignore it. Its toggle in the strip is harmless to leave alone.

## Troubleshooting

Problem: The left icon strip has disappeared from the window.
Fix: Choose **View > Left strip** from the menu bar to show it again. If the strip still does not appear, check **View > Show strip on start**, which decides whether the strip opens when TabForge starts.

Problem: I moved a panel and now I cannot find it.
Fix: Choose **View > Reset all panels to default positions**. Every panel returns to its starting place. This does not change your song.

Problem: The score is too big or too small for my screen.
Fix: Press `Ctrl+=` to zoom in or `Ctrl+-` to zoom out. You can also pick a fixed size under **View > Zoom**, or use the zoom box in the **View and tempo** panel.

Problem: The fretboard takes up too much of the window, and I need more room for the score.
Fix: Choose **View > Instrument view** to hide the fretboard. Choose it again whenever you want it back.

Problem: I clicked a track but the score still shows a different track.
Fix: Click an empty part of the track's row, not its name or a button. The score shows the track that is highlighted as selected in the list.

Problem: I can't see the track I want, because the track list is full.
Fix: Scroll the track list with the mouse wheel. Or press `Ctrl+Shift+Down` to step to the next track, which brings it into view.

Problem: The status bar shows a message I do not understand.
Fix: The message describes the last thing TabForge did, such as an export or a file that could not open. Read it again after you try the step you were on. If it still does not make sense, press `Ctrl+Shift+A` and search for the action you were trying to do.

Problem: The timeline bars are too wide or too narrow.
Fix: Press `Ctrl+Alt+Shift+=` to widen the bars, or `Ctrl+Alt+Shift+-` to narrow them. This changes only the timeline, not the score.

Problem: I don't know what a button does.
Fix: Hover over the button and wait a moment. A tooltip shows its name and, when it has one, its shortcut. You can also press `Ctrl+Shift+A` to search every command.

Related: Getting started, Files and export, Tracks and mixer
