# Tracks and mixer

A song is a band, and each player in the band is a track. A track has its own notes, its own sound, its own tuning, its own volume and its own place between the speakers. This chapter shows you how to add and remove tracks, change how each one sounds, and balance them against each other in the mixer.

Changing a track's sound, volume, pan or mute never changes its notes. So you can experiment freely with the sound of a song without worrying about spoiling the music.

## What a track is

Each track is one part of the song. A guitar track holds the guitar part, a bass track holds the bass, and a drum track holds the drum part. Each track also has a sound, which is the instrument it plays with.

The score shows the track you have selected. The track list shows every track in the song, one row for each. The fretboard shows the instrument of the selected track, so a drum track shows a drum map and a keys track shows a keyboard.

Tip: If you cannot tell which track you are on, look at the track list. The selected row is the one the score is showing.

## The track list at a glance

Each row in the track list has a few small controls.

- **M** mutes the track, so it is silent.
- **S** solos the track, so you hear only it.
- **FX** opens the track's effects chain.

Clicking an empty part of a row selects that track. Clicking the name or a control does something else, so click the empty space to make a track current.

## Add a track

There are two ways to add a track. Choose whichever is easier for you.

1. Press `Ctrl+T` to open the **Add track** window. Choose the kind of track you want and its basic settings, then confirm. The new track is added and selected.
2. Or choose **Insert > Track** and pick one of the ready-made kinds from the list.

The ready-made kinds are:

- **Tab track**, a guitar-style track with tablature.
- **Bass**, a four-string bass track.
- **Drums**, a drum track with a drum map.
- **Keys**, a keyboard track.
- **Audio / MIDI track**, an empty track for audio or MIDI clips.

A Bass, Drums or Keys track you add this way goes to the end of the list and is selected straight away. The **Add track** button at the end of the track list does the same job as `Ctrl+T`.

Tip: Use **Insert > Track** when you want a common track quickly. Use `Ctrl+T` when you want to set the track up first.

Note: There is also a second shortcut for **Add track**, `Ctrl+Shift+Insert`. Which of the two keys is the main one depends on the keyboard preset you chose in **Settings**.

## Delete a track

1. Click the track in the track list to select it.
2. Choose **Track > Delete track**, or press `Ctrl+Shift+Delete`.

If you change your mind straight away, press `Ctrl+Z`. Deleting a track is a single undo step, so one `Ctrl+Z` brings it back with its notes.

Warning: Deleting a track removes its notes from the song. Before you delete, check that the part is not something you want to keep, or save a copy first.

## Move a track up or down

The track order decides how the rows appear in the track list.

- Press `Alt+Shift+Up` or `Alt+Shift+Down` to move the selected track up or down.
- Or choose **Track > Move up** or **Track > Move down** from the menu.
- Press `Ctrl+Shift+Up` or `Ctrl+Shift+Down` to select the previous or next track without moving it.

Tip: Keep the bass and drums near the top of the list, because you are likely to balance them first.

## Track properties

The track properties window holds everything about one track in one place.

1. Select the track in the track list.
2. Choose **Track > Properties**, press `F6`, or right-click the track's row and choose **Properties**.

The window has several sections, and you can scroll down to reach them all.

- **Track colour** sets the colour of the track's row, which helps you tell the parts apart. Right-click the colour swatch to pick a named colour. A left click on the swatch does nothing.
- **Played by** chooses the instrument, which is the sound the track makes.
- **Notes** holds the **Keep fret numbers** tick box, which decides what happens to notes when you retune the track. See the tuning section below.
- **Drum notation** sets the drum map for a drum track, and has a button to edit a custom drum map.
- **Tuning** sets the note of each open string.
- **Capo** sets a capo across the neck.
- **Mixer** sets the volume and pan of the track, and has the **FX chain** button.
- **MIDI** sets the **MIDI program** and the **MIDI channel** the track plays on.

Tip: Click **OK** to keep your changes to the track. Your song is then saved again the next time you save it with `Ctrl+S`.

## Choose an instrument

An instrument is the sound a track makes. The same notes played on a piano sound very different from the same notes played on a violin, and nothing about the notes themselves changes.

1. Select the track and open its properties with `F6`.
2. In the **Played by** section, click the instrument button. A list of sounds opens.
3. Type a word such as `bass` or `piano` to narrow the list.
4. Pick a sound. The track plays with it straight away.

Tip: Play a few notes after you change the sound. Listen first, then decide if it is right.

Note: A drum track is meant to play on the drum channel. The MIDI section notes that channel 9, the tenth channel, is the General MIDI drum channel. Keep a drum track on that channel so the drum sounds play correctly.

## Tuning

Tuning is the note each open string plays. Standard guitar tuning, from the lowest string to the highest, is E A D G B E.

1. Open the track properties with `F6`.
2. In **Tuning**, choose a common tuning from the **Preset** list if one matches your instrument.
3. To change one string, use its buttons to move it up or down by a semitone, which is a half step.
4. Decide what happens to notes you have already written, with the **Keep fret numbers** tick box in the **Notes** section.

With **Keep fret numbers** ticked, the fret numbers stay as they are and the pitch changes. This is like retuning a real guitar, where pressing the same fret gives a different note. With it unticked, the sound stays the same and the fret numbers change to match.

Tip: If you are not sure, tick **Keep fret numbers** first. Your tab then stays readable as a fingering, and you can hear the change in the sound.

Warning: Retuning a track with **Keep fret numbers** unticked rewrites the fret numbers in the song. Save first, or use `Ctrl+Z` straight after, if the tab no longer looks right.

## Capo

A capo clamps across the neck and raises every open string by the same amount. It makes the chord shapes easier to play in a different key.

The **Capo** section holds the capos for the track.

- A **full capo** covers every string at one fret.
- A **partial capo** covers only some of the strings, from one string to another. You can have up to four partial capos.
- **Capo fret** sets the fret the capo sits on.
- **Capo from string** and **Capo to string** set which strings a partial capo covers.
- **Remove selected** takes away the capo you have picked in the list.
- **Remove all capos** takes away every capo on the track.

In the tab, fret numbers count from the capo, so a chord written with a capo at the 2nd fret shows the shape you actually play.

Note: If you already have four partial capos, the window says that the partial capo limit has been reached. Remove one before you add another.

## Mute and solo

The **M** and **S** buttons on each row are the quickest controls for hearing your song.

- **M** mutes the track, so it falls silent.
- **S** solos the track, so you hear only that one and every other track is silenced.

Click a button again to switch it off. Muting and soloing change only what you hear. The notes stay in the song, and they come back the moment you unmute.

Tip: Solo a track to hear a single part on its own while you check it. Remember to switch the solo off before you play the whole band.

Warning: A soloed track silences all the others, so it is easy to forget. If the whole song sounds empty, look for a lit **S** button first.

## Volume and pan

Volume sets how loud a track is. Pan sets where the track sits between the left and right speakers, with the centre in the middle.

You can set both in two places.

- In the track properties window, in the **Mixer** section.
- In the **Mixer** window, which shows every track at once.

Volume runs on a scale from 0 to 16, and its default is 13. Pan runs from left at -8 to right at +8, with 0 in the middle. Some views show these as a slider, and others as a knob. You can choose which one you prefer in the settings for the track list.

## Type an exact value into a knob

The volume and pan controls are often knobs, which you turn rather than type into. To set an exact value, do this.

1. Double-click the knob, or right-click it. A small box opens.
2. Type the value, such as `75%`, `-6 dB` or `100 ms`, depending on the control.
3. Press `Enter` to apply it. Press `Esc` to cancel.

Other ways to turn a knob are these.

- Drag it, or turn the mouse wheel over it, to change the value.
- Hold `Shift` while you drag for finer steps.
- Hold `Ctrl` and click it, or press `Home` when it has focus, to reset it to its default.

Tip: Hover over a knob to see what kind of value it expects. A red outline in the box means the value you typed is not valid.

## The Mixer window

The mixer shows every track side by side, one strip for each track. It is the best place to balance a whole song.

Open it in one of these ways.

- Choose **View > Mixer / VST**.
- Click the **Mixer** button in the transport bar.
- Click the **Mixer** button in the left icon strip, if you have it there.

Here is what the mixer offers.

- Each strip has its own volume, pan, **M** and **S** buttons, and an **FX** button.
- The tracks can be grouped by instrument, into guitars, basses, keys, drums and other, or grouped in a compact way, or left with no groups at all. The grouping is chosen at the top of the window.
- Each group has a group strip in front of it. The group strip sets the level, the pan and the pitch of every track in that group at once. It also has its own mute and solo.
- Right-click a track strip to move it into another group.
- The **MIDI** column lets you choose whether the track's General MIDI sound plays alongside its plug-ins.

Changes in the mixer are live, so you hear them at once. They are saved with the song, and they can be undone with `Ctrl+Z`.

Tip: Balance the drums and the bass first, because they carry the beat. Then place the guitars and keys around them, and raise the lead last.

Note: The **Mix Table** is a separate tool for changing one beat. Select a beat in the score and press `F10` to change its instrument, volume, pan, effects or tempo from that point on.

## Effects and the FX button

Each track can have its own effects and plug-ins. Click the **FX** button on the track's row, or on its mixer strip, to open the track's effects chain.

The chain window lists the effects in the order the sound passes through them.

- Drag an effect up or down the list to change the order.
- Tick an effect to turn it on, or untick it to bypass it, which hears the sound without the effect.
- Press `Delete` to remove the selected effect.
- Press `Ctrl+C`, `Ctrl+X` and `Ctrl+V` to copy, cut and paste effects between chains. `Ctrl+A` selects every effect in the chain.
- Press `Ctrl+S` to save the whole chain as a chain file, and `Ctrl+O` to load one.

The **FX** button has a power symbol. Turning it on uses the track's plug-in chain, and turning it off switches the track back to the sound from Windows MIDI.

A plug-in is an instrument or an effect made by another company. TabForge can host VST2 and VST3 plug-ins. Plug-ins run in a separate audio engine, not inside the editor, so a plug-in that misbehaves should not bring the whole program down.

Warning: A plug-in can do anything you can do on your computer. Approve only plug-ins from makers you trust, and leave out any that you do not recognise.

## Approving plug-ins

TabForge loads a plug-in only after you have approved it. If a plug-in is not approved yet, it does not load, and a notice appears above the status bar.

1. Click **Review** in the notice.
2. In the review window, tick only the plug-ins you trust.
3. Close the window. The plug-in can now load.

Plug-in folders are set in the audio and plug-in settings, under **Options > Preferences > Audio & Plug-ins**. Only the folders you list are scanned, unless you turn on the option to scan the standard VST folders as well.

## When a plug-in stops working

If a plug-in crashes, TabForge switches it off for safety, and the track's **FX** button shows that something is wrong. Playback carries on without the crashed plug-in.

1. Open the track's **FX** chain.
2. Look for the plug-in marked as switched off.
3. When you have dealt with the cause, click **Allow again** to let it load once more.

If it stops again straight away, the plug-in may be faulty, so leave it off and use another one.

## Troubleshooting

Problem: I muted a track and now the song has a hole in it.
Fix: Mute hides the sound only, so the notes are still there. Click **M** on that track again to bring the sound back.

Problem: Every track is silent, and I don't know why.
Fix: Look for a lit **S** button. A soloed track silences all the others. Click the **S** button to switch the solo off, then press `Space` to play again.

Problem: One track plays a sound I did not choose.
Fix: Open the track's properties with `F6`, then change the sound in the **Played by** section.

Problem: I retuned a track, and now the fret numbers are all wrong.
Fix: Press `Ctrl+Z` straight away to undo the change. Next time, tick **Keep fret numbers** in the **Notes** section before you retune, so the fret numbers stay the same.

Problem: I can't hear one of my tracks at all.
Fix: Check that it is not muted, that another track is not soloed, and that its volume is not turned right down. Then open its **FX** chain and check for a plug-in that has been switched off.

Problem: A plug-in has been switched off and the sound has gone quiet.
Fix: Open the track's **FX** chain and click **Allow again**. If it switches off again, leave that plug-in out and use a different one.

Problem: A notice says a plug-in is not loaded.
Fix: Click **Review**, tick only the plug-ins you trust, and confirm. TabForge does not load a plug-in until you have approved it.

Problem: I deleted a track by mistake.
Fix: Press `Ctrl+Z` straight away. Deleting a track is one undo step, so the track and its notes come back.

Problem: The mixer does not show the groups I expect.
Fix: The grouping is set at the top of the mixer window. Choose grouping by instrument, compact grouping or no groups, and the strips rearrange to match.

Problem: My knob will not take the value I type.
Fix: The value may be outside the allowed range, or it may be in a format the knob does not accept. A red outline means the entry is not valid. Press `Esc` to cancel, then try a value inside the range shown when you hover over the knob.

Problem: I changed the volume of a track by accident, and I do not know what it was before.
Fix: Ctrl+click the volume knob, or press `Home` while it has focus, to reset it to its default of 13. If you have just changed it, `Ctrl+Z` gets the old value back.

## Common questions

Question: Does muting a track delete its notes?
Answer: No. Muting only silences the track's sound. The notes stay in the song, and unmuting brings the sound back.

Question: What is the difference between volume and pan?
Answer: Volume is how loud the track is. Pan is where it sits between the left and right speakers, from left to right, with the centre in the middle.

Question: Can I change a track's sound without changing its notes?
Answer: Yes. Change the instrument in the **Played by** section of the track properties. The notes stay exactly as they were.

Question: What is the difference between solo and mute?
Answer: Mute silences one track. Solo silences every other track, so you hear only the soloed one. Both switch off again with another click.

Question: What is a capo doing in my tab?
Answer: A capo raises every string by the same amount. In the tab, fret numbers count from the capo, so the numbers show the shape you actually play.

Question: Do I have to use plug-ins?
Answer: No. Every track plays its sound through the General MIDI sounds that come with Windows, so you can write and listen without any plug-in. Plug-ins are optional extras.

Question: Are the mixer changes saved?
Answer: Yes. The mixer changes, including groups, levels and pan, are saved with the song when you save the song.

Related: Getting started, Interface tour, Files and export
