# src/TabForge (project root)

The application's root files: the process entry, the application start, and `MainWindow`, which is split into partial files by topic. `MainWindow` and `TabEditorControl` are composition roots: they wire things together. New behaviour goes into a small class with a host interface, never into another `MainWindow` partial.

## Files
| File | Purpose |
| --- | --- |
| `Program.cs` | Process entry; the audio engine mode (`--audio-engine`) runs without WPF |
| `App.xaml.cs` | Application start: command-line options (each `--option` runs a handler on the opened main window), recovery |
| `LocalReferenceSongs.cs` | Resolves the local-only reference songs for the self-tests, by key |
| `GpDialogs.cs` | Small standard modal dialogs built in code (no XAML) |
| `MainWindow.xaml.cs`, `MainWindow.Lifetime.cs`, `MainWindow.Keyboard.cs`, `MainWindow.ProbeAccess.cs` | Window state; what the window attaches and undoes once on close; keyboard; the probe access for command-line checks |
| `MainWindow.Documents.cs`, `MainWindow.Tabs.cs`, `MainWindow.File.cs`, `MainWindow.Import.cs` | Activating documents; title-bar tabs; new, open, save, export, print; background import |
| `MainWindow.Editing.cs`, `MainWindow.Selection.cs`, `MainWindow.EffectEditors.cs`, `MainWindow.Palette.cs`, `MainWindow.Tools.cs`, `MainWindow.ScaleFinder.cs` | Score commands; the shared selection; note-effect entries; the tool palette; the Tools menu; the scale finder |
| `MainWindow.Menus.cs` (the table of plain main-menu commands), `MainWindow.ContextMenus.cs`, `MainWindow.TimelineMenus.cs`, `MainWindow.TrackMenu.cs`, `MainWindow.MenuPrewarm.cs` | Right-click menus built from menu specs; the track-row menu and its keys; prewarming one hidden menu at idle |
| `MainWindow.Arrangement.cs`, `MainWindow.Tracks.cs`, `MainWindow.AudioTrack.cs`, `MainWindow.Clips.cs`, `MainWindow.Instrument.cs` | The arrangement timeline; track and mixer headers; audio tracks; clip lanes; the fretboard panel |
| `MainWindow.Playback.cs`, `MainWindow.Recording.cs`, `MainWindow.VideoRecord.cs` | Transport, playhead and score following; record arm and recording; the video recording hooks |
| `MainWindow.Commands.cs` | The plain menu and key commands as `id -> handler` registry lines (the rest of the command routing is `RunHotkey` in `MainWindow.Settings.cs`) |
| `MainWindow.Mixer.cs`, `MainWindow.Settings.cs`, `MainWindow.SettingsApply.cs`, `MainWindow.View.cs`, `MainWindow.MediaApproval.cs`, `MainWindow.Autosave.cs`, `MainWindow.Updates.cs` | The Mixer window and FX windows; settings load, the ordered apply (`SyncFromSettings`), the per-area appliers and save; the dock layout and view; approval notices; autosave and background services; the update check |

## Pathway
`START_HERE.md`, Golden pathways: **Edit the song**, **Save and close**, **Open a song**, **Settings** and **Commands**.

## Tests
`docs/feature-map/windows-tabs-and-documents.md`.
