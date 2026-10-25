# 0006. Each open document has its own media context

Status: accepted

## Context

Several songs can be open at once. Work on a song's audio (reading waveforms, syncing clips, measuring a drop, reviewing linked files) can finish after the user switched tabs, saved under a new name or closed the song. Looking up "the current document" or a global at that point attaches the result, or an approval, to the wrong song.

## Decision

Every document owns one `MediaContext` (`Source/src/TabForge.Core/Services/MediaContext.cs`, reached as `SongDocument.Media`). Media operations receive it explicitly. It carries:

- the song's identity for this run (never stored in a file);
- the base folder where relative clip paths resolve;
- the approval scope: a saved song is scoped to its canonical path, an unsaved song to its own session in memory;
- a revision that changes on Save As and on close, so work started earlier is dropped when it completes.

Nothing in the media code reads a window, the focused tab or a global. Code with no document uses `MediaContext.Anonymous`, which cannot hold approvals and refuses relative paths.

## Consequences

- Late results and approvals cannot reach another song.
- Media functions take one more parameter, and tests drive them with a fake document and no window (the `document-context` self-test group).
- A new operation that touches song media must accept a `MediaContext`.
