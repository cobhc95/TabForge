# TabForge.AudioEngine/Editors

Windows of plug-in editors (the plug-in's own UI). Does not own the plug-in instance (`Plugins/`) or the isolated host (`Isolation/`).

## Files
| File | Purpose |
| --- | --- |
| `EditorWindows.cs` | Plug-in editor windows: plain Win32 top-level windows owned by the main window, so they stay above it |
| `X11EditorHost.cs` | Linux: plug-in editor windows as plain top-level X11 windows, and the main-thread loop that runs the VST3 host run loop |
| `X11Native.cs` | Linux: the libX11 calls those windows use, and the X error handler that keeps a bad request from ending the engine |

## Pathway
Plug-in trust and approval are decided in `Source/src/TabForge.Core/Plugins/` before a plug-in gets here.

## Tests
