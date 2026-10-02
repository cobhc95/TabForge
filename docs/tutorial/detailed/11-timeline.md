---
title: Arranging and recording
id: timeline
order: 11
keywords: timeline, arrangement, arrange, section, sections, add section, move section, resize section, duplicate section, copy bars, paste bars, move bars, select bars, loop area, repeats, endings, record, recording, clip, snap
summary: Read the timeline, build sections, copy and move bars across tracks, reorder tracks, and optionally record and manage audio clips.
est-minutes: 45
---

# Arranging and recording

A riff becomes a song when you give it a shape: an intro, a verse that comes back, a bar of silence before the next idea. The timeline is where you build that shape. It shows every track at once, so you can move whole stretches of music with a few clicks.

This chapter also shows how to record yourself playing along. That part is optional, and it needs an audio input such as a microphone or an audio interface.

## What you will learn

- Read the timeline: its rows, ruler, section lane and playback marker.
- Add, rename, recolour, move, resize and duplicate sections.
- Select bars across every track, then copy, cut, paste, move or skip them.
- Remove a section without losing music by accident.
- Reorder tracks and group them in the track list.
- Record a take and work with audio and MIDI clips (optional).

## What the timeline shows

The timeline (**View > Arrangement overview**) is the strip along the bottom of the window. It has one row for each track. Each bar that holds notes appears as a block in that row, so you can see where every instrument plays.

Along the top runs the ruler with bar numbers. Below it is the section lane, a row of coloured blocks with names such as Intro and Chorus. The playback marker shows where the music is now. By default it is a thin vertical line.

The mouse helps you find your way. The bar cell under the pointer is shaded. Click a bar to put the edit cursor there. Turn the mouse wheel over the timeline to zoom in or out around the pointer. The wheel zooms and does not scroll, so use the scroll bar in the status bar to move sideways. Two small zoom buttons at the right of the timeline header do the same as the wheel.

You can change the marker. Open **Preferences > Timeline & Tracks**, find the **Timeline display** group and open its **More options**. **Playback position marker** offers **Line**, **Bar marker** or **Both**. The bar marker is a small dark square in the current bar of the selected track, and it moves one bar at a time.

## Add and name sections

A section is a named stretch of bars with a job in the song, such as Intro, Verse or Chorus. Sections make a long song easy to navigate, and they are the units you will move and copy. A song can have only one section starting on each bar.

There are three ways to add a section.

- Press `M`.
- Click **Add** under the list in the **Sections** panel.
- Right-click the section lane at a bar and choose **Add section at bar 5** (the number is the bar you clicked).

A small window opens. Type a name in **Marker name**, choose a **Colour**, then click **Add**. If a section already starts on that bar, the same window opens so you can edit that section instead.

Sections that share a base name share a colour. **Verse 1** and **Verse 2** both count as Verse, so they look alike. You can turn this off in **Preferences > Timeline & Tracks** under **Same colour for similar sections**.

The **Sections** panel lists every section in its colour. Click a name to jump the cursor to that section. The **Go** button does the same, **Edit** reopens the name and colour window, and a padlock marks a locked section.

> **Note:** **Bar > Section…** is a different, text-only command. It stores a plain text name on one bar, with no colour, and the timeline does not use it. To build the arrangement, use the methods above.

## Move and resize sections

Sections can move in two different ways. Drag a block to move only its marker: the bars stay where they are, and the section takes the place of free bars beside it. Hold `Ctrl` and drag to move the section together with its bars, and the sections around it make room.

A plain drag needs free bars next to the section. In a song where sections fill every bar, hover over a block to read its hint. If no free bars exist, the hint says the section cannot move on its own. Use `Ctrl` and drag instead.

To resize a section, drag its left or right edge. Making a section shorter leaves bars that belong to no section. Making it longer pushes the sections after it along. A locked section does not move or resize. To unlock one, right-click it and untick **Lock section position**.

### Try it: Move a chorus

*Goal: move the first chorus of the demo song, then put it back.*

1. Choose **File > Open…**, go to the `Samples` folder beside `TabForge.exe`, choose `TabForge Demo - Ashen Meridian.gp` and click **Open**.
2. Turn the mouse wheel up over the timeline until the blocks in the section lane are wide enough to grab.
3. Find the block named **Chorus 1**, which starts at bar 40.
4. Hold `Ctrl` and drag the **Chorus 1** block to the right, past **Post-chorus**. The sections beside it move to make room.
5. Press `Ctrl+Z`. The chorus jumps back to bar 40, and the song is as it was.

The song you opened is an unsaved copy, so the sample file on disk never changed.

## Select an area of bars

An area is a range of bars across every track. Drag sideways over the bars on the timeline. Start the drag on a bar, and move in a steady line. A tiny wobble counts as a click, which only moves the cursor.

The area is shared with the score: the same bars are selected in both. It is also the loop area, so press `F9` and TabForge repeats those bars. Press `Esc` to clear the area. A plain click on the timeline clears it too.

> **Tip:** Right-click inside the shaded area to see everything you can do with it.

## Copy, cut, paste and move bars

Three right-click menus handle most arranging. Which one opens depends on where you click.

| You right-click | The menu | It acts on |
|---|---|---|
| Inside the selected area | The area menu | The selected bars on every track |
| A bar outside the area | The bar menu | One bar, on one track or on all tracks |
| A section block | The section menu | A whole section on every track |

You can also open the menu from the keyboard. Press `Shift+F10` or the `Menu` key. With bars selected you get the area menu. Without a selection you get the bar menu for the current bar.

The area menu opens with a line such as "Bars 40-43 selected". Below it you find **Copy**, **Cut**, **Paste** and **Delete**. Copy takes the selected bars of every track. **Cut** removes the bars and closes the gap. **Paste** inserts the copied bars in front of the selection. **Delete** removes the bars and closes the gap too, without copying.

**Loop selection** turns the loop on for those bars. **Arrange** holds two more commands. **Move selection…** asks you to click the new position on the timeline, and the bars move there on every track. **Skip during playback** makes the song jump over the bars when it plays. Choose **Play all skipped areas again** to bring them back.

The bar menu works on one bar. **Copy bar**, **Paste bar** and **Delete bar** act on the selected track only, and with several tracks they say "(this track)". **Insert bar** adds an empty bar before or after this one on every track. The **All tracks** submenu copies, pastes or deletes the bar on every track at once. The **Section** submenu appears when the bar belongs to a section.

The section menu works on a whole section. **Copy section**, **Cut section** and **Paste section** work on its bars on every track. Paste puts a copied section after this one. **Duplicate section** makes a second copy right after the first, with its name and colour. **Rename / recolour section…** reopens the name window. **Go to section** moves the cursor. **Loop section** loops it, and **Lock section position** protects it.

> **Tip:** Every arrangement action is one undo step, so `Ctrl+Z` takes back a whole paste or move at once.

### Try it: Arrange the first riff

*Goal: turn four bars into an Intro, two Verses and a Rest bar, then loop the Verse.*

You can use your own riff from **Chapter 9: Shaping the sound**, or the optional download `first-riff-09.gp` that comes with the guide.

1. Choose **File > Open…**, pick `first-riff-09.gp` and click **Open**. The riff opens with its three tracks and four bars.
2. Click bar 1 on the timeline. The edit cursor moves to the first bar.
3. Press `M`. The **Add score marker** window opens.
4. Type `Intro` in **Marker name**, then click **Add**. A section called Intro covers bars 1 to 4.
5. Right-click the **Intro** block and choose **Duplicate section**. A second block appears over bars 5 to 8.
6. Right-click that second block and choose **Duplicate section** again. A third block covers bars 9 to 12.

7. Right-click the second block, choose **Rename / recolour section…**, type `Verse`, choose **Green** in **Colour** and click **Save**.
8. Right-click the third block, choose **Rename / recolour section…**, type `Verse 2` and click **Save**. It takes the same colour as **Verse**.
9. Right-click bar 12 on the timeline and choose **Insert bar > After this bar**. An empty bar 13 appears on all three tracks.
10. Press `M`, type `Rest`, choose **Slate** in **Colour** and click **Add**.
11. Drag across bars 5 to 8 on the timeline, then press `F9`.
12. Press `Space`. You hear the four bars of the Verse repeat, and the **Sections** panel lists Intro, Verse, Verse 2 and Rest.

Press `Esc` twice. The first press clears the area, and the second stops playback. Your song has the same bars and sections as the optional download `first-riff-11.gp`. The loop is not part of it, because a loop is not stored in a song file.

## Remove a section safely

TabForge has two different ways to remove a section, and they have very different results. One action removes only the marker. The bars and their notes stay, and you can undo it. The other action removes the section's bars and their notes from every track, and asks you first.

In the **Sections** panel, the **Remove** button takes only the marker. In the right-click menu of a section on the timeline, **Delete section and its bars…** takes the bars as well. It asks first, and **Undo** restores everything. **Cut section** also removes the bars, because cutting means taking them out.

> **Warning:** Removing a section's bars deletes their notes from every track. Press `Ctrl+Z` straight away if you chose the wrong one, or use **Remove** in the **Sections** panel to take away only the marker and keep your music.

## Repeats and endings

Repeats let a few bars play twice without writing them twice. You met them in **Chapter 4: Reading tab and notation**. Here is the short version, because arranging often needs them.

Place the cursor on the first bar to repeat and choose **Bar > Repeat open**. Place it on the last bar and choose **Bar > Repeat close…**. A small window asks how many times to play the passage, from 2 to 99. To give the repeat a first and second ending, choose **Bar > Directions / ending…** on those bars and type the ending number, 1 or 2.

On the first pass the song plays the first ending. At the repeat sign it jumps back to the start. On the second pass it skips the first ending and plays the second. The timeline plays in this order too.

## Reorder tracks and groups

The track list sits at the left of the timeline. To change the order, drag a track's number up or down. You can also select a track and choose **Track > Move up** or **Track > Move down**, or press `Alt+Up` or `Alt+Down`. Playback carries on while you do it.

The track list always fits its rows. Drag the splitter under the list to make the rows taller or shorter, between 30 and 90 pixels, and double-click the splitter to reset them. The command to reset the row height is in the **View** menu and has no default key.

Groups keep a big song tidy. Right-click empty space in the track list and tick **Show tracks in groups**. A header appears for each group, such as guitars or drums. Click the arrow on a header to collapse its tracks, or drag the header to move the whole group.

To show groups in every new song, tick **Show tracks in groups in new songs** in **Preferences > Timeline & Tracks**.

## Record a take

Recording is optional, and it needs an audio input. You also need the audio engine running, which **Chapter 10: Plug-ins, effects and audio devices** explains. TabForge records into a track, so you hear the song and your playing together.

Save the song first. A saved song keeps its recordings in a folder named after the song, with the word Media added, beside the song file. An unsaved song records into a TabForge Recordings folder inside your Music folder.

Click the red **●** button on a track row to arm it. An armed track shows a strip under its row. The strip has a **Monitor** button, a list of inputs and a level meter. The inputs are **Input 1**, **Input 2**, **Inputs 1+2 (stereo)** and **MIDI (all inputs)**. **Monitor** lets you hear the input through the track while you play.

Press `Ctrl+R` to record. Playback starts if it is not running, and every armed track records. Press `Ctrl+R` again, or click the stop button, to finish. The cursor returns to where you began, so `Space` plays the take from its start.

**Preferences > Recording** holds two settings. **Recording device** chooses the input. **Recording offset (ms)**, under **Timing** inside **More options**, shifts takes if they sound late against the song. Enter a positive number to move them earlier.

With **MIDI (all inputs)** the take is a MIDI clip instead of audio. A MIDI keyboard plays through the track while you record. Afterwards you can turn the clip into notes, as described below.

> **Tip:** Wear headphones when you monitor. Speakers and a microphone together can feed back into a loud howl.

When you record with the loop on, every pass becomes its own take on its own lane. A lane is a row under the track that holds clips. The newest take plays, and the older takes grey out. Click a take to make its lane the one you hear. Hold `Ctrl` and click to hear several lanes together.

### Try it: Record a take

*Goal: record one pass over the riff, then mute the take (optional).*

1. With your riff open, press `Ctrl+S`, type a name for the song and click **Save**.
2. Click the red **●** button on the guitar track. The input strip appears under the row.
3. Choose your input from the list in the strip.
4. Put on headphones, then press `Ctrl+R`. The song plays and a take grows on the lane.
5. Play along, then press `Ctrl+R` again. Recording stops, and the finished take sits on the lane.
6. Right-click the take and tick **Mute**. The take turns grey.
7. Press `Space`. You hear the song without your take, and you can un-mute it to hear both.

**Stuck?** See **Chapter 14: Troubleshooting and FAQ**.

## Work with clips

A recording or an audio file on the timeline is a clip. Click a clip to select it. Drag its body to move it, even onto another track. Drag its left or right edge to trim it. Right-click it for a menu with **Copy**, **Cut**, **Paste**, **Duplicate**, **Delete**, **Mute** and **Properties…**.

Some keys work while a clip is selected. `Left` and `Right` nudge it by one beat. `Shift+Left` and `Shift+Right` nudge it by 10 milliseconds. `Ctrl+D` duplicates it, `Ctrl+M` mutes it and `F2` opens its properties.

The properties window is called **Audio clip properties**. It has **Name**, **Volume** from -36 to +12 dB, **Pitch** from -12 to +12 semitones, **Speed** from 0.50 to 2.00 times and a **Muted** box. Pitch and speed change independently. The audio file itself never changes.

The **Snap** button, a small magnet in the timeline header, makes clips jump to neat positions as you move or trim them. Hover over it to read its current key in brackets, which is `Alt+S` by default. Right-click it to open **Snap / grid settings**. There you choose the **Grid size**, from **Bar** down to 1/32, and what to snap to: the grid, the edges of other clips and the playback line. Hold `Alt` while dragging to skip snapping for one move.

To add audio or MIDI, drag the files from Windows Explorer, or out of a plug-in's own window, onto a track in the timeline. While you drag, a see-through block in the track's colour shows where the files will land. It starts at the snapped position (hold `Alt` to place freely), is exactly as long as the files, and shows their name and length. Several files lie end to end with a divider between them. If that stretch of the lane is already taken, the block moves to a new lane that opens under the track. If you drop below the last track, TabForge opens a new track: drum MIDI makes a drum track, other MIDI a keys track and audio an audio track. A block that cannot land shows the reason, and the pointer shows a no-drop sign. One drop is one undo step.

A dropped MIDI file becomes a MIDI clip that plays through the track's instrument and effects. It is placed beat for beat on the song's own tempo map, so a groove from a drum plug-in lands on the bar grid at the song's tempo. Files that a plug-in drags out of a temporary folder are copied into the song's media folder, so the clip keeps working afterwards.

You can also arm the track, right-click the empty lane under it and choose **Add audio file…**. TabForge reads `.wav`, `.mp3`, `.aif`, `.aiff`, `.flac`, `.ogg`, `.m4a` and `.wma` audio files, and `.mid` and `.midi` files. Song files dropped on the window or on the timeline do not become clips; they open in new tabs instead.

A MIDI clip can become real notation. Right-click it and choose **Write into the track's notation**. TabForge writes the notes into the track's bars, choosing frets for you, and the clip is removed.

## Quick recap

- The timeline shows one row per track, a ruler, a section lane and the playback marker, and the wheel zooms it.
- Add a section with `M`. Drag moves a marker into free bars, and `Ctrl` with drag moves the bars too.
- Drag across bars to select an area across every track. It is also the loop area, and `Esc` clears it.
- Three right-click menus copy, cut, paste, move and skip bars. `Shift+F10` opens them from the keyboard.
- Removing a section's bars deletes its notes from every track, so undo at once if it was a mistake.
- Arm a track, press `Ctrl+R`, and your take lands on a lane that you can mute, move, trim or snap.

## What next

You now have a song with a shape. Go on to **Chapter 12: Saving, sharing and exporting**, which shows how to save it safely, recover work after a crash, and send it out as a PDF, MIDI, text tab or audio file.
