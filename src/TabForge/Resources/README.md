# Resources

Data files shipped with the app.

## Contents
- `src/TabForge/Resources/DrumMaps/`: JSON drum maps (General MIDI and common drum libraries), loaded through `DrumMapLibrary`.

## Pathway
Add a map as a JSON file in the DrumMaps folder; `DrumMapLibrary` and `DrumMaps` (in `src/TabForge/Services/`) pick it up.

## Must not depend on
Nothing; these are data only.

## Tests
`TestMidiProcessors`, `TestDrumAndMelodicDispatchTogether`.
