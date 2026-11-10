# Playback and practice

This chapter explains how to listen to your song, slow it down, loop a hard bar, count yourself in, play along with a keyboard, and record sound. You do not need to know music theory to use any of it. Each part below says what the control does, when you would use it, and what to do step by step.

If you feel lost, start with the section called Play, pause and stop. Once you can start and stop a song, the rest of the chapter builds on that.

## What this chapter covers

- The transport bar, the row of buttons that controls playback.
- Play, pause, stop and rewind.
- Speed, which changes how fast a song plays without changing its written tempo.
- Loop, which repeats a stretch of the song.
- The metronome and the count-in, which give you a steady click.
- Following the playhead, so the score scrolls with the music.
- Keyboard mode, the practice view with falling notes and a wait-for-me option.
- Recording audio and MIDI, live input, and video recording.
- Audio device setup, including drivers such as ASIO and WASAPI.
- MIDI input devices.

## The transport bar

The transport bar is the row of buttons that controls playback. It sits in the timeline header. The buttons are listed here from left to right, as they usually appear.

- **Rewind to beginning** moves the playhead back to the start of the song. Use it when you want to hear the song from the top. It has no keyboard key of its own.
- **Stop playback** stops the song and leaves the cursor where it was. Use it when you are finished listening for now.
- **Record** records every armed track while the song plays. Press it again to stop. Read the Recording section below before you use it.
- **Play or pause** starts the song, or pauses it where it is. Press it again to carry on from the same place.
- **Next section** jumps the playhead to the next section of the song. Use it to skip a part you already know.
- **Count-in** switches the count-in on or off. The count-in plays a few bars of click before the song starts. Left-click toggles it. Right-click opens its settings.
- **Metronome** switches the steady click on or off. Left-click toggles it. Right-click opens its settings.
- **Loop** switches looping on or off. Left-click toggles it. Right-click opens the loop settings.
- **Zoom in timeline** and **Zoom out timeline** make the bars of the timeline wider or narrower. The score zoom is a different control, found under the View menu.
- **Mixer** opens the mixer, where you change the level, pan and effects of each track.
- **Live input** lets you hear your instrument through the track effects without recording. It is described further below.
- **Global tuning** opens the tuning window. Double-click it to type a semitone shift. Right-click it for quick tuning options.
- **Snap** turns snapping of clips on or off. Snapping makes clips line up with the grid, other clips and the playhead. Hold Alt while you drag to place a clip freely.

Tip: If a button seems to do nothing, hover over it. The tooltip tells you what it does and shows the key that works for it. Some buttons are greyed out in the preview, where playback is not available.

## Play, pause and stop

Use these steps the first time you play a song.

1. Click an empty part of the score so the keyboard is listening to the score.
2. Press `Space` to start playback. The playhead begins to move across the score.
3. Press `Space` again to pause. The song stays where it is.
4. Press `Space` once more to carry on from the same place.
5. Press `Shift+Space` to play from the start of the song.
6. Press `Ctrl+.` to stop. The song stops and the cursor returns to where you were editing.

The keys above come from the default **TabForge** shortcut set. You can change them, as explained in the Settings, themes and hotkeys chapter.

Note: Pausing and stopping are different. Pause keeps the playhead in place. Stop returns the cursor to the edit position.

## Speed

Speed changes how fast the song plays back. It is a listening control, so it does not change the written tempo or the pitch of any note.

- The **Speed** box in the toolbar shows the current speed as a percentage.
- Click the arrow on the box to choose a preset: 50%, 75%, 100%, 125%, 150% or 200%.
- Use `Ctrl+Alt+Down` to move to the previous preset, and `Ctrl+Alt+Up` to move to the next preset.
- Use `Ctrl+Alt+0` to return to 100%, which plays the song at its own tempo. The 0 is the number key on the top row of the keyboard.

Speed is not tempo. The tempo is the written beats per minute of the song, shown in the BPM box. Speed multiplies it while you listen. At 50% a song written at 150 BPM plays as if it were 75 BPM.

To practise a hard part:

1. Set the speed to 50% or 75%.
2. Turn on Loop (see the next section) so the hard part repeats.
3. Play it until every note is clean.
4. Step the speed up one preset at a time.

Tip: Play slowly enough to be clean, and only then speed up. Fast and sloppy practice teaches the mistakes.

## Loop

Loop plays a stretch of the song again and again. Use it for a bar or section you are learning.

- Press `F9`, or click the **Loop** button, to switch looping on or off.
- With no area selected, Loop repeats the whole song.
- To loop a few bars, drag across them on the timeline first. The selected area stays highlighted even when Loop is off. With Loop on, the area repeats.
- Right-click inside a selected area to see Copy, Cut, Paste before, Move, Delete, Loop, Skip during playback and Clear selection.
- Right-click a section on the timeline and choose **Loop section** to loop that section. The menu row is ticked while that section loops. Click it again to stop.
- Right-click the **Loop** button to open the loop settings.

The loop settings let you choose:

- How many times the loop repeats. Leave the box empty, or use the infinity symbol, to loop forever. A small badge beside the button shows how many loops are left.
- Whether a count-in plays before every loop.
- Whether the loop is a simple loop or a speed trainer. A speed trainer starts at one percentage and raises the speed by a set amount after each pass, so every pass is a little faster.

Note: By default, Loop with no area selected loops the whole song. A setting called "Loop button loops what is playing" changes this; it is off by default.

Tip: Use the speed trainer when you want to build up to full speed without thinking about the steps. Set it to start slow and add a small amount each pass.

## Metronome

The metronome plays a steady click on every beat, so you can hear where the beat falls.

- Left-click the **Metronome** button to switch the click on or off. The change takes effect at once, even while the song plays.
- Right-click the **Metronome** button to open the metronome settings.
- The click is boosted, or layered, by default, which makes it easier to hear.
- The count-in volume slider sits in the same area and is set to 70% by default.
- There is no default key for the metronome. You can give the command a key in **Options > Preferences > Shortcuts**, under the Transport group.

Step by step to hear the click while you play:

1. Click the **Metronome** button so it lights up.
2. Click **Play** or press `Space`.
3. If the click is too quiet, right-click the **Metronome** button and raise its volume.

## Count-in

The count-in plays a few bars of click before the song starts. It gives you time to get your hands ready.

- Left-click the **Count-in** button to switch it on or off.
- Right-click the **Count-in** button to open its settings. In the settings you can change the volume (70% by default), the length (from 1 to 4 bars), and the click sound.
- The count-in can play only at the start of the song, before every section, or before every loop repeat. Choose the setting that suits your practice.

Tip: Turn the count-in on when you loop a part. A count-in before each loop repeat gives you a clean start every time.

## Following the playhead

Follow keeps the score and the timeline in view while the song plays.

- Press `Ctrl+Shift+F`, or use View > Follow playback, to turn following on or off.
- The default is page turn. When the playhead nears the edge of the page, the score jumps to the next page or line.
- Smooth page turn makes the same turns, but glides them. The View menu has a command for it. It has no default key.
- Preferences > Playback & Practice has the Follow group, with options such as Scroll the score while playing, Horizontal follow, Vertical follow, Pause following when I scroll by hand and Stop scrolling once the end is visible.
- Highlight playing bar shades the bar that is playing. It is on by default and subtle. You can change its colour in Preferences > Appearance.

Note: If the score jumps around while you are trying to read it, turn off Follow with `Ctrl+Shift+F`. Turn it back on when you want the score to follow.

## Keyboard mode: practise with falling notes

Keyboard mode is the practice view. It shows the notes of the selected track falling onto a row of keys, in time with the song. You play the keys on a MIDI keyboard, or on the on-screen keys, while the song waits or carries on. The View menu marks it as experimental, so expect small rough edges.

To open it:

1. Choose **View > Keyboard mode (experimental)**, or press `Ctrl+Alt+L`.
2. A panel opens with the falling notes and a control bar above them.
3. To open the same view in its own window, choose **View > Keyboard mode: view in its own window**, or press `Ctrl+Alt+O`. Press `F11` in that window for full screen, and `Esc` to leave full screen.

The control bar has these controls, left to right:

- **Track** chooses which track the keyboard follows and practises.
- **Play** starts or pauses the song. **Stop** stops it.
- **Speed** sets the playback speed, using the same presets as the main Speed box.
- **Wait for me** makes the song stop at each chord until you play its notes. Press `Ctrl+Alt+W` to turn it on or off.
- **Skip** gives up on the chord the song is waiting for. It counts as a miss, and the song carries on. Press `Ctrl+Alt+Q` for Skip.
- **Loop** loops the selected bars, or the section at the cursor.
- **Hands** chooses which hand to practise. The choices are Both hands, Left hand, Right hand, Left hand only and Right hand only. Notes of the hand you are not practising are faded, or hidden when you pick "only".
- **Look-ahead** shows you more or fewer seconds of notes above the keys. The minus button makes the notes longer and easier to read. The plus button shows more time ahead, so the notes are shorter. Press `Ctrl+Alt+-` and `Ctrl+Alt+=` to change it from the keyboard.
- **Note names** writes the note name on each falling note.
- **Fingers** shows finger numbers on the falling notes, when the score has fingering.
- **MIDI in** chooses the MIDI keyboard to play along with. The choices are Off, Any available device, or one named device. A dot shows whether a device is connected. The text beside it says No device connected when nothing is found.

Keyboard mode also shows a score panel. It shows your **Accuracy** as a percentage, your **Streak** with your best streak, and the counts of **Hits** and **Misses**.

Step by step to practise a chord part with Wait for me:

1. Open Keyboard mode with `Ctrl+Alt+L`.
2. Choose the track you want in the **Track** box.
3. Set **Hands** to the hand you are practising, for example Right hand only.
4. Turn on **Wait for me**.
5. Press **Play**. The song stops at each chord.
6. Play the notes of the chord on your MIDI keyboard. The song continues when you have played them.
7. If you cannot play a chord, press **Skip** to move on and count it as a miss.

Note: Keyboard mode is experimental and has not been tried with every MIDI device. If a device does not respond, check the MIDI in box and read the MIDI input section below.

Tip: Start with Hands set to one hand and Speed at 50%. Add the second hand and raise the speed when the first hand is steady.

## Recording

You can record sound from an audio input, or record notes from a MIDI keyboard, onto the armed tracks of your song.

Before you record:

1. Connect your instrument or microphone to your audio interface.
2. Open **Options > Preferences > Audio & Plug-ins** and check the **Audio driver**, the **Output device** and the **Recording device**. The steps are in the audio setup section below.
3. Make sure the track you want to record on is armed. Arming is done with the red button on that track's row. The command Track.Arm, which arms a track, has no default key.

To record:

1. Arm the track with its red button.
2. Press `Ctrl+R`, or click the **Record** button on the transport bar.
3. The song plays. Play your part.
4. Press `Ctrl+R` again, or click **Record** again, to stop. The take appears on the timeline.

If no track is armed when you press Record, the top bar shows a message that no track is armed. Arm a track and press Record again.

Each track has an input picker that chooses what it hears. It has two parts. The audio input is one input of your audio device, or none. The MIDI input is None, All MIDI inputs, or one MIDI device.

Recording settings are in Preferences > Audio & Plug-ins:

- **Recording device** chooses the device armed tracks record from. With ASIO, the recording comes through the ASIO driver instead.
- **Recording timing correction** shifts new recordings along the timeline, in milliseconds, from -1000 to 1000. It works on top of the delay the device reports. Enter a positive number if your recordings sound late against the song, so they move earlier. Enter a negative number if they sound early. The default is 0.
- **When recording over a MIDI clip** chooses what happens when you record notes over an existing MIDI clip. New lane, the default, puts the take on its own lane above the clip. Merge adds the notes into the clip underneath, as one undo step.
- **Quantize MIDI input** snaps the start of each recorded MIDI note to the grid when it lands. It is off by default, so the raw timing is kept.
- **Monitor MIDI input** decides whether you hear a MIDI keyboard through a track's own sound. Armed tracks plus the selected track is the default. Armed tracks only plays only the armed ones. Off is silent; the keys on the piano roll still light up, and recording is not affected.
- **Use all inputs of the audio device** is on by default. Turn it off only if the device misbehaves with all its inputs open.

Warning: Recording while you hear the song through speakers can record the song as well as your instrument. Use headphones if you record while the song plays.

## Live input

Live input lets you hear your instrument through the track effects and the master output, without recording. It is useful for checking your sound before you record.

- Click the **Live input** button on the transport bar, or use the Transport > Live input command. It has no default key.
- Click the button again to turn live input off.
- Right-click the button for its menu. The menu lets you open the live FX chain, choose audio input rows, choose MIDI rows, and open the audio device settings.
- Live input is for the whole program. It stays on when you switch tabs or windows.

Note: Live input sends your sound out through the program. Turn the volume down before you connect a new instrument.

## Record video

Record video captures the screen and the sound as an MP4 file.

- Press `Ctrl+Alt+V`, or choose **File > Record video**, to start. Press it again to finish.
- The resolution, frame rate, what to record and the folder are set in **Options > Preferences > General**, in the Video recording group.
- Starting or stopping a video recording never starts or stops the song.

## Render to an audio file

To save the mix of your song as a file, use **File > Export > Audio file**, or press `Ctrl+Alt+R`.

- The render can produce a WAV or MP3 file, with the master mix, the separate stems, or both.
- It works faster than real time, so a long song finishes sooner than it would take to play.

## Audio device setup

Audio device setup decides which sound card or interface TabForge uses, and how fast it responds. Open it from **Options > Preferences > Audio & Plug-ins**. Options > MIDI / Audio setup opens the MIDI output window of the selected track, which is a different window.

The main choices on the Audio & Plug-ins page are:

- **Audio driver** chooses how audio reaches your speakers. WASAPI shared is the safe default that lets other programs keep playing sound. WASAPI exclusive and ASIO give the least delay. DirectSound is the slow fallback.
- **Output device** chooses the device your audio plays on. Choose (Windows default) to follow your Windows sound settings. With ASIO, this is the installed ASIO driver to use.
- **Configure ASIO driver** opens the control panel of the chosen ASIO driver. It is active only with ASIO.
- **ASIO output: first channel** and **ASIO output: last channel** choose the output channels. Names come from the driver.
- **Buffer size** sets how much audio is prepared at once, from 16 to 8192. A smaller buffer means less delay, but more chance of crackles. A larger buffer means fewer crackles, but more delay. It is remembered for each device.
- **Sample rate** sets the rate the device uses. Device default lets the device decide. A higher rate costs more processor power. It is remembered for each device.
- **Play the whole song through the audio engine** sends every track through TabForge's own engine, including ASIO. It uses a little more memory and processor time. Off means tracks without plug-ins play on Windows MIDI.
- **Sound engine** chooses the General MIDI sound. Windows GM needs no download. The banks in the list are played by TabForge's own synth, and some need a download first. None silences the built-in synth, which is useful when you load your own instruments. Open your own .sf2 chooses any SoundFont file on the computer, and TabForge plays it in place.
- **Windows MIDI delay** delays the Windows MIDI sound, in milliseconds, around 200 by default. Raise it if plug-in tracks sound early, and lower it if they lag.
- **Follow the Windows volume** is for ASIO, which ignores the Windows volume by default. On makes TabForge follow the Windows master volume and mute.
- **Protect speakers from pops when audio settings change** fades the sound out and back in around device changes.
- **Protect live playback from loud peaks** adds a small safety limit to live playback. It is off by default because it adds a tiny delay.

Step by step to choose a device:

1. Open **Options > Preferences > Audio & Plug-ins**.
2. Choose the **Audio driver**. Start with WASAPI shared if you are not sure.
3. Choose the **Output device**. Leave it on (Windows default) if you are not sure.
4. Press `Space` to play a song and listen.
5. If you hear crackles, raise the **Buffer size**. If the sound is late against your playing, lower it a little.

Tip: Only change the Buffer size one step at a time, and play for a minute after each change.

## MIDI input devices

A MIDI keyboard or controller can send notes to TabForge. Where a MIDI device is used:

- **Keyboard mode** has a **MIDI in** box. Choose Any available device to use whichever keyboard is connected. Choose one device to lock to a single keyboard. Choose Off to ignore MIDI. New devices are picked up while the view is open.
- **Track input picker** has a MIDI input choice for each track: None, All MIDI inputs, or one device. Use it to record from a keyboard onto a track.
- **Monitor MIDI input** in Preferences decides whether you hear the keyboard through a track's sound.
- **When recording over a MIDI clip** and **Quantize MIDI input** in Preferences control how MIDI takes land.

Step by step to connect a keyboard for Keyboard mode:

1. Plug the keyboard into the computer and wait a few seconds for Windows to find it.
2. Open Keyboard mode with `Ctrl+Alt+L`.
3. Open the **MIDI in** box and choose Any available device.
4. Play a key. The dot beside the box should light up, and the text should show the device name.

## Troubleshooting

Problem: I press Space and nothing happens.
Fix: Click an empty part of the score first, so the keyboard is listening to the score rather than a text box. Then press Space again; if the song still does not play, check the Audio & Plug-ins page for the output device.

Problem: The song plays but I cannot hear it.
Fix: Check the mute and solo buttons on the track row, check the master volume, and make sure the Sound engine is not set to None. Then open Preferences > Audio & Plug-ins and confirm the Output device is correct.

Problem: My recording sounds late against the song.
Fix: Open Preferences > Audio & Plug-ins, find Recording timing correction in the Audio input group, and enter a positive number of milliseconds, for example 20. Record a short test and listen. Adjust it a little at a time.

Problem: Pressing Record does nothing, or a message says no track is armed.
Fix: Arm the track first by clicking its red button on the track row. Then press Ctrl+R to start recording, and Ctrl+R again to stop.

Problem: The Wait for me button does not stop the song at chords.
Fix: Make sure Wait for me is switched on in the control bar, and that the Track box shows the track with the chords. Check that the MIDI in box is not set to Off, because the song waits for notes from the MIDI keyboard.

Problem: My MIDI keyboard does nothing in Keyboard mode.
Fix: Set MIDI in to Any available device, play a key, and watch the dot beside the box. If the dot stays off, unplug the keyboard, plug it in again, and wait for Windows to finish installing it. Then close and reopen Keyboard mode.

Problem: The loop keeps going when I want it to stop.
Fix: Right-click the Loop button and set the number of loops, or clear the setting so the loop repeats forever. To stop, click the Loop button or press F9.

Problem: The count-in is too quiet to hear.
Fix: Right-click the Count-in button and raise its volume. The default is 70%. You can also choose a longer length, up to 4 bars.

Problem: Pauses and crackles appear while the song plays.
Fix: Raise the Buffer size on the Audio & Plug-ins page, one step at a time. Turn off other programs that use a lot of processor power, and close any unused plug-in windows.

## Common questions

Question: What is the difference between speed and tempo?
Answer: Tempo is the written beats per minute of the song, shown in the BPM box, and it is saved with the song. Speed is a listening percentage that makes the song play faster or slower without changing the written tempo.

Question: How do I stop the song and go back to where I was editing?
Answer: Press Ctrl+., or click Stop playback. The cursor returns to where you were editing.

Question: Can I practise only the left hand?
Answer: Yes. Open Keyboard mode and set Hands to Left hand only. The right hand notes are hidden, and only the left hand notes are judged.

Question: Does the metronome change the song I record?
Answer: No. The click is a guide for your ears. It is not part of the recorded track unless you record the speakers, so use headphones if you want to keep the click out of your recording.

Question: Why does the score scroll away from the playhead?
Answer: Follow is probably off, or Pause following when I scroll by hand is on in Preferences > Playback & Practice. Press Ctrl+Shift+F to turn following on.

Question: Where do I change the audio driver?
Answer: Open Preferences > Audio & Plug-ins and choose the Audio driver. Choose WASAPI shared if you are not sure.

Related: Keyboard and fretboard tools, Settings, themes and hotkeys, Troubleshooting and FAQ
