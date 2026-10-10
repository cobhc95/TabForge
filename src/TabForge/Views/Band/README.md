# Views/Band

The Band view: one strip or row per track, scrolled together and following the playhead. Does not own the score editor (`../Score/`, used inside each lane) or the layout of the main window. The view reaches its window only through `IBandViewHost`.

## Files
| File | Purpose |
| --- | --- |
| `BandAppearance.cs` | Copies the main score's look (paper, ink, lines, spacing, labels) onto a lane's editor |
| `BandFollow.cs` | How a lane follows the playhead: `BandFollowMode` (hold, jump, continuous), from the score's follow setting |
| `BandHost.cs` | `IBandViewHost` over narrow delegates: the window's pane host, the song, instrument options, cursor, track move and the Band menu's zoom and settings items; `MainWindow` only builds it |
| `BandLane.cs` | One track's strip (tab, notation or both), engraved once and slid with a render transform |
| `BandLayoutState.cs` | Which tracks show, in what order, how many rows fit and each row's height; the drop maths of a row drag |
| `BandNoteGlow.cs` | The glow of the notes sounding in one lane |
| `BandReorder.cs` | Dragging a row by its name strip; the others glide aside and the drop slides the row into place |
| `BandRow.cs` | One track's row: name and instrument view on the left, tab lane on the right, a grip on the bottom edge |
| `BandScroll.cs` | The one scroll position every lane stands at |
| `BandView.cs` | The panel's content: track pills on top, a list of rows sized to fit below |
| `BandViewController.cs` | the `IBandViewHost` contract and the controller that builds the rows and ticks the frame while the panel is on screen |

## Pathway
Registrations (the Band hotkey rows and commands, the "Band view" Preferences rows and group) live in `Services/Band/` (`BandFeatureModule`), not in this folder.
`START_HERE.md`, Find a feature (`docs/feature-map/band-view.md`).

## Tests
`docs/feature-map/band-view.md`.
