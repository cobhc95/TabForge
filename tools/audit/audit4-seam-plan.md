# Audit-4 Seam Plan — PLAN ONLY (no moves; build offline NU1301)

Date: 2026-10-19. Status: data complete (tasks 21+22+23), gate passed
(Core 0 / Avalonia 0 / Engine 0 missing Owns; tutorial True/False intact).
No using moved: any seam refactor needs `dotnet build` with network.

## Core seams (next-touch, mechanical, one seam file each)
- A. Services(8)→Playback via `MidiTimelineBuilder` façade:
  AudioToInstrumentConversion, BarRangeGaps, ClipDeleteImpact, MidiExportService,
  NotationToMidiClip, SectionClips, SongExtent, TrailingBars.
- B. Score(8)→Playback via `IScoreRenderHost` (exists):
  ScoreRenderer{,.Bars,.Cursor,.Marks,.Passages,.TabLinks}, ScorePlayState,
  ScoreEditCommands{,.Marks}.
- C. Score(6)→Views statics via geometry seam:
  StaffNotationDrawing{,.Noteheads}, StaffNotationLayoutBuilder, StaffNotationArcs,
  StaffNotationGeometry, StaffNotationModels.
- D. Rendering(3) via `RenderSpec`: RenderJob, RenderSpecBuilder, VideoExportFlow
  (VideoExportFlow fans out to 6 areas — first candidate when build returns).
- E. Documents(2)→Audio via engine-client seam: DocumentController, DocumentSaveFlow.
- F. ScorePreviewEditor(4) split preview-host seam (heaviest intra-Core fan-out).
- Playback partials 5×9 usings → one shared `PlaybackEngine.Seams.cs` (5×9→5×3).

## Avalonia (Views/ 166 files)
- 71/166 (43%) `using TabForge.Services` vs Presenters 57% / Controllers 0%.
- All five 20-using files import BOTH Services+Presenters (mixed, not bypass).
- Next-touch: per-call-site attribution needs compiler pass; prefer Presenters
  for domain text/rules (MixerStripPresenter pattern), Services for plumbing only.
- Controllers 0/166: intentional (thin), not a gap — do not force-filter.

## Engine — NO ACTION
- Isolation PASS: 0 using-refs to Core/Avalonia (1 comment-only test-name hit).
- PInvoke 54/7 files = OS/plug-in ABI, not coupling.

## Gate
- Quick-light gate after any future move: Owns counts + tutorial refs + `dotnet build`
  of touched projects (blocked 2026-10-19 by NU1301 offline; re-run with network).
