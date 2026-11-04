# Add an import format (one page)

Opening a song goes through DocumentController / ScoreOpenFlow, then ImportWorker (a separate, budgeted process), then GuitarProImporter for the actual conversion. alphaTab reads the file format; TabForge converts its score into a SongProject. The pieces are:

- Source/src/TabForge.Core/Services/Files/FileTypes.cs: Importable, built from GuitarPro, MusicXml and Midi.
- Source/src/TabForge.Core/Services/Import/GuitarProImporter.cs: format detection, MusicXmlInput.Prepare (if the format needs a pre-pass), and conversion.
- Source/src/TabForge.Core/Services/Import/ helpers: the bar/beat/track converters and their guarded reflection.

## Steps

1. **Extensions.** Add the extension(s) to the matching array in FileTypes.cs (GuitarPro, MusicXml or Midi). Importable and the open dialog update automatically; GuitarProImporter.SupportedExtensions reads FileTypes.Importable, and ImportWorker checks the same list.
2. **Detection.** If alphaTab cannot recognise the file by its bytes, add the magic/signature check to GuitarProImporter.ImportBytes and a clear refusal message (see the .gtp message).
3. **Pre-pass (optional).** If the file needs normalising before alphaTab reads it, add a MyFormatInput.Prepare(byte[]) helper beside MusicXmlInput and call it from ImportBytes.
4. **Conversion.** Let alphaTab load the score, then reuse the existing conversion (GuitarProImporter.Import and the bar/beat/track converters). Add a MyFormatExtras pass only for data alphaTab does not expose.
5. **Safety.** Keep the import inside ImportWorker's budget and the existing InputLimits / plausibility checks. Do not parse unbounded input on the UI thread.
6. **Tests.** Add a small fixture under tests/TabForge.Avalonia.Tests/Import/, a round-trip or conversion test, and extend TestGuitarProFiles / the plausibility tests. A malformed file must produce the friendly refusal, not an exception on the UI thread.

## Partial capos

Partial capos are **not imported**. `.gp5` has no partial-capo field (the feature arrived after that format). The `.gp` gpif format stores partial capos for the current programs, but the published format notes of the import stack mark `Tuning > Partial Capo` as not supported and name no element or attribute for it. Do not guess a field: if a documented field appears later, read it into `TrackModel.PartialCapos` (a list of `PartialCapo { Fret, StringMask }`, bit i = string i, and the higher of the full and partial fret wins per string). Until then a full capo still imports, and no partials are invented. Tests: `PartialCapoImportTests`. State: not verified against a real file (no representative file was available).

If the format needs a new dependency, add it to TabForge.Core.csproj and its lock file, and keep the licence in Source/src/TabForge.Avalonia/licenses/.
