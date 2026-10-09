# Mixer and audio engine

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `src/TabForge.AudioEngine/` (`MixEngine`, `TrackChain`, `ClipPlayer`); client in `AudioEngineClient`; wire types in `src/TabForge.Audio.Contracts/`
- **Pathway to use:** The UI reaches the engine only through `AudioEngineClient`; every message has a bounded reader. Audio-callback code does not allocate or lock.
- **Tests:** `--areas engine`; `TestMixer`, `TestMuteSoloTruthTable`, `TestWatchdogPolicy`, `TestCommandFrameRobustness`; `TestArchitectureLayering`
