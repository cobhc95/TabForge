# Shell

Window plumbing shared by every window: lifetime, tab tear-off, frame timing.

## Key types
- `OwnedSubscriptions`: event subscriptions released together when a window closes.
- `ChildEventRelease`: releases child events so a closed window can be collected.
- `TabWindowRegistry`: the open tab windows.
- `BrowserTabDragPolicy`: tab drag and tear-off decisions.
- `FrameTicker`: one frame timer for the playhead.
- `AppOptions`: the playback and view options every window and song shares (created by `App`, handed to each `MainWindow`).
- `WindowPolish`: caption and resize-border handling.

## Pathway
Subscribe to events through `OwnedSubscriptions`, never with a bare `+=` that outlives the window. Helpers held by a window keep their host in interface-typed fields (`ChildEventRelease` clears delegate fields).

## Must not depend on
Song data or the audio engine.

## Tests
`TestWindowLifetime`, `TestRepeatedOpenCloseReleasesWindows`, `TestBrowserTabShell`, `TestTabUi`.
