---
title: Plug-ins, effects and audio devices
id: plugins
order: 10
keywords: plug-in, plugin, vst, vst3, instrument plug-in, effect, fx chain, bypass, gm sound, through chain, master fx, wiring, crash, trust, audio driver, wasapi, asio, directsound, latency, buffer size
summary: Add instrument and effect plug-ins to a track, build an FX chain, understand how TabForge protects you, and choose an audio driver.
est-minutes: 45
---

# Plug-ins, effects and audio devices

The built-in GM sound is enough to write and balance a song. A plug-in goes further. It can add a reverb to the guitar, a different instrument sound to a track, or a tone you cannot get from the GM sound. Plug-ins come from other makers, and TabForge hosts them.

This chapter explains what plug-ins are, how to add one safely, and how to build a chain of effects on a track. It also covers the audio driver and buffer size, which decide how quickly you hear what you play. You can read the whole chapter without owning a single plug-in.

## What you will learn

- Explain what instrument plug-ins and effect plug-ins are.
- Tell TabForge where your plug-ins are, and find one in the **Add plug-in** window.
- Build an FX chain on a track: add, reorder, bypass and set the volume.
- Use the **Through chain** and **GM sound** switches.
- Know how TabForge protects the editor from a crashing plug-in, and how approval works.
- Choose an audio driver and a buffer size, and understand latency.
- Add effects to a group and the master, and save a song that uses plug-ins.

## What is a plug-in?

A plug-in is a small program that adds a sound or an effect to a host program such as TabForge. There are two kinds.

- An **instrument plug-in** makes sound from notes. Examples are a synthesiser, a sampled drum kit and a modelled guitar. A track can use one in place of the GM sound.
- An **effect plug-in** changes a sound that already exists: a reverb, a delay, a distortion or an equaliser.

TabForge supports two plug-in formats. **VST2** plug-ins are 64-bit `.dll` files, and **VST3** plug-ins are `.vst3` files. You install a plug-in with its own installer, which puts it in a folder on your computer. TabForge then needs to know that folder.

> **Note:** Plug-ins are not part of TabForge, and you never need one to finish this guide.

## Safety first

A plug-in is code written by somebody else, so TabForge handles it with care. Three rules matter.

First, plug-ins never run inside the editor. They run in a separate audio-engine process. If a plug-in crashes or freezes, TabForge names it, switches it off, and playback carries on. The editor and your song stay open.

Second, a song file only names its plug-ins. It never carries them. TabForge loads a plug-in only from a place it trusts: Program Files or Common Files on a local drive, or a file you added or approved yourself. A plug-in you add through the **Add plug-in** window is approved for you. A plug-in elsewhere needs your approval, a plug-in on a network or removable drive always needs it, and a plug-in file that changes needs it again.

When a song uses plug-ins that are not approved, a bar appears above the status bar. It says the plug-ins are not loaded and shows a **Review…** button. Click it to open **Review plug-ins**. Tick only the plug-ins you trust, then click **Allow selected**, or click **Keep disabled** to leave them off.

In the FX chain window, an unapproved plug-in shows a message and an **Allow** button instead of its controls.

Third, and most important, plug-ins run with your user rights.

> **Warning:** A plug-in can do anything you can do on your computer. TabForge isolates crashes, not malicious code. Load only plug-ins from makers you trust, and approve nothing you do not recognise.

## Tell TabForge where your plug-ins are

On a fresh install, TabForge has no plug-in folders, so the list of plug-ins is empty. This is normal. You add the folders where your plug-ins are installed.

You can add folders in two places. In **Preferences > Audio & Plug-ins**, the **Plug-in folders** row lists them, and its **Browse…** button adds another. In the **Add plug-in** window, the **Add folder…** button does the same and then scans.

The **Add plug-in** window opens from the FX chain, as the next section shows. When no folder is set yet, it says so.

- **Scan the standard VST folders** is a button on a fresh profile. One click turns on the option **Also scan the standard VST folders** and scans them. Its tooltip lists the folders. Nothing is scanned until you choose this or add a folder yourself.
- **Add folder…** adds one or more folders and scans them.
- **Browse for a plug-in file…** adds a single `.vst3` or `.dll` file.
- **Rescan** reads the folders again after you install or remove a plug-in.

The list shows each plug-in's **Name**, **Vendor**, **Format** and **Role**. Role says whether it is an instrument or an effect. Type in the search box to filter the list.

> **Tip:** Add only the folders you need. A short list scans quickly and is easier to read.

## Your first effect chain

Every track has an **FX chain**: an optional instrument, followed by any number of effects, each feeding the next.

The **FX** button on each track row is a split button. Click its left half to open the chain window. Click its right half, the power switch, to play the track through its chain or switch back to its normal built-in sound. The label glows when the chain holds plug-ins.

### Try it: Open an empty FX chain

*Goal: see where plug-ins go, without needing any.*

1. Open `TabForge Demo - Ashen Meridian.gp` from the `Samples` folder.
2. Select the **Lead Gtr** track.
3. Click the left half of its **FX** button. The window **FX: Lead Gtr** opens.
4. Read the message in the middle. It says "No plug-ins yet" and tells you to use **Add…**.
5. Press `Esc` to close the window. You know the chain is empty, and nothing changed.

### Work with a chain

Click **Add…** in the chain window, or choose **FX > Add plug-in…**, to open the **Add plug-in** window. Double-click a plug-in, or select it and press `Enter`, to add it to the end of the chain. An instrument goes first.

Each plug-in is one row, marked **INST** for an instrument or **FX** for an effect.

- Untick a row to bypass that plug-in. It stays in the chain but stops changing the sound, and a red **BYPASSED** stamp covers its window.
- Drag a row up or down to change the order. The sound passes through the chain from the top.
- Double-click a row to float the plug-in's own window. Click **Show it here** to dock it again.
- Select a row and click **Remove** to take it out of the chain.

The bar on the right holds a preset list for the selected plug-in, a **+** button to save or delete your own presets, and a **Volume** knob. The knob sets that plug-in's output between -60 dB and +12 dB. It works like the knobs in **Chapter 9: Shaping the sound**: double-click it and type `-6 dB`, or hold `Ctrl` and click it to return to 0 dB. The **On** tick beside it is the same switch as the row's tick.

### Try it: Add an effect to the guitar

*Goal: hear a plug-in effect, then bypass it. This exercise is optional: you need a VST3 effect of your own.*

1. Select the **Lead Gtr** track and click the left half of its **FX** button.
2. Click **Add…**. The **Add plug-in** window opens.
3. If the list is empty, click **Add folder…** and choose the folder that holds your plug-ins.
4. Double-click an effect in the list. It appears in the chain.
5. Check that **Through chain** is ticked at the top of the window.
6. Press `Space` and listen to the guitar with the effect.
7. Untick the effect's row. The **BYPASSED** stamp appears, and the guitar sounds as it did before.
8. Tick the row again. The effect returns.

Close the window and then the document tab without saving. The demo on disk never changed.

### Save and load a chain

To reuse a chain on another track or in another song, use the **FX** menu in the chain window.

- **Save FX chain…** saves the plug-ins and their settings as a `.tfchain` file.
- **Load FX chain…** replaces the chain with a saved one.
- **Add FX chain to the end…** adds a saved chain after the plug-ins already there.
- **Clear chain** removes every plug-in.

## An instrument plug-in on a track

When a track has an instrument plug-in, a few switches decide who makes the sound. They sit in a bar at the top of the chain window.

- **Through chain** plays the track through its chain. Untick it and the track plays its normal built-in sound.
- **GM sound** also plays the track's GM instrument. With effects and no instrument plug-in, the effects shape the GM sound.
- **Auto-switch to GM sound when no VST instrument plays** ticks **GM sound** for you whenever the instrument stops playing, and unticks it when the instrument plays again. That covers switching the track's FX off, bypassing or removing the instrument (even the last plug-in), and a plug-in that crashed, is untrusted, is missing or fails to load. A tick or untick you made yourself is respected, so untick **GM sound** if you want silence instead.
- **Match pitch automatically** works out which octave an instrument plug-in sounds in and shifts it to match the written notes. Drum tracks are never shifted.

If **Through chain** is on and **GM sound** is off, and the track has no instrument plug-in, the track is silent on purpose. The **MIDI** column in the Mixer holds the same **GM sound** tick for every track.

## Groups, the master and monitoring

Effects also work on many tracks at once. In the **Mixer** (**View > Mixer / VST**), each group row has an **FX** button. Its chain processes the sum of the group's tracks before they reach the master. The power half of the button bypasses that chain.

The **Master** row at the top has its own **FX** button. Its chain processes the whole mix, so a reverb here covers everything. The window is titled **FX: Master**.

Next to it, the **MON** button opens the monitoring chain. Its effects change only what you hear while you play. They are never included when you render or export a song. A typical use is to correct for the speakers in your room. By default, one monitoring chain applies to every song.

> **Tip:** When you monitor or record through speakers, use headphones. They stop the sound from feeding back into a microphone or an instrument input.

## Audio drivers and latency

An audio driver is the way TabForge talks to your sound device. Latency is the short delay between a note and the moment you hear it. For listening to a song it hardly matters. When you play along live or record, you want it small.

Open **Preferences > Audio & Plug-ins** (`F12`), or click the audio device button in the status bar. Four drivers are on offer.

| Driver | Good for | Trade-off |
|---|---|---|
| **WASAPI (shared)** | Everyday use. The default. | Shares the device with other programs. |
| **WASAPI (exclusive)** | A shorter delay. | Takes the whole device, so other programs go silent. |
| **ASIO** | The shortest delay, with an interface that has its own ASIO driver. | Needs that driver, and takes the interface. |
| **DirectSound** | An older fallback. | A larger buffer, so more delay. |

Changing the driver resets **Output device** to **(Windows default)**. With ASIO, a **Configure…** button next to the device opens the driver's own control panel.

**Buffer size** is the amount of audio TabForge prepares ahead. A small buffer gives a quicker response but asks more of your computer. A large buffer is safer but slower to respond. If you hear crackles, raise the buffer. **Sample rate** is how many times a second the sound is sampled; leave it on the default unless a device needs another rate.

The status bar button shows the real figures for the device in use. TabForge also offers a **Safety limiter on rendered audio**, which is on by default, and a **Safety limiter on live playback**, which is off. Each stops sudden peaks from clipping.

### Try it: Pick a safe audio output

*Goal: set the output to the everyday choice and confirm it.*

1. Press `F12`. **Preferences** opens.
2. Go to the **Audio & Plug-ins** page.
3. Set **Audio driver** to **WASAPI (shared)**.
4. Set **Output device** to **(Windows default)**.
5. Close **Preferences**.
6. Press `Space` to play a song. The audio device button in the status bar shows **WASAPI (shared)** and the device name.
7. Press `Space` again to pause.

## Saving a song with plug-ins

A song that uses plug-ins, FX chains or mixer groups carries audio settings. The first time you save it as a `.gp` file, TabForge asks how to keep them in the **Save song with audio settings** window.

- A single `.gp` file with the audio settings inside. This is the recommended choice. Other programs that open the file ignore the settings, and a program that saves it again removes them.
- A clean `.gp` file with a `.tfaudio` file beside it. Keep the two together in one folder, and TabForge loads the settings when you open the `.gp`.
- A `.tforge` project. It is TabForge's own format and keeps everything, but other programs cannot open it.

A song never contains the plug-ins themselves. To open it on another computer, install the same plug-ins there and approve them.

## When a plug-in misbehaves

Most plug-ins work without trouble. When one does not, TabForge tells you.

- A plug-in that crashes is named in the status bar, and TabForge switches it off so the rest of the song keeps playing. A red **!** appears on that track's **FX** button. A track whose instrument plug-in stopped falls back to its GM sound, unless you unticked **GM sound**.
- To bring a switched-off plug-in back, select it in its FX chain window and click **Allow again**. You can also open **Preferences > Audio & Plug-ins**, open **More options** and look at **Plug-ins switched off after a crash**, which lists them all. The plug-in loads again the next time you play, and the red **!** clears. Allowing it again does not approve a plug-in that was not approved before.
- A plug-in that takes a long time to load opens **Plug-in loading slowly**. Click **Keep waiting** to wait longer, or **Disable it** to play the song without it. Disabling lasts only while that song stays open.
- A plug-in marked blocked in the FX chain window is not approved. Allow it only if you trust it.

If the sound stops, check the basics from **Chapter 9: Shaping the sound** first. For more, see **Chapter 14: Troubleshooting and FAQ**.

## More options

These are for later, and none is needed to follow this guide.

- **Wiring…** in the FX chain window opens a window that decides how a plug-in is fed: which audio channels it takes in, which track's MIDI it listens to, and where its MIDI goes next.
- **MIDI…** opens a list of MIDI processors that reshape the notes before they reach the plug-in, such as a transpose, a velocity change or a drum map.
- **Run each plug-in in its own process** (**Preferences > Audio & Plug-ins**) lets a crash stop only that plug-in. It uses more processor time and memory.
- **Auto-load for this instrument** saves a chain as the default for a kind of instrument.
- **Windows MIDI latency (ms)** and its timing check button matter only if you switch off **Play the whole song through the audio engine**. The check covers the Windows MIDI path and nothing else.

## Quick recap

- An instrument plug-in makes sound from notes. An effect plug-in changes a sound. TabForge hosts VST2 and VST3.
- Plug-ins run in a separate process. A crash is named and switched off, and the editor stays open.
- Add the folders that hold your plug-ins. TabForge loads a plug-in only if you added or approved it.
- The **FX** button opens a track's chain. Untick a row to bypass it, and drag to reorder.
- **Through chain** and **GM sound** decide who plays the track.
- The group, **Master** and **MON** rows take effects too, and **MON** is never rendered.
- Start with **WASAPI (shared)**, and raise the buffer if you hear crackles.

## What next

Go on to **Chapter 11: Arranging and recording**. It shows how to name sections, copy bars, use repeats and lay out a whole song on the timeline, using the mix you now know how to build.
