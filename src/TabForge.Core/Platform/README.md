# Core/Platform

Ports that Core logic needs from the operating system. Each port is an interface with no Windows or WPF types in its signatures; the Windows adapters live in `Source/src/TabForge.Core/Platform/Windows/` and `Source/src/TabForge.Avalonia/Platform/`, and the app passes them in at its composition point (`Program.Main`) or holds it in a readonly field.

| File | Owns |
|---|---|
| `IPathResolver.cs` | The final path of a file once every link and junction is followed (`FinalPath`, null when unknown). |
| `IScreenCapture.cs` | The screen pixel port for video recording: `OpenFrame`, and the frame's `Clear`, `StretchFromScreen`, `CopyPixelsTo`. |
| `IFileAssociations.cs` | The per-user "Open with TabForge" registration: `IsRegistered`, `Register`, `Unregister` (adapter: `Source/src/TabForge.Avalonia/Platform/AvaloniaFileAssociations.cs`). |
| `IWindowChrome.cs` | What a custom title bar asks of its window: `BeginMove`, `BeginResize(ResizeEdge)`, `Minimize`, `ToggleMaximise`, `Close`, `State` and `StateChanged` (adapters come with the Avalonia shell). |
| `IMonitorWorkAreas.cs` | `WorkAreaNear(UiRect)`: the usable screen area of the monitor nearest a rectangle. |
| `Testing/GeometryRecorder.cs` | An `IDrawContext` decorator that logs every draw call (kind, points, rect, path bounds, text and its box) and forwards it; used by the score geometry proof. |

Adapter: `Source/src/TabForge.Core/Platform/Windows/WindowsPathResolver.cs` (the one final-path P/Invoke in the app).
Tests: `TestPathResolverAdapter`, `TestFloatingSessionRules` (the window and monitor ports).
