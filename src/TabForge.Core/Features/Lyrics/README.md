# Lyrics

Song lyrics behaviour over the document model. UI thread only.

## How to change me
1. Entry files: `LyricsController.cs`.
2. Owner class: `LyricsController`.
3. Tests to run: `LyricsPaneTests`, `LyricsSongPaneTests`.
4. Docs to update: `CHANGELOG.md` (list user-visible changes).

## Key types
- `LyricsController`: reads `Documents`/`Models`, exposes lyric lines and positions.

## Must not depend on
UI frameworks; playback and rendering (lyrics are data, not timing).
