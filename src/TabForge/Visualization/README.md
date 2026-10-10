# Visualization

Fretboard, drum and keyboard displays: the visual state of one frame, the layout, and the drawings for the instrument panel.

## How to change me
1. Entry files: `InstrumentVisualizer.cs` (builds the visual state), `Renderers.cs` (the drawings), `FretboardGeometry.cs` (layout).
2. Owner classes: `InstrumentVisualizer` (state from the score and playback), `FretboardGeometry` (string and fret positions), and one `IInstrumentRenderer` per instrument in `Renderers.cs`.
3. Tests to run: `--areas ui,notation` (full-suite build); check in the running app, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`.

## Key types
- `InstrumentVisualizer`: static builder. `InstrumentVisualizer.Build` and `InstrumentVisualizer.BuildEditingSelection` turn playback, editing or settings state into an `InstrumentVisualState`; `InstrumentVisualizer.KindOf` and `InstrumentVisualizer.NaturalKind` give the instrument kind of a track. It does not pick a renderer.
- `InstrumentVisualState`, `VisualNote`: what is sounding now.
- `IInstrumentRenderer`: the drawing contract; `FretboardRenderer`, `DrumRenderer`, `KeyboardRenderer` (all in `Renderers.cs`) implement it.
- `FretboardGeometry`: string and fret positions.
- `FretboardStrum`: a strummed chord's stroke arrow and merged tag pill on the fretboard.
- `FretboardConnector`: decides whether the fretboard draws a movement line from the sounding shape to the next shape (never for a repeated or already-sounding chord).
- `VisualOptions`: colours and display options.
- `MarkerInk`: white or near-black marker numbers, whichever reads on the marker's real surface.

## Pathway
`InstrumentPanel` (`src/TabForge/Views/InstrumentPanel.cs`) feeds the visual state from playback. Its `InstrumentPanel.ApplyActiveState` picks the renderer from `state.Kind` (drums give `DrumRenderer`, keyboard `KeyboardRenderer`, anything else `FretboardRenderer`) and swaps it only when the kind changes. The renderers only draw the state; drawing stays lightweight, with no layout work per frame.

## Must not depend on
Song editing or the audio engine.

## Tests
`TestInstrumentVisualState`, `TestFretboardGeometry`, `TestInstrumentArtwork`, `TestGp5SvgIcons`.
