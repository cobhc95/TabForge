# Vocabulary Glossary — PLAN ONLY (no renames; offline build)

Date: 2026-10-19. Data: tasks 24+25+26 (Core 569 / Avalonia 473 / Engine 80 files).
Verdict from all 3 lanes: NO mass renames. File co-occurrence ≠ confusion;
enforce 3 glossary entries + 4 layer rules in review instead.

## Glossary (ambiguous terms — write the sense you mean)
1. Preset (4 senses — glossary candidate #1): plug-in preset | drum/articulation map |
   rig preset (per-track chain shape) | instrument preset. Never bare `Preset` in new code.
2. Chain vs Rig (co-occur everywhere, distinction real but undocumented):
   Rig = per-track chain SHAPE (stored); Chain = live WIRED instance (running).
3. Passage vs Section (Passage 29 hits/8 files vs Section 302/65; Bar 2269 dominates):
   Passage = render-range term (ScoreRenderer.Passages, ScorePassages);
   Section = musical span. Do not use interchangeably.

## Layer rules (which word, where)
- R1 Project/Song/Document: persistence layer says Project; song/musical code says
  Song; window/doc-flow glue says Document (DocumentController/Edits legitimately mix).
- R2 Playback/Transport/Playhead: engine internals say Playback; user-facing motion
  says Transport; read-out position says Playhead (PlaybackTransport mixes legitimately).
- R3 Bar≫Measure (Avalonia 9:1), Clip≫Region (19:1, Segment unused):
  UI term is Bar and Clip. Measure survives only in Core-facing/theory spots;
  Take (recording) and Lane (clip lane) are distinct sub-concepts, not synonyms.
- R4 Track/Row/Strip: Track = model (TrackModel); Row = timeline UI row;
  Strip = horizontal strip control. 153-file overlap is structural, not confusion.
- R5 Engine nouns: Chain/Slot/Frame/Transport (Slot 588, Frame 191, Transport 97).
  Section/Rig/Passage = 0 in Engine (correctly absent — Core/Avalonia concepts).
  EngineProtocol.cs is the one sanctioned mixer (Song+Project+Document,
  Clip+Bar, Preset+Chain+Map).

## No-dup attestations (do NOT re-flag)
- NoteNames single-homed: Contracts/NoteNames.cs; Core NoteMapper = InstrumentLayouts
  (different domain). KeyNames/TechniqueNames/TuningPresets: zero Engine counterparts.
- Engine isolation: 0 using-refs to Core/Avalonia (task-23 PASS).

## Gate
- New public names must use the layer-rule term; banned bare `Preset`, `Passage`
  outside render-range. Enforce in review until analyzer lands.
