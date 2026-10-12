# Views/MasterLane

The optional master track: a display-only overview lane above track 1, across the track list and the timeline. Off by default
(Settings > Timeline & Tracks, or "Show master track" in the track list's right-click menus). Tests: `TestMasterLane`.

| File | Owns |
|---|---|
| `MasterLane.cs` | The lane's rules: label, default colour and opacity range, which songs show it, and the one-row offset the row geometry adds above track 1 (`OffsetOf`). |
| `ArrangementPanel.MasterLane.cs` | The track list's half: `ShowMasterLane`, the "Master" row, `ApplyMasterLane` (the three settings), the menu item and the row menu's command. |
| `ArrangementPanel.MasterControls.cs` | The Master row's controls: master FX button, master volume and master pan in the track rows' columns, wired to the state the Mixer's Master row uses (`MasterMix`, the master volume knob, `Mixer.MasterPan`, `Mixer.Master`). |
| `TrackTimeline.MasterLane.cs` | The timeline's half: one thin line per track, filled per bar where the track has notes or a clip (`MasterLaneHasContent`), and its drawing. |

Hooks in existing code: `ArrangementPanel.RowTopOf` / `RowIndexAt` start below the lane (every row position follows from those two),
`RebuildControls` adds the row, `Bind` marks the song, `BuildEmptyAreaMenu` and `TrackRowMenus.Build` add the menu item, and
`TrackTimeline.RenderCore` calls `DrawMasterLane` once per full render. Nothing is saved in the song or sent to the engine.
