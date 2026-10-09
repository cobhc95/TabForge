---
title: Troubleshooting and FAQ
id: troubleshooting
order: 14
keywords: troubleshooting, problem, no sound, silent, delay, latency, crackle, error, crash, plug-in crashed, not working, shortcut not working, red bar, lost work, recovery, file will not open, high cpu, blurry, report a bug, faq
summary: Find the cause of a common problem in your own words, such as no sound or a red bar, and fix it step by step.
est-minutes: 20
---

# Troubleshooting and FAQ

Most problems have a small cause and a quick fix. This chapter lists them in the words people use, such as "there is no sound". Find your symptom, read the likely cause, then try the steps in order.

Each answer ends with "Still stuck?", which says where to go next.

## What you will learn

- Find the cause of a sound problem in a few minutes.
- Fix a red bar, a wrong fret and a shortcut that does nothing.
- Recover a song after a crash.
- Deal with plug-in warnings and audio device messages.
- Report a problem so that it can be fixed.

> **Tip:** Ask first: does it happen in a new song? If it does not, the cause is that one song, not your set-up.

## Sound and playback

### There is no sound

The usual causes are a muted track, a soloed track, a volume at zero or the wrong output device. Work through the steps in order.

1. Look at the track list for the **M** (mute) and **S** (solo) buttons. Switch off any mute, and switch off any solo you did not mean to set. A track set to solo silences every other track.
2. Check the master volume knob in the timeline header, and the volume of the track. Make sure neither is at zero.
3. Look at the right end of the status bar. It names the audio device in use. Click it to open **Preferences > Audio & Plug-ins**.
4. Set **Audio driver** to `WASAPI (shared)` and **Output device** to `(Windows default)`. This works on most computers.
5. Watch the status bar for a message that starts "Audio output:". It names a device problem.
6. Open the track's FX chain. If **Through chain** is ticked, there is no instrument in the chain, and **GM sound** is unticked, the track is silent by design. Tick **GM sound**. If the instrument plug-in was switched off, crashed or went missing, TabForge brings the GM sound in for you, unless you unticked **GM sound** yourself.
7. Check the Windows volume and default output device.

**Still stuck?** If the status bar says ASIO and idle, your audio interface may be switched off or unplugged. Choose `WASAPI (shared)` in step 4. **Chapter 10: Plug-ins, effects and audio devices** explains the drivers.

### One track is silent

Check that track's **M** button, then whether another track or group is set to solo. A soloed group silences all other groups. Then check its volume. If it has plug-ins, see "There is no sound", step 6.

**Still stuck?** See **Chapter 9: Shaping the sound** for the mixer.

### The sound crackles or pops

The computer cannot prepare the audio fast enough. A larger buffer gives it more time.

1. Open **Preferences > Audio & Plug-ins**.
2. Raise **Buffer size**, for example from `256` to `512`, then `1024`.
3. Set **Audio driver** to `WASAPI (shared)`.
4. Close other heavy programs and plug-ins you do not need.

**Still stuck?** Lower **Sample rate** in the same group.

### The sound is late

A large buffer adds delay, so it fixes crackle and causes lateness. Lower **Buffer size** in steps until you reach the smallest value that stays clean. The status bar shows the delay in milliseconds. `WASAPI (exclusive)` and ASIO are faster, but they take the whole device, so other programs may fall silent.

**Still stuck?** Stay on `WASAPI (shared)`. A little delay matters only when you play live.

### The sound is very loud

The demo song is loud, and the metronome is boosted by default. Lower the master volume knob, and in **Metronome settings** untick **Boosted (layered, much louder)** or lower **Metronome volume**.

> **Warning:** Sudden loud sound can hurt your ears. Turn the Windows volume down before you press play, then raise it slowly.

### The song is too fast or too slow

Check the **Speed** box in the top toolbar. At `100%` the song plays at its written tempo. Choose a lower percentage to slow it down. The **BPM** box in the toolbar is the written tempo itself. See **Chapter 3: Practice tools**.

### Test sound says it worked, but nothing plays

**Test sound** in **Sound > MIDI / Audio setup** checks the Windows MIDI path. It does not test the audio driver you chose in Preferences. Use the steps in "There is no sound" instead.

## Writing and editing

### A bar is red

A red bar does not add up: its notes and rests are longer or shorter than the time signature allows. It is a warning, and the song still plays.

1. Choose **Tools > Check bar duration** (`F4`). A message lists every bar that does not add up.
2. Click the red bar and compare its notes with the time signature.
3. Change a note value, or add a rest, until the bar adds up. The red tint goes away.

**Still stuck?** See **How long is each note** in **Chapter 4: Reading tab and notation**.

### My notes land on the wrong string

The new note goes on the string where the edit cursor sits. Click the line of the string you want in the TAB, then type the fret.

### I typed a number and the wrong fret appeared

In **Standard notation only** view a digit chooses a string, not a fret. Choose **View > Tablature + standard**, then type again. For a two-digit fret, type both digits quickly, within a moment of each other.

### A shortcut does nothing

Try these steps in order.

1. Click an empty part of the score. A key typed into a text box goes to the box, not to the command.
2. Hover the button for that command. The key in brackets at the end of its tooltip is the one that works now.
3. If a clip is selected on the timeline, press `Esc`. A clip can take a key that the score would use.
4. Press `Ctrl+Shift+A` to open the command palette, and type the command's name. The palette shows its current key.
5. Open **Preferences > Shortcuts**. The **Preset** box and the command row show whether the key was changed.

**Still stuck?** See **Chapter 13: Make TabForge yours** for how to reset a key.

### My notes changed pitch after I changed the tuning

When you retune a track, **Keep fret numbers** is ticked. The fret numbers stay, so the notes sound different. Untick it in **Track properties** to keep the pitch and let the fret numbers move. See **Chapter 7: Tracks, instruments and tunings**.

### I cannot find a panel or the transport buttons

The transport buttons sit at the left of the timeline header. Show the timeline with **View > Arrangement overview**. To bring every panel back, choose **View > Reset all panels to default positions**. For one panel, choose **View > Panels** and tick it.

### I cannot right-click

Select what you want, then press `Shift+F10` or the `Menu` key. The right-click menu opens at the selection.

## Files and saving

### A song will not open

TabForge opens `.gp`, `.gp5`, `.gpx`, `.gp4`, `.gp3` and `.tforge` files. MIDI, MusicXML and PDF files cannot be opened. They are formats you export to. Open a song with **File > Open…** (`Ctrl+O`) and choose one of the supported types.

### Save asks me for a name

A song you opened is an unsaved copy with no file name. Your original is never changed. Press `Ctrl+Shift+S` or choose **File > Save As…**, type a name, and keep the type `.tforge` or `.gp`.

### I opened a song and my other song disappeared

`Ctrl+O` opens a song in the current tab, after asking whether to save the old one. To keep both songs, use **File > Open in new tab…** (`Ctrl+Shift+O`).

### How do I print

To print, export a PDF and print it from your PDF viewer (see **Chapter 12: Saving, sharing and exporting**).

### I dropped a file and nothing happened

A song file (`.tforge`, `.gp`, `.gpx`, `.gp5`, `.gp4` or `.gp3`) dropped anywhere on the window opens in a new tab. An audio file or a MIDI file only lands as a clip when you drop it on the timeline, as **Chapter 11: Arranging and recording** describes. Other file types are ignored.

### I lost my work

TabForge keeps recovery copies of unsaved songs. After a crash, a question appears the next time you start it.

1. Read the message. It says how many unsaved songs were found.
2. Click **Yes**. Each song opens in its own tab, as an unsaved song.
3. Choose **File > Save As…** and give the song a name.

> **Warning:** Clicking **No** deletes the recovery copies. Click **Yes** if you are not sure, then close what you do not need.

For a mistake in a song that is still open, press `Ctrl+Z` to undo it.

### The title says "restart recommended"

TabForge met an error and recommends a restart. Choose **File > Save As…** and save the song under a new name. Close TabForge, start it again, and open the new file.

## Plug-ins and audio devices

### A plug-in shows a red exclamation mark

The mark on the **FX** button means a plug-in in that chain crashed and was switched off. The rest of the song keeps playing. Open the chain to see which plug-in it was.

To bring it back, select it in the FX chain window and click **Allow again**, or find it in **Preferences > Audio & Plug-ins**, **More options**, **Plug-ins switched off after a crash**. It loads again the next time you play. If it crashes every time, leave it out.

**Still stuck?** Save your song first, then leave the plug-in out.

### A plug-in is not approved

A bar appears when a song names plug-ins from places you have not approved. It says they are not loaded. 

1. Click **Review…** in the bar.
2. Read the list of plug-in files.
3. Tick only the ones you trust, then click **Allow selected**.

> **Note:** Approve a plug-in only if you trust its source. Click **Keep disabled** to leave it off.

### The plug-in list is empty

TabForge looks only in the folders you give it. In the **Add plug-in** window, click **Add folder…** and choose the folder where your plug-ins are installed. You can also click **Scan the standard VST folders**, which turns on the option **Also scan the standard VST folders** and scans them, or choose **Browse for a plug-in file…**. TabForge loads 64-bit VST2 and VST3 plug-ins.

### The status bar talks about the audio engine

A message such as "The audio engine stopped several times" means the sound engine had trouble. Tracks with plug-ins then play on Windows MIDI. Open **Preferences > Audio & Plug-ins**, choose `WASAPI (shared)`, and restart TabForge.

**Still stuck?** Remove the plug-in you added last. **Chapter 10: Plug-ins, effects and audio devices** has more.

## Windows and screen

### Windows warns about the download

Windows may show a blue message that it protected your PC, because it does not know the program yet. Continue only if you downloaded TabForge from its official project page: choose **More info**, then **Run anyway**.

### The window looks blurry or small

Open **Preferences > Appearance** and raise **UI scale** a little at a time. The window changes as you move the setting. Click **OK** to keep it, or **Cancel** to go back.

### TabForge is slow or uses a lot of CPU

1. Close plug-ins and other programs you do not need.
2. Raise **Buffer size** in **Preferences > Audio & Plug-ins**.
3. Tick **Reduce animations** in **Preferences > Appearance**.

## Getting help

### I cannot find a setting or a command

To find a setting, press `F12` and type a word in the search box at the top of **Preferences**.

To find a command, press `Ctrl+Shift+A` and type part of its name. **Help > Tutorial…** opens the tutorial window, which has a search box of its own.

### Try it: Run the no-sound checklist on the demo song

*Goal: walk through the checks for "There is no sound", so you know them before you need them.*

1. Open **Ashen Meridian** with **File > Open…**, from the `Samples` folder.
2. Press `Space`, listen briefly, then press `Space` to pause.
3. Look at the **M** and **S** buttons on every track. Switch off any that are lit.
4. Look at the master volume knob in the timeline header. Check that it is above zero.
5. Click the audio device in the status bar. **Preferences** opens on **Audio & Plug-ins**.
6. Read **Audio driver** and **Output device**, but do not change them. Click **Cancel**.

You know it worked when you can say which device plays your sound, and you have checked every track.

### How do I report a problem

Write the problem down so that it can be passed to the people who maintain TabForge. A good report has four parts:

- What you did, one step at a time.
- What you expected, and what happened instead.
- The version number, shown in **Help > About**.
- Whether it also happens in a new song.

> **Note:** TabForge never sends your music anywhere. Describe the problem in words, and share a song only if you choose.

## Quick recap

- Ask first: does it happen in a new song?
- No sound is usually mute, solo, volume or the output device. The status bar names the device.
- A red bar does not add up. Check it with **Tools > Check bar duration** (`F4`).
- Hover a button to read the key that works now.
- After a crash, click **Yes** to recover songs, then save each one.
- A report needs the steps, the version from **Help > About**, and no music.

## What next

You have reached the end of the guide. Go back to the chapter that matches your problem, or open the shortcut reference at the back for the keys used here.
