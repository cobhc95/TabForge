# Recording

Index: [Feature map](../FEATURE_MAP.md). Generated test tables: [tests.md](tests.md).

- **Main code:** `RecordingController`; engine side `InputCapture`, `Recorder`
- **Pathway to use:** The controller talks to the engine through the client; the recorded take becomes a clip on the timeline.
- **Tests:** `--areas recording`; `TestRecordingPipeline`, `TestCaptureResampling`, `TestClips`
