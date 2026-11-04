# 0004. A clean .gp file plus a .tfaudio sidecar, or an embedded project

Status: accepted

## Context

A `.gp` file cannot hold what TabForge adds: per-track sound source, plug-in rig and state, mixer, clips and lanes. Users also want to hand a song to other programs that read the format without anything unknown inside it.

## Decision

When a song with audio data goes to `.gp`, the user chooses (`AudioDataSaveChoice` in `Source/src/TabForge.Core/Documents/DocumentSaveFlow.cs`):

1. a clean `.gp` plus a `.tfaudio` file next to it (JSON, versioned, capped in size; `AudioDataFile`);
2. a `.gp` with the whole project embedded as one extra zip entry, `TabForge/project.tforge.gz` (other readers ignore it);
3. a native `.tforge` file.

A clean `.gp` never contains anything TabForge-specific. A sidecar that does not validate is ignored entirely, never half-applied. Before a lossy export, a preflight report says what the file cannot hold.

## Consequences

- The `.gp` stays a plain interchange file; audio data survives as long as the two files stay together.
- Two files can be separated. The embedded option exists for users who want one file.
- Both paths are covered by round-trip tests. A new field in the project must be placed in one of these three forms on purpose.

See `ARCHITECTURE.md`, section 6.
