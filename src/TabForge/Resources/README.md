# Resources

Data files shipped with the app.

## How to change me
1. Entry files: the JSON file in `src/TabForge/Resources/DrumMaps/`.
2. Owner class: `DrumMapLibrary` (loads the maps).
3. Tests to run: `TestDrumEntryAndQuickAddBars` (full-suite build); `--areas hygiene`, plus `--areas architecture,hygiene` (see the test box in `docs/TESTING.md`).
4. Docs to update: `CHANGELOG.md`.

## Contents
- `src/TabForge/Resources/DrumMaps/`: JSON drum maps (General MIDI and common drum libraries), loaded through `DrumMapLibrary`.

## Pathway
Add a map as a JSON file in the DrumMaps folder; `DrumMapLibrary` and `DrumMaps` (in `src/TabForge/Services/`) pick it up.

## Must not depend on
Nothing; these are data only.

## Tests
`TestMidiProcessors`, `TestDrumAndMelodicDispatchTogether`.
