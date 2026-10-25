# Menus (Core)

The right-click menu rows as plain data, shared by the WPF and Avalonia builds.

- `MenuSpec.cs`: the row shape (caption, shortcut text, tick, radio choice, Preferences row, submenus, separators).
- `TimelineMenus.cs`: the bar, selection, section and clip menus of the arrangement timeline, with the `TimelineCommand` each row runs.
- `TrackRowMenus.cs`: the track row menu and the master track row's item.
- `ScoreMenus.cs`: the score's empty-area menu.
- `TrackListMenus.cs`: the empty track list's menu (groups, colours, master track row, reset height, settings).

Hosts turn the rows into items: `ContextMenuBuilder` (Avalonia, `Source/src/TabForge.Avalonia/Views/ContextMenuBuilder.cs`).
