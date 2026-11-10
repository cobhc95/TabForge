# Troubleshooting and FAQ

This chapter is for the moments when something goes wrong. It is organised by symptom, so you can find the problem in the words you would use yourself, such as "there is no sound" or "my song will not open". Each problem has a fix, and the fixes start with the simplest checks.

Work through one problem at a time. Change one setting, test it, and then move on. If the first fix does not work, undo it before you try the next, so you always know which change helped.

## How to use this chapter

- Find the group that matches your problem: Audio, MIDI, Files, Editing, Display, Crashes and recovery, Performance, Install and updates, or Settings and reset.
- Read the Problem line, then try the Fix. Each fix tells you where to click or which key to press.
- If nothing helps, go to Reporting a problem near the end. It explains what to collect before you ask for help.

Tip: Before you change anything, save a copy of your song with File > Save as. A copy lets you compare the working version with the broken one.

## Audio

Audio problems are the most common. Most are a mute, a solo, a volume at zero, or an output device that is not the one you are listening on.

## No sound at all

Check these in order, and test with the song playing after each one.

1. Look at the **M** (mute) and **S** (solo) buttons on the track rows. A muted track is silent, and a soloed track silences every track that is not soloed. Click the buttons to clear them.
2. Check the master volume and the track volume. Neither should be at zero.
3. Open **Options > Preferences > Audio & Plug-ins**. Check that the **Output device** is the speaker or headphones you are listening on. Choose (Windows default) to follow Windows.
4. Check the **Sound engine**. If it says None, the built-in General MIDI sound is silent. Choose Windows GM, which needs no download.
5. Check the Windows volume and the output device that Windows is using. Use the Windows sound settings to confirm.

Problem: I press play and I hear nothing at all, from any track.
Fix: Clear any solo, check the master and track volumes, set the Output device to (Windows default), and set the Sound engine to Windows GM if it says None.

Problem: Only one track is silent.
Fix: Look at that track's M button and at whether any other track is soloed. Also open the track's FX chain and check whether its instrument is turned on.

Problem: The sound comes out of the wrong speakers or headphones.
Fix: Open Preferences > Audio & Plug-ins and change the Output device to the one you want. If you use ASIO, choose the installed ASIO driver for your interface.

Problem: I changed the driver and now there is no sound.
Fix: Set the Audio driver back to WASAPI shared, and set the Output device to (Windows default). WASAPI shared works on most computers and lets other programs keep playing.

Problem: The ASIO driver is chosen but nothing plays.
Fix: Check that the ASIO driver is installed and that the interface is switched on. Click Configure ASIO driver to open its own control panel and check its settings there. Then press play again.

## The wrong audio device

Problem: TabForge plays through my monitor speakers instead of my interface.
Fix: Open Preferences > Audio & Plug-ins and choose the interface in Output device. If it is not in the list, check that Windows shows it as connected.

Problem: The output device list is empty or says it is checking.
Fix: Wait a few seconds, then reopen the page. If the list stays empty, unplug the device and plug it in again, and then check that the Audio driver matches the driver the device needs.

Problem: The device I chose disappears after a restart.
Fix: Connect the device before you start TabForge, then choose it again in Output device. Buffer size and sample rate are remembered for each device, so set them again if the device was unplugged.

## Latency and delay

Latency is the delay between playing a note and hearing it. Recording can also be late.

Problem: There is a noticeable delay between my playing and the sound.
Fix: Choose WASAPI exclusive or ASIO as the Audio driver if your device supports it. Then lower the Buffer size one step at a time, from 16 to 8192, and test after each step.

Problem: Recordings sound late against the song.
Fix: Open Preferences > Audio & Plug-ins, find Recording timing correction, and enter a positive number of milliseconds. Record a short test. Adjust by small amounts, because the range is -1000 to 1000.

Problem: Recordings sound early against the song.
Fix: Enter a negative number in Recording timing correction, so that new recordings move later.

Problem: Plug-in tracks sound ahead of the Windows MIDI tracks, or lag behind them.
Fix: Adjust Windows MIDI delay in Audio & Plug-ins. The default is about 200 ms. Raise it if plug-in tracks sound early, and lower it if they lag.

## Crackling, popping and dropouts

Problem: The sound crackles or pops while the song plays.
Fix: Raise the Buffer size one step, then test. A larger buffer gives fewer crackles but more delay. Close other programs that use a lot of processor power, and close plug-in windows you are not using.

Problem: I hear a pop when I change an audio setting.
Fix: Turn on Protect speakers from pops when audio settings change in Audio & Plug-ins. It fades the sound out and back in around a change.

Problem: The sound is distorted or clips on loud passages.
Fix: Turn on Protect live playback from loud peaks for live sound, or Protect rendered files from distortion for files you export. Lower the track or master volume, which is the simplest fix.

Problem: The sound stutters only with a plug-in.
Fix: Look at the status bar. A plug-in that crashes or freezes is named there and switched off. Open its FX chain window and press Allow again if you trust it. If it keeps failing, remove it from the chain.

## Recording audio

Problem: I press Record and nothing is recorded.
Fix: Arm the track first by clicking its red button. Then check that the track's audio input is set to your interface input, not None. Press Ctrl+R to record, and Ctrl+R again to stop.

Problem: A message says no track is armed.
Fix: Arm a track with its red button on the track row, then press Record again.

Problem: My recording is silent even though the track is armed.
Fix: Check the Recording device in Audio & Plug-ins, check the track's input picker, and check that the input is not muted in the Windows sound settings. Use Live input to hear the input before you record.

Problem: My recording picks up the song through the speakers.
Fix: Use headphones while you record, so the song does not get into the microphone or the instrument input.

Problem: My interface has many inputs but only two appear.
Fix: Turn on Use all inputs of the audio device in Audio & Plug-ins. If the device misbehaves with all its inputs open, turn it off again.

## Live input

Problem: Live input is on, but I cannot hear my instrument.
Fix: Check the audio input chosen for the live input, using the right-click menu of the Live input button. Then check the output device and the volumes.

Problem: Live input stays on after I switch tabs, and I did not mean it to.
Fix: Live input is for the whole program, so it stays on across tabs and windows. Click the Live input button again to turn it off.

## MIDI

MIDI covers keyboards and controllers that send notes to the computer.

Problem: My MIDI keyboard is not found, or the device list is empty.
Fix: Plug the keyboard in, wait for Windows to finish installing it, and then open Keyboard mode. Set MIDI in to Any available device. Unplug and plug the keyboard in again if the dot stays dark.

Problem: My keyboard was found, but now it is not listed.
Fix: Close and reopen the window that shows the MIDI in box. New devices are picked up while the view is open, but a device that was unplugged may need a reconnect.

Problem: The keys I press do not appear in Keyboard mode.
Fix: Check that MIDI in is not set to Off. Choose Any available device, press a key, and watch the dot beside the box.

Problem: I play on the keyboard, but nothing is recorded to my track.
Fix: Open the track's input picker and choose a MIDI input: All MIDI inputs, or your device. Arm the track, then press Ctrl+R to record.

Problem: I hear nothing when I play the MIDI keyboard into a track.
Fix: Open Preferences > Audio & Plug-ins and check the Monitor MIDI input setting. Armed tracks plus the selected track is the default. Choose it if the setting is Off or Armed tracks only.

Problem: A note keeps sounding after I let go of the key.
Fix: Choose Tools > Panic (all notes off). It silences every stuck note and the sustain pedal on all outputs, and it works while the song is stopped too.

Problem: My recorded MIDI notes are off the beat.
Fix: Turn on Quantize MIDI input in Audio & Plug-ins, which snaps each recorded note to the grid. Leave it off if you want the raw timing kept.

Problem: My MIDI take is on a new lane, but I wanted it merged into the clip.
Fix: Open Audio & Plug-ins and set When recording over a MIDI clip to Merge. The notes are then added into the MIDI clip underneath, as one undo step.

Problem: I want to hear my MIDI track through a different instrument.
Fix: Use the track's instrument button on the timeline, or Track properties > Instrument > Change. The MIDI output window is opened from Options > MIDI / Audio setup, for the selected track.

Problem: I want to insert a MIDI file into my song.
Fix: Use Insert > MIDI file. It places the file as a MIDI clip at the cursor time. Use Insert > Empty MIDI clip if you want to draw a new one.

## Files

Problem: My song will not open.
Fix: Check that the file is a TabForge song or one of the other song types listed in the Open dialog. Try copying the file to another folder, and then open the copy. If a message says the file is damaged or too large, the file itself needs repair from a backup.

Problem: I opened a song and my other song disappeared.
Fix: File > Open replaces the song in the current tab. Use Ctrl+Shift+O, which is Open score in new tab, to keep both songs open.

Problem: A song I dropped onto the window did not open.
Fix: Drop TabForge song files onto the window itself. They open in new tabs. Other files, such as audio, are added to a track instead.

Problem: MIDI, MusicXML and PDF files do not open as songs.
Fix: These are formats you export to, not formats you open as songs. Insert a MIDI file as a clip with Insert > MIDI file. To get a PDF or MusicXML, use the File > Export menu on the song you have.

Problem: The audio in my song says it is missing.
Fix: Choose File > Find missing media. It looks for the linked audio files and lets you relink them. If the files are on another drive, use the approved folder list described below.

Problem: The song plays from a network drive or a USB drive, but a message asks for approval.
Fix: Linked audio on network or removable drives needs your approval. Choose View > Manage linked audio approvals, or open Preferences > Files & Backups, to approve the folder or revoke it later.

Problem: I want to take my song to another computer, with its audio.
Fix: Choose File > Collect media. It copies the audio files the song uses from outside its folder into the song's media folder, so they travel with it.

Problem: Saving asks a question I do not understand.
Fix: Y saves the song, N closes without saving, and Esc cancels the close. If you are not sure, press Esc and use File > Save as first.

## Editing

Problem: A note is in the wrong place on the staff or tab.
Fix: Press Ctrl+Z to undo the last edit. Then check the string and fret: Alt+Up and Alt+Down move the selected note to a higher or lower string and keep its pitch. Shift+Up and Shift+Down move the note by a semitone.

Problem: A note is at the right pitch, but the fret looks wrong.
Fix: Check the tuning and capo of the track in Track properties, which opens with F6. A wrong tuning gives wrong frets for the same pitch.

Problem: A bar is red.
Fix: A red bar does not add up. Choose Bar > Check bars, or press F4, to list every bar that does not fill its time signature. Change a note length or add a rest until the red goes away.

Problem: I cannot change a note's length because the bar would overfill.
Fix: Open Preferences > Editing and look for Prevent rhythms that overfill a bar. Turn it off to allow any rhythm. Free rhythm entry is the default, which allows any change and marks bars that do not add up in red.

Problem: The plus and minus keys do the opposite of what I expect.
Fix: Open Preferences > Editing and look for Plus key lengthens notes. Turn it on or off to swap the two keys.

Problem: The Delete key removed the wrong thing.
Fix: Delete clears the notes of the beat first, and removes an empty beat in the same way. Press Ctrl+Z to undo it, then use Delete beats (shift left) if you want to remove the beat and close the gap.

Problem: The clip I dragged landed in the wrong place on the timeline.
Fix: Snap is on by default, so clips line up with the grid. Hold Alt while you drag to place a clip freely, or turn snapping off with Alt+S.

Problem: A section moved when I wanted only its marker to move.
Fix: Drag a section block to move it with its bars. Hold Ctrl and drag to move only its marker into free bars, which leaves the bars where they are. Ctrl+Z undoes the move.

Problem: I deleted bars and now the rest of my song is in the wrong place.
Fix: Press Ctrl+Z straight away. For bar deletions on the timeline, the prompt offers to clear the bars, remove them and close the gap, or insert a gap, so check the choice before you confirm.

Problem: I cannot find an edit I made earlier.
Fix: Press Ctrl+Z repeatedly to go back, and Ctrl+Y to move forward again. Each step is one edit.

## Display

Problem: The fretboard or keyboard is not showing.
Fix: Choose View > Instrument view. The row is ticked while the panel shows. If it is on but blank, the track may have no instrument view that suits it; check Default instrument view in Preferences > Fretboard & Keyboard.

Problem: The left strip has vanished.
Fix: Open the View menu and tick Left strip, or turn on Show strip button so that the toggle icon appears at the left end of the menu bar.

Problem: The text and icons are too small or too large.
Fix: Open Preferences > Appearance and change Interface size one step at a time. Click Recommended size for your monitor to return to the size that suits your screen.

Problem: The score is drawn in the wrong colours for my theme.
Fix: Use View > Score page to choose Dark or Light for the score page. Or set Theme to System, Dark or Light in Appearance.

Problem: A panel is in a strange place, or a panel has disappeared.
Fix: Choose View > Reset all panels to default positions. Every dockable panel goes back to its default place.

Problem: The score is too small or too big on screen.
Fix: Use View > Zoom in and Zoom out, or Ctrl+= and Ctrl+-. Zoom 75%, 100% and 150% are also in the View menu.

Problem: The timeline bars are too wide or too narrow.
Fix: Use Ctrl+Alt+Shift+= and Ctrl+Alt+Shift+-, or the zoom buttons on the transport bar, to change the timeline's zoom.

Problem: The window is hard to use in full screen.
Fix: Press F11 to toggle full screen. Press F11 again to return to the normal window.

Problem: The playhead does not stay in view while the song plays.
Fix: Turn on Follow playback with Ctrl+Shift+F, or check the Follow group in Preferences > Playback & Practice.

## Crashes and recovery

Problem: TabForge closed by itself, and my unsaved work may be lost.
Fix: The next time you start TabForge, it offers to recover your unsaved songs. Choose Yes, and each song opens in its own tab. Then use File > Save as to give each one a name.

Problem: The recovery offer does not appear.
Fix: Autosave copies are kept in the Recovery folder described below, and stay there until the reopened song is saved or the session has its own copy. If the offer does not appear, check the Recovery folder yourself, and open the song from File > Open.

Problem: The program crashes every time I do one thing.
Fix: Note exactly what you do, then follow the Reporting a problem section. Use Ctrl+Z to step back if the program stays open, and save a copy before you try again.

Problem: Autosave is not saving my changes.
Fix: Autosave copies a song that has changed, at the interval set in your settings. While a song plays or records, the interval is stretched but not stopped. Save with Ctrl+S to be safe.

Problem: A plug-in stopped working and a message appeared in the status bar.
Fix: The plug-in crashed or froze, and TabForge switched it off. The rest of your song keeps playing. Open its FX chain window and press Allow again. You can also find it under Settings > Audio & Plug-ins > More options > Plug-ins switched off after a crash.

Problem: A plug-in will not load and I do not know why.
Fix: Check the crash log, avalonia-crash.log, in `%APPDATA%\TabForge`, and the Diagnostics folder in `%LOCALAPPDATA%\TabForge`. Plug-ins installed under Program Files load without approval, and plug-ins anywhere else need a scan or your approval. Use the Add plug-in window and its rescan option after installing a new plug-in.

Problem: I need to stop everything that is playing right now.
Fix: Press Space to pause, or choose Stop from the transport bar. If notes are stuck on, use Tools > Panic (all notes off).

## Performance

Problem: The program feels slow when I move around the score.
Fix: Turn on Reduce animations in Preferences > Appearance. Lower the Animation speed if it is set high. Hide panels you are not using, such as the Arrangement overview with F8.

Problem: The song plays badly on a long piece with many plug-ins.
Fix: Remove plug-ins you do not use, close plug-in windows that are open, and raise the Buffer size a step if you hear crackles. Turn off Play the whole song through the audio engine if your tracks do not need it, because that option uses more memory and processor time.

Problem: The timeline is hard to read because it has too many tracks.
Fix: Turn on Compact mode from the View menu or with Ctrl+Alt+C. It hides every track row and gives the master lane more room. Choose it again to bring the tracks back.

Problem: The fretboard panel takes too much space.
Fix: Drag its edge to make it smaller, or turn on Lock fretboard size at the height you want.

Problem: Playback stutters while the screen is busy with other windows open.
Fix: Close the windows you are not using, such as the mixer and the FX chain windows. Then play again.

Tip: The fastest way to get back to a responsive program is to close all other windows and reset the panel layout with View > Reset all panels to default positions.

## Install and updates

Problem: I do not know which version of TabForge I am running.
Fix: Choose Help > About. The About window shows the version and the licence. Use Check for updates to compare with the latest release.

Problem: TabForge does not tell me when a new version comes out.
Fix: Choose Help > Check for updates…. It asks GitHub whether a newer release exists, with one anonymous HTTPS request.

Problem: A new version is out, but I am not sure how to install it.
Fix: Read the release notes on the project's Releases page before you install. Keep your settings and song files where they are, because they stay in your user folders.

Problem: TabForge does not start after an update.
Fix: Restart the computer, then start TabForge again. If it still fails, check the crash log in the TabForge folder described below, and report the problem as explained near the end of this chapter.

## Settings and reset

Problem: I changed too many settings and I want the defaults back.
Fix: Open Preferences and use Reset page on the page you changed, or Reset all settings to reset everything. Reset all settings is found by searching in Preferences.

Problem: I want to start completely fresh, as if TabForge were newly installed.
Fix: Open Preferences > Advanced and choose Reset entire app. Read the list of items, tick only what you want to remove, back up first if you may need the data, and type the word the dialog asks for.

Problem: I lost my keys or shortcuts.
Fix: Open Preferences > Shortcuts and choose Reset to return the keys to their defaults, or switch the preset back to TabForge. If you exported your settings, use Import settings to bring your keys back.

Problem: Something is wrong with my settings file, and the program will not start properly.
Fix: Close TabForge. Rename settings.json in the TabForge folder under your roaming application data, which is `%APPDATA%\TabForge`, so TabForge makes a new one. Keep the old file so you can put it back if needed.

Problem: I want to copy my settings to another computer.
Fix: Open Preferences, choose Manage settings, and then Export settings. Copy the file across, and use Manage settings > Import settings on the other computer.

## Where settings and files are stored

TabForge keeps its files in two per-user places.

- Roaming data is in `%APPDATA%\TabForge`. It holds settings.json, your plug-in library, chain states, presets, drum maps, the Templates folder and the crash log, avalonia-crash.log.
- Local data is in `%LOCALAPPDATA%\TabForge`. It holds the Recovery folder with autosave copies of unsaved songs, and the Diagnostics folder with the first-chance and error logs and the self-test logs.

Tip: Type `%APPDATA%` in the address bar of Windows Explorer to open the roaming folder. Type `%LOCALAPPDATA%` to open the local one.

Note: Keep copies of your song files in your own folders. TabForge's folders hold settings and recovery copies, not your main songs.

## Reporting a problem

When you ask for help, a few facts make the difference between a quick answer and a long back-and-forth.

1. Write down what you did, in order, just before the problem appeared. Include the keys you pressed.
2. Note the version from Help > About.
3. Note the audio driver and the output device from Preferences > Audio & Plug-ins, if the problem is about sound.
4. Save a copy of the song that shows the problem, using File > Save as. Do not send your only copy.
5. Find the crash log, avalonia-crash.log, in `%APPDATA%\TabForge`. Find the Diagnostics folder in `%LOCALAPPDATA%\TabForge`, and note the names of the logs there.
6. Send the steps, the version and the logs to the project's Issues page on GitHub. Attach the song copy if the problem is in the song.

The crash message itself names the two places to look: the crash log in the roaming folder, and the Diagnostics folder in the local folder.

Warning: Logs and song files can contain file names and folder names from your computer. Check them before you share them, and remove anything you do not want to show.

Note: If you need help with an audio device, say which device and driver you use. That is often the key detail.

## The Help window and search

Problem: F1 does nothing.
Fix: Click the score or a panel first, so the main window has the keyboard focus, then press `F1`. A text box that has the focus can take keys. You can also choose **Help > Help**. If you changed the key in **Options > Preferences > Shortcuts**, use the key you set, or search the Command palette for Help.

Problem: I pressed F1 twice and only one Help window opened.
Fix: That is how it works. TabForge keeps one Help window and brings it to the front. Close it with `Esc` and press `F1` again to open it fresh.

Problem: The search box says it is preparing the search.
Fix: The search index is built once, in the background, the first time the Help opens. It takes a moment. Type again when it says the search is ready.

Problem: The search finds nothing for my words.
Fix: Try fewer or simpler words, such as `sound`, `save` or `slow`. Search also understands everyday phrases such as `no sound` or `wrong string`. Put words in quotes to find an exact phrase. When nothing matches, the page lists the chapters that people open most.

Problem: I cannot find a command or its key in the Help.
Fix: Press `Ctrl+Shift+A` to open the Command palette. It lists every command with its hotkey and runs the one you choose.

Problem: The Help window is too small or opened off the screen.
Fix: Drag its edges to resize it, or drag the title bar to move it. TabForge remembers the size, and the position for this session.

## Common questions

Question: Why is there no sound when I press Play?
Answer: The most common causes are a mute or solo on the track rows, a volume at zero, or an Output device that is not the one you are listening on. Check those first, then open Preferences > Audio & Plug-ins.

Question: Which Audio driver should I choose?
Answer: Start with WASAPI shared, which works on most computers and lets other programs keep playing. WASAPI exclusive or ASIO give less delay if your device supports them, and DirectSound is the slow fallback.

Question: How do I stop a song and get back to editing?
Answer: Press Stop from the transport bar, or Ctrl+.. The cursor returns to where you were editing.

Question: Why does my keyboard not show up in Keyboard mode?
Answer: Check that MIDI in is set to Any available device or to your device, and that the keyboard is plugged in and installed. Press a key and watch the dot beside the box.

Question: What is the fastest way to find a key or a command?
Answer: Open the Command palette with Ctrl+Shift+A and type part of the command's name. The key is shown beside it.

Question: Can I undo a change I made by mistake?
Answer: Yes. Press Ctrl+Z to undo, and Ctrl+Y to redo. Each step is one edit, so press it several times to go back further.

Question: Does TabForge send my songs anywhere?
Answer: Only when you choose Check for updates, TabForge makes one anonymous request to GitHub to see whether a newer release exists.

Question: Where are my settings kept?
Answer: In settings.json in `%APPDATA%\TabForge`. Use Manage settings > Export settings to make a copy.

Question: Who should I contact if nothing here fixes my problem?
Answer: Use the project's Issues page on GitHub. Follow the steps in Reporting a problem, and include the version, the steps, and the logs.

Related: Playback and practice, Settings, themes and hotkeys, Composer and left strip, Keyboard and fretboard tools
