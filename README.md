# TabForge 0.3.0 Beta 2

**A keyboard-driven tablature and notation editor for Windows. Opens and saves Guitar Pro files.** Write, import and play back guitar,
bass, drum and keyboard parts, with a live fretboard / keyboard, an arrangement timeline, audio and MIDI
recording lanes, mixer groups, VST effects and instruments, and offline audio rendering.

[![TabForge with a drum plug-in](docs/screenshots/github-main.jpg)](docs/screenshots/github-main.jpg)

*The main screenshot shows TabForge's own demo song and a third-party VST3 plug-in (Superior Drummer 3 by Toontrack, sold separately) as an example of plug-in hosting. TabForge does not include that plug-in and is not affiliated with Toontrack.*

> [!WARNING]
> **TabForge 0.3.0 Beta 2 is a pre-release.** No bugs are known at the moment, but extensive testing is still
> ongoing, so expect surprises. Keep backups of your projects and your original Guitar Pro files, especially
> before using recording or third-party plug-ins.

TabForge is an independent project. Guitar Pro is a trademark of Arobas Music; TabForge is not affiliated with, sponsored or endorsed by Arobas Music, Steinberg, Toontrack or any other company named here.

## Download

Get the latest build from [Releases](https://github.com/cobhc95/TabForge/releases):

**0.3.0 Beta 2 downloads:** [Windows installer](https://github.com/cobhc95/TabForge/releases/download/v0.3.0-beta.2/TabForge-0.3.0-beta.2-setup.exe) · [Portable ZIP](https://github.com/cobhc95/TabForge/releases/download/v0.3.0-beta.2/TabForge-0.3.0-beta.2-win-x64-portable.zip) · [SHA-256 checksums](https://github.com/cobhc95/TabForge/releases/download/v0.3.0-beta.2/TabForge-0.3.0-beta.2-SHA256.txt)

Installer, portable ZIP, SHA-256 checksums and release notes are on the [Releases page](https://github.com/cobhc95/TabForge/releases).

**Unsigned binaries.** The downloads are not code-signed, so Windows SmartScreen may warn when you first run
them. Releases built by the GitHub Actions release workflow from the tagged source come with build attestations
(`gh attestation verify <file> --repo cobhc95/TabForge`) and SHA-256 checksums, so you can verify that a
download matches that build.

- **`TabForge-<version>-setup.exe`** — installer: Start menu entry, optional desktop icon and an optional
  *"Open Guitar Pro and TabForge files with TabForge"* association. Installs for your Windows account;
  no administrator rights needed.
- **`TabForge-<version>-win-x64-portable.zip`** — extract anywhere and run `TabForge.exe`. Nothing is
  installed and nothing is registered.

Songs double-clicked in Explorer open as a new tab in the running TabForge (or a new window — your choice in
Settings > General).

Windows 10/11, 64-bit. No separate .NET install. Sound plays through Windows' built-in MIDI synthesiser
or any MIDI output you choose, or through TabForge's audio engine.

Includes an original demo song, *Ashen Meridian* (CC0), used in all screenshots.

A `licenses/` folder beside `TabForge.exe` (portable zip and installer) holds the full licence texts of every bundled component. SoundTouch.Net (LGPL-2.1) ships as a separate, replaceable `SoundTouch.Net.dll`; each release attaches its source.

## Why it exists

The author wanted a fast, keyboard-driven way of writing tabs — with freedom to move sections around,
customise the look and save ideas without fighting the program. The longer-term idea is to blur the line a
little between writing a song and the first steps of a demo (recorded ideas, a VST on a track, drum
plug-ins), only where it is genuinely useful.

---

## Feature tour

### 1. The workspace

[![Main window](docs/screenshots/main-window.png)](docs/screenshots/main-window.png)

- **Document tabs in the title bar**, like a browser: several songs open at once, and **several song tabs can play at the same time** — each tab keeps playing when you focus another; drag a tab to reorder it,
  drag it out to open it in its own window, or drop it onto another TabForge window.
- **Score** in the middle (standard notation + TAB, or either alone), **fretboard** on top,
  **arrangement timeline** and track list at the bottom, **tool palette** and **sections** on the right.
  Every panel can be docked, floated, resized or hidden.

### Light and dark themes


**Dark**, **Light** and **System** are theme presets: one click sets the interface, accent, icons, transport
buttons and score page together (the light theme is a soft grey, with a wood-coloured fretboard). Every single
colour — score paper, notation, staff lines, playback colours, fonts — can still be changed afterwards
(Settings > Appearance & colours).

### Customisable interface

The whole workspace can be rearranged to suit how you write:

- **Drag any panel by its tab** (Tools, Structure, Rhythm, Layout, Sections, Practice / Mixer, Zoom,
  the fretboard or the arrangement). While you drag, the place it will land is highlighted in blue.
- **Drop on the middle of a panel** to add it as another tab there, or **near an edge** to split that area and
  put the panel beside, above or below it. The tool palette pages can sit side by side instead of as tabs.
- **Drop outside the window** to float the panel as its own window (e.g. on a second screen); drag it back to
  re-dock it.
- **Resize** any split by dragging the divider; hide panels you do not use from the View menu.
- The layout is remembered between sessions, and **Reset all panels to original positions** (View menu) puts everything back.
- Colours, theme, fonts, interface scale, score spacing and every hotkey are customisable too (see Settings).

### 2. Menus

| | | |
|---|---|---|
| [![File](docs/screenshots/menu-file.png)](docs/screenshots/menu-file.png) | [![Edit](docs/screenshots/menu-edit.png)](docs/screenshots/menu-edit.png) | [![Bar](docs/screenshots/menu-bar.png)](docs/screenshots/menu-bar.png) |
| [![Track](docs/screenshots/menu-track.png)](docs/screenshots/menu-track.png) | [![Note](docs/screenshots/menu-note.png)](docs/screenshots/menu-note.png) | [![Effects](docs/screenshots/menu-effects.png)](docs/screenshots/menu-effects.png) |
| [![Sections](docs/screenshots/menu-sections.png)](docs/screenshots/menu-sections.png) | [![Tools](docs/screenshots/menu-tools.png)](docs/screenshots/menu-tools.png) | [![Sound](docs/screenshots/menu-sound.png)](docs/screenshots/menu-sound.png) |
| [![View](docs/screenshots/menu-view.png)](docs/screenshots/menu-view.png) | [![Options](docs/screenshots/menu-options.png)](docs/screenshots/menu-options.png) | [![Help](docs/screenshots/menu-help.png)](docs/screenshots/menu-help.png) |

Familiar menu order and shortcuts for people coming from other tab editors (a "Classic (influenced by Guitar Pro 5)" shortcut preset is included); every command shows its shortcut (tooltips too, in brackets), and every shortcut can be
changed (see [Hotkeys](#8-settings)).

### 3. Writing and editing

- **Keyboard-first entry**: type frets (two quick digits for 10+), arrows move by beat/string,
  `+` / `-` make a note shorter / longer (reversible in Settings), `.` dots it.
- **Free rhythm entry**: a note can be lengthened even past the end of the bar — the bar turns red instead
  of the change being refused (a strict mode is available). **Check bars (F4)** lists every bar that does
  not add up, using the same rule as the red highlight.
- **30+ techniques**: bends (with curves), slides, hammer-ons/pull-offs, palm mute, let ring, vibrato and
  wide vibrato, natural/artificial/pinch/tapped harmonics, dead and ghost notes, tremolo picking, trills,
  fades, accents, staccato, tenuto, grace notes, arpeggio/brush strokes, tapping, slap and pop.
- **Two voices per bar**, tuplets, ties, rests, fermatas, chord names, text and lyrics.
- **Mix table points (F10)**: tempo, volume and pan changes inside a song.
- Copy/cut/paste beats and bars, repeat selections, insert/delete beats that keep the bar length, full undo.

### 4. Arrangement timeline and sections

[![Arrangement timeline](docs/screenshots/arrangement-timeline.png)](docs/screenshots/arrangement-timeline.png)

- One block per bar and track shows at a glance where each instrument plays.
- **Sections** (Intro, Verse, Chorus…) as coloured bars across the top. Sections of the same type share a
  colour; the section list on the right always shows the same colours.
- **Drag a section to move its marker** — the section starts somewhere else while the bars stay put.
  **Ctrl+drag** moves the section *with* its bars (all tracks together; playback continues seamlessly).
- **Add a section** with `M`, the Sections panel, or right-click on the section lane → *Add section at bar N*.
- **Drag a section's edge to resize it**: shrinking leaves empty bars, growing pushes the next section along.
- **Drag across bars to select an area**: copy, cut, paste, move, delete, loop it, or skip it during
  playback. `Esc` clears the selection.
- Right-click menus for bars, tracks and sections; drag tracks up or down to reorder them.

[![Sections panel](docs/screenshots/sections-panel.png)](docs/screenshots/sections-panel.png)

**Tool palette** (redrawn icons) — durations, dynamics, effects and bar structure (plus rhythm and layout tools), grouped in tabs; right-click a tool to pin it to the quick strip:

[![Tool palette](docs/screenshots/tool-palette.png)](docs/screenshots/tool-palette.png)

### 5. Fretboard, keyboard and scale finder

[![Fretboard](docs/screenshots/fretboard.png)](docs/screenshots/fretboard.png)

- **Matches the instrument**: stringed instruments (guitar, bass, oud, cello, violin, ukulele, mandolin,
  banjo…) get a fretboard with the track's own strings — a 5-string bass shows five; drums get a
  **percussion map** that lights up on each hit and writes a hit on click; piano, winds, brass and synths get a
  **keyboard** (88 keys by default; 76/61/49/37/25 from the right-click menu). Right-click to show one track or
  all tracks differently; the default is in Settings > Fretboard.
- Follows playback: the sounding note glows, the next notes are outlined, a line shows where the hand
  moves next. Chords move as one shape.
- Click to write a note; hover previews where it would go. Left-handed view, note names, 12 or 24 frets,
  scale highlighting, alternative preview layouts.
- **String spacing** (right-click > *Appearance*, also in Settings > Fretboard and bindable): *Compact*, *Natural* (default) or *Wide*. The board keeps natural proportions in any pane shape: a tall pane centres it and a narrow pane scales the drawing down instead of squeezing the frets.
- **Appearance** (right-click > *Appearance*, saved for every song): scale highlight as shaded cells,
  **circles** or rings, in the colour you choose; fret-marker dots in any of several colours and
  brightness levels; keyboard keys grey or white (matching the theme by default).

| | |
|---|---|
| [![Keyboard view](docs/screenshots/instrument-keyboard.png)](docs/screenshots/instrument-keyboard.png) | [![Drum view](docs/screenshots/instrument-drums.png)](docs/screenshots/instrument-drums.png) |

**Scale finder** — the **Scales** button beside the fretboard, its right-click menu (*Select scale* /
*Find scale…*) or Tools > Scale finder. *Likely scales* analyses the notes of the **selection** (score or
timeline) or the **entire song**, for one track or all, and lists every key and scale they fit, best first
(usually several do); *All scales* picks any key and scale directly. The choice is highlighted on the fretboard
or the keyboard (root marked more strongly); *Scale > Clear selection* removes it.

[![Scale finder](docs/screenshots/scale-finder.png)](docs/screenshots/scale-finder.png)

### 6. Tracks, instruments and tuning

| | |
|---|---|
| [![Track properties — guitar](docs/screenshots/track-properties-guitar.png)](docs/screenshots/track-properties-guitar.png) | [![Track properties — bass](docs/screenshots/track-properties-bass.png)](docs/screenshots/track-properties-bass.png) |
| [![Track properties — drums](docs/screenshots/track-properties-drums.png)](docs/screenshots/track-properties-drums.png) | [![Instrument picker](docs/screenshots/instrument-picker.png)](docs/screenshots/instrument-picker.png) |

- **Add track** (the **+ Track** button): the same window as Track properties for the new track — pick the
  instrument from the catalogue, set tuning, mixer and details — plus **where it goes**: last, first, after the
  selected track, or as track number N.
- **Instrument picker** with the full General MIDI set, pictures, categories and search.
- **Tuning editor**: per-string `−`/`+`, double-click a string to type a note (e.g. `Eb3`), presets for
  4-/5-/6-/7-string bass and 6-/7-/8-/9-string guitar, `+ / − Low string` to change the number of strings.
  Extended-range instruments are named by string count, e.g. *Electric Bass (Finger) (5 strings)*.
  Choose whether retuning keeps the fret numbers or the sounding pitches.
- **Drum notation presets** (Guitar Pro 5, Guitar Pro 6/7, line-per-instrument drum tab) plus a fully
  custom map: TAB line, TAB text, staff position and notehead for every GM drum sound.
- **Global tuning** shifts every track at once, e.g. the whole song down a half step.
- **Instrument and tool icons** are original artwork, redrawn for this release.

[![Add track](docs/screenshots/add-track.png)](docs/screenshots/add-track.png)

[![Global tuning](docs/screenshots/global-tuning.png)](docs/screenshots/global-tuning.png)

### 7. Playback and practice

- MIDI playback with mute/solo/volume/pan per track, live while playing: bends, slides, let ring,
  palm mute, ghost/dead notes and triplet feel/swing.
- **Song structure (repeats, endings, D.C./D.S.) played as written:** repeats (up to x99, several closes, multi-pass endings such as
  1.2.3.), D.S. / D.C. / Coda / Fine, and tempo changes that last until the next one, including changes partway
  through a bar. Covered by synthetic test songs that run on every build.
- **Metronome and count-in** with their own sounds and levels, subdivisions, count-in before every loop.
- **Seamless looping** of the song, a section or any selected area; set a number of loops; a **speed
  trainer** that raises the tempo each pass; skip areas during playback.
- **Score follow**: the view keeps the playing bar in sight and stops if you scroll by hand. Right-click the
  score > *Follow playback*: **Page turn** (default: jumps half a page or to the next line near the edge) or
  **Smooth page turn** (the same turns, glided at the display refresh rate and idle in between).
- **Two score views**, both on the right-click menu: *Score layout* (page / continuous) and *Score scrolling* —
  **vertical** (bars wrap into lines, scroll down) or **horizontal** (the whole song on one line that scrolls and
  follows to the right, centred vertically in page mode). Only the bars near the view are drawn, so even long songs
  stay light.

[![Horizontal score scrolling](docs/screenshots/score-horizontal.png)](docs/screenshots/score-horizontal.png)

[![Mix table](docs/screenshots/mix-table.png)](docs/screenshots/mix-table.png)

### 8. Recording, clip lanes and grouping

[![Recording lanes and track controls](docs/screenshots/recording-layout.png)](docs/screenshots/recording-layout.png)

- **Ctrl+R** records armed tracks into audio or MIDI clips; choose an audio input or *MIDI (all inputs)*,
  watch the input meter, and toggle live monitoring from the lane. Recording stops at the take's start.
- **Multiple lanes per track** keep overlapping and loop-recorded takes separate. Click a take to choose
  what plays; Ctrl+click to layer takes. Waveforms and MIDI notes appear while recording.
- Click anywhere on a lane to seek to that bar, just as on a notation row; clicking a take also selects it.
- **Snap** clips to a beat grid, playhead or other clip edges; move, trim, mute, duplicate, or edit clip
  properties. MIDI clips can be written into the track's notation.
- **Track groups** can be shown as collapsible headers in the track list with arm, mute, solo, level,
  pan and FX power controls. The mixer offers instrument-based, compact or ungrouped views.

[![Mixer groups and track controls](docs/screenshots/mixer.png)](docs/screenshots/mixer.png)

[![MIDI take lanes](docs/screenshots/recording-lanes.png)](docs/screenshots/recording-lanes.png)

### 9. Settings

[![Settings — General](docs/screenshots/settings-general.png)](docs/screenshots/settings-general.png)

Settings are grouped into pages and **searchable** — type a word and every matching setting from every page
is listed with its path, and changes preview live before you apply them:

[![Settings search](docs/screenshots/settings-search.png)](docs/screenshots/settings-search.png)

| | | |
|---|---|---|
| [![Appearance](docs/screenshots/settings-appearance-colours.png)](docs/screenshots/settings-appearance-colours.png) | [![Score](docs/screenshots/settings-score-notation.png)](docs/screenshots/settings-score-notation.png) | [![Playback and sound](docs/screenshots/settings-playback-sound.png)](docs/screenshots/settings-playback-sound.png) |
| [![Audio and VST](docs/screenshots/settings-audio-vst.png)](docs/screenshots/settings-audio-vst.png) | [![Editing](docs/screenshots/settings-editing.png)](docs/screenshots/settings-editing.png) | [![Timeline](docs/screenshots/settings-timeline-sections.png)](docs/screenshots/settings-timeline-sections.png) |
| [![Fretboard](docs/screenshots/settings-fretboard.png)](docs/screenshots/settings-fretboard.png) | [![Tabs and windows](docs/screenshots/settings-tabs-windows.png)](docs/screenshots/settings-tabs-windows.png) | [![Hotkeys](docs/screenshots/settings-hotkeys.png)](docs/screenshots/settings-hotkeys.png) |

- **Themes**: dark, light and system presets or fully custom colours; interface scale that applies live;
  score fonts per text area.
- **Every setting is audited**: a self-test changes each one and checks it is stored, survives save and reload
  and never changes another; a live probe checks each one visibly changes the app.
- **Hotkeys**: every command is rebindable, with presets for TabForge, Classic (influenced by Guitar Pro 5) and TuxGuitar and
  conflict warnings. The full list is in Settings > Hotkeys.
- **Updates**: *Check for updates automatically* is the first setting on the General page — once a day TabForge
  asks GitHub whether a newer release exists and offers to open its download page (see
  [Security](#security-and-safety) for exactly how limited that request is). Help > *Check for updates…* checks now.

  [![Update available](docs/screenshots/update-available.png)](docs/screenshots/update-available.png)

- **Help > About** shows the version, the independent-project notice and a **Licences** button that opens the third-party licence list and the `licenses/` folder.
- **Windows integration**: turn "Open Guitar Pro and TabForge files with TabForge" on or off (see
  [Security](#security-and-safety) for exactly what it writes).
- Import/export all settings as a file.

[![Project settings](docs/screenshots/project-settings.png)](docs/screenshots/project-settings.png)
[![Score text style](docs/screenshots/score-text-style.png)](docs/screenshots/score-text-style.png)

### 10. Files and compatibility

| Format | Open | Save |
|---|---|---|
| Guitar Pro 3 / 4 / 5 (`.gp3`, `.gp4`, `.gp5`) | ✔ | |
| Guitar Pro 6 (`.gpx`) | ✔ | |
| Guitar Pro 7 / 8 (`.gp`) | ✔ | ✔ (default) |
| TabForge project (`.tforge`) | ✔ | ✔ |
| Standard MIDI file (`.mid`) | | export |
| ASCII tab (`.txt`) | | export |

Drum parts import from every format, including Guitar Pro 6 (`.gpx`) extended articulations and Guitar Pro 7/8
(`.gp`) drum kits; the import is covered by synthetic round-trip tests that run on every build. Known
limit: Guitar Pro 3–5 files with more than 1,000 bars cannot be opened by the reader library yet (save them as
`.gp`/`.gpx` from Guitar Pro 6/7/8). Guitar Pro 1/2 (`.gtp`) and TuxGuitar (`.tg`) files are not supported.

A `.gp` saved by TabForge follows the `.gp` file format, **and** carries the complete TabForge project inside it
(an extra entry other readers can ignore), so reopening it in TabForge loses nothing — drum map, section sizes,
score styles, mix points. Imports are checked against the reference parser (alphaTab) note-for-note on real
GP3/GP4/GP5/GPX files.

Saved `.gp` files carry correct zip checksums and keep the album, copyright, tab author, music, words and instructions fields.

### 11. Mixer, VST plug-ins and FX chains

[![Track FX chain](docs/screenshots/fx-chain.png)](docs/screenshots/fx-chain.png)

- **Mixer** (fader button beside the tuning fork): every track as a row, grouped **by instrument** (guitars,
  basses, keys, drums, other), **compact** (guitars, basses, drums, everything else) or not at all. Each group row
  sets level, pan and pitch for all its tracks at once; tracks can be moved between groups.
- **FX chains**: the **FX** button on each track (and in the mixer) opens the chain; its power
  switch plays the track through the chain or plain Windows MIDI. A chain is a **VST2 or VST3 instrument** followed
  by **effects** in series, each with bypass, wet/dry and its own plug-in window. With effects but no VST
  instrument, the track's own General MIDI sound is played through the effects (rendered from Windows' own sound
  bank); a *MIDI sound* switch turns it off.
- **Audio output**: WASAPI (shared / exclusive), ASIO or DirectSound, with device, sample rate and buffer size in
  **Settings > Audio & VST**. Plug-in folders are added by browsing (no automatic scan unless you turn it on).
- **Safety**: plug-ins never run inside the editor. They run in a separate audio engine process that starts only
  when a track uses plug-ins; if a plug-in crashes or freezes, TabForge names it, switches it off and carries on.
  *Run each plug-in in its own process* isolates every plug-in on its own, so a crash loses only that plug-in (a little more CPU).
  That is crash isolation only, not a security sandbox: plug-ins run with your user rights, so load only plug-ins you trust.
- **Saving**: a song with plug-ins or mixer groups saved as `.gp` asks once: `.gp` with the settings inside
  (recommended), a clean `.gp` plus a small `.tfaudio` file (no extra entry inside the `.gp`), or `.tforge`.

| Plug-in wiring | MIDI processing |
|---|---|
| [![Audio and MIDI pin routing](docs/screenshots/plugin-wiring.png)](docs/screenshots/plugin-wiring.png) | [![MIDI processor chain](docs/screenshots/midi-processing.png)](docs/screenshots/midi-processing.png) |

Plug-in wiring selects audio input channels, the MIDI source/channel filter, sidechain input (VST2 with four or
more inputs) and MIDI output forwarding to another track.

- **Serial MIDI chain:** MIDI flows through the plug-in list in order, so MIDI-effect plug-ins
  shape the notes for the next slot; instruments add their audio, effects process everything before them.
- **Group buses and master FX:** each mixer group has an effects bus, and the mixer's Master row has an FX chain,
  pan and volume over the whole mix. The **Master row sits at the top** of the Mixer, under the column headers.
- **Monitoring FX (MON)**: a **MON** button on the Master row (its own colour) opens a chain that is heard live only and is never rendered or exported. It applies to all projects by default; opt out per project to give a project its own monitoring chain.
- **Per-plug-in MIDI processors** run right before their plug-in: note mapping, transposition, velocity,
  humanisation, delay, CC tools, scale/chord tools, arpeggiator, randomizers, LFOs, step sequencer, audio-to-MIDI
  drum trigger and loopers. Each has an "Applies to" note filter, the window has whole-list presets (with
  built-ins) and a search that indexes every parameter. All is saved with the song.

### 12. Render to audio

[![Render dialog](docs/screenshots/render.png)](docs/screenshots/render.png)

**File > Render…** (`Ctrl+Alt+R`) exports the master mix, track stems or both, for the full song,
a selection or a custom range. Choose tail length, file-name wildcards and WAV 16/24/32-bit float;
MP3 bit rates are offered when Windows has an encoder. Rendering supports progress and cancellation.

---

## Complete feature list

Everything TabForge does, by area. Every command below can be found in the menus, the command palette and
Settings > Hotkeys (all rebindable).

### Files, tabs and export
- **New**, **New tab**, **Open**, **Open in new tab**, **Save**, **Save as**, **Close tab**, **Duplicate tab**, Exit.
- **Templates**: File > *New from template* (built-in and your own) and *Save as template* (your templates live in `%APPDATA%\TabForge\Templates`).

  [![New from template](docs/screenshots/new-from-template.png)](docs/screenshots/new-from-template.png)

- **Export**: Standard MIDI, ASCII tab, **PDF** (engraved notation and tab as A4 pages, no printer needed), **MusicXML** (`.musicxml`, one part per track with a tab staff), clean **Guitar Pro `.gp`**, and **Render to audio** (WAV / MP3, master and stems).
- **Print** and **Print preview**; **Score information** (F5).
- **Command palette** (Ctrl+Shift+A): fuzzy-search every command by name, see its key, press Enter to run it.

  [![Command palette](docs/screenshots/command-palette.png)](docs/screenshots/command-palette.png)

- **Autosave and recovery**: unsaved songs are copied to the Recovery folder every 1 to 30 minutes (Settings > General); after a crash or power loss TabForge offers to reopen them. Your own files are never overwritten.

  [![Autosave setting](docs/screenshots/autosave.png)](docs/screenshots/autosave.png)

- **Safe saving**: atomic writes, and a `.gp` plus `.tfaudio` pair is restored to its last complete state if a save is interrupted.
- **Command line**: `TabForge.exe song.gp5 [more files]` opens files as tabs (Explorer double-click hands over to the running instance, or opens a new window, as you choose); `--theme dark|light|system` starts with that theme (kept in your settings).
- Drag-and-drop tabs between windows; **Windows 11 Snap Layouts** on the maximise button; full-screen mode (F11).

### Editing and notation
- **Notation and playback** covered by automated tests: engraving (bends, whammy diagrams, grace notes, key/time signatures, tempo marks, dynamics) and playback (repeats, jumps, tempo changes, tremolo, strokes, bend channel).
- Note entry: fret digits, arrow navigation, **durations** (whole to 64th, increase/decrease, dotted, double dotted, triplet), rest, tie, fermata, accent, staccato, tenuto, **shift pitch** by semitone, **move to higher/lower string** (Alt+Up/Down in the score), copy last beat.
- **Effects**: dead, ghost, palm mute, let ring, hammer/pull, bend, legato and shift slide, vibrato, wide vibrato, tremolo bar, natural and artificial harmonics, tapping, slap, pop, trill, tremolo picking, fade in/out, wah open/close, brush and arpeggio up/down, grace note, chord names, beat text, lyrics.
- **Bars**: insert, delete, duplicate, repeat selection as bars, time signature, key signature, clef, triplet feel, repeat open/close, directions and endings, double bar, repeat one or two bars, section editor, **Check bars (F4)**, **Go to bar**, mix table points (F10).
- Insert/delete beats, cut/copy/paste, select whole track, full undo/redo (fast on large songs).
- **Copy, cut and paste** of beat ranges (from the cursor, flowing over bar lines by the target's time signature) and whole bars, shared with the timeline clipboard. Paste asks only what it needs (replace or insert, overwrite or insert before/after, keep the pitch or shift an octave, bar settings) with "Remember my choice" (Preferences > Editing > Copy and paste); it works between instruments (guitar to bass, other tunings, drums) and **Paste special** (Ctrl+Shift+V) adds repeats, an octave shift and keep string and fret. Every paste is one undo step and cutting bars empties them.
- **Tools menu**: Check bar duration, **Transpose**, **Scale finder**, **Tuner**, **Metronome**, **Count-in**, preview note sound.
- **Tuner**: a chromatic tuner; the engine detects the pitch of the armed input and shows the note, a cents needle and the track's string tunings with the nearest string highlighted.

  [![Tuner](docs/screenshots/tuner.png)](docs/screenshots/tuner.png)

- **Score views**: tab + standard, tab only, standard only; dark or light score page; continuous line or individual notes; page/continuous layout; vertical or horizontal scrolling; zoom 75 / 100 / 150 % and Ctrl+/- zoom; **stylesheet** (F7).
- **Screen-reader support**: the tab editor announces track, bar, beat, string, fret and note name as the cursor moves, and every control has an accessible name and a stable id.

### Side panes and panels
- **Tools** (durations, dynamics, effects, bar structure), **Structure**, **Rhythm** and **Layout** palette pages, pinnable quick strip.
- **Sections** panel; **Lyrics**; **Practice / Mixer**; **Song stats**; **Chord finder**; **Scale highlight**.
- **Zoom & speed**: one compact row with zoom out/in, zoom box and the single playback-speed box (25 to 200 %, presets 50 to 200 %).

  [![Zoom and speed](docs/screenshots/zoom-speed.png)](docs/screenshots/zoom-speed.png)

- **Speed hotkeys**: Ctrl+Alt+Up (faster), Ctrl+Alt+Down (slower), Ctrl+Alt+0 (back to 100 %).
- **Layouts**: Compose, Practice and Mix (Ctrl+1/2/3), save, delete, reset; show/hide the side panel and the fretboard; *Reset all panels to original positions*.
- Practice / learn mode, loop settings (loop count, speed trainer that raises the tempo each pass), metronome and count-in settings (volume, click sound, subdivision, accent).

### Fretboard, keyboard and drums
- Fretboard, 88-key (or 76/61/49/37/25) keyboard and drum percussion map; left-handed view, note names, 12/24 frets, scale highlight and *Clear selection*, appearance options (circles or rings, colours, fret-marker dots, key colours).
- **Number size** (Small, Medium, Large) under right-click > Appearance and in Settings; **Lock fretboard size** (off by default, right-click menu and hotkey); the pane scales its drawing when resized; alternative preview layouts.

  [![Fretboard number size](docs/screenshots/fretboard-number-size.png)](docs/screenshots/fretboard-number-size.png)

- **Show / hide the fretboard** (toolbar button and hotkey), *Switch instrument view*, per-track or all-tracks instrument view.

### Tracks and Mixer
- **Add** Guitar, Bass, Drums, Keys or any instrument (**+ Track**, with position choice), delete, move up/down, **Track properties** (F6), instrument picker, tuning editor, drum notation presets, **Global tuning**, next/previous track, multitrack (F3) and global (F8) views.
- **Track list**: arm, mute, solo, level, pan, FX button (red "!" if a plug-in was switched off), collapsible **groups**, *Show tracks in groups* (right-click empty space or from the Mixer), *Auto-resize track list to fit*, Alt+Up/Down to reorder.

  [![Track list groups menu](docs/screenshots/track-groups-menu.png)](docs/screenshots/track-groups-menu.png)

- **Mixer**: instrument, compact or ungrouped views; Master row at the top with **MON** monitoring FX (live only, never rendered); **drag and drop** tracks or whole groups (the track list reorders in step, one undo step per drop, animated); group rows set level, pan and pitch for all tracks; Master row with FX, pan and volume; smooth sliders on one fine scale; **Alt+Up / Alt+Down** moves the selected track or group.

  [![Mixer](docs/screenshots/mixer.png)](docs/screenshots/mixer.png)

- **Selected-track routing** and MIDI output device; detailed track mixer; test selected track output; MIDI / audio setup.

### Recording and clips
- **Record** (Ctrl+R) audio or MIDI clips, arm track, input meter, live monitoring, multiple take lanes, loop-recorded takes, recording offset.
- **Clip commands** (bindable): delete, deselect, move (coarse and fine), move between lanes, copy, cut, paste, duplicate, mute, properties (F2), **Snap** on/off (Alt+S).

### Plug-ins and audio engine
- **VST2 / VST3** instruments and effects, serial chains, wiring window, MIDI processing (many processors, presets, search), **group bus FX** and **master FX** chains, plug-in trust and *Review* prompts, crash isolation with a per-plug-in process option, automatic pitch matching, startup tracks.
- **Audio output**: WASAPI, ASIO, DirectSound; **Windows MIDI latency** with a **Measure** button; every song plays through the audio engine by default (a Settings toggle).

  [![Preferences, Audio and VST](docs/screenshots/prefs-audio.png)](docs/screenshots/prefs-audio.png)

- **Render window**: range, stems checklist, tail length, file names, WAV / MP3, progress and cancel.

### Playback and practice
- Play / pause, play from start, stop, loop (F9), first/last bar, previous/next section, go to section.
- Repeats up to x99, D.S./D.C./Coda/Fine, tempo changes (also mid-bar), mix-table fades, dynamics, technique playback; **score follow** with page turn or smooth page turn (stops only on a genuine manual scroll).

### Settings and help
- Preferences (F12) with search, themes (Dark, Light, System), colours, fonts, interface scale, project settings, import/export of all settings, **Keyboard shortcuts** (F1) with TabForge, Classic (influenced by Guitar Pro 5) and TuxGuitar presets, **Check for updates**, About, Windows file-association option.

### Menus, panels and windows in detail

**Score / tab editor right-click menu**
- Show standard notation, pitch up / down a semitone, move to the string above / below, delete note, zoom in / out / fit width and zoom percentages, *Score options*.
- *Score layout* (page / continuous), *Score scrolling* (vertical / horizontal), *Follow playback* (page turn / smooth page turn), *Score page* (dark / light), *Text & fonts*, *Playback line colour*, *Duration glow colour*, *Playback and glow settings*, ledger lines (standard, minimal, hidden), hide grid in empty bars, subtle bar glow.

**Fretboard / keyboard right-click menu**
- *Keyboard size* (88 to 25 keys), *Appearance* (number size, scale highlight style and colour, fret-marker colour and brightness, key colours), *Scale* (select scale, off, find scale, clear selection), *Preview next notes*, *Preview layout* and *Preview length*, *Note names*, *Left-handed*, *Lock fretboard size*, the per-track or all-tracks *Fretboard* choice, and a shortcut to Settings > Fretboard.

**Arrangement timeline menus**
- *Selected area*: copy, cut, paste before, move (click the new position), delete, loop, skip during playback, play all skipped areas again, clear selection (Esc).
- *Bar*: copy bar (this track or all tracks), copy section, paste bar into this track or all tracks, paste section here, add bar in front / behind, delete bar (this track or all tracks).
- *Section*: add section at bar, copy, cut, paste, duplicate, delete, loop section, go to section, edit title, lock / unlock position, show section brackets, same colour for similar sections, group colours.
- *Clips*: duplicate, copy, cut, paste, delete, mute, properties, write into the track's notation, advanced notation conversion, add audio file.
- Tracks can be dragged to reorder; panels close or reset from their own menu (*Reset this panel to original position*, *Close panel*).

**Track list menus and controls**
- Empty-area menu: *Show tracks in groups*, *Auto-resize track list to fit*, *Reset column layout*.
- Track and group rows: mute, solo, arm, volume and pan as **knob or slider** (choice in Settings), *Centre pan*, *Set exact pan*, track colour tint, instrument-family group headers with their own controls, right-click a group to open the Mixer at it.
- **+ Track** right-click menu: quick add by instrument family.

**FX chain window**
- Add / remove plug-ins, *Add FX chain to the end*, *Save FX chain*, *Load FX chain*, *Clear chain*, per-plug-in power (bypass), wet/dry, its own plug-in window (docked in the FX window or floating, always on top optional), *Through chain*, *GM sound* with auto-switch, *Match pitch automatically*, *Auto-load for this instrument*, *Add as a track on startup*, track volume, plug-in folders and audio settings, *Wiring* and *MIDI processing* buttons.
- **Wiring**: audio input channels, MIDI source and channel filter, sidechain 3/4, pass MIDI on, send MIDI output on, instrument output add / replace, MIDI output forwarding to another track.
- **MIDI processing**: search, add processors, reorder (move up / down), remove, enable / bypass, "Applies to" note filter with learn, drum-map folder and reload, presets (save, load, delete), *Configure MIDI input*, and a live MIDI log.

**Render window**
- Source: master mix, stems (checked or all tracks), or master plus stems; range: entire song, time selection, selected bars, custom seconds; tail (auto until silent); file name wildcards and output folder; WAV 16 / 24 / 32-bit float and MP3 128 / 160 / 192 / 320 kbps; sample rate, mono mix-down, engine rate, threads, real-time pace for streaming samplers, open the folder when done, progress and cancel, warning when a plug-in did not load.

**Preferences (F12), every page**
- *General*: check for updates, open Explorer songs in a new tab or window, autosave, default save format, file association, toolbar and status-bar visibility, confirm before closing unsaved work, warn before discarding settings.
- *Appearance & colours*: theme, UI scale, track colour tint, density, reduce animations and animation speed, accent / selection / hover colours, custom palette, fonts, icon size, score fonts, spacing, ledger and staff opacity, bar numbers, section headings, hover and selection intensity, score paper, ink and line colours, edit cursor colour.
- *Score & notation*: default display (tab + staff, tab only, staff only), follow mode and its options (horizontal / vertical, look-ahead, trigger, manual-scroll pause, frame rate), playing-note and duration tint, playback line colour and thickness.
- *Playback & sound*: metronome (click and accent notes, volumes, subdivisions), count-in bars, playback speed, note preview, let-ring limit.
- *Audio & VST*: driver, device, ASIO channels and inputs, play through the engine, GM auto-switch, pitch matching, startup tracks, Windows MIDI latency and Measure, follow Windows volume, recording device and offset, sample rate, buffer size, plug-in folders, common folders, remember the list, plug-in window docking, always on top, per-plug-in process.
- *Editing*: default note value, advance after entering, reverse + / - keys, strict bar length, confirm bar deletion, wheel scroll distance, track volume / pan control style.
- *Timeline & sections*: same colour for similar sections, brackets, glow intensity, bracket thickness, confirm section deletion, animate section dragging, scrollbar.
- *Fretboard*: position, frets, left-handed, note names, default instrument view, keyboard size and key colours, marker colour and brightness, number size, preview style (TabForge or alternative layouts), look-ahead notes, scale highlight, update during playback.
- *Tabs & windows*: window size memory, tab shape, widths, height, title size, close button, double-click / middle-click actions, new-tab position, opening behaviour, drag a tab out or onto another window, mark playing tabs.
- *Hotkeys* (search, presets, conflict warnings) and *Advanced*; settings import / export, reset page, reset all, live preview with Apply / Cancel.

**Side panes**
- *Tools*, *Structure*, *Rhythm* and *Layout* pages hold the tool palette (durations, dynamics, effects, bar structure, rhythm and layout tools); right-click a tool to pin it to the quick strip.
- *Sections*: add, edit, delete, colour, loop and go to a section. *Lyrics*: per-track lyrics editor. *Practice / Mixer*: preview, scale highlight, song stats, chord finder and track mixer. *Zoom & speed*: described above.

**Tool palette pages, every tool**
- *Tools* page: **Edit** (selection cursor, erase note, change accidental); **Duration** (whole to 64th note, dotted, double-dotted, tie, triplet, choose tuplet ratio); **Dynamic** (ppp, pp, p, mp, mf, f, ff, fff); **Beat** (chord, choose chord, text annotation, brush down / up, pick-stroke down / up); **Effects** (vibrato, bend, tremolo bar, slide, dead note, hammer-on / pull-off, ghost note, accent, heavy accent, let ring, natural harmonic, grace note, trill, tremolo picking, palm mute, staccato, tapping, slapping, popping, fade in).
- *Structure* page: **Key and bars** (key signature, triplet feel, free time, double barline); **Step through** (back / forward one beat); **Markers** (add marker, marker list, previous / next marker); **Repeats and directions** (one-bar repeat, two-bar repeat, score directions); **Bar editing** (insert bar before the cursor, add bar at the end, duplicate bar, delete bar, check bar durations); **Bars** (time signature, tempo change, repeat start, repeat end / count, alternate ending).
- *Rhythm* page: **Tuplets and ties** (N-tuplet, tie note, tie beat / chord); **Sounding pitch and duration** (sound duration, 8va, 8vb, 15ma, 15mb).
- *Layout* page: **Voices** (voice 1, voice 2, gray inactive voice); **Beaming** (automatic beaming, force beam group, break primary / secondary beam); **Stems** (automatic and inverted stem direction); **System layout** (force / prevent line break).
- Palette pages can be tabbed or side by side, and any tool can be pinned to the quick strip.

**Lyrics pane and Practice / Mixer pane**
- *Lyrics*: a collapsible text box under the Sections list; type or paste the song's lyrics, they are stored with the song when you leave the box.
- *Practice*: **Preview** (preview next notes, note names, left-handed, look-ahead count), **Scale highlight** (off or a chosen scale, 12 or 24 frets, show on the fretboard), **Song stats** (notes, tracks and bars), **Chord finder** (root, type, show, insert name), **Scale finder** (scale, show scale).
- *Mixer*: **Selected track routing** (MIDI output device, test sound, refresh devices), **Track FX** (open FX chain, Mixer), and the **detailed track mixer** table (drag a row to reorder; name, mute, solo, channel, program, volume, pan, chorus, reverb, transpose, speed).

**Preferences: Advanced page**
- An informational page only (no runtime toggles): it points to settings import / export, reset page and reset all, which are always available from the Preferences window footer.

## Quality checks

- **Architecture rules**: one-directional dependencies, no UI code in the model or playback layers, one owner
  per piece of state, one place per rule (bar length, section bounds, technique names, colour parsing...).
- **A headless regression suite of about 2,000 deterministic checks** (`TabForge.exe --selftest <log>`) covers timing,
  ties, repeats, import/export round trips, notation layout, editing semantics, hotkeys, security boundaries,
  every Settings row and scale detection. Every group is isolated so one crash cannot hide failures, and a
  missing input is reported as **SKIP**, never as a pass.
- **A live playback test** (`--playtest`) runs the real scheduler for several seconds and fails on any early,
  late or dropped note.
- **Zero compiler warnings, enforced**: any warning fails the build.

## Security and safety

TabForge treats every file, clipboard paste and settings file as **untrusted input**:

- **Bounded reads everywhere**: projects and Guitar Pro files are capped at 128 MB, settings at 2 MB, JSON
  nesting at 64 levels. Files are size-checked *before* they are read into memory.
- **Structure limits** checked before and after parsing — e.g. at most 256 tracks, 20,000 bars per track,
  1,000,000 notes, 16 strings, 64 techniques per note, 512 bend points — to keep the memory and CPU a crafted file can use bounded
  (hardened continuously; not a guarantee). Every loaded project, undo snapshot and paste is validated the same way.
- **Zip-bomb safe `.gp` reading**: the embedded project is inflated with a hard cap (its declared size is
  not trusted), and anything wrong falls back to the normal import.
- **Safe saving**: files are written to a temporary file and swapped in atomically, so a crash or full disk
  never leaves a half-written song; paths and extensions are checked before writing.
- **Crash safety**: an unexpected error is logged, every song with unsaved changes is copied to
  `%LOCALAPPDATA%\TabForge\Recovery`, and you are told where — the window stays open so you can save.
- **Plug-ins are isolated from the editor, for crashes**: folder discovery is bounded and does not run plug-in code inside
  TabForge. Plug-in identification and audio hosting use separate processes; an individual plug-in can still
  crash or hang its host, so keep backups. **This is crash isolation, not a security sandbox**: a plug-in runs
  with your user rights (in its own process too), so load only plug-ins you trust.
- **A plug-in is loaded only if you scan or approve it, and only while it is the file you approved**: a song can
  only name plug-in paths. Outside Program Files / Common Files an approval covers that exact file (its SHA-256),
  network and removable locations always need an approval, and a file that changed is not loaded until you
  approve it again (a new build signed with the same key is accepted).
- **Update check — the only network access, and deliberately tiny.** No telemetry, no auto-updates, no
  analytics. The optional update check (on by default; switch at the top of **Settings > General > Updates**,
  and a tick box in the update window itself) is limited to:
  - **one anonymous HTTPS GET, at most once a day**, to a single fixed address —
    `https://api.github.com/repos/cobhc95/TabForge/releases` — about 8 seconds after start-up, in the background;
  - **nothing sent** except the User-Agent GitHub requires (`TabForge/<version>`): no account, no cookies, no
    identifiers, no file or song information;
  - **TLS 1.2/1.3 only** with normal Windows certificate validation; **no redirects followed**, no cookies stored,
    no credentials, no decompression; 10-second timeout; the reply is capped at **256 KB** (headers 64 KB) and
    must be JSON;
  - **only two fields are read** from the reply (`tag_name` and `draft`) and a tag is accepted only if it is a
    plain version such as `v0.1.0-beta.2` (strict pattern, time-limited regex) — no text, links or files from
    GitHub are shown, opened or saved;
  - the **download page is built locally** from that validated version on `github.com/cobhc95/TabForge`, and it
    only opens in your browser when you click *Open download page*; **TabForge never downloads or installs
    anything itself**;
  - **offline or blocked networks are silent** (no error pop-ups); nothing runs when it is switched off, except
    Help > *Check for updates…* when you choose it; tests, probes and screenshot runs never contact the network.
  Diagnostics are opt-in, size-capped and never include your music.
- **Windows integration is opt-in and reversible**: it writes only TabForge's own per-user entries
  (`HKCU\Software\Classes\TabForge.*` and TabForge in each extension's *Open with* list), never takes over a
  file type another program already owns, and turning it off (or uninstalling) removes exactly those entries.
- **Minimal supply chain**: a small, listed set of dependencies (alphaTab, NAudio, MeltySynth, SoundTouch.Net, plus a native VST3
  bridge built from the Steinberg VST3 SDK; see [THIRD_PARTY.md](THIRD_PARTY.md)), pinned with lock files, restored
  in locked mode and checked by NuGet's vulnerability audit on every build.

## Resources and performance

TabForge is built to stay light while you write and play:

- No per-frame work unless something is actually moving: the score, timeline and fretboard redraw only what
  changed; drum-hit glows animate only while they fade.
- Precompiled (ReadyToRun) and memory-conserving .NET settings; memory is returned to Windows after loading
  a song; undo history is stored compressed (about 1% of the raw size).
- Playback runs on its own high-precision thread; the interface only observes it, so a busy UI never
  makes the music stutter, and settings changes never restart the engine unless they change the sound.
- Adjustable in Settings: smooth-scroll frame-rate cap, follow mode (off / jump / smooth), fretboard and
  highlight effects, panels you do not need can be hidden.
- On the author's test PC it typically uses **~150–250 MB RAM and ~1–2% CPU** in normal use on a modern CPU.

## Keyboard basics

| Keys | Action |
|---|---|
| `0`–`9` | Enter a fret (type two digits quickly for 10+) |
| `←` `→` / `↑` `↓` | Previous/next beat / string up/down |
| `+` `-` `.` | Shorter / longer note (reversible in Settings), dotted |
| `Space` / `Shift+Space` | Play/pause / play from the start |
| `F9` | Loop on/off |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |

## Building from source

Requires the .NET 8 SDK on Windows.

```cmd
dotnet build src\TabForge\TabForge.csproj -c Release
```

Or publish a self-contained build with `dotnet publish src\TabForge\TabForge.csproj -c Release`.

## Releases and issues

Downloads and release notes: [Releases](https://github.com/cobhc95/TabForge/releases). Bugs and ideas:
[Issues](https://github.com/cobhc95/TabForge/issues) - the song file and the steps to reproduce help a lot.

## Licence

MIT — see [LICENSE](LICENSE). Guitar Pro file reading/writing uses [alphaTab](https://github.com/CoderLine/alphaTab) (MPL-2.0);
see [THIRD_PARTY.md](THIRD_PARTY.md). Full licence texts ship in the `licenses/` folder (SoundTouch.Net, LGPL-2.1, is a replaceable DLL). Guitar Pro is a trademark of Arobas Music; TabForge is an independent project and is not affiliated with,
sponsored or endorsed by Arobas Music, Steinberg, Toontrack or any other company named here.
