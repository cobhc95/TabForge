# Getting started

## What TabForge is

TabForge is a program for writing, reading and playing tablature. Tablature, or tab, shows which string and which fret to press for each note. You can read tab without knowing how to read standard notation, which makes it a friendly way to learn a song or to write down a riff you have in your head.

TabForge also shows standard notation beside the tab, and it plays the song back so you can hear what you have written. You can use it to learn a part, to check a part you have written, or to build a whole band one track at a time.

A song in TabForge is made of tracks. Each track is one player in the band, such as a lead guitar, a bass or the drums. Each track has its own notes, its own sound, its own tuning and its own volume. The score shows the track you have selected, and the track list shows every track in the song.

You do not need to know music theory to start. This chapter takes you through the first steps, one at a time. Everything else in the help can wait until you need it.

## Before you start

You need a Windows computer with some way to hear sound, such as speakers or headphones. TabForge plays the song through the computer's sound output, so check that the volume is turned up before you press play.

Keep one idea in mind as you read: nothing you do to play, move around or look at a song changes the file on disk. Only saving changes the file. This means you can explore freely without fear of spoiling a song.

## First launch

Start TabForge from the Start menu, or double-click `TabForge.exe` in its folder.

There is no welcome screen. You land straight in an empty song called **Untitled**. It has one guitar track and a few empty bars. This is your blank page. You can start writing on it straight away, or open a song that already exists.

The window has a menu bar along the top, a tab for each open song just below it, the score in the middle, and the transport controls and status bar at the bottom. The next chapter, **Interface tour**, explains each part in turn.

Tip: If you feel lost at any point, press `Ctrl+Shift+A`. This opens the command palette, which lists every command in TabForge. Type what you want, and it finds it for you.

## Open a song

1. Choose **File > Open** or press `Ctrl+O`.
2. A window appears. Find the song file you want.
3. Click **Open**.
4. The song replaces the empty **Untitled** song in the current tab.

If you want to keep the song you already have open, choose **File > Open in new tab** or press `Ctrl+Shift+O` instead. The new song opens in its own tab, and the old one stays where it was.

TabForge opens its own project files, which end in `.tforge`. It can also open song files that were saved by other tab programs. If a file will not open, check the troubleshooting section below.

Tip: The demo song is a good first song to try. Look in the `Samples` folder beside `TabForge.exe`. Its name starts with `TabForge Demo`. It is safe to play around with, because nothing changes on disk until you save.

## Create a new song

1. Choose **File > New** or press `Ctrl+N`.
2. A new untitled song opens in a new tab, ready to write on.

You can also click the **+** button in the tab strip, next to the last tab. Its tooltip reads **New tab**. If you often start from the same layout, choose **File > New from template** instead. It starts a new song from a template you have saved.

## Play the song

Playback is the sound of your song. It starts from the edit cursor, which is the marker that shows where you are working in the score.

1. Click a beat in the score to place the edit cursor where you want to start.
2. Press `Space` to start playing. The score scrolls to follow the music, and the notes sound.
3. Press `Space` again to pause. The song stops where it is, and the edit cursor stays put.
4. Press `Shift+Space` to play again from the very start of the song.
5. Press `Ctrl+.` (the period key) to stop. Playback stops and returns to the edit cursor.

Pause and stop are different. Pause keeps your place in the song. Stop ends playback and brings you back to where you began. Use pause when you want to take a break in the middle, and stop when you want to start again.

The transport buttons in the bar at the bottom of the window do the same jobs. Hover over a button and wait a moment to read its name. Its tooltip also shows the shortcut, when it has one.

While the song plays, the fretboard above the score shows the notes that are sounding. For a drum track it shows a drum map, and for a keys track it shows a keyboard.

Note: If you hear nothing, do not worry yet. Check the volume on your computer, then check that the track is not muted. The troubleshooting section below has a full checklist.

## Move around the score

The score is the large area in the middle of the window. Moving around it is simple.

- Click a beat to move the edit cursor there. Clicking only moves the cursor. It never adds a note.
- Press `Left` and `Right` to move one beat at a time.
- Press `Up` and `Down` to move between strings.
- Hold `Ctrl` and press `Left` or `Right` to move a whole bar at a time.
- Press `Ctrl+Home` to jump to the first bar of the song.
- Press `Ctrl+End` to jump to the last written beat of the song.
- Roll the mouse wheel over the score to scroll it up and down without moving the cursor.

Tip: Click a bar in the timeline at the bottom of the window to jump straight to that part of the song. It is faster than scrolling for a long song.

## Change the size of the score

If the notes look too small or too large on your screen, change the zoom. The zoom changes only how big the score looks. It never changes the song.

- Press `Ctrl+=` (the plus key) to zoom in, and `Ctrl+-` (the minus key) to zoom out.
- Choose **View > Zoom** to pick a fixed size, such as 75%, 100% or 150%.
- Open the **View and tempo** panel at the side of the window. It has a zoom box with presets, including **Fit width**, and you can type your own value into it.

Tip: **Fit width** is the easiest choice when you are not sure what size to use. It fits one bar line across the whole score.

## The three views

The score can show the music in three ways. Choose one from the **View** menu.

- **Tab and staff** shows the tablature and the standard notation together. This is the best choice when you are learning, because you can see both at once.
- **Tab only** shows just the tablature, with the string and fret numbers. Pick this when you only want the fingering.
- **Staff only** shows just the standard notation, the five lines with the notes sitting on them. Pick this when you want to read the rhythm and the notes.

There is also a view that works more like a piano roll. Choose **View > Keyboard mode (experimental)**, or press `Ctrl+Alt+L`. The notes of the selected track fall down towards a strip of keys, in time with the music. Because it is marked experimental, expect some rough edges.

Tip: If you change to a view and do not like it, choose another from the same menu. Nothing in your song changes when you change the view.

## Find any command with the command palette

The command palette is the place to find any command in TabForge, together with its keyboard shortcut.

1. Press `Ctrl+Shift+A`, or choose **Help > Command palette**.
2. Type a few letters of what you want, such as `save`, `mixer` or `zoom`.
3. Each match shows its name and, if it has one, its shortcut.
4. Press `Enter` to run the command you picked, or `Esc` to close the palette without running anything.

Use the palette whenever you do not know where a command lives in the menus. Because it shows each shortcut, you will learn the keys over time without trying to memorise them all at once.

Tip: Press `F1` at any time to open this Help, or choose **Help > Help**. The Command palette shows every command with its key; this Help explains how to do things.

## Undo a mistake

Everything you change in a song can be undone. If you put a note in the wrong place, press `Ctrl+Z` to take the change back. Press `Ctrl+Z` again to go back one more step. You can also choose **Edit > Undo** from the menu.

Undo works on edits to the song, such as notes, bars and tracks. It does not undo playing, zooming or moving the cursor, because those do not change the song.

Warning: Undo works only while the song is open. If you close the tab or the program, the undo steps are lost, so save first if you want to keep your work.

## Save your work

1. Choose **File > Save** or press `Ctrl+S`.
2. A new song has no file name yet, so TabForge asks you for one. Choose a folder, type a name and click **Save**.
3. To save a copy under a new name or into a different folder later, choose **File > Save As** or press `Ctrl+Shift+S`.

TabForge saves a song as a `.tforge` project file. This keeps everything in the song, including its effects and mixer settings, so it is the safest choice for your own work.

A song that has changes not yet saved shows a small dot on its tab. When the dot is there, press `Ctrl+S` before you close the tab, or TabForge will ask you what to do.

Warning: Saving under the name of an existing file replaces that file. If you want to keep the old version, use **File > Save As** and choose a new name.

## Your first session, step by step

Follow this short walk-through once. It takes about ten minutes, and it covers almost everything in this chapter.

1. Start TabForge and look at the empty **Untitled** song.
2. Press `Ctrl+O` and open the demo song from the `Samples` folder.
3. Press `Space` to hear the first part of the song. Press `Space` again to pause.
4. Click a beat a little further along the score, then press `Space` to play from there.
5. Press `Ctrl+.` to stop. Notice that the cursor goes back to where you clicked.
6. Press `Ctrl+=` a few times to make the score bigger, then `Ctrl+-` to make it smaller.
7. Press `Ctrl+Shift+A` and type `mixer`. Read the shortcut that appears, then press `Esc`.
8. Press `Ctrl+W` to close the demo tab. If it asks you to save, choose **No**, because you only explored.

You know the first session worked when you played the demo from a place you chose yourself, and you closed the tab without changing any file.

## Common questions

Question: Do I have to save my song every few minutes?
Answer: Save whenever you finish a part you would hate to lose. TabForge also keeps recovery copies of unsaved songs while you work, but a recovery copy is a safety net, not a replacement for saving.

Question: Will playing or zooming change my song file?
Answer: No. Playing, pausing, zooming, scrolling and moving the cursor never change the file. Only saving changes it.

Question: Why does the score look different from the tab?
Answer: The score can show tab only, staff only, or both together. Use the three views in the **View** menu to pick the one you want.

Question: Can I undo a change after I have saved?
Answer: Yes, for as long as the song stays open. Press `Ctrl+Z` to step back through your recent edits. Once you close the tab, the undo steps are gone.

Question: I cannot remember a shortcut. What should I do?
Answer: Press `Ctrl+Shift+A`, type a word from the command, and read the shortcut shown beside it. Hovering over a button also shows its shortcut.

Question: Can I open a song I got from someone else?
Answer: Yes, with **File > Open**. Opening does not change their file. Save it under a new name if you want to keep your own changes separate.

## Troubleshooting

Problem: I can't hear anything when I press Space.
Fix: Turn up the volume on your computer and check that the speakers or headphones are the ones Windows is using. Then click a beat in the score, press `Space`, and check that the track is not muted. Its **M** button should not be lit.

Problem: The song plays, but only one part is audible and the rest are silent.
Fix: Another track may be soloed. Look for a track with its **S** button switched on, and click that **S** again to switch the solo off.

Problem: The music starts from the wrong place.
Fix: Playback starts from the edit cursor. Click the beat where you want to begin, then press `Space`. Use `Shift+Space` to start again from the very beginning.

Problem: I pressed a key and the notes or bars moved somewhere I did not want.
Fix: Press `Ctrl+Z` to undo the change. Repeat it until the song is back the way it was.

Problem: I can't find a command in the menus.
Fix: Press `Ctrl+Shift+A` to open the command palette, type a word from the command's name, and press `Enter` to run it.

Problem: I can't see the notes because the score is too small or too big.
Fix: Press `Ctrl+=` to zoom in or `Ctrl+-` to zoom out. For a quick fit, pick **Fit width** in the **View and tempo** panel.

Problem: The window closed, and my song seems to be gone.
Fix: Reopen TabForge. If the song had unsaved changes, TabForge may offer to recover it at start-up. Choose **Yes** to open the recovered copy, then save it under a name with `Ctrl+Shift+S`.

Problem: A panel I need has disappeared from the window.
Fix: Choose **View > Reset all panels to default positions**. It puts every panel back where it started and does not change your song.

Problem: I opened a song, changed it, and now I want the original back.
Fix: Do not save. Close the tab with `Ctrl+W`, choose **No** when TabForge asks whether to save, and open the file again. The file on disk is unchanged until you save over it.

Problem: The Save window asks for a name every time I press Save.
Fix: A song that has never been saved has no file name yet, so TabForge asks once. After that first save, `Ctrl+S` saves straight to the same file. If it keeps asking, use **File > Save**, not **Save As**.

Related: Interface tour, Files and export, Tracks and mixer
