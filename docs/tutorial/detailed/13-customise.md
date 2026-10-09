---
title: Make TabForge yours
id: customise
order: 13
keywords: preferences, settings, options, theme, dark mode, light mode, colours, accent, interface scale, font, shortcuts, hotkeys, rebind, preset, layout, customise, personalise, defaults, reset, export settings
summary: Open Preferences, switch themes, set score defaults, change shortcuts, save your own layouts and back up or reset your settings safely.
est-minutes: 30
---

# Make TabForge yours

TabForge works well out of the box, but everyone likes things a little different. You might want a lighter window, larger text, a key that suits your hand or a layout built around the way you work.

This chapter shows where those choices live, how to try them without risk and how to put everything back. Every exercise ends by restoring the setting, so you can experiment freely.

## What you will learn

- Open **Preferences**, search it and understand its pages.
- Switch themes, change the interface scale and pick an accent colour.
- Set defaults for the score, the fretboard and the timeline.
- Change keyboard shortcuts and meet the shortcut presets.
- Save and manage your own workspace layouts.
- Back up your settings and reset them safely.

## Preferences in two minutes

**Preferences** holds every setting that belongs to the program. Open it with **Options > Preferences…** (`F12`), or click the gear at the right of the title bar. The window is called **TabForge Settings**, and it remembers the page you used last.

The page list on the left has 14 pages. **Common settings** comes first and gathers the choices people change first: the theme, the interface scale, the sound output, the metronome, the playback speed and how the score scrolls. Below it are four captioned groups.

- **BASICS:** **General** and **Appearance**.
- **MUSIC:** **Score & Notation**, **Fretboard & Keyboard**, **Timeline & Tracks** and **Editing**.
- **SOUND:** **Playback & Practice**, **Audio & Plug-ins** and **Recording**.
- **SYSTEM:** **Tabs & Windows**, **Shortcuts**, **Files & Backups** and **Advanced**.

Each page shows its everyday settings first. The rarer ones sit behind a **More options** button at the foot of the group, which shows how many are hidden. A search or a link from elsewhere in the program opens the right group for you.

Changes show in the program as you make them. The footer tells you that you are previewing and that nothing is saved yet. **Apply** keeps the changes and leaves the window open. **OK** keeps them and closes it. **Cancel**, or `Esc`, throws them away. If you have unapplied changes, TabForge asks before it discards them.

> **Note:** Preferences are for the program. A song's own settings live elsewhere: **Project settings** at the right end of the toolbar, and **Track > Properties…** for a track.

## Search the settings

You do not need to know which page holds a setting. Click the box at the top of the window and type a word. Every word you type has to match, and TabForge looks in names, descriptions, page names and everyday synonyms. Typing `dark` finds **Theme**, and typing `latency` finds the audio buffer.

Each result is a working row, so you can change it right there. Click the result's title to jump to its page, and the row flashes so you can spot it. Keyboard shortcuts appear in the results too. The **All categories** list beside the box narrows a search to one page. Press `Esc` once to clear the search, and again to cancel the window.

> **Tip:** Use the search box before you browse. It is faster than hunting through the pages.

## Themes and colours

Open **Appearance**. The **Theme** card has one list with four choices.

- **Dark** is the default.
- **Light** is a bright window with a light score page.
- **System** follows the light or dark choice you made in Windows.
- **Custom** keeps whatever colours you set yourself.

Choosing **Dark**, **Light** or **System** applies a whole palette at once: the interface, the accent colour and the score page. You can still change any colour afterwards. The **Custom palette** colours near the bottom of the page only take effect when **Theme** is **Custom**. The **Reset theme…** button in the **Theme** card puts the built-in palette, sizes, text and motion back, after asking you first.

Under **Size and text** you find **UI scale**, from 0.8x to 1.5x, and **Spacing**, which packs the toolbar and buttons **Compact**, **Comfortable** or **Spacious**. Larger scales help on a high-resolution screen. **More options** adds the interface font, its size and the icon size. **Accent colour**, in **Interface colours**, sets the colour of focus outlines and selections.

### Try it: Go light

*Goal: preview the Light theme and the System theme, then undo both.*

1. Press `F12`. **Preferences** opens.
2. Click **Appearance** in the page list.
3. In the **Theme** card, choose **Light** from the **Theme** list. The whole window turns light at once, and the footer says you are previewing.
4. Choose **System**. The window follows your Windows light or dark choice.
5. Optional: change **UI scale** to `1.25x`, using the box's arrows or by typing. The window grows at once.
6. Click **Cancel**. If TabForge asks about discarding changes, confirm. The window returns to how it looked before you opened **Preferences**.

You know it worked when the window is back to its original theme and size.

## Score, fretboard and timeline looks

Three pages control how the main work areas look.

**Score & Notation** sets what the score shows and how it is laid out. **Default score display** chooses between **Tablature + standard**, **Tablature only** and **Standard notation only**. **Score page layout** and **Score scrolling** pick continuous or page layout, and vertical or horizontal scrolling. **Tablature spacing** opens the notes out for readability. You can show or hide bar numbers, section headings and dynamics, and change the score's font and text size. The staff lines and the short ledger lines above and below the staff are drawn as one unit, so a single opacity setting makes both lighter or stronger. The **Text & fonts…** button opens a **Score text & fonts** window, where each kind of score text has its own style.

**Fretboard & Keyboard** has an **Appearance** group, behind **More options**. **Scale highlight style** chooses shaded cells, small circles or rings. **Scale highlight colour** picks the colour. **Scale highlight strength** runs from 10% to 150%, where 100% is the standard look. Lower is dimmer and higher is brighter, and the root note always stays stronger than the other notes. The group also holds the fret marker colour and brightness, and the fret number size. A fresh installation starts with a shaded blue scale highlight, white fret dots at their original brightness, large fret numbers and natural string spacing; a choice you saved earlier is kept. The commands **Scale highlight brighter** and **Scale highlight dimmer** move the strength in steps of 10% and have no default keys.

**Timeline & Tracks** has a **Timeline display** group. Behind its **More options**, **Playback position marker** sets how the timeline shows where the song is playing: **Line** (the default), **Bar marker** or **Both**. The command **Cycle playback position marker** steps through the three, and has no default key.

The **Editing** page holds how notes are entered, such as the **Default note value** and whether the cursor moves on after a note. **Chapter 5: Writing your first riff** explains these.

## Change keyboard shortcuts

Every command in TabForge can have a key. Open **Shortcuts** to see them all, grouped by what they do. TabForge starts with its default preset, and the keys in this guide are from that preset.

The **Commands** card at the top has a **Preset** list. It offers the default preset and two alternatives that follow other common layouts. After you change any single key, the list reads **Custom**. **Reset category** restores the default keys for the commands shown.

> **Note:** Choosing a different preset replaces every key you have changed. Pick the default preset again to get the original keys back.

To change one key, click the key button on that command's row, then press the new combination. `Esc` cancels. If another command already uses the key, the row says "Already used by" and names that command, and a **Reassign** button appears. **Reassign** moves the key to this command and leaves the other one without it. A changed row carries a **CUSTOM** tag. **Clear** removes a key, and **Reset** restores that row's default.

After you rebind a key, the menus, the tooltips and the command palette (`Ctrl+Shift+A`) show the new key at once. A command with no key shows none. **Chapter 1: Getting started and a tour of the window** shows both.

### Try it: Make a shortcut your own

*Goal: change the key for Loop, see a conflict warning, then restore the original key.*

1. Press `F12`, then click the search box and type `loop`. A **Transport commands** card lists **Loop** with its key, `F9`.
2. Click the `F9` button on the **Loop** row. The row asks you to press a key combination.
3. Press `Space`. The row says another command already uses it and shows **Reassign**. Do not click **Reassign**.
4. Click the key button again and press `Ctrl+Shift+L`. The row shows the new key and a **CUSTOM** tag.
5. Click **Reset** on the **Loop** row. The key returns to `F9` and the tag disappears.
6. Click **Cancel**.

You know it worked when the conflict message appeared in step 3 and the original key came back in step 5.

**Stuck?** If a key does nothing, hover the button first. The tooltip shows the key it really uses.

## Save your own layouts

**Chapter 1: Getting started and a tour of the window** introduced the **Compose**, **Practice** and **Mix** layouts. You can add layouts of your own. A saved layout remembers where every panel sits and the size of the window. It never contains song data.

- **View > Layouts > Save current layout as…** asks for a name, up to 40 characters, and saves what you see now. You can keep up to 24.
- Choosing your layout's name in **View > Layouts** switches to it.
- **View > Layouts > Delete layout** lists your layouts, and choosing one removes it.
- **View > Layouts > Reset built-in layouts** brings back the original **Compose**, **Practice** and **Mix**.

A floating panel is a window of its own, so you can drag it onto a second screen.

> **Tip:** Saving under the name **Compose**, **Practice** or **Mix** replaces that built-in layout. To get the original back, choose **Delete layout** and then the name followed by "(back to built-in)".

### Try it: Save a layout

*Goal: save a layout, switch away and back, then delete it.*

1. Press `Ctrl+2` to switch to the Practice layout.
2. Choose **View > Panels > Sections**. The tick clears, and the panel closes.
3. Choose **View > Layouts > Save current layout as…**. A **Save layout** prompt opens.
4. Type `Study` in the name box and click **OK**. The status bar says the layout is saved.
5. Press `Ctrl+1` to switch to the Compose layout.
6. Choose **View > Layouts > Study**. The window returns to your arrangement, without the **Sections** panel.
7. Choose **View > Layouts > Delete layout > Study** to remove it.
8. Choose **View > Reset all panels to default positions**.

You know it worked when step 6 brought back your arrangement and the **Study** entry is gone after step 7.

## Tabs and windows

The **Tabs & Windows** page controls the document tabs. In **Opening and closing tabs**, **Open projects in the current tab** is on by default, so **File > Open…** replaces the current song. Untick it, and **Open…** always uses a new tab. **New tab position** puts a new tab next to the current one or at the end. **When the last tab is closed** opens a fresh blank tab or closes the window. **Show playing badge** marks a tab whose song is playing.

Behind **More options** you can choose whether a double-click closes a tab, what a middle-click on a tab does, and whether tabs can be dragged out to a new window or onto another TabForge window. To choose whether songs opened from Windows Explorer arrive in a new tab or a new window, use **Files & Backups > Opening and saving > Open songs from Explorer in**.

## Back up and reset

Before a big change, save a copy of your settings. Click **Manage settings** at the bottom left of **Preferences**. Its menu has four items.

- **Export settings…** writes your settings to a file.
- **Import settings…** loads a settings file that TabForge has checked.
- **Reset current page…** restores the defaults on the page you are viewing.
- **Reset all settings…** restores every setting and every shortcut.

Most pages also have a **Reset page…** button at the top, and the **Theme** card has **Reset theme…**. Every reset asks before it acts, and **Cancel** still undoes it until you press **OK** or **Apply**. The **Advanced** page shows the version and where the settings file is kept.

> **Warning:** **Reset all settings…** returns every setting and every shortcut to its default. Export your settings first, so you can bring them back with **Import settings…**.

## Quick recap

- **Options > Preferences…** (`F12`) opens the settings, and the search box finds any setting or shortcut.
- Changes preview live. **Cancel** undoes them, and **OK** or **Apply** keeps them.
- **Appearance > Theme** switches between **Dark**, **Light**, **System** and **Custom**, and **UI scale** resizes the window.
- **Shortcuts** lets you rebind any key, with a warning if the key is taken.
- **View > Layouts > Save current layout as…** saves your panel arrangement.
- **Manage settings** exports, imports and resets your settings.

## What next

Go on to **Chapter 14: Troubleshooting and FAQ**. It lists common problems in the words people use for them, such as "there is no sound", with the fix for each.
