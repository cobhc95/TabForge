using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Models;
using TabForge.Platform.Theme;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Fretboard settings window (a modeless tool window): a settings-style section rail on the left (Look, Notes and markers, Layout and size,
//   Strings and capo, Scale highlight, each with its glyph), the chosen section's rows on the right, and a footer with "More settings in Preferences",
//   Reset section and Done. Every pick is written to the settings and applied at once; a slider updates the board while it is dragged and saves when
//   it is let go, so a drag never broadcasts settings.
// Does not own: the settings types (EditingSettings, AppearanceSettings), the look names (FretboardStyles), the rail look (PreferencesWindow's
//   PrefsNavRow style) or saving and opening Preferences (the callbacks the window host passes).
// Tests: TabForge.Avalonia.Tests.FretboardStyleTests, FretboardSettingsWindowTests.
// Threads: UI thread only.
public sealed partial class FretboardSettingsWindow : Window
{
    /// <summary>The sections, in sidebar order.</summary>
    public const int SectionCount = 5;

    public static string SectionName(int section) => section switch
    {
        0 => "Look",
        1 => "Notes and markers",
        2 => "Layout and size",
        3 => "Strings and capo",
        _ => "Scale highlight",
    };

    private static IEnumerable<string> SectionNames() => Enumerable.Range(0, SectionCount).Select(SectionName);

    /// <summary>The rail glyph of each section: the Settings glyphs, plus the scale finder.</summary>
    private static string SectionGlyph(int section) => section switch
    {
        0 => SettingsCatalog.Appearance,
        1 => SettingsCatalog.Score,
        2 => SettingsCatalog.Tabs,
        3 => SettingsCatalog.Fretboard,
        _ => "Scale highlight",
    };

    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly ScrollViewer _page = new() { HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly ListBox _nav = new() { Width = ChromeMetrics.SettingsRailWidth, BorderThickness = default };
    private readonly Dictionary<int, SettingsNavigationIconView> _navIcons = new();

    /// <param name="settings">The live settings the rows read and write.</param>
    /// <param name="changed">Saves the settings and raises SettingsChanged, so the main window's fretboard follows.</param>
    /// <param name="morePreferences">Opens Preferences on the Fretboard and Keyboard page.</param>
    public FretboardSettingsWindow(AppSettings settings, Action changed, Action morePreferences)
    {
        (_settings, _changed) = (settings, changed);
        Title = "Fretboard settings";
        Width = 820;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        MinWidth = 640;
        MinHeight = 320;
        var more = new Button { Name = "FretboardMoreSettings", Content = "More settings in Preferences" };
        more.Classes.Add("PrefsActionButton");
        more.Click += (_, _) => morePreferences();
        var reset = new Button { Name = "FretboardResetSection", Content = "Reset section" };
        reset.Classes.Add("PrefsActionButton");
        reset.Click += (_, _) => ResetSection(_nav.SelectedIndex);
        var done = new Button { Name = "FretboardSettingsDone", Content = "Done", IsDefault = true };
        done.Classes.Add("PrefsActionButton");
        done.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        done.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = FretboardChromeMetrics.FooterGap, HorizontalAlignment = HorizontalAlignment.Right, Children = { more, reset, done } }
            .WithMargin(12, 8, 12, 12);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ChromeMetrics.SettingsRailWidth},*") };
        foreach (var section in Enumerable.Range(0, SectionCount))
            _nav.Items.Add(new ListBoxItem { Content = NavRow(section), Tag = section, MinHeight = ChromeMetrics.SettingsRailRowHeight, Classes = { AvaloniaFormControlStyles.PrefsNavRowClass } });
        _nav.Background = null;
        body.Children.Add(_nav);
        Grid.SetColumn(_page, 1);
        body.Children.Add(_page);
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        _nav.SelectionChanged += (_, _) => { SyncRailSelection(); ShowSection(SelectedSection); };
        _nav.SelectedIndex = 0;
    }

    /// <summary>A rail row: the section's glyph, then its name.</summary>
    private Grid NavRow(int section)
    {
        var glyph = new SettingsNavigationIconView(SectionGlyph(section)) { Width = ChromeMetrics.SettingsRailIconSize, Height = ChromeMetrics.SettingsRailIconSize, VerticalAlignment = VerticalAlignment.Center };
        _navIcons[section] = glyph;
        var label = new TextBlock { Text = SectionName(section), FontSize = ChromeMetrics.SettingsRailLabelFontSize, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var row = new Grid { ColumnDefinitions = FretboardChromeMetrics.NavColumns };
        row.Children.Add(glyph);
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        return row;
    }

    /// <summary>Marks the open section's glyph selected.</summary>
    private void SyncRailSelection()
    {
        foreach (var (section, glyph) in _navIcons) glyph.IsSelected = section == _nav.SelectedIndex;
    }

    /// <summary>Shows a section by index (used by the right-click menu rows that name one).</summary>
    public void Select(int section) => _nav.SelectedIndex = Math.Clamp(section, 0, SectionCount - 1);

    internal int SelectedSection => _nav.SelectedIndex;

    internal Control Page => _page;

    /// <summary>A pick was made: save and rebuild the page after the click ends so the chip that raised it is not removed mid-event.</summary>
    internal void Changed(bool rebuild = true)
    {
        _changed();
        if (rebuild) Dispatcher.UIThread.Post(() => ShowSection(_nav.SelectedIndex));
    }
}
