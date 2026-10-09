# Timeline and clips

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Views/` (`ArrangementPanel`, `TrackTimeline`); `ArrangementController`
- **Pathway to use:** Clip and lane edits are `DocumentEdits.Run`. Audio files are handled with the document's `MediaContext`.
- **Tests:** `--areas recording` (clips, drops), `--areas ui` (geometry, menus); `TestClipMoves`, `TestMediaDropPlan`, `TestArrangementGeometry`, `TestTimelineContextMenus`
