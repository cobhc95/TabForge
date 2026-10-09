# Playback

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge/Playback/` (`ScoreToMidiCompiler`, `ScoreTimeline`, `PlaybackEngine`, `PlaybackOrder`)
- **Pathway to use:** The compiled timeline is authoritative; views only read it (`PlaybackEngine.TimelineChanged`).
- **Tests:** `--areas playback`; `TestTimelineBasics`, `TestPlaybackOrderSpec`, `TestNoHangingNotes`; headless check: `TabForge.exe --playtest <song>`
