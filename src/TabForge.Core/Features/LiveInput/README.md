# LiveInput

The program's live instrument input: arming, MIDI polling and the session that feeds the engine.

## How to change me
1. Entry files: `LiveInputArm.cs`, `LiveInputMidi.cs`, `LiveInputSession.cs`, `LiveInputSettings*.cs`.
2. Owner class: `LiveInputArm` (one per engine client; every window and tab shares it).
3. Tests to run: `LiveInputSessionTests`, `LiveInputButtonLitTests`, `LiveInputFxTitleTests`.
4. Docs to update: `CHANGELOG.md` (list audible changes).

## Key types
- `LiveInputArm`: arm state; `LiveInputMidi`: background poll reposted via `IUiThread` (`UiPriority.Send`).
- `LiveInputSession`: save/restore around engine use; `LiveInputSettings`: per-track settings.

## Must not depend on
UI frameworks (talk through `IUiThread`); the scheduler thread (never block it; see `Platform/IUiThread.cs`).
