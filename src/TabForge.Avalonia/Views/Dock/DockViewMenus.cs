using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Docking;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Docking;

// Owns: the View menu's Panels and Layouts submenus for the Avalonia window. Their rows and their order come from DockLayoutCatalog
// (the layout list) and DockPaneTable (the side panes); a layout row applies that layout, a panel row
//   shows or closes its pane, and each check mark follows the dock.
// Does not own: the dock tree (DockWorkspaceController), the saved-layout store (MainWindowSettings), the list rules
//   (SavedLayoutRules, DockLayoutCatalog). The Band view and Keyboard mode panes are not Panels rows: View > Band view and
//   View > Keyboard mode switch their whole-window layouts.
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
public sealed class DockViewMenus
{
    private readonly DockWorkspaceController _dock;
    private readonly MainWindowSettings? _settings;
    private readonly Window _owner;
    private readonly Action<string>? _status;
    private readonly Func<DockWorkspaceState?>? _realLayout;
    private readonly Func<FretboardMode>? _mode;
    private readonly MenuItem _layouts = new() { Header = "Layouts" };
    private readonly MenuItem _panels = new() { Header = "Panels" };
    private readonly List<(string PaneId, MenuItem Item)> _panelItems = new();
    private string? _currentLayout;

    /// <summary>The status notices the layout rows show.</summary>
    private const string LayoutSavedText = "saved";
    private const string TooManyLayoutsText = "Too many saved layouts; delete one first";
    private const string LayoutDeletedText = "deleted";
    private const string LayoutNotFoundText = "not found";
    private const string LayoutAppliedText = "Layout: ";

    /// <param name="dock">The dock the rows act on.</param>
    /// <param name="settings">The saved layouts (none when the window runs without settings).</param>
    /// <param name="owner">The window the save prompt opens over and whose size a saved layout records.</param>
    /// <param name="status">The status notice the layout rows show; null shows nothing (the Compose call leaves it unset).</param>
    /// <param name="realLayout">The layout a save stores while Keyboard mode shows its temporary arrangement; null stores the dock as shown.</param>
    /// <param name="mode">The editor mode on screen; a layout the user picks remembers the fretboard shown in that mode (Tab when null).</param>
    public DockViewMenus(DockWorkspaceController dock, MainWindowSettings? settings, Window owner, Action<string>? status = null, Func<DockWorkspaceState?>? realLayout = null,
        Func<FretboardMode>? mode = null)
    {
        _dock = dock;
        _settings = settings;
        _owner = owner;
        _status = status;
        _realLayout = realLayout;
        _mode = mode;
        // The window starts in Compose when no settings say otherwise (the preview window has none).
        // A removed preset (Compose, Practice, Mix) or a deleted layout falls back to Default; the dock itself is left as it is.
        _currentLayout = DockLayoutCatalog.Resolve(SavedLayouts, settings?.Settings.LastLayout ?? DockLayoutCatalog.DefaultName)?.Name ?? DockLayoutCatalog.DefaultName;
        _layouts.AddHandler(MenuItem.SubmenuOpenedEvent, (sender, args) => { if (ReferenceEquals(args.Source, _layouts)) RefreshLayouts(); });
        _panels.AddHandler(MenuItem.SubmenuOpenedEvent, (sender, args) => { if (ReferenceEquals(args.Source, _panels)) RefreshPanelChecks(); });
        BuildPanels();
        RefreshLayouts();
    }

    /// <summary>The Panels and Layouts items, in the order the View menu shows them.</summary>
    public IReadOnlyList<MenuItem> Items => new[] { _panels, _layouts };

    // Without settings (the preview window) the saved layouts live in memory for this window only; nothing is written.
    private readonly List<SavedLayout> _unsavedLayouts = new();
    private List<SavedLayout> SavedLayouts => _settings?.Settings.SavedLayouts ?? _unsavedLayouts;

    private void PersistSettings() => _settings?.SaveSettings();

    private void RefreshLayouts()
    {
        _layouts.Items.Clear();
        var customStarted = false;
        foreach (var row in DockLayoutCatalog.LayoutMenuRows(SavedLayouts))
        {
            _layouts.Items.Add(LayoutItem(row));
            if (!customStarted && SavedLayouts.Count > 0) { _layouts.Items.Add(new Separator()); customStarted = true; }
        }
        _layouts.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save current layout as…" };
        save.Click += async (_, _) => await SaveCurrentLayoutAsync();
        _layouts.Items.Add(save);
        _layouts.Items.Add(DeleteMenu());
    }

    private MenuItem DeleteMenu()
    {
        var delete = new MenuItem { Header = "Delete layout", IsEnabled = SavedLayouts.Count > 0 };
        foreach (var layout in SavedLayouts.ToList())
        {
            var name = layout.Name;
            var item = new MenuItem { Header = DockLayoutCatalog.IsBuiltIn(name) ? name + " (back to Default)" : name };
            item.Click += (_, _) => DeleteLayout(name);
            delete.Items.Add(item);
        }
        return delete;
    }

    private async System.Threading.Tasks.Task SaveCurrentLayoutAsync()
    {
        var typed = await DialogLauncher.Ask<TextPromptWindow, string?>(_owner, () => new TextPromptWindow("Save layout", "Layout name:", _currentLayout ?? "My layout"));
        if (string.IsNullOrWhiteSpace(typed)) return;
        var name = DockLayoutCatalog.SavedLayoutName(typed.Trim());
        if (DockLayoutCatalog.FindSaved(SavedLayouts, name) is null && SavedLayouts.Count >= DockLayoutCatalog.MaxSavedLayouts)
        {
            _status?.Invoke(TooManyLayoutsText);
            return;
        }
        var state = _realLayout?.Invoke() ?? _dock.State;
        var saved = SavedLayoutRules.SaveCurrent(SavedLayouts, typed, state, _owner.WindowState == WindowState.Maximized, _owner.Width, _owner.Height);
        if (saved is null)
        {
            _status?.Invoke(TooManyLayoutsText);
            return;
        }
        _currentLayout = saved.Name;
        if (_settings is not null) _settings.Settings.LastLayout = saved.Name;
        PersistSettings();
        _status?.Invoke($"Layout \"{saved.Name}\" {LayoutSavedText}");
    }

    private void DeleteLayout(string name)
    {
        SavedLayoutRules.Delete(SavedLayouts, name);
        if (string.Equals(_currentLayout, name, StringComparison.OrdinalIgnoreCase) && !DockLayoutCatalog.IsBuiltIn(name))
        {
            _currentLayout = null;
            if (_settings is not null) _settings.Settings.LastLayout = null;
        }
        PersistSettings();
        _status?.Invoke($"Layout \"{name}\" {LayoutDeletedText}");
    }

    private MenuItem LayoutItem(DockLayoutCatalog.LayoutMenuRow row)
    {
        var item = new MenuItem
        {
            Header = row.Name, ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = string.Equals(_currentLayout, row.Name, StringComparison.OrdinalIgnoreCase),
            InputGesture = row.HotkeyId is null ? null : MainWindowMenu.GestureOf(row.HotkeyId, Hotkeys),
        };
        // The shortcut text is the catalogue's display form ("Ctrl+1"), not the key-parser name the gesture carries ("Ctrl+D1").
        if (row.HotkeyId is not null) AvaloniaMenuTemplates.SetGestureText(item, HotkeyCatalog.DisplayAll(Hotkeys, row.HotkeyId));
        item.Click += (_, _) => ApplyLayout(row.Name);
        return item;
    }

    private HotkeySettings Hotkeys => _settings?.Settings.Hotkeys ?? new HotkeySettings();

    private void ApplyLayout(string name)
    {
        if (DockLayoutCatalog.Resolve(SavedLayouts, name) is not { } layout)
        {
            _status?.Invoke($"Layout \"{name}\" {LayoutNotFoundText}");
            return;
        }
        // The fretboard side is the setting's, whatever the layout holds.
        var bottom = _settings?.Settings.Appearance.FretboardAtBottom ?? false;
        _dock.ApplyLayout(DockLayoutTree.WithInstrumentSide(layout.State, bottom));
        if (_settings is not null)
            _settings.RememberFretboard(_mode?.Invoke() ?? FretboardMode.Tab, _dock.IsPanelVisible("instrument"));
        _currentLayout = layout.Name;
        var saved = layout.Saved;
        if (saved is not null && saved.WindowWidth > 0)
        {
            if (_owner.WindowState == WindowState.Maximized || saved.Maximised) _owner.WindowState = WindowState.Maximized;
            else
            {
                _owner.WindowState = WindowState.Normal;
                _owner.Width = Math.Max(_owner.MinWidth, saved.WindowWidth);
                _owner.Height = Math.Max(_owner.MinHeight, saved.WindowHeight);
            }
        }
        if (_settings is not null) _settings.Settings.LastLayout = layout.Name;
        PersistSettings();
        _status?.Invoke($"{LayoutAppliedText}{layout.Name}");
    }

    private void BuildPanels()
    {
        foreach (var row in DockPaneTable.Rows.Where(r => r.Side))   // the side panes only: the fretboard, arrangement, Band view and Keyboard mode panes have their own View rows
        {
            var item = new MenuItem { Header = row.MenuTitle, ToggleType = MenuItemToggleType.CheckBox };
            var paneId = row.Id;
            item.Click += (_, _) =>
            {
                _dock.SetPanelVisible(paneId, !_dock.IsPanelVisible(paneId));
                RefreshPanelChecks();
            };
            _panelItems.Add((paneId, item));
            _panels.Items.Add(item);
        }
    }

    private void RefreshPanelChecks()
    {
        foreach (var (paneId, item) in _panelItems) item.IsChecked = _dock.IsPanelVisible(paneId);
    }

}
