# Views/Band

The Band view: one strip or row per track, scrolled together and following the playhead. Does not own the score editor (`../Score/`, used inside each lane) or the layout of the main window. The view reaches its window only through `IBandViewHost`.

## Files
| File | Purpose |
| --- | --- |
| `BandAppearance.cs` | Copies the main score's look (paper, ink, lines, spacing, labels) onto a lane's editor |
| `BandFollow.cs` | How a lane follows the playhead: `BandFollowMode` (hold, jump, continuous), from the score's follow setting |
| `BandHost.cs` | `IBandViewHost` over narrow delegates: the window's pane host, the song, instrument options, cursor, track move and the Band menu's zoom and settings items; `MainWindow` only builds it |
| `BandLane.cs` | One track's strip (tab, notation or both), engraved once and slid with a render transform |
| `BandLaneHover.cs` | The note length outline under the mouse in a lane: mouse moves handed to the lane score's own hover handling |
| `BandLayoutMenu.cs` | The roll-down button after the caption and its menu: fretboard beside or above the tab, track colour frame; the two commands behind it |
| `BandLayoutState.cs` | Which tracks show and in what order (the only part kept in the song), this view's row heights, widths and instruments, started from the Band settings and remembered there as rules for every tab; the drop maths of a row drag |
| `BandNoteGlow.cs` | The glow of the notes sounding in one lane |
| `BandReorder.cs` | Dragging a row by its name strip; the others glide aside and the drop slides the row into place |
| `BandRow.cs` | One track's row: name and instrument view on the left, tab lane on the right, a grip on the bottom edge |
| `BandRowFit.cs` | The default row heights: two systems in the main (first) row and one in the others when that fits, rows exactly as tall as their content, spare height to whole systems; the fitted height of an instrument above its lane |
| `BandRowFrame.cs` | A row's track colour frame: a rounded line and a dim halo of plain border rings beside the content (no Effect) |
| `BandScroll.cs` | The one scroll position every lane stands at |
| `BandWidthDrag.cs` | A drag of the instrument/lane grip: the column width once per frame, the lanes fitted and the width stored when it ends |
| `BandView.cs` | The panel's content: track pills on top, a list of rows sized to fit below |
| `BandViewController.cs` | the `IBandViewHost` contract and the controller that builds the rows and ticks the frame while the panel is on screen |

## Pathway
Registrations (the Band hotkey rows and commands, the "Band view" Preferences rows and group) live in `Services/Band/` (`BandFeatureModule`), not in this folder.
`START_HERE.md`, Find a feature (`docs/feature-map/band-view.md`).

## Tests
`docs/feature-map/band-view.md`.
