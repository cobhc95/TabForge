# TabForge.AudioEngine/Editors

Windows of plug-in editors (the plug-in's own UI). Does not own the plug-in instance (`Plugins/`) or the isolated host (`Isolation/`).

## Files
| File | Purpose |
| --- | --- |
| `EditorWindows.cs` | Plug-in editor windows: plain Win32 top-level windows owned by the main window, so they stay above it |

## Pathway
`START_HERE.md`, Golden pathways: **Reach the engine**. Plug-in trust and approval are decided in `src/TabForge/Plugins/` before a plug-in gets here.

## Tests
`docs/feature-map/plug-ins.md`.
