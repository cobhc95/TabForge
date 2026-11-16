# Services/Export

The export formats: Guitar Pro (.gp), MIDI, MusicXML, ASCII tab and rendered-audio file names, plus the lossy-export preflight.

## Files
| File | Owns |
| --- | --- |
| `AsciiExportService.cs` | rendering a song as plain-text tablature. |
| `AudioTrackExport.cs` | what the export formats do with audio tracks (skip them) and the notice texts for it. |
| `GpExportPreflight.cs` | the lossy-export preflight for a clean .gp export: finding what the format cannot hold and planning the files for each     choice. |
| `GpifAutomationWriter.cs` | writing a song's volume and pan mix changes into the gpif of a clean .gp (alphaTab's writer emits none). |
| `GuitarProExporter.Beats.cs` | the beats of the Guitar Pro exporter: one beat per cell (a grace beat before its principal), its effects and its notes with their slides, harmonics, bends and drum articulations. |
| `GuitarProExporter.Embed.cs` | the TabForge project embedded in a .gp: writing its bytes, reading it back (bounded) and the zip directory reads both sides need. |
| `GuitarProExporter.Helpers.cs` | the pure helpers of the Guitar Pro exporter (clef, transposed positions, bend/whammy simplification, durations, rests). |
| `GuitarProExporter.cs` | saving a song as a Guitar Pro 7/8 (.gp) file. |
| `MidiExportProcessors.cs` | applying each track's MIDI processors during MIDI file export (the same chain the live engine   runs, not a copy of it), so an exported file plays what TabForge plays. |
| `MidiExportRange.cs` | resolving the Export MIDI dialog's choices to a track set and a song range, with the same   performed-order mapping Render uses (RenderSpecBuilder.Bounds, RenderSnip for the white selection). |
| `MidiExportService.cs` | Standard MIDI File export from the canonical playback timeline with each track's MIDI processors applied. |
| `MidiExportSlices.cs` | the range-limited Standard MIDI File chunks (a conductor and track slice starting at tick 0). |
| `MusicXmlExportService.Measures.cs` | MusicXML measure encoding: one voice of one bar on one staff, and the note-to-note link state. |
| `MusicXmlExportService.Notes.cs` | MusicXML note encoding: note elements, techniques, bends, harmonies and navigation marks. |
| `MusicXmlExportService.Parts.cs` | MusicXML part encoding: score-part elements, backup, tempo, clefs and whole-bar rests. |
| `MusicXmlExportService.cs` | uncompressed MusicXML export of a song. |
| `RenderNaming.cs` | the file names of rendered audio files. |

Tests: see the `// Tests:` line at the top of each file.
