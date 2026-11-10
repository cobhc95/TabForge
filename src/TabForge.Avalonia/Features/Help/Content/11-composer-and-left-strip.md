# Composer and left strip

The left strip is a slim column of icons that gives you quick buttons for the things you use most: opening and saving songs, undo and redo, rendering audio, and about forty other commands you can choose yourself. The Composer is a panel that opens beside the strip. It is an early preview, and this chapter describes only what it shows today.

If you have never seen the strip, you may not even notice it. It may sit at the left edge of the window, or beside the menu bar. Read the first section to find it, then the sections after to show, hide, move and customise it.

## What this chapter covers

- What the left strip is, and where it sits.
- What each default icon does.
- How to show and hide the strip, using the View menu and the toggle at the left of the menu bar.
- How to move the strip to the left, the right, or beside the menus.
- The right-click menu of the strip.
- The Customize strip window, for adding, removing and reordering more than forty icons.
- The Composer panel, a preview of a riff library that is still being built.

## Finding the left strip

The strip normally sits as a vertical column at the left edge of the window, beside the score. You can also choose to place it in a single row beside the menus at the top.

If you cannot see it at all, work through these checks:

1. Look at the left end of the menu bar. A small toggle icon there shows or hides the strip. If that icon is missing, the option **View > Show strip button** is off.
2. Open the **View** menu and look at **Left strip**. The row is ticked when the strip is showing. Click it to show the strip.
3. If the strip is hidden each time you start TabForge, open the **View** menu and check **Show strip on start**. It is ticked by default, which means the strip shows when the program starts.

Tip: The keyboard command View.ToggleLeftStrip shows or hides the strip. It has no default key. You can give it a key in Options > Preferences > Shortcuts if you use it often.

## The default icons

When TabForge starts with the default layout, the strip shows these icons from top to bottom. A thin line separates the groups.

- **New** creates a new score in a new tab. It is the same as File > New.
- **Open** opens a song in the current tab. It is the same as File > Open.
- **Save** saves the active song.
- **Save as** saves the active song under a new name.
- **Render** exports the song to an audio file. It is the same as File > Export > Audio file. It can write WAV or MP3 files.
- A line separates the file group from the editing group.
- **Undo** reverses your last edit.
- **Redo** puts back the last edit you undid.
- A second line separates the editing group from the Composer toggle.
- **Composer** shows or hides the Composer panel. It is described below.

Step by step to use the icons:

1. Hover over an icon to read its tooltip. The tooltip names the command and, if it has one, the key that does the same thing.
2. Click the icon once. The command runs at once.
3. To undo a mistake, click **Undo**, or press `Ctrl+Z`.

Note: The icons run the same commands as the menus and the keyboard. Whatever key you change in Options > Preferences > Shortcuts also applies to the matching icon.

## Showing and hiding the strip

There are three ways to hide the strip, and each leaves the others alone.

- **View > Left strip** turns the whole strip on or off. Use this when you want more room for the score.
- The toggle icon at the left end of the menu bar shows or hides the strip, if it is turned on in **View > Show strip button**.
- Right-click the strip itself and choose **Hide strip**.

To show the strip again, use the same menu row, or the toggle icon at the menu bar.

Note: Hiding the strip may also hide the Composer panel that sits beside it. If the panel is still open after you hide the strip, close it with View > Composer.

Note: **View > Show strip on start** only decides what happens when TabForge starts. It does not hide the strip while you are working.

Step by step to hide the strip for a distraction-free view:

1. Open the **View** menu.
2. Click **Left strip** so the tick is removed. The strip disappears at once.
3. Click **Left strip** again to bring it back.

## Moving the strip

The strip can sit in three positions: as a vertical column on the left, as a vertical column on the right, or in a single row beside the menus.

To change the position quickly:

1. Right-click the strip.
2. Choose **Place beside menus** to put the icons in a row beside the menu bar.
3. Choose **Keep vertical** to return to a column. Only one of these two rows is ticked at any time.

To choose the position more precisely, use the Customize strip window, described below. It has three choices: Left vertical, Beside menus and Right vertical.

Tip: The beside-the-menus layout is good on a wide screen with a short window. The vertical layout is better when you want many icons visible at once.

## The right-click menu of the strip

Right-click any icon, or the empty strip, to open its menu. It has four items:

- **Place beside menus** and **Keep vertical**, the two layouts. Exactly one is ticked, and the tick follows the choice you made.
- **Customize strip…** opens the Customize strip window.
- **Hide strip** hides the strip. Show it again from the View menu or the toggle at the menu bar.

## The Customize strip window

The Customize strip window lets you choose which icons appear, in which order, and how they look. It opens from the right-click menu, or by choosing the Customize strip option in the strip's menu.

The window has three areas:

- **Available** on the left is the list of every icon the strip can show. It has a **Search** box at the top. Type part of a name to narrow the list.
- **On the strip** on the right is the list of icons currently shown, in order from top to bottom.
- The buttons between the two lists add an icon, remove an icon, and move the selected icon up or down.

To add an icon:

1. Type a name in the **Search** box, or scroll the **Available** list.
2. Click the icon, then click the add button. It moves to **On the strip**.
3. You can also double-click an icon in **Available** to add it.

To remove an icon, select it in **On the strip** and click the remove button. You can also double-click it to remove it.

To reorder icons, either select an icon in **On the strip** and use the move up and move down buttons, or drag its row to the place where the insertion line shows.

Below the lists are the appearance choices:

- **Position**: Left vertical, Beside menus, or Right vertical.
- **Size**: Normal or Large.
- **Tooltips**: show a tooltip when you hover over an icon.
- **Show labels**: write the name under each icon.

The footer has **Reset to default**, which puts back the first set of icons, **Cancel**, which closes without changes, and **Apply**, which saves your choices and redraws the strip at once.

Step by step to add the Undo, Redo and Tempo icons to a strip that does not have them:

1. Right-click the strip and choose **Customize strip…**.
2. Type `tempo` in the Search box. Select the Tempo icon, and click the add button.
3. Clear the search box and type `undo`. Add the Undo icon, then Redo.
4. Select the Tempo icon in **On the strip** and move it to where you want it.
5. Click **Apply**. The strip updates at once. Click **Cancel** instead if you want to drop your changes.

### What you can put on the strip

The catalogue holds more than forty icons. They are grouped as follows. The group names are shown in the Available list.

- **File**: Open, Save, Save as, Render, New, Import audio, Print, Export PDF and Export MIDI.
- **Edit**: Undo, Redo, Copy, Paste, Add bar, Add track and Go to bar.
- **Playback**: Play, Stop, Loop, Metronome, Count-in, Record, Mixer and Tuner.
- **Song**: Tempo, Time signature, Key signature, Transpose, Tuning, and Lyrics.
- **View**: Zoom in, Zoom out, Band view, Compact mode, Fretboard and Hotkeys.
- **App**: Chord finder, Scale finder, Preferences, About and Composer.

Note: The Hotkeys icon is a shortcut to the keys list. To look up any command and its key, use the Command palette, explained in the Settings, themes and hotkeys chapter.

Tip: Keep the icons you use every day, such as Open, Save, Undo and Play, near the top. Put rarely used icons at the bottom, or leave them out and use the Command palette instead.

## The Composer panel

The Composer is a panel beside the left strip. It is an early preview and not yet a finished feature.

The panel shows:

- A heading that says Composer.
- Three placeholder riff rows, named Half-time chug, Blast 16ths and Gallop fill. They are examples of the kind of thing the panel will hold. You cannot yet drag them onto the timeline.
- A small **Preview** box.

The Composer is a preview of a riff library that is planned for the future. The idea is that you will be able to drag riffs from the library onto the timeline. Nothing in the panel works that way yet, so do not rely on it for a song.

To show or hide the Composer:

1. Choose **View > Composer**, or click the **Composer** icon on the strip.
2. The panel opens beside the strip. Choose the same row again to close it.
3. The keyboard command View.ToggleComposer shows or hides it. It has no default key.

The Composer can open even when the strip is hidden. It sits at the left edge in that case. You can close it at any time with View > Composer.

Note: The Composer is an early preview. Its layout and content will grow in later versions, and the riff rows are placeholders.

## Troubleshooting

Problem: I cannot see the left strip at all.
Fix: Check the toggle icon at the left end of the menu bar. If it is missing, open the View menu and turn on Show strip button. Then open View > Left strip to show the strip.

Problem: The strip disappears every time I start TabForge.
Fix: Open the View menu and tick Show strip on start. This row decides whether the strip shows at startup.

Problem: The strip is in the wrong place.
Fix: Right-click the strip and choose Keep vertical for a column, or Place beside menus for a row. For left or right placement, use Customize strip and choose Position.

Problem: An icon I want is not on the strip.
Fix: Right-click the strip, choose Customize strip…, search for the icon's name, and add it to the strip.

Problem: The icons are too small to click easily.
Fix: In the Customize strip window, choose Size: Large, then click Apply.

Problem: The icons have no names, so I cannot tell them apart.
Fix: In the Customize strip window, tick Show labels, then click Apply.

Problem: The Composer panel is empty, or the riffs do not do anything when I click them.
Fix: This is expected. The Composer is an early preview, and the riff rows are examples only. Dragging riffs from it onto the timeline is not available yet.

Problem: I changed the strip and now it looks wrong, with no way back.
Fix: Right-click the strip, choose Customize strip…, and click Reset to default. The first set of icons comes back.

Problem: The strip hides the score on a small screen.
Fix: Choose Place beside menus, which uses a row at the top instead of a column. Or hide the strip with View > Left strip while you work.

## Common questions

Question: Do the strip icons run different commands from the menus?
Answer: No. Each icon runs the same command as the matching menu row or key.

Question: Can I make my own icon?
Answer: No. You choose from the catalogue of icons shown in the Customize strip window.

Question: Why is the Composer not dragging riffs onto my song?
Answer: The Composer is a placeholder for a riff library that is still being built. Its riff rows are examples, and the drag feature is not available yet.

Question: How do I know which key an icon uses?
Answer: Hover over the icon. The tooltip shows the command name and its key, if it has one.

Question: Will my strip layout be kept when I restart TabForge?
Answer: Yes. The choices you apply in the Customize strip window are saved, and the strip is rebuilt with them the next time you start.

Question: Is there a key to show the strip?
Answer: The command View.ToggleLeftStrip has no default key. Give it a key in Options > Preferences > Shortcuts if you want one.

Related: Playback and practice, Keyboard and fretboard tools, Settings, themes and hotkeys, Troubleshooting and FAQ
