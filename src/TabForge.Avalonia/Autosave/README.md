# Autosave: one window's autosave

The copy rules, the registry and the recovery files are Core (`TabForge.Autosave`, `AutosaveRunner`, `AutosaveHealth`); this folder is the
window's side of it. It is one clock and one notice, not a document feature.

| Class | Job |
|---|---|
| `MainWindowAutosave` | One window's autosave: the cheap tick (the interval setting is read live), the pass over the window's songs through `AutosaveRunner`, the notice bar that says while autosave is failing (with a retry), and the start-up offer to reopen the songs a crashed session left behind (the first window that asks gets it). |
| `MainWindowAutosaveHost` | The window's side of its autosave: its tabs, the process's windows (for songs that left a window), the autosave interval, whether a song plays, the recovery question, and the tab a recovered song opens in. |

Rules: the clock is cheap and idle when nothing is dirty; a failing autosave never interrupts editing; recovery stays an offer, never a silent
reopen.

Tests: `MainWindowAutosaveTests`. Run the Avalonia test project locally with
`--filter "FullyQualifiedName~MainWindowAutosaveTests"`.
