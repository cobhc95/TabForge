---
title: Tracks and sound
id: tracks-and-sound
order: 4
keywords: track, add track, instrument, tuning, capo, mute, solo, volume, pan, knob, exact value, fx, plug-in, plugin, vst, effects, allow again, mixer
summary: Add a track, choose its instrument and tuning, mute, solo and balance tracks, and meet the FX button and plug-ins.
---

# Tracks and sound

A song is a band, and each player in the band is a track. A track has its own notes, its own instrument sound, its own volume and its own tuning. This chapter shows how to add a track, change how it sounds, and balance the tracks against each other.

By the end you can add a bass to a song, choose an instrument for it, silence or isolate any track, and set a volume exactly. You will also know what the **FX** button is for.

## What you will learn

- Add a track and choose its instrument.
- Change a tuning, and understand a capo.
- Mute, solo and balance tracks.
- Type an exact value into a knob.
- Know what the **FX** button and plug-ins do, and what **Allow again** means.

## The track list

The track list sits beside the timeline at the bottom of the window, with one row per track. Click an empty part of a row to select that track. The score, the fretboard and the status bar then show it. The score always shows the selected track.

Each row has a few small controls. **M** mutes the track, **S** plays it alone, and the volume and pan controls set how loud it is and where it sits between the left and right speakers. The **INSTRUMENT** button shows the track's sound and changes it, and **FX** opens the track's effects. Hover any of them to read what it does.

## Add a track

Open the **Track** menu and choose **Add Guitar**, **Add Bass**, **Add Drums** or **Add Keys**. The new track goes to the end of the list and is selected. **Add Bass** gives you a four-string bass.

The **+ Track** button above the track list opens an **Add track** window instead. There you choose the name, the instrument, the tuning and the position in the list before you click **Add track**.

To remove a track, select it and choose **Track > Delete track**. Press `Ctrl+Z` if you change your mind.

## Choose an instrument

An instrument is the sound a track makes. Pick a different one, and the same notes sound like a piano or a violin. Click the **INSTRUMENT** button on the track row for a quick list. For the full catalogue, choose **Track > Properties…**, then click **Change…** on the instrument card. You can search the catalogue by typing a word such as `bass` or `piano`.

Choosing an instrument changes the sound, not the notes.

## Tuning and capo

A tuning is the note each open string plays. Standard guitar tuning, from the lowest string to the highest, is E A D G B E. Open **Track > Properties…** and look at the tuning area. A **Preset** list offers common tunings, and each string has buttons to move it up or down a semitone.

A tickbox called **Keep fret numbers** decides what happens to notes that are already written. Ticked, the numbers stay and the pitch changes, as when you retune a real guitar. Unticked, the sound stays and the numbers change.

A capo clamps across the neck and raises every open string by the same amount. The tuning area has a **Capo** box, and in TAB, fret numbers count from the capo.

## Mute, solo and balance

Muting and soloing change only what you hear. They never change the notes in the song.

- **M** silences that track.
- **S** silences every other track, so you hear only the soloed one.

A good balance has a plan. Bring up the drums and the bass first, because they carry the beat. Then place the other instruments, panning guitars a little left and right. If the lead is hard to hear, lower the parts around it before you raise the lead.

The **Mixer** window, under **View > Mixer / VST**, shows every track side by side.

## Type an exact value into a knob

Some controls are round knobs. You find them in **Track properties**, and a volume or pan knob can also appear on the track row if you choose it in **Preferences**.

Drag a knob to change it, or scroll the mouse wheel over it. To set an exact value, double-click the knob or right-click it. A small box opens. Type a value such as `80%` and press `Enter`, or press `Esc` to cancel. Hold `Ctrl` and click a knob to reset it to its default.

> **Tip:** Hover a knob to see the kind of value it accepts.

## The FX button and plug-ins

Every track makes its sound with the General MIDI sound that comes with Windows, so you never need anything extra. The **FX** button opens the track's effects chain, where you can add plug-ins. A plug-in is an instrument or an effect made by another company. TabForge hosts 64-bit VST2 and VST3 plug-ins.

Plug-ins run in a separate process from the editor. If one crashes, TabForge switches it off, marks the **FX** button, and playback carries on. When you have dealt with the problem, open the chain and click **Allow again** to let that plug-in load once more.

A song file only names its plug-ins. It never carries them, and TabForge loads a plug-in only from a place you have approved. If a bar appears above the status bar saying plug-ins are not loaded, click **Review…** and tick only the ones you trust.

> **Warning:** A plug-in can do anything you can do on your computer. Approve only plug-ins from makers you trust, and leave out any that you do not recognise.

### Try it: Add a bass and balance it

*Goal: add a bass track to the demo song, solo it and change its volume.*

1. Open the demo song and choose **Track > Add Bass**. A track called **Bass** appears at the end of the track list and is selected.
2. Click **M** on the **Lead Gtr** track and press `Space`. The lead guitar goes quiet.
3. Press `Space` to pause, and click **M** again to unmute.
4. Click **S** on the **Bass** track and press `Space`. You hear silence, because the new bass has no notes yet. Click **S** again to switch the solo off.
5. Choose **Track > Properties…**, double-click the volume knob, type `50%` and press `Enter`. Click **OK**.

You know it worked when the bass volume knob shows the new value. Close the tab without saving, and the song on disk stays as it was.

## Quick recap

- Each track is one player with its own notes, sound, volume and tuning.
- **Track > Add Bass**, **Add Guitar**, **Add Drums** and **Add Keys** add ready-made tracks.
- **M** mutes a track and **S** plays it alone. Neither changes the song.
- Double-click a knob to type an exact value, and hold `Ctrl` and click to reset it.
- **FX** opens a track's effects. **Allow again** lets a plug-in that crashed load once more.

## What next

Go on to **Chapter 5: Saving and sharing**. You will save your work, recover it after a crash and export it.
