# Settings, themes and hotkeys

This chapter covers how to make TabForge look and behave the way you want. It explains the Preferences window and its pages, the light and dark themes and the accent colour, how to change a key on the keyboard, the hotkey presets, the Command palette that finds any command, how settings are saved, and how to reset them when something goes wrong.

You do not need to change anything here to use TabForge. The defaults work well. Come back to this chapter when you want a different colour, a different key, or a fresh start.

## What this chapter covers

- Opening Preferences, and using its search box.
- A tour of the Preferences pages.
- Themes: System, Dark, Light and Custom.
- The accent colour and other colours.
- Interface size, spacing and motion.
- Shortcuts: rebinding keys, Hotkey 1 and Hotkey 2, and presets.
- The Command palette, which finds any command and its key.
- Saving, cancelling and applying your settings.
- Exporting and importing settings.
- Resetting a page, the theme, all settings, or the whole app.
- Where your settings are stored.

## Opening Preferences

There are three ways to open the Preferences window:

- Press `Ctrl+,`..
- Press `F12`, which is the second key assigned to Preferences.
- Choose **Options > Preferences…** from the menu bar.

The window opens on the page you used last. Its title is TabForge Settings.

Tip: If you cannot remember where a setting lives, type a word in the search box at the top of the Preferences window. The results show every setting that matches, and clicking one opens its page.

## The Preferences pages

The left side of the Preferences window is a list of pages. Each page groups related settings. Here is what each page is for.

- **Common settings** is the home page of the window, with links to the other pages. Use the search box to find a setting quickly.
- **General** holds program-wide options. It includes the check for updates button, labelled Check now, and the video recording options.
- **Appearance** holds the theme, the colours, the interface size and the motion settings. It is described in detail below.
- **Score & Notation** holds the options for how the score is drawn and how it is read.
- **Fretboard & Keyboard** holds the look of the fretboard and keyboard, the default instrument view, and the keyboard size. See the Keyboard and fretboard tools chapter.
- **Timeline & Tracks** holds the timeline and the track list. This is where you find options such as the playback position marker and the lines between tracks.
- **Editing** holds the note entry options. These include whether the plus key lengthens notes, and whether rhythms that overfill a bar are blocked. It also holds the safety prompts for deleting bars.
- **Playback & Practice** holds the follow options, which keep the score in view while the song plays.
- **Audio & Plug-ins** holds the audio driver, the output device, buffer size, sample rate, recording options and the plug-in folders. The playback chapter covers these in detail.
- **Recording** holds options for recording takes.
- **Tabs & Windows** holds options for tabs and windows, such as whether a middle-click on the title bar opens a new tab.
- **Shortcuts** lists every command and its keys. It is described below.
- **Files & Backups** holds file options, such as how songs open from Explorer, and the list of approved linked audio folders.
- **Advanced** holds the reset options and other settings that most people never need to touch.

Step by step to find a setting you know by name:

1. Open Preferences with `Ctrl+,`.
2. Click in the search box at the top.
3. Type a word from the setting, for example `buffer`.
4. Click the result. Its page opens with the setting shown.

## Themes and colours

The theme sets the overall colours of the program: the panels, the buttons, the text and the score page. Open **Appearance** to change it.

The **Theme** choice has four options:

- **System** follows Windows. When Windows is in dark mode, TabForge is dark, and when Windows is in light mode, TabForge is light.
- **Dark** sets the interface, the accent and the score colours together to the dark set.
- **Light** sets them together to the light set.
- **Custom** keeps all the colours you have chosen yourself, and does not change them when you pick a theme.

Dark or Light keeps any colour you picked yourself, such as the accent colour. To drop those and go back to the theme's colours, use **Reset colours**, which is on the Appearance page.

Score page colours are separate. You can draw the score page dark or light with **View > Score page**, choosing Dark or Light. These commands have no default keys.

Step by step to switch to the light theme:

1. Open Preferences and choose **Appearance**.
2. Set **Theme** to Light.
3. Watch the window change. Click **OK** to keep it.

To restore the built-in theme, including the palette, sizing, typography, motion and the score page look, use **Reset theme**, which you find by searching in Preferences. It asks for confirmation first.

### The accent colour

The **Accent colour** sets the colour of focus, active controls and the primary selection. Pick a swatch, or choose a custom colour. The accent colour stays when you switch between Dark and Light, unless you use Reset colours.

The playback highlight colour decides how the notes and the line that show what is playing look. The option **Custom** uses the colour you choose. The option **Follow accent** uses the accent colour. Custom is the default.

Track colour tint sets how strongly each track's row and timeline lane take the track's colour. Set it to 0 to turn the tint off.

Muted track dimming sets how grey a muted track looks. Zero means not dimmed, and 100 means the strongest dimming.

### Interface size, spacing and motion

- **Interface size** makes the text and icons bigger or smaller. 1 is normal. Until you change it, TabForge uses the size recommended for your monitor. The button **Recommended size for your monitor** sets it back to that size.
- **Spacing** controls how tightly the toolbar and buttons are packed. The choices are Extra compact, Compact, Comfortable and Spacious.
- **Reduce animations** turns off short tab and timeline drag transitions. Turn it on if the program feels slow or the motion distracts you.
- **Animation speed** sets how fast tab and timeline drag animations run. 1 is normal.

Tip: If the text is too small to read, raise **Interface size** by a small step, such as 1.25, rather than jumping to a large number.

## Shortcuts: changing a key

Every command in TabForge can have a key. The **Shortcuts** page lists them all, grouped by category, such as File and tabs, Editing, Transport, View and Bars and sections.

Each command has two key slots:

- **Hotkey 1** is the main key. It is the one shown in menus and tooltips.
- **Hotkey 2** is an optional second key for the same command. Each slot has its own button, and its own **Clear**.
- **Reset** returns both slots to their defaults.

To change a key:

1. Open Preferences and choose **Shortcuts**.
2. Find the command. Type part of its name in the search box to narrow the list.
3. Click the **Hotkey 1** button for the command. The button waits for a key.
4. Press the key you want. The button shows the new key.
5. Click **OK** to keep the change, or **Cancel** to go back.

To add a second key, click the **Hotkey 2** button and press the key. To remove a key, click the **Clear** button beside its slot.

If you press a key that another command already uses, TabForge offers **Reassign**. It takes the key from the other command, so that key now does only the new command. Choose Reassign when you are sure, and choose Cancel when you are not.

Note: A key can belong to only one command in one area. If Hotkey 1 and Hotkey 2 of the same command name the same key, Hotkey 1 wins.

Note: Some commands have no default key, and some cannot be bound yet. The Change accidental tool is an example: it cannot be bound until it is supported.

Tip: Before you change a key, write down the old one, so you can put it back by hand if you change your mind.

### Hotkey presets

A preset is a ready-made set of keys. The **Preset** box on the Shortcuts page lists the choices:

- **TabForge** is the default set, and the one this help chapter quotes.
- **Classic** is a set that matches a familiar layout. The main difference is that the two keys for Add track are swapped, so Ctrl+Shift+Insert becomes Hotkey 1.
- **Alternative** is a second set with a few different keys.
- **Custom** appears when you change Hotkey 2 keys yourself. It shows that your set is your own.

Switching preset changes the keys of every command at once. If you have made your own changes, switch preset only when you are ready to lose them, or write them down first.

## The Command palette

The Command palette is the easiest way to find any command and its key. Use it when you want to run a command but cannot remember its name, or when you want to know which key does a job.

To open it, press `Ctrl+Shift+A`, or choose **Help > Command palette…**.

1. Type part of the command's name, for example `loop`. The list narrows as you type.
2. Press Up or Down to move through the list.
3. Read the key at the right of each row. It is the key that runs that command now.
4. Press `Enter` to run the highlighted command.
5. Press `Esc` to close the palette without running anything.

Tip: The Command palette shows the key you actually set, including any changes you made in Shortcuts. It is the most reliable place to check.

Press `F1` to open this Help. The Help explains how to do things, and the Command palette lists every command with its key.

## Saving, applying and cancelling

Preferences saves as you go, but in a careful way.

- Each change you make is shown at once, so you can see the effect.
- **OK** keeps your changes and closes the window.
- **Apply** keeps your changes and leaves the window open, so you can keep working.
- **Cancel**, or closing the window without OK, puts back the values from when you opened the window.
- The status line at the bottom of the window says **All changes saved** when everything is kept. While changes are waiting, the status line is blank and a changes link tells you so.

The Fretboard settings window works the same way. Its Cancel puts back the values from the last Apply, or from when it opened.

Note: Settings are also saved in a file, as described in the Where settings are stored section below.

## Exporting and importing settings

You can copy your settings to a file, and load them again later or on another computer.

1. Open Preferences and click **Manage settings** at the bottom left of the window.
2. Choose **Export settings…** to save a file. The default name is TabForge-settings.json.
3. To bring settings back, click **Manage settings** again and choose **Import settings…**. Pick the file you saved.

An import is staged first, so you can check it before it takes effect. If the file cannot be read, the status line says the import failed.

## Resetting settings

TabForge gives you several levels of reset. Use the smallest one that fixes your problem.

- **Reset page** clears the settings on the page you are looking at, and puts that page's defaults back. It is in the footer of the Preferences window.
- **Reset theme** restores the built-in theme: palette, sizing, typography, motion and the score page look. It asks first.
- **Reset all settings** puts every setting back to its default. It is in the search results of Preferences.
- **Reset entire app** is in the Advanced page. It is a two-step process. First you choose what to wipe from a list of items, with buttons to select all or none. You can back up first. Then you type the word the dialog asks for, to confirm.

Warning: Reset entire app removes the items you tick. Read the list carefully before you confirm, and choose Back up first if you may need the data again.

Fretboard settings have their own **Reset section** and **Reset all** buttons, described in the Keyboard and fretboard tools chapter.

## Where settings are stored

TabForge keeps its files in two places, both per user:

- The settings file is called settings.json. It lives in the TabForge folder in your roaming application data, which is `%APPDATA%\TabForge`. The same folder holds your plug-in library, chain states, presets, drum maps and templates. Templates are kept in a Templates folder there.
- The crash log, named avalonia-crash.log, sits beside settings.json in `%APPDATA%\TabForge`.
- Recovery copies of unsaved songs and the diagnostic logs live in the TabForge folder in local application data, which is `%LOCALAPPDATA%\TabForge`. The Recovery folder holds the autosave copies. The Diagnostics folder holds the first-chance and error logs, and the self-test logs.

Note: You can type `%APPDATA%` into the address bar of Windows Explorer to open the roaming folder directly. Do the same with `%LOCALAPPDATA%` for the local folder.

## Troubleshooting

Problem: I changed a setting and now I cannot find how to change it back.
Fix: Open Preferences, type a word from the setting in the search box, and use Reset page on that page. If that does not help, use Reset all settings.

Problem: I pressed a key and a different command ran.
Fix: Open the Command palette with Ctrl+Shift+A and search for the command you wanted. Read its key, then open Shortcuts and check whether the key is also bound to another command. Use Reassign or Clear to fix it.

Problem: A key I set does nothing.
Fix: Click an empty part of the score first, so the key goes to the score and not a text box. Then check the key in the Command palette. If the key shows a different command, another command has taken it.

Problem: The Preset I chose has undone my own keys.
Fix: Switching preset replaces the keys of every command. Choose Custom or set the keys again. If you exported your settings earlier, use Manage settings > Import settings to bring them back.

Problem: The colours do not match my theme any more.
Fix: Choose Appearance, then Reset colours. This drops the colours you picked yourself and applies the theme's colours again.

Problem: The text is too small to read.
Fix: In Appearance, raise Interface size one step at a time, or click Recommended size for your monitor.

Problem: Changes I made were lost after I pressed Cancel.
Fix: Cancel puts back the values from when you opened the window. Make your changes again and press OK, not Cancel.

Problem: The program feels slow after I changed settings.
Fix: Turn on Reduce animations in Appearance, and use Reset page on any page you changed recently to go back to its defaults.

## Common questions

Question: How do I open Preferences?
Answer: Press Ctrl+,, press F12, or choose Options > Preferences.

Question: What does Hotkey 2 do?
Answer: It is an optional second key for the same command. Either key runs the command. Hotkey 1 is the one shown in menus and tooltips.

Question: Can I make my own set of keys?
Answer: Yes. Change keys in Shortcuts, and the preset changes to Custom.

Question: How do I find the key for a command?
Answer: Open the Command palette with Ctrl+Shift+A and type part of the command's name. The key is shown at the right of the row.

Question: How do I move my settings to another computer?
Answer: Use Manage settings > Export settings… on the first computer, copy the file across, and then use Manage settings > Import settings… on the second.

Question: What is the difference between Reset page and Reset entire app?
Answer: Reset page changes only the settings on one page. Reset entire app removes the chosen items from the program's folders, and it asks you to confirm in two steps.

Question: What do F1 and Ctrl+Shift+A do?
Answer: F1 opens this Help. Ctrl+Shift+A opens the Command palette, which lists every command and its hotkey.

Related: Playback and practice, Keyboard and fretboard tools, Composer and left strip, Troubleshooting and FAQ
