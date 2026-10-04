# Visualization

Fretboard, drum and keyboard displays: geometry and drawing for the instrument panel.

## How to change me
1. Entry files: `InstrumentVisualizer.cs`, `FretboardGeometry.cs`.
2. Owner class: `InstrumentVisualizer` (drawing), `FretboardGeometry` (layout).
3. Tests to run: `--areas ui,notation` (full-suite build); check in the running app, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`.

## Key types
- `InstrumentVisualizer`: picks the renderer and draws the current state.
- `InstrumentVisualState`, `VisualNote`: what is sounding now.
- `FretboardGeometry`: string and fret positions.
- `FretboardRenderer`, `DrumRenderer`, `KeyboardRenderer`: the drawings.
- `VisualOptions`: colours and display options.

## Pathway
`InstrumentPanel` (`src/TabForge/Views/`) feeds the visual state from playback; the renderers only draw it. Drawing stays lightweight, with no layout work per frame.

## Must not depend on
Song editing or the audio engine.

## Tests
`TestInstrumentVisualState`, `TestFretboardGeometry`, `TestInstrumentArtwork`, `TestGp5SvgIcons`.
