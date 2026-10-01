---
title: Saving and sharing
id: saving-and-sharing
order: 5
keywords: save, save as, tforge, gp, autosave, recovery, recover, crash, export, midi, pdf, musicxml, ascii tab, render, audio, wav, mp3, share, print
summary: Save a song as a .tforge or .gp file, recover work after a crash, and export MIDI, PDF or audio.
---

# Saving and sharing

A song is safe only once it is saved, and it is useful to other people only once it leaves TabForge in a form they can use. This chapter covers both: saving without fear, getting work back after a crash and sending a song out.

## What you will learn

- Save a song and choose between the two file types.
- Understand autosave and recover work after a crash.
- Export a song as MIDI or a PDF.
- Render a song to an audio file.

## Save a song

Choose **File > Save** (`Ctrl+S`). Choose **File > Save As…** to save under a new name or in a new folder. A new song has no file name yet, so **Save** asks you for one. A song you opened from another file can ask too, because an opened song is a copy in memory.

TabForge saves two kinds of file.

- A `.tforge` file is TabForge's own project. It keeps everything in the song.
- A `.gp` file is a standard song file with the TabForge settings stored inside it. Other programs read the notes and ignore the extra part.

If another program saves a `.gp` file again, the TabForge-only settings, such as plug-in chains, can be lost, although the notes survive. Keep your own copy as your working file.

While a song has changes that are not saved, its document tab shows a dot. If you close it, a window asks whether to save first, with **Yes**, **No** and **Cancel**.

> **Warning:** Saving under the name of an existing file replaces that file. To keep the original, use **File > Save As…** and choose a new name.

## Autosave and recovery

Autosave is a safety net for crashes and power cuts. It is not a save. At a set interval, TabForge copies every open song with unsaved changes into a recovery folder. It never touches your own song files.

If TabForge ends unexpectedly, the next start asks **Recover unsaved songs**. Choose **Yes**, and each copy opens in its own document tab, marked as unsaved. Then save each one under a name. Choosing **No** deletes the copies, so choose **Yes** if you are unsure.

To change how often autosave runs, open **Options > Preferences…**, type `autosave` in the search box and use the **Autosave unsaved songs** row. A normal save or a normal exit removes a song's recovery copy.

## Export a song

An export writes a copy of the song in another form, for sharing. It never changes the open song, and it never changes whether the song counts as saved. Open the **File** menu to see the choices.

- **Export MIDI…** writes a `.mid` file with the notes and tempo of the whole song. It holds no sound, so the program that opens it chooses the instruments.
- **Export PDF…** writes the score of the selected track as pages you can print or send. Select the track you want first.
- **Export MusicXML…** writes a file that other notation programs can open.
- **Export ASCII tab…** writes a plain text tab that you can paste into a message.

Exports are for sharing, not for editing back in. TabForge opens `.tforge`, `.gp`, `.gpx`, `.gp5`, `.gp4` and `.gp3` files, but not the export types.

To print a song, export a PDF and print it from a PDF viewer.

## Render to audio

Rendering turns the whole mix into a sound file. Choose **File > Render to audio file…**. The **Render to file** window opens. The defaults render the whole song as one file of the master mix. You can also choose a different format, such as WAV or MP3, a range of bars, or separate files for each track. Click **Render**, and a progress bar shows how far it has got.

TabForge never overwrites an existing file. It adds a number to the new file's name instead. The render leaves out the metronome and the count-in.

### Try it: Export a PDF and a MIDI file

*Goal: share a track of the demo song in two forms.*

1. Open the demo song and click the **Lead Gtr** track in the track list.
2. Choose **File > Export PDF…**, type a name and click **Save**. The status bar reports the export.
3. Open the PDF in any PDF viewer. You see the lead guitar part.
4. Back in TabForge, choose **File > Export MIDI…**, type a name and click **Save**.

You know it worked when both files are in the folder you chose. Close the tab without saving.

## Before you share

- Save a working copy first, as a `.tforge` or `.gp` file.
- Fill in the title and artist with **File > Score information…**.
- Choose the export that suits the other person: PDF to read, MIDI to play in their own program, audio to listen.
- Listen to a rendered file all the way through before you send it.

## Quick recap

- **File > Save** (`Ctrl+S`) saves, and **File > Save As…** saves under a new name.
- `.tforge` and `.gp` are the two types TabForge saves.
- Autosave copies unsaved songs, and the next start offers them back after a crash.
- **File > Export…** commands make copies for sharing: MIDI, PDF, MusicXML and text tab.
- **File > Render to audio file…** makes an audio file of the mix.

## What next

Go on to **Chapter 6: Troubleshooting and help**. It lists the common problems and how to find any command or shortcut.
