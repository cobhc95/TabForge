# Features/Band

The Band view's rules and its Preferences rows. The view is in `TabForge.Avalonia`.

## Files
| File | Owns |
| --- | --- |
| `BandRowRules.cs` | the rules of how many rows (systems) a track's Band pane shows: the limits, the snap of a dragged height to whole rows, and the   per-song store of the chosen counts (SongProject.BandPaneRows, by track id). |
| `BandSettingsRows.cs` | the "Band view" rows of the settings catalogue (Timeline page); BandFeatureSettings hands them to the catalogue. |

Tests: see the `// Tests:` line at the top of each file.
