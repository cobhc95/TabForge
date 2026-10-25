# Core/Shell

Pure window-chrome rules: tab drag decisions and chrome hit regions. No WPF or Avalonia types; the toolkit ends forward them.

## How to change me
1. Entry files: `ChromeHitRules.cs` (regions, resize edges, leading inset), `TabDragRules.cs` (drag threshold, insertion, reorder, tear-off).
2. Owner class: `ChromeHitRules` and `TabDragRules`.
3. Tests to run: `TestChromeHitRules` and `TestBrowserTabShell` (full-suite build, `--only`); then `--areas basic`.
4. Docs to update: this README.

## Key types
| File | Owns |
|---|---|
| `ChromeHitRules.cs` | `ChromeHit` (regions and the 8 resize edges and corners), `ChromeOs`, `Classify` (caption button, tab action, tab, caption, client), `ClassifyResizeEdge` (border rule, maximised has none), `LeadingInset` (macOS 78, else 0). |
| `TabDragRules.cs` | `TabTearOffAction`, the drag threshold, `CrossedThreshold`, `InsertionIndex`, `InsertIndex` (screen rectangles), `ReorderDestination`, `ReorderSlideOffsets`, `TearOffAction`, `IsBeyondTearOff`, `ResolveDropTarget`, `AcceptsDrop`. |
| `NativeHitCodes.cs` | `NativeHitCodes`: the Win32 `WM_NCHITTEST`, `WM_NC*` mouse and `HT*` numbers that the WPF and Avalonia Windows shells both use. Plain values, no P/Invoke. |

