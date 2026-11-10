# What's new in 0.9.3

This chapter lists what changed in the 0.9.3 release. It is short on purpose: read it once, then use the other chapters when you want the full story of any feature.

## The left strip

The window now has a slim strip of icon buttons beside the score. Each button runs one command, so the jobs you do most are one click away.

- By default the strip holds **New**, **Open**, **Save**, **Save as** and **Render**, then **Undo** and **Redo**, then the **Composer** toggle.
- A small sidebar icon at the left end of the menu bar shows or hides the strip.
- Choose **View > Left strip** to show or hide it, and **View > Show strip button** and **View > Show strip on start** to decide whether its toggle icon is shown and whether the strip opens when TabForge starts.
- Right-click the strip for its menu: place it beside the menus, keep it vertical, customize it, or hide it. The current placement is ticked.

The strip sits beside the score only, above the full-width timeline, and it stays in place in Band view and compact mode.

Tip: If you do not want the strip or its Composer panel, choose **View > Left strip** once and they stay out of the way.

## The Composer panel

The **Composer** button on the strip opens a Composer panel beside it. In this release the panel is a placeholder: it shows a few rows and a small preview box, and it has no riff library yet.

- Choose **View > Composer** to open or close it. It opens on its own even when the strip is hidden.
- Nothing in the panel changes your song.

## Customize the strip

Right-click the strip and choose **Customize strip** to pick your own icons.

1. Choose icons from the **Available icons** list. Use the search box to narrow the list. There are 41 icons to choose from.
2. Click **Add** to put an icon on the strip, **Remove** to take it off, and **Move up** or **Move down** to order the list. You can also drag rows.
3. Add a **Separator** wherever you want a divider line. You can add several.
4. Choose a **Position** (left, beside the menus, or right), a **Size** (normal or large), and whether to show **Tooltips** and **Labels**.
5. Click **Apply**. **Reset to default** brings back the first layout, and **Cancel** closes the window without changes.

## One New command: Ctrl+N

`Ctrl+N` is now the one New command. It opens a new untitled song in a new tab. **File > New**, the **+** button in the tab strip, the tab menu and the left strip **New** icon all run it. The separate New tab command is gone.

## F1 opens this Help

`F1` opens this Help window. You can also choose **Help > Help**. The old Keyboard shortcuts dialog is gone: press `Ctrl+Shift+A` to open the Command palette, which lists every command with its hotkey, and use **Options > Preferences > Shortcuts** to change a key.

## Light theme

- Light mode is now outlined: selected chips, rows and tabs get a pale tint with accent text, check boxes are filled with the accent, and the OK button is the only solid fill. Every shade comes from your accent colour.
- Panels, dialogs and the track list now follow the theme when you switch to Light. A settings reset had left them dark, and TabForge repairs settings saved that way once at start.

Related: Getting started, Interface tour, Composer and left strip, Settings, themes and hotkeys
