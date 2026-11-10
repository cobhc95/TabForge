# Files and export

This chapter covers everything about files: opening a song, saving it safely, bringing in sound from other files, and sending your work to other people. Saving is the most important part, so read the first two sections with care. The rest you can come back to when you need it.

## Files at a glance

TabForge works with a few kinds of file. Knowing which is which makes the menus much less confusing.

- A **`.tforge` project file** is TabForge's own file. It keeps everything in the song, including effects, mixer settings and plug-in choices. It is the safest file to use for your own work.
- A **song file for other programs** is a standard tab file that other tab programs can open. TabForge can save one, but it may leave out TabForge-only settings, so TabForge tells you first.
- **Older song file versions** are older standard versions of the same kind of file, for programs that cannot read the newer one.
- **Audio files** such as WAV, MP3, FLAC, OGG, AIFF, M4A and WMA can be brought into a song as recordings.
- **MIDI files**, ending in `.mid` or `.midi`, can be brought in as MIDI clips.
- **Export files** are copies you send to other people: MIDI, PDF, MusicXML, plain text tab, a rendered audio file and an MP4 video.

Note: The export files are one-way. TabForge can open its own files and standard song files, but it cannot open an export such as a PDF or a MIDI file back into a song.

## Open a song

1. Choose **File > Open** or press `Ctrl+O`.
2. In the window that appears, find the file you want.
3. Click **Open**.

The song opens in the current tab and replaces the song that was there. If you have unsaved changes in the current song, save them first, or the tab will be replaced without a save.

To keep the current song open and open the new one beside it, choose **File > Open in new tab** or press `Ctrl+Shift+O`.

You can also drag a song file from Windows Explorer onto the TabForge window. Each song dropped that way opens in its own new tab.

Note: Opening a file never changes it on disk. The file changes only when you save over it, so you can look at other people's songs without fear.

## Start from a template

A template is a saved starting layout, such as a set of tracks you use again and again.

1. Choose **File > New from template**.
2. Pick the template you want.
3. A new song opens from it, in a new tab.

To make your own template, set up a song the way you want it, then choose **File > Save as template**. Give it a name, and it appears in **New from template** next time.

Tip: Templates save you time when you write in the same style often. You can make one for a bass and drums setup, for example.

## Save your work

1. Choose **File > Save** or press `Ctrl+S`.
2. A new song has no file name yet, so TabForge asks for one. Choose a folder, type a name, and click **Save**.
3. After that first save, `Ctrl+S` saves straight to the same file, without asking again.

To save a copy under a different name or in a different folder, choose **File > Save As** or press `Ctrl+Shift+S`. The original file stays where it was, and the copy becomes the file you are now working on.

The `.tforge` project file is the safest choice for your own work. Use it unless you need a standard song file for another program.

Warning: Saving under the name of an existing file replaces that file. If you want to keep the old version, use **File > Save As** and choose a new name, such as one with the date in it.

Tip: Save often. Each save is quick, and it means you lose very little if something goes wrong.

## Save a song for other programs

Some songs use features that a standard song file cannot hold. Examples include effects, plug-in chains, and mixer groups. When you save a song in a standard form, TabForge checks first, and it lists anything that would be lost.

You then choose one of these options.

- Save the standard file with the TabForge settings kept inside it. This is the recommended choice. Other programs read the notes and ignore the extra part.
- Save a plain standard file, with the TabForge settings written to a separate settings file beside it. This is the most compatible choice for other programs, but you must keep the two files together.
- Save a `.tforge` project file instead, so nothing is lost.
- Cancel, and change nothing.

Warning: If another program saves over a standard song file, the TabForge-only settings can be lost, although the notes survive. Keep your own `.tforge` copy as your working file.

## Close a tab and unsaved changes

A song with changes you have not saved shows a small dot on its tab. When you close that tab, or close TabForge, a window asks whether to save first.

- **Yes** saves the song, then closes it.
- **No** closes the song and throws away the changes made since the last save.
- **Cancel** keeps the song open, and nothing is closed.

On the keyboard, `Y` means save, `N` means do not save, and `Esc` means cancel.

To close the current tab, press `Ctrl+W` or choose **File > Close tab**.

Tip: When you close TabForge with several unsaved songs open, it asks about each one in turn. **Cancel** stops the whole process and keeps everything open, so you can save the ones you care about.

## Autosave and recovery

Autosave is a safety net for crashes and power cuts. It is not a save. TabForge regularly copies each song that has unsaved changes into a recovery folder. Your own song files are never touched by autosave.

You control how often autosave runs.

1. Choose **Options > Preferences**.
2. Find the setting named **Autosave unsaved songs** under the **General** section.
3. Change how often it runs.

If TabForge closes unexpectedly, the next time you start it asks **Recover unsaved songs**.

- Choose **Yes** to open each recovered copy in its own tab. Each copy is marked as unsaved, so save it under a name straight away.
- Choose **No** and the recovered copies are thrown away.

If you are not sure, choose **Yes**. You can always close a recovered copy you do not need.

Note: A normal save or a normal exit removes a song's recovery copy, because the real file is up to date. Recovery copies only remain after a crash.

If autosave cannot write its copies, a notice bar appears with a retry option. Editing carries on as normal while the notice is shown.

## Bring in audio and MIDI

You can add sound from other files to a song.

- **Insert > Audio file** adds an audio file to the song, for example a recording of a backing track.
- **Insert > MIDI file** adds the notes of a MIDI file as a MIDI clip.
- **Insert > Empty MIDI clip** adds an empty MIDI clip that you can fill later.

You can also drag files straight from Windows onto a track.

- Drag an audio file onto a track to place it there. The audio can be WAV, MP3, FLAC, OGG, AIFF, M4A or WMA.
- Drag a MIDI file onto a track to make a MIDI clip. It plays through that track's instrument.
- A translucent block shows where the file will land. It snaps to the grid, so it lines up with the bars.
- Hold `Alt` while you drag to place the file freely, without snapping.

Tip: Dragging a file is quick, but the menu items are clearer when you are starting out. Use **Insert** first, then try dragging once you know where things go.

## Missing audio files

Audio in a song is linked to the file on disk. If you move or rename that file, the song can no longer find it.

- Choose **File > Find missing media** to look for the missing files and point TabForge at them again.
- Choose **File > Collect media** to copy the audio files used by the song into the song's own media folder. The song then carries its audio with it, so you can move the whole folder safely.

Tip: Collect media before you send a song to someone else, so the audio comes with it.

## Export a copy for sharing

An export writes a copy of the song in another form. It never changes the song you have open, and it never changes whether the song counts as saved. Open the **File** menu and look under **Export**.

- **MIDI** writes a MIDI file of the notes and tempo of the whole song, played through each track's MIDI processors. It holds no sound of its own, so the program that opens it chooses the instruments. Articulation keyswitches are not written, but their pitch and velocity effects are.
- **PDF** writes the score as pages you can print or send. It holds the track you have selected, so select the track before you export.
- **MusicXML** writes a file that other notation programs can open.
- **ASCII tab** writes plain text tab that you can paste into an email, a forum post or a chat message.
- **Video (MP4)** writes a video of the song.
- **Audio file** writes the whole mix as a sound file. This is called rendering, and it has its own section below.
- **Compatible song file** and the **older song file versions** write a standard song file for other tab programs, as described in the save section above.

Tip: Export a copy, not your original. Your working `.tforge` file stays exactly as it is.

## Render the song to audio

Rendering turns the whole mix into a sound file that you can listen to or share.

1. Choose **File > Export > Audio file**, or press `Ctrl+Alt+R`.
2. The **Render to file** window opens. By default it renders the whole song as one file of the master mix.
3. Pick a format. WAV is always available. MP3 needs an encoder that some editions of Windows do not have, so it can be missing on yours.
4. If you want each track in its own file, choose to render the tracks as stems. The **All** and **None** buttons select every track or none.
5. If the window offers a range of bars, choose it to render only part of the song.
6. Click **Render**. A progress window shows how far the render has got, and it has a cancel button.

TabForge never overwrites an existing file. If the name is already taken, it adds a number to the new file's name. The render leaves out the metronome and the count-in, so they never end up in your file.

Tip: Always listen to the whole rendered file from start to finish before you send it to anyone. Check the ending, because a render that stops early is easy to miss.

Note: Rendering runs faster than real time, so a long song can finish in less time than it lasts.

## Record a video

To record your screen and the sound together as an MP4 video, press `Ctrl+Alt+V`, or choose **File > Record video**. Press the same keys again to finish the recording. Starting or stopping a recording never starts or stops the playback.

## Print

Printing is not available yet in this version. The **Print** and **Print preview** items in the **File** menu are disabled, and their shortcuts do nothing yet.

To print a score anyway, export it as a PDF with **File > Export > PDF**, open the PDF in any PDF viewer, and print it from there.

## Score information

Before you share a song, fill in its details.

1. Choose **File > Score information**.
2. Type the title and the artist, and any other details the window offers.
3. Click **OK** to keep them.

These details are saved with the song, so they stay filled in the next time you open it.

## Common questions

Question: Is my work safe if I just close TabForge?
Answer: Only if you have saved it. A closed song with unsaved changes asks you to save first. If the program crashes, autosave copies can be recovered at the next start.

Question: What is the difference between Save and Export?
Answer: Save writes your song to its own file, and it becomes the file you are working on. Export writes a copy in another form, such as PDF or MIDI, for sharing, and your song is left as it was.

Question: Which file type should I use for my own work?
Answer: The `.tforge` project file. It keeps everything in the song. Use a standard song file only when you need another program to open it.

Question: Can I send a song to someone who does not have TabForge?
Answer: Yes. Export a PDF for them to read, a MIDI file for them to play in their own program, or a rendered audio file for them to listen to.

Question: Why is the MP3 option missing?
Answer: MP3 needs an encoder that comes with some editions of Windows. Without it, WAV is always available instead.

Question: Does TabForge print for me?
Answer: Not yet. Export a PDF, then print that file from any PDF viewer.

## Troubleshooting

Problem: My changes disappeared after I closed the program.
Fix: The changes were never saved. Reopen the song from its file. If TabForge offers **Recover unsaved songs** when it starts, choose **Yes**, then save the recovered copy under a name with `Ctrl+Shift+S`.

Problem: I pressed Save and it asked me something about things that would be lost.
Fix: Read the list of what would be lost. To keep everything, choose the option that keeps the TabForge settings inside the file, or save as a `.tforge` project file instead.

Problem: I saved over my old song and now I want the old version back.
Fix: Check whether you saved a copy elsewhere, or whether your system has a backup of the folder. TabForge replaces the file on save, so keep a copy with **File > Save As** before big changes.

Problem: The audio in my song shows as missing.
Fix: Choose **File > Find missing media** and point TabForge to where the file is now. To avoid this next time, use **File > Collect media** so the audio is copied into the song's folder.

Problem: I can't find an MP3 option when I render.
Fix: Your edition of Windows does not have the encoder MP3 needs. Choose WAV instead, which works on every edition.

Problem: My rendered file has no metronome click.
Fix: The render leaves out the metronome and the count-in by design. If you want the click in a recording, record it live, or export the audio without the click.

Problem: The rendered file is silent, or the track I wanted is missing.
Fix: Check that the track is not muted and that another track is not soloed. Then check that the render was set to include that track.

Problem: My PDF only shows one track.
Fix: A PDF holds the track that was selected when you exported it. Select the track you want in the track list, then export the PDF again.

Problem: Print does nothing when I choose it.
Fix: Printing is not available yet in this version, and the items are disabled. Export a PDF and print that file from a PDF viewer.

Problem: The song opened, but some tracks sound different from before.
Fix: The song may have been saved by another program, which can leave out TabForge-only settings such as effects and mixer groups. Open your original `.tforge` copy if you have one.

Problem: I dropped a file onto a track, but it did not appear.
Fix: Check that the file is a supported audio file, such as WAV, MP3 or FLAC, or a MIDI file ending in `.mid`. Other file types are ignored. Try again with **Insert > Audio file** or **Insert > MIDI file**.

Related: Getting started, Interface tour, Tracks and mixer
