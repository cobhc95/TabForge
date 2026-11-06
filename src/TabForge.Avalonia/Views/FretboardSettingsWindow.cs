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
//   Strings and capo, Scale highlight, each with its glyph), the chosen section's cards on the right, and the Settings footer: "More settings in Preferences"
//   on the left; the status, Cancel, Apply and OK on the right. Every pick is written to the settings and shows on the board at once; Apply and OK keep
//   the picks, and Cancel or closing the window writes back the values from the last Apply (or from opening).
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
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _apply = new() { Name = "FretboardSettingsApply", Content = SettingsCatalog.Special(SettingsCatalog.ActionApply).Label, IsEnabled = false };
    private AppSettings _kept;
    private readonly Dictionary<FretNoteRole, ColourButton> _noteColourButtons = new();
    private bool _dirty;

    /// <param name="settings">The live settings the rows read and write.</param>
    /// <param name="changed">Saves the settings and raises SettingsChanged, so the main window's fretboard follows.</param>
    /// <param name="morePreferences">Opens Preferences on the Fretboard and Keyboard page.</param>
    public FretboardSettingsWindow(AppSettings settings, Action changed, Action morePreferences)
    {
        (_settings, _changed) = (settings, changed);
        _kept = PreferencesCatalog.Copy(settings);
        Title = "Fretboard settings";
        Width = 820;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 700;
        MinWidth = 640;
        MinHeight = 320;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ChromeMetrics.SettingsRailWidth},*") };
        foreach (var section in Enumerable.Range(0, SectionCount))
            _nav.Items.Add(new ListBoxItem { Content = NavRow(section), Tag = section, MinHeight = ChromeMetrics.SettingsRailRowHeight, Classes = { AvaloniaFormControlStyles.PrefsNavRowClass } });
        _nav.Background = null;
        body.Children.Add(_nav);
        Grid.SetColumn(_page, 1);
        body.Children.Add(_page);
        var root = new DockPanel();
        var footer = BuildFooter(morePreferences);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        _nav.SelectionChanged += (_, _) => { SyncRailSelection(); ShowSection(SelectedSection); };
        _nav.SelectedIndex = 0;
        Closing += (_, _) => Revert();
    }

    /// <summary>The Settings footer band: the link to Preferences on the left; the status, Cancel, Apply and OK on the right.</summary>
    private Border BuildFooter(Action morePreferences)
    {
        var more = new Button { Name = "FretboardMoreSettings", Content = "More settings in Preferences" };
        more.Classes.Add("PrefsActionButton");
        more.Click += (_, _) => morePreferences();
        var cancel = new Button { Name = "FretboardSettingsCancel", Content = SettingsCatalog.Special(SettingsCatalog.ActionCancel).Label, IsCancel = true };
        cancel.Classes.Add("PrefsActionButton");
        cancel.Click += (_, _) => Close();
        _apply.Classes.Add("PrefsActionButton");
        _apply.Click += (_, _) => Apply();
        var ok = new Button { Name = "FretboardSettingsOk", Content = SettingsCatalog.Special(SettingsCatalog.ActionOk).Label, IsDefault = true };
        ok.Classes.Add("PrefsActionButton");
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ok.Click += (_, _) => { Apply(); Close(); };
        ShowStatus();
        var right = ToolWindowParts.Row(_status.WithMargin(0, 0, FretboardChromeMetrics.FooterGap, 0), cancel, _apply, ok);
        DockPanel.SetDock(more, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        var footer = new DockPanel { LastChildFill = false, Children = { more, right } }.WithMargin(12, 8, 12, 8);
        var band = new Border { Child = footer, BorderThickness = FretboardChromeMetrics.FooterBandBorder };
        band[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WindowBrush");
        band[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("BorderSoftBrush");
        return band;
    }

    /// <summary>Keeps the picks: Cancel now returns to here.</summary>
    internal void Apply()
    {
        _kept = PreferencesCatalog.Copy(_settings);
        _dirty = false;
        ShowStatus();
    }

    /// <summary>Writes back what the window's rows changed since the last Apply and saves, leaving every other setting as it is now.</summary>
    private void Revert()
    {
        if (!_dirty) return;
        foreach (var section in Enumerable.Range(0, SectionCount)) CopySection(section, _kept.Editing, _kept.Appearance);
        _settings.Editing.ScaleHighlight = _kept.Editing.ScaleHighlight;
        _dirty = false;
        _changed();
    }

    private void ShowStatus()
    {
        _status.Text = _dirty ? "Changes not applied" : "All changes saved";
        _apply.IsEnabled = _dirty;
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
        _dirty = true;
        ShowStatus();
        _changed();
        if (rebuild) Dispatcher.UIThread.Post(() => ShowSection(_nav.SelectedIndex));
    }
}
