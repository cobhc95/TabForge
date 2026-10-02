# Visualization

Fretboard, drum and keyboard displays: geometry and drawing for the instrument panel.

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
