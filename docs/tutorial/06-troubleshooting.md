---
title: Troubleshooting and help
id: troubleshooting
order: 6
keywords: troubleshooting, help, no sound, silent, cannot hear, song will not open, red bar, shortcut, shortcuts, keys, command palette, preferences, search, problem, fix, crash
summary: Fix no sound, a song that will not open and red bars, and find any command or shortcut.
---

# Troubleshooting and help

Most problems have a small cause and a quick fix. This chapter lists the common ones in the words people use, such as "there is no sound". It also shows how to find any command or shortcut for yourself, so that you are never stuck.

## What you will learn

- Work through a no-sound checklist.
- Deal with a song that will not open.
- Fix a red bar.
- Find a command, a setting or a shortcut.

> **Tip:** Ask first whether the problem happens in a new song. If it does not, the cause is that one song, and not your set-up.

## There is no sound

Almost every case of silence is one of these. Check them in order.

1. Look at **M** and **S** on the track list. Switch off any mute, and any solo you did not mean to set, because a soloed track silences all the others.
2. Check the master volume and the volume of the track. Neither may be at zero.
3. Look at the right end of the status bar. It names the audio device in use. Click it to open **Preferences** on its audio page.
4. In **Audio driver**, choose `WASAPI (shared)`, and in **Output device**, choose `(Windows default)`. These work on most computers and let other programs keep playing sound.
5. Check the Windows volume, and which output device Windows is using.
6. Open the track's **FX** chain. If the track plays through the chain but has no instrument in it, tick **GM sound**.

> **Warning:** Sudden loud sound can hurt your ears. Turn the Windows volume down before you press play, then raise it slowly.

If one track alone is silent, check its **M** button first, and then whether another track is soloed.

## A song will not open

TabForge opens `.tforge`, `.gp`, `.gpx`, `.gp5`, `.gp4` and `.gp3` files. MIDI, MusicXML, PDF and text files are formats you export to, and TabForge cannot open them. Use **File > Open…** and choose one of the supported types.

If a supported file still fails, copy it to another folder and try again. A message tells you when a file is damaged or too large.

If you opened a song and your other song disappeared, **File > Open…** replaces the song in the current tab. Use **File > Open in new tab…** to keep both.

## A bar is red

A red bar does not add up. Its notes and rests are longer or shorter than its time signature allows. It is a warning, and the song still plays.

1. Choose **Tools > Check bar duration**. A message lists every bar that does not add up.
2. Click the bar and compare its notes with the time signature.
3. Change a note length, or add or remove a rest, until the red goes away.

**Chapter 3: Reading and writing tab** explains note lengths.

## I lost my work

After a crash, TabForge offers to recover your unsaved songs the next time you start it. Choose **Yes**, and each song opens in its own tab. Then use **File > Save As…** to give each one a name. For a mistake in a song that is still open, press `Ctrl+Z` to undo it. **Chapter 5: Saving and sharing** has the details.

## A plug-in shows a warning

A mark on the **FX** button means a plug-in crashed and was switched off. The rest of the song keeps playing. Open the chain and look at which plug-in it was. If you trust it, click **Allow again**. If it keeps crashing, leave it out.

## Find a shortcut, a command or a setting

You do not need to remember keys. TabForge shows them where you need them.

- Hover any button or control. The tooltip names it and shows its current shortcut at the end.
- Open **Options > Preferences…** and choose the **Shortcuts** page. It lists every command with its key, and it is where you change a key or restore the defaults.
- Choose **Help > Keyboard shortcuts…** for a quick list.
- Choose **File > Command palette…**, then type part of a command's name. The list narrows as you type, and each row shows the command's key.
- To find a setting, type a word in the search box at the top of **Preferences**.

If a shortcut does nothing, click an empty part of the score first, so that the key does not go into a text box. Then hover the command to check which key it uses now. You may have changed it, or a different preset may be active.

> **Note:** The keys printed in this guide are the defaults. A key that you change is the one that works, and its tooltip shows it.

### Try it: Find a key without looking it up

*Goal: use the command palette and a tooltip to find the shortcut for Loop.*

1. Choose **File > Command palette…**. A small window opens.
2. Type `loop`. The list narrows to matching commands.
3. Read the key at the right of the **Loop** row, then press `Esc` to close the palette.
4. Hover the **Loop** button on the transport. Its tooltip shows the same key.
5. Open **Options > Preferences…** and choose **Shortcuts**. Find the same command in the list, then click **Cancel**.

You know it worked when all three places showed the same key.

## Quick recap

- No sound is usually a mute, a solo, a volume at zero or the wrong output device.
- TabForge opens song files, but not MIDI, MusicXML or PDF files.
- A red bar does not add up. **Tools > Check bar duration** lists it.
- After a crash, choose **Yes** to recover your songs.
- Hover a control, or look in **Preferences > Shortcuts**, to find any shortcut.

## What next

You have reached the end of the guide. The menus are the best map of everything else TabForge can do, so explore them with the demo song open. You can return here at any time from **Help > Tutorial…**.
