using TabForge.Avalonia.Views.Dialogs;
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
//   Strings and capo, Scale highlight, each with its glyph), the chosen section's cards on the right, and the Settings footer: "More settings" and "Reset all"
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
    private readonly ScrollViewer _page = SettingsWindowChrome.CreateCardsScroll();
    private readonly ListBox _nav = SettingsWindowChrome.CreateNavigationRail();
    private readonly Dictionary<int, SettingsNavigationIconView> _navIcons = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _apply = new() { Name = "FretboardSettingsApply", Content = SettingsCatalog.Special(SettingsCatalog.ActionApply).Label, IsEnabled = false };
    private AppSettings _kept;
    private readonly Dictionary<FretNoteRole, ColourButton> _noteColourButtons = new();
    private ColourButton? _scaleCustomColourButton;
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
        FontSize = DialogMetrics.FontSize;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{ChromeMetrics.SettingsRailWidth},*") };
        _nav.Items.Add(SettingsWindowChrome.CreateBandHeader("APPEARANCE"));
        for (var section = 0; section < SectionCount; section++)
        {
            if (section == 2) _nav.Items.Add(SettingsWindowChrome.CreateBandHeader("BOARD"));
            _nav.Items.Add(SettingsWindowChrome.CreateNavigationItem(SectionGlyph(section), SectionName(section), section, out var glyph));
            _navIcons[section] = glyph;
        }
        var titleBlock = SettingsWindowChrome.CreateTitleBlock("Fretboard Settings", "Fretboard & neck");
        var sidebar = new DockPanel();
        DockPanel.SetDock(titleBlock, Dock.Top);
        sidebar.Children.Add(titleBlock);
        sidebar.Children.Add(_nav);
        body.Children.Add(sidebar);
        Grid.SetColumn(_page, 1);
        body.Children.Add(_page);
        var root = new DockPanel();
        var footer = BuildFooter(morePreferences);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        _nav.SelectionChanged += (_, _) => { if (_nav.SelectedItem is ListBoxItem { Tag: int s }) { SyncRailSelection(); ShowSection(s); } };
        _nav.SelectedItem = _nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is int s && s == 0);
        Closing += (_, _) => Revert();
    }

    /// <summary>The Settings footer band: "More settings" (the link to Preferences) and "Reset all" on the left; the status, Cancel, Apply and OK on the right.</summary>
    private Border BuildFooter(Action morePreferences)
    {
        var more = new Button { Name = "FretboardMoreSettings", Content = "More settings" };
        more.Classes.Add("PrefsActionButton");
        more.Click += (_, _) => morePreferences();
        ToolTip.SetTip(more, "Open Preferences for the settings that are not about the fretboard");
        var resetAll = new Button { Name = "FretboardResetAll", Content = "Reset all" };
        resetAll.Classes.Add("PrefsActionButton");
        resetAll.Click += (_, _) => ResetAll();
        ToolTip.SetTip(resetAll, "Restore every fretboard setting to its default; Cancel still goes back to how they were");
        var left = ToolWindowParts.Row(more, resetAll);
        var cancel = new Button { Name = "FretboardSettingsCancel", Content = SettingsCatalog.Special(SettingsCatalog.ActionCancel).Label, IsCancel = true };
        cancel.Classes.Add("PrefsActionButton");
        cancel.Click += (_, _) => Close();
        _apply.Classes.Add("PrefsActionButton");
        _apply.Click += (_, _) => Apply();
        var ok = DialogButtons.Ok(SettingsCatalog.Special(SettingsCatalog.ActionOk).Label, "FretboardSettingsOk");
        ok.Classes.Add("PrefsActionButton");
        ok.Click += (_, _) => { Apply(); Close(); };
        ShowStatus();
        var right = ToolWindowParts.Row(_status.WithMargin(0, 0, FretboardChromeMetrics.FooterGap, 0), cancel, _apply, ok);
        return SettingsWindowChrome.CreateFooterBand(left, right);
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
        _settings.Editing.ShowScale(_kept.Editing.ScaleHighlight);
        _dirty = false;
        _changed();
    }

    private void ShowStatus()
    {
        _status.Text = _dirty ? "Changes not applied" : "All changes saved";
        _apply.IsEnabled = _dirty;
    }

    /// <summary>Marks the open section's glyph selected.</summary>
    private void SyncRailSelection()
    {
        var current = SelectedSection;
        foreach (var (section, glyph) in _navIcons) glyph.IsSelected = section == current;
    }

    /// <summary>Shows a section by index (used by the right-click menu rows that name one).</summary>
    public void Select(int section)
    {
        var target = Math.Clamp(section, 0, SectionCount - 1);
        _nav.SelectedItem = _nav.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is int s && s == target);
    }

    internal int SelectedSection => _nav.SelectedItem is ListBoxItem { Tag: int s } ? s : 0;

    internal Control Page => _page;

    /// <summary>A pick was made: save and rebuild the page after the click ends so the control that raised it is not removed mid-event.</summary>
    internal void Changed(bool rebuild = true)
    {
        _dirty = true;
        ShowStatus();
        _changed();
        if (rebuild)
        {
            var offset = _page.Offset;
            Dispatcher.UIThread.Post(() =>
            {
                ShowSection(SelectedSection);
                _page.Offset = offset;
            });
        }
    }
}
