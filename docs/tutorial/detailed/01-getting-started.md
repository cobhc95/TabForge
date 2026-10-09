---
title: Getting started and a tour of the window
id: getting-started
order: 1
keywords: install, installer, portable, download, first start, first launch, untitled, window tour, layout, panel, dock, float, side panel, status bar, toolbar, command palette, find a command, reset layout, workspace, lost my panel
summary: Install TabForge, start it for the first time, learn the name of every part of the window and find any command quickly.
est-minutes: 25
---

# Getting started and a tour of the window

TabForge keeps everything in one window. Later chapters say things like "click **Loop**" or "look at the status bar", so it helps to know where each part lives and what it is called. This chapter gets TabForge running and walks you around the window once.

By the end you can name every region, show and hide panels, switch the whole layout with one key, and find any command without hunting through menus.

## What you will learn

- Install TabForge, or run it from a folder, and start it.
- Name the parts of the main window.
- Show, hide, move and reset panels.
- Switch between the **Compose**, **Practice** and **Mix** layouts.
- Find any command with the command palette, tooltips and **Preferences**.

## Before you install

TabForge runs on Windows 10 and Windows 11, 64-bit. You do not need to install any extra software, and you do not need a sound card driver or plug-ins to hear music: TabForge plays through the sound that comes with Windows.

The download is not signed with a publisher certificate. On the first run, Windows may therefore show a warning window that stops the program and says it does not recognise the publisher. The window has a link for more details. Choose it, and a button appears that lets you run the program anyway. Do this only for a copy you downloaded from the official TabForge download page.

## Install TabForge

There are two ways to get it. Pick one.

- **The installer** is a single setup file. It adds a Start menu entry, offers an optional desktop icon, and offers to make TabForge the program that opens your `.gp`, `.gp5` and `.tforge` song files. It does not need administrator rights.
- **The portable zip** needs no setup. Extract it to any folder, then run `TabForge.exe` from inside.

In both cases the demo song sits in a `Samples` folder beside `TabForge.exe`. It is called `TabForge Demo - Ashen Meridian.gp`, and this guide calls it Ashen Meridian. You will open it in a moment.

## Start TabForge for the first time

Start TabForge from the Start menu, or by running `TabForge.exe`. There is no welcome screen. You land in an empty song called **Untitled**, with one **Guitar** track and 32 empty bars, in the dark theme.

To get something to look at, open the demo song. Choose **File > Open…** (`Ctrl+O`), go to the `Samples` folder beside `TabForge.exe`, choose `TabForge Demo - Ashen Meridian.gp` and click **Open**. **Chapter 2: Opening and playing a song** explains opening in full. The tour below assumes Ashen Meridian is open.

## The top of the window

The window has nine regions. They are numbered, and the rest of this section follows the numbers.

**1. Title bar and document tabs.** Each open song lives in its own *document tab* in the title bar. Click a tab to switch songs. The **+** button at the right of the tabs starts a new blank song in a new tab, and the gear at the far right opens **Preferences**. A song with unsaved changes shows a dot on its tab.

**2. Menu bar.** Twelve menus run along the left: **File**, **Edit**, **Bar**, **Track**, **Note**, **Effects**, **Sections**, **Tools**, **Sound**, **View**, **Options** and **Help**. Open any of them to see what the program can do.

**3. Toolbar.** The toolbar items sit at the right end of the menu bar. The **BPM** box holds the song's written tempo. Next to it are two read-outs, the time signature and the key. Then come three buttons: one shows or hides the side panel, one shows or hides the fretboard, and **Project settings** opens the song's information, credits, tempo and key.

## The score, panels and status bar

**4. Fretboard.** The panel across the top of the work area draws the selected track as a fretboard. For a drum track it becomes a drum map, and for keys it becomes a keyboard. When a song plays, dots show which notes are sounding. A small legend at the bottom right explains the colours: **now**, **next**, **upcoming** and **recent**.

**5. Score.** The large area in the middle shows the song as tablature (TAB) and standard notation. This is where you read and write music.

**6. Side panel.** The panel on the right is two stacks of pages, each with tabs. The upper stack is the tool palette: **Tools**, **Structure**, **Rhythm** and **Layout**. The lower stack holds **Sections**, and the zoom and speed boxes sit in the top toolbar.

**7. Timeline and track list.** The strip at the bottom shows the whole song at a glance: one row per track and a coloured lane of sections across the top. The track list on its left names each track and carries its buttons. You can open the timeline from **View > Arrangement overview**.

**8. Transport.** The transport buttons are at the left of the timeline's header, from left to right: **Rewind to beginning**, **Stop**, **Record**, **Play / pause**, **Next section**, **Count-in**, **Metronome** and **Loop**. If you lose them, **View > Reset all panels to default positions** brings them back.

**9. Status bar.** The bar along the bottom reports what is happening, from left to right.

- The last action, such as "Layout: Practice".
- The **MIDI** light, grey when idle and green while a song plays.
- The position: the bar number, the track name and the cell.
- The bar state, such as "4/4 · 16:16": the time signature, then the length written in the bar and the length the time signature expects. It turns red when a bar does not add up.
- The tempo, key and current note value.
- A scroll bar for the timeline.
- At the right, the audio device or the MIDI output name.

Here is the list as a table, with the chapter that teaches each region.

| Region | Its job | Where to learn more |
|---|---|---|
| Title bar and document tabs | Switch between open songs | **Chapter 2: Opening and playing a song** |
| Toolbar | Tempo, time signature, key, panel buttons | **Chapter 5: Writing your first riff** |
| Fretboard | Show and enter notes on the instrument | **Chapter 4: Reading tab and notation** |
| Score | Read and write TAB and notation | **Chapter 4: Reading tab and notation** |
| Side panel | Tools and sections | **Chapter 3: Practice tools** |
| Timeline and track list | See and arrange the whole song | **Chapter 11: Arranging and recording** |
| Transport | Play, loop, count in | **Chapter 2: Opening and playing a song** |

## Get around the score

Click a beat in the score to put the edit cursor there. Clicking only moves the cursor; it never adds a note. Roll the mouse wheel to scroll the score. Hold `Ctrl` and roll the wheel to zoom it in or out.

The zoom box lives in the top toolbar, left of the tempo box. Its presets run from 50% to 200%, and **Fit width** is the first entry and the default. You can also type a value from 50 to 200, or use `Ctrl+Plus` and `Ctrl+Minus`.

> **Note:** The mouse wheel over the timeline zooms the timeline. It does not scroll it. To move along the timeline, drag the scroll bar in the middle of the status bar.

## Show, hide and move panels

A panel is any block of the window you can rearrange. The two toolbar buttons at the right end of the menu bar show and hide the side panel and the fretboard. **View > Panels** lists the panels, **Tools**, **Structure**, **Rhythm**, **Layout**, **Sections**, **Fretboard** and **Arrangement**, each with a tick you can switch.

To move a panel, drag it by its tab. Where you let go decides what happens, and a blue highlight shows the result before you release the mouse button.

- Drop on the middle of another panel, and the two share a tab strip.
- Drop near an edge, and the panel splits off beside, above or below.
- Drop outside the window, and the panel floats as a window of its own.

In the default layout the **Fretboard** and **Arrangement** panels sit alone in their areas and have no tab of their own, so practise dragging with a side panel tab such as **Sections** or **Tools**. Right-click any panel tab for **Reset this panel to default position**, **Close panel** and **Reset all panels to default positions**.

> **Tip:** If a panel goes missing, **View > Reset all panels to default positions** brings every panel back. It never touches your song.

## Workspace layouts

Moving panels one by one is slow when you change tasks. A layout is a saved arrangement of panels, and TabForge has three. Switch with **View > Layouts**, or press `Ctrl+1`, `Ctrl+2` or `Ctrl+3` on the number row.

- **Compose** (`Ctrl+1`) gives the score most of the room, with the fretboard and the tool palette beside it and a small timeline. It suits writing.
- **Practice** (`Ctrl+2`) keeps the score and fretboard large, with **Sections** at the right. The timeline is closed to make room. It suits learning a part.
- **Mix** (`Ctrl+3`) makes the timeline large and the score small. It suits balancing tracks.

Switching layouts moves panels only. Your song is never changed. The status bar confirms the switch, for example "Layout: Practice". **Chapter 13: Make TabForge yours** shows how to save a layout of your own.

### Try it: Rearrange your workspace

*Goal: hide a panel, switch layouts, move a panel and put everything back.*

1. Open Ashen Meridian with **File > Open…** (`Ctrl+O`). The song opens and its title appears on the document tab.
2. Click the side-panel button at the right end of the menu bar. The side panel disappears and the score becomes wider.
3. Click the same button again. The side panel returns.
4. Press `Ctrl+2`. The window switches to the Practice layout, and the timeline closes.
5. Press `Ctrl+1`. The window switches to the Compose layout.
6. Press `Ctrl+2` again, then drag the **Sections** tab to the left edge of the score. A blue highlight shows where it will land.
7. Release the mouse button. **Sections** now sits as its own panel beside the score.
8. Choose **View > Reset all panels to default positions**. Every panel returns to its default place, and the timeline is back with the transport buttons at its left.

You know it worked when the window looks like the window described in the tour above, with every panel in its place, and the song is still open and unchanged.

**Stuck?** If a panel is still missing, repeat step 8. It always works.

## Find a command

TabForge has far more commands than buttons, and there are four ways to find one.

- **Menus.** Every command lives in a menu. Menus print a key at the right of many items, read from your current key bindings, so a rebind shows at once.
- **Tooltips.** Hover a button and read its name. A long tooltip wraps onto several lines. Any button that runs a command shows its current shortcut in brackets at the end, for example "Play / pause (Space)". If you change a shortcut later, the tooltip shows the new key at once.
- **The command palette.** Choose **File > Command palette…** (`Ctrl+Shift+A`). Type a few letters of a command's name, and the list narrows. Each row shows the command, with its key at the right. Press `Enter` to run the highlighted row, or `Esc` to close the palette. Every command appears here, including some that have no menu item.
- **Preferences.** **Options > Preferences…** (`F12`) lists every keyboard shortcut on its **Shortcuts** page.

To open a right-click menu from the keyboard, press `Shift+F10` or the `Menu` key.

> **Tip:** Hover a button before you look anything up. The tooltip shows the key you are actually using, even if you have changed it.

### Try it: Find a command without the menu

*Goal: use the command palette to find the key for Loop and run it.*

1. Press `Ctrl+Shift+A`. A small window titled **Command palette** opens.
2. Type `loop`. The list narrows to commands that match.
3. Find the row **Transport: Loop**. Its key, `F9`, shows at the right.
4. Press `Enter` with that row highlighted. The palette closes and the **Loop** button lights up.
5. Press `F9`. The **Loop** button goes dark again.

You know it worked when the **Loop** button lit up after step 4 and went dark after step 5.

### The Help menu

The **Help** menu holds **Tutorial…**, which opens the tutorial window, which starts with the Basic Guide, **Keyboard shortcuts…**, **Check for updates…** and **About**. **Keyboard shortcuts…** shows a short reminder of the main keys. For the full list, use **Preferences > Shortcuts**. **Check for updates…** asks whether a newer version exists and can open its download page. TabForge never downloads or installs anything by itself. For what to expect from this version, see **Chapter 0: Welcome to TabForge**.

## Quick recap

- The window has nine regions: title bar, menu bar, toolbar, fretboard, score, side panel, timeline, transport and status bar.
- Each song lives in a document tab, and the gear opens **Preferences**.
- Drag a panel by its tab to dock, split or float it, and use **View > Reset all panels to default positions** to undo any mess.
- `Ctrl+1`, `Ctrl+2` and `Ctrl+3` switch the Compose, Practice and Mix layouts.
- `Ctrl+Shift+A` opens the command palette, and a tooltip shows each button's current key.

## What next

Go on to **Chapter 2: Opening and playing a song**. You will open Ashen Meridian properly, press play and watch the notes follow along.
