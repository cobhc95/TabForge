# Headless screenshot capture (`--capture`)

Diagnostic only; never used in normal runs.

```
TabForge.exe --profile <scratch folder> --capture <script.json> <outDir>
```

- `--profile` is required (and must not be the real user folder). Settings, recent files and plug-in lists of the scratch profile are empty.
- The real main window is created at Left/Top = -20000, `ShowActivated=false`, no taskbar button, root DPI 2x. Nothing appears on a monitor and focus is never taken.
- Tool windows and dialogs are placed off-screen the same way (modal dialogs are intercepted through `DialogHost.Capture`).
- Menus and right-click menus are never opened (a popup would be pulled back onto a monitor): the menu's template content is laid out in a detached host and rendered. Document them with `target: "menu"` after a `menu` or `context` step.
- Shots are PNGs at 2x (a 1600x1000 window gives 3200x2000). Window chrome of tool windows (title bar) is not part of the image.
- Per script, `<script>.log` and `<script>.report.json` (id, size, theme, track, bar, notation, selection, window) are written next to the PNGs. A failed step is logged and the script continues; the exit code is 1 if any step failed.
- The demo song or a file under a `samples` folder may be opened; anything else (and anything under `Tabs`) is refused.
- Defaults at start: demo song, dark theme, 1600x1000.

## Steps

A step is a JSON object; its first property is the verb. Other properties are options.

| Step | Meaning |
|---|---|
| `{"open":"demo"}` / a path under `samples` | open a song |
| `{"theme":"dark"\|"light"}`, `{"paper":"dark"\|"light"}` | interface theme / score paper |
| `{"size":[w,h]}`, `{"scale":1.25}` | window size in DIPs, UI scale |
| `{"notation":"tab"\|"staff"\|"both"}` | score view mode |
| `{"view":"vertical"\|"horizontal"\|"page"\|"continuous"}` | score layout |
| `{"track":0}` | select track (0-based) |
| `{"bar":12}` | go to bar (1-based, as shown in the app) |
| `{"select":[12,14]}`, `{"clear-selection":true}` | selection (bars, 1-based; score and timeline mirror it) |
| `{"playhead":[13,0.4]}` | draw the playhead at bar 13, 40% |
| `{"band-layout":"above"}` | put every Band view row's instrument above its tab (`"beside"` puts it back) |
| `{"band-playing":[13,0.4]}` | show the Band view as playing at bar 13, 40% of it (select the `band` panel first; no audio runs) |
| `{"learn-playing":[13,0.4]}` | show the Keyboard mode pane as playing the selected track (any kind, at its sounding pitches) at bar 13, 40% of it (select the `learn` panel and the track first; no audio runs). A third value `"keys"` turns the selected track into a keyboard track with a two-hand riff (in memory) and fills the falling-notes view with made-up held keys, score and grade; `"keys-wait"` shows the wait cue instead. Run it before `{"panel":"learn"}` |
| `{"learn-midi-menu":true}` | render the MIDI input list of the Keyboard mode control bar as it shows when opened (after `{"panel":"learn"}`; then `target: "menu"`) |
| `{"panel":"sections"}` / `"hide:tools"` | show and select a dock panel (ids: instrument, timeline, tools, structure, rhythm, layout, sections, learn, playback; `band` switches to the Band layout, `hide:band` leaves it as the toolbar button does) |
| `{"layout":"Compose"\|"Practice"\|"Mix"\|"Band"}` | switch layout (every run starts from the default layout, even with a reused profile) |
| `{"restore-saved":true}` | bring back the layout the profile was saved with (as after a restart) |
| `{"instrument":"fretboard"\|"keyboard"\|"drums"\|"auto"}` | instrument view |
| `{"highlight-scale":"Minor Pentatonic"}` (`null` clears) | fretboard scale |
| `{"window":"Mixer"}` | open a tool window: Mixer, FxChain, FxChain-example, Render, Preferences (`"page":"Appearance"`), AddTrack, TrackProperties, Tuner, SongInfo, ProjectSettings, GlobalTuning, ScaleFinder, InstrumentPicker, ScoreTextStyle, MixTable, ChordFinder, SongStats, NewFromTemplate, CommandPalette (`"text":"tun"`), AddPlugin, Shortcuts, PasteOptions, KeyboardModePopout (Keyboard mode on and the keyboard view in its own window; run `learn-playing` with `"keys"` first) |
| `{"window":"Mixer"}` | open a tool window: Mixer, GroupRules, FxChain, FxChain-example, Render, Preferences (`"page":"Appearance"`), AddTrack, TrackProperties, Tuner, SongInfo, ProjectSettings, GlobalTuning, ScaleFinder, InstrumentPicker, ScoreTextStyle, MixTable, ChordFinder, SongStats, NewFromTemplate, CommandPalette (`"text":"tun"`), AddPlugin, Shortcuts |
| `{"page":"Audio & Plug-ins"}`, `{"search":"tempo"}` | Preferences page / search box (after opening Preferences) |
| `{"collapse-groups":true\|false\|"Guitars"}` | collapse every mixer group, expand every group, or collapse one group |
| `{"mixer-rules":"example"\|"reset"}` | apply example custom mixer groups (Piano, Pads & strings) to the open song, or drop its own rules |
| `{"scroll-mixer":"end"}` | scroll the open Mixer to its last group |
| `{"close":"Mixer"\|"all"}` | close tool windows |
| `{"menu":"File"}`, `{"menu":"View/Layouts"}` | render a main menu or submenu |
| `{"context":"score"\|"note"\|"timeline"\|"fretboard"\|"timeline-empty"\|"track-row"\|"mixer-colour"}` | render a right-click menu (`mixer-colour`: the selected track's colour palette from its mixer chip) |
| `{"popup":"metronome"\|"countin"\|"loop"}` | render a playbar popover (then `target: "menu"`) |
| `{"example-plugins":true}` | add placeholder slots "Example instrument" and "Example effect" to the selected track (removed when the run ends). Also: windows `FxChain-example`, `Wiring`, `MidiProcessing` |
| `{"quarantine-example":true}` | after `example-plugins`: the next FX chain window shows the example instrument as "Switched off after a crash" with "Allow again" |
| `{"midi-add":["velocity","transpose"]}`, `{"midi-select":1}`, `{"midi-search":"random"}`, `{"midi-param":["Mul",0.8]}` | drive the open `MidiProcessing` window: add processors by catalog type, select a list row, type in the processor search, set a parameter of the selected processor (a number, or a text such as `["To","Tabs to SD3"]`) |
| `{"note-map-editor":"shipped"\|"copy"\|"advanced"\|"error"}` | open the drum map editor as the tool window `NoteMapEditor` on the shipped kit map against General MIDI: read-only as shipped, a duplicate with one more changed row, the changed rows with the piece grid open, or a refused map with its message |
| `{"preset-panel":"seed"}`, then `"plugin"\|"chains"\|"midi"\|"processor"\|"midi-rename"` | `seed` writes example presets of every kind into the scratch profile (before the window opens); the others draw that drop-down's open list, with the organisable rows of the user's presets, over the last opened tool window (`FxChain-example` for plugin and chains, `MidiProcessing` with a `velocity` processor for the rest), under its drop-down. `midi-rename` shows a row being renamed with a refused name |
| `{"fx-list":"five"\|"select3"\|"drag"\|"containers"\|"container"\|"drop-into"\|"drop-before"\|"drop-between"\|"drop-end"}` | on the open `FxChain-example` window: fill the chain to five example plug-ins, select three of them, lift the selected rows as a drag in progress; put them in an open and a collapsed container, focus the first container (its pane shows), drag a plug-in onto the collapsed container, to the top edge of the open container (in front of it), between two of its plug-ins, to its end (inside) |
| `{"preset-manager":"plugin"\|"processor"\|"search"\|"builtin"\|"selected"}` | after `{"preset-panel":"seed"}`: open the preset manager as the tool window `PresetManager` on the example plug-in, on MIDI processors › Velocity, with a search typed, or on the drum maps with a built-in map selected; `selected` seeds the containers with the example presets and selects three of them (shot with `"target":"window:PresetManager"`) |
| `{"message-box":"delete"}` | the preset delete question for three presets, with its unticked "Don't ask again when deleting presets" check box, as the tool window `MessageBox` (shot with `"target":"window:MessageBox"`); `"warning"` and the default are the message box's own examples |
| `{"fx-list-width":420}` | width of the FX chain window's plug-in list (before the window opens) |
| `{"timeline-drag":"drop"|"dropnew"|"dropbelow"|"move"}`, `{"playhead-style":"Both"}` | draw a media drag or clip move over the timeline; set the playback marker style |
| `{"clip-edit":true}`, `{"delete-prompt":true}`, `{"marker-size":140}` | after `audio-track`: fade and split the example clip and select it; show the bar-range Delete prompt over the selection (shoot `window:DeletePrompt`); fretboard note marker size in percent |
| `{"timeline-height":150}`, `{"section-tip":0}` | squeeze the timeline pane to a height (DIPs) as a user collapse does; render the section lane's hover hint for a section (then `target: "menu"`) |
| `{"rec-state":true\|false}` | show (or hide) the Record video REC state without recording: the red dot on the title-bar camera button and the status bar's REC time |
| `{"tab-strip":1100}` | a title-bar tab strip that wide with idle, playing (inactive and active), unsaved, long-titled and playing + unsaved tabs (shoot `window:TabStrip`) |
| `{"frames":"name","action":A,"p":[..],"count":40,"interval":50,"hold":8,"target":T,"region":[..]}` | animated sequence: writes `name_000.png`.. and `name.frames.json` (ms of each frame) for assembling a GIF outside the app. Actions: `playback` (p = from, to as bar.fraction), `band-playback` (same, drives the Band view), `track-drag` (rows from, to), `section-drag` (section index, land-before index; optional `"zoomout":N`), `area-move` (first bar, last bar, insert-before bar, after `select`), `record` (start bar, seconds; grows a synthetic live take on a new audio track), `clip-edit` (after `audio-track`), `type-notes` (bar, duration; `"notes":[[string,fret],..]`), `mixer` (track; after `window:Mixer`) |
| `{"effect":"bend"\|"tremolobar"\|"trill"\|"grace"\|"harmonic"}` | open a note-effect editor on the note at the cursor (track and bar first), leave its dialog open as the tool window `Effect<Kind>` (`EffectBend`, `EffectTremoloBar`, `EffectTrill`, `EffectGrace`, `EffectHarmonic`) and answer Cancel; then `{"shot":"effect-bend-dark","target":"window:EffectBend"}` photographs it and `{"close":"EffectBend"}` closes it. Needs a note at the cursor on the selected string (a `note` step), else the step fails |
| `{"note":[string,fret]}` | capture only: types a fret on the selected string of the first beat of the current bar, in memory (the song is never saved). Run it after `{"bar":n}` and before an `effect` step; the demo song has no note at bars 2, 5 and 9 |
| `{"live-input":true\|false}` | show the live input button lit or not (no audio runs, nothing is put in the engine) |
| `{"controls-width":400}` | set the track list's controls column width (raised to its minimum, where the toolbar shows two rows) |
| `{"compact-mode":true\|false}` | turn compact mode on or off through its View menu row |
| `{"master-track":true\|false\|3}` | show or hide the master track lane above track 1; a number shows it and keeps only that many tracks of the open song (in memory) |
| `{"solo":4}` | solo a track |
| `{"dump":"song"}` | write `<script>.song.txt`: sections with bars, tracks, time signatures, tempo changes, repeats |
| `{"wait":500}` | wait (ms) |
| `{"shot":"id","target":T}` | write `id.png`. Optional: `"region":[x,y,w,h]` (DIPs inside the target), `"settle":ms` |

Targets: `window` (default), `window:<Tool name>`, `menu` (last menu/context step), `panel:<dock id>` (pane with its tab strip), `score`, `timeline`, `fretboard`, `tracks`, `tools`, `sections`, `transport`, `titlebar`, or `element:<x:Name or AutomationId>`.

Privacy: the status bar's audio device text is replaced by a generic text before each shot.

Not capturable: items that open only through real popups other than the menus above (combo-box drop-downs, tooltips, the timeline's section/clip/track-row menus, the metronome/count-in/loop popovers), native file dialogs and message boxes, and anything needing live audio.

## Fixture, pointer and live-strip verbs (CAPTURE2)

| Step | Meaning |
|---|---|
| `{"audio-fixture":"repeat"\|"end"}` | add audio tracks with synthetic clips: over the repeat (a long clip and a pickup clip starting in pass 2), or a short clip near the song's end |
| `{"timeline-scroll":"end"\|N}` (`"target":"score"`) | scroll the timeline (or the horizontally scrolling score) to the end or to bar N |
| `{"pointer":"press","clip":[track,clip]}` / `{"bar":5.5,"zone":"section"\|"ruler"\|"lanes","row":2}`, `{"pointer":"move","by-bars":4.5}`, `{"pointer":"release"}` | a real routed pointer drag on the timeline |
| `{"record-fake":"start","bar":19}`, `"grow"` (`"sec"`), `"stop"` | a growing live take with a made-up input level, landing as a clip |
| `{"live-fx":"open"}`, `{"live-fx":"readings","in":0.7,"out":0.5,"hz":82,"repeat":6}` | the live input FX window (target `window:LiveInputFx`) with meter and tuner readings set by the script, no audio |
| `{"fx-chain":"reaper"}` | an imported REAPER chain with a container; plug-ins are not installed (target `window:FxChain`) |

Shot names must be relative paths under `--out`; the status bar's audio device text is replaced by "Audio device" before each shot.
