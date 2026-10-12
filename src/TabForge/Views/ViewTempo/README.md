# Views/ViewTempo

The View and tempo pane's row of four equal buttons: score zoom, playback speed, tempo, and time signature with key. A click (or Enter / Space) opens a menu of presets; a right click (or the Menu key) opens a small popup to fine-tune; the wheel over zoom or speed steps it. Does not own the zoom, speed or tempo logic: the bar reaches the app only through `IViewTempoHost`.

In the main window the pane keeps its value controls (`ZoomCombo`, `SpeedCombo`, `TempoBox`, `TimeSigLabel`, `KeyLabelText` and the two zoom step buttons) in an unseen zero-size host; the rest of the window reads and writes them as before, and the bar drives them and shows their text.

## Files
| File | Purpose |
| --- | --- |
| `ViewTempoBar.cs` | The four buttons (a `UniformGrid`, so they always share the row equally): captions, tooltips, and opening the menu or popup |
| `ViewTempoControlsHost.cs` | `IViewTempoHost`, and its implementation over the pane's value controls, found by name on load |
| `ViewTempoMenus.cs` | The preset menu of each button, current value checked, live keys on command rows |
| `ViewTempoPopups.cs` | The fine-tune popup of each button: slider, exact-value box, reset or step buttons |

## Tests
`TestToolbarZoomAndSpeed` (values applied through the bar, captions, tooltip), `TestToolbarZoomSpeedNarrow` (one row of equal, whole buttons at a 250 px content width).
