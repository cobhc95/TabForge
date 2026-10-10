# Feature map

Index. Find a feature here, then open its page for the main code, the pathway to use and the self-tests. Read `CONTRIBUTING.md` for the rules and `ARCHITECTURE.md` for the design.

- Feature pages (hand-kept): `docs/feature-map/<feature>.md`.
- Every test, area and group (generated, do not edit): [docs/feature-map/tests.md](feature-map/tests.md). Refresh with `TabForge.exe --feature-map`.
- Every source file with its folder and a one-line purpose (generated, same command): [docs/feature-map/files.md](feature-map/files.md).
- Find a keyword across the map, tests and folder READMEs: `TabForge.exe --find <keyword>`.

Run a self-test area with `TabForge.exe --selftest <log> --areas <area>`, then read the last line of the log (`N passed, M failed, K skipped`). A group runs with the whole suite and `--require ci`.

## Features

| Feature | Owns | Page |
| --- | --- | --- |
| Editing and notation | Score model, edit commands, notation view, undo | [editing-and-notation.md](feature-map/editing-and-notation.md) |
| Playback | Score-to-MIDI timeline and playback engine | [playback.md](feature-map/playback.md) |
| Mixer and audio engine | Mix engine, track chains, engine client | [mixer-and-audio-engine.md](feature-map/mixer-and-audio-engine.md) |
| Recording | Recording controller and engine capture | [recording.md](feature-map/recording.md) |
| Import and export | Score files, MIDI, MusicXML and ASCII read and write | [import-and-export.md](feature-map/import-and-export.md) |
| Timeline and clips | Arrangement panel, track timeline, clip edits | [timeline-and-clips.md](feature-map/timeline-and-clips.md) |
| Settings and Preferences | Settings store, validation, migration, Preferences window | [settings-and-preferences.md](feature-map/settings-and-preferences.md) |
| Windows, tabs and documents | Document sessions, save and close flows, window lifetime | [windows-tabs-and-documents.md](feature-map/windows-tabs-and-documents.md) |
| Plug-ins | Plug-in trust, MIDI processors, isolated hosting | [plug-ins.md](feature-map/plug-ins.md) |
| Tutorial | Tutorial window, guide library, PDF export | [tutorial.md](feature-map/tutorial.md) |
| Band view | One row per track: instrument beside a tab lane, and the Band layout | [band-view.md](feature-map/band-view.md) |
| Rendering to file | Render jobs and the offline renderer | [rendering-to-file.md](feature-map/rendering-to-file.md) |
| Video | Video export (MP4), frame source, encoder and live Record video | [video.md](feature-map/video.md) |
| Instrument views | Fretboard, keyboard and drum views of the instrument panel, and the view choice | [instrument-views.md](feature-map/instrument-views.md) |
| Note right-click menu | Note and score empty-area right-click menus, and Shift+F10 on the score | [note-context-menu.md](feature-map/note-context-menu.md) |
| Feature modules and hosts | Feature module shape and registry, the Band and Video modules, the Mixer, Dock and Band host classes | [feature-modules-and-hosts.md](feature-map/feature-modules-and-hosts.md) |
| Menus and commands | Command table, main-menu table and golden files, settings appliers, dock pane and technique tables, engine message pairs | [menus-and-commands.md](feature-map/menus-and-commands.md) |
