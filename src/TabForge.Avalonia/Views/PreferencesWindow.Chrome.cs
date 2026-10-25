using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Settings window's chrome: the page header (caption, title, subtitle and the search box), the title block and the footer
//       (the Reset page…, Manage settings and status line, Cancel, Apply and OK row).
// Does not own: the cards and rows (PreferencesWindow.cs), the reset questions (PreferencesWindow.cs) or the footer's rules (PreferencesPagePresenter).
// Tests: TabForge.Avalonia.Tests.PreferencesWindowTests.

public sealed partial class PreferencesWindow
{
    /// <summary>The page header (caption, page title and subtitle) and the search box.</summary>
    private StackPanel BuildHeader()
    {
        _searchBar.Changed += Rebuild;
        _pageSubtitle[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        var header = new StackPanel { Margin = new Thickness(16, 12, 16, 4), Spacing = 4 };
        header[!Panel.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        DockPanel.SetDock(_resetPage, Dock.Right);
        // The link sits at the top of the title row, not stretched down the header, as the WPF ResetPageButton.
        _resetPage.VerticalAlignment = VerticalAlignment.Top;
        _resetPage.Margin = new Thickness(ChromeMetrics.SettingsResetPageMarginLeft, ChromeMetrics.SettingsResetPageMarginTop, 0, 0);
        _resetPage.FontSize = ChromeMetrics.SettingsResetPageFontSize;
        header.Children.Add(new DockPanel { LastChildFill = true, Children = { _resetPage, _pageTitle } });
        header.Children.Add(_pageSubtitle);
        return header;
    }

    /// <summary>The title block above the settings rail: TabForge Settings with the Preferences subtitle, at the top left as in the WPF window.</summary>
    private Border BuildTitleBlock()
    {
        var caption = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = WindowTitle, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Preferences", FontSize = 12, Margin = new Thickness(0, 2, 0, 0), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") },
            },
        };
        var block = new Border { Width = ChromeMetrics.SettingsRailWidth, Height = ChromeMetrics.SettingsTopRowHeight, Padding = new Thickness(18, 16), BorderThickness = new Thickness(0, 0, 1, 1), Child = caption };
        block[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        block[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return block;
    }

    /// <summary>The Manage settings label with a menu chevron after it (the BAND VIEW caption's chevron: 10x6, 1.4 stroke, muted, round caps).</summary>
    private static StackPanel ManageContent(string text)
    {
        var chevron = new Path
        {
            Data = Geometry.Parse("M 1,1 L 5,5 L 9,1"), Width = 10, Height = 6, Stretch = Stretch.None, StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
        };
        chevron[!Shape.StrokeProperty] = new DynamicResourceExtension("MutedBrush");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4, IsHitTestVisible = false,
            Children = { new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, chevron },
        };
    }

    /// <summary>Runs a footer command by its registry id: the footer buttons and the Manage settings menu, and a search result's button, all call this.</summary>
    internal void RunFooterAction(string id)
    {
        switch (id)
        {
            case SettingsCatalog.ActionResetPage: Platform.UiTasks.Forget(ResetPageAsync(), "Reset page", text => _status.Text = text); break;
            case SettingsCatalog.ActionResetAll: Platform.UiTasks.Forget(ResetAllAsync(), "Reset all settings", text => _status.Text = text); break;
            case SettingsCatalog.ActionImport: ImportSettingsFromDialog(); break;
            case SettingsCatalog.ActionExport: ExportSettingsToDialog(); break;
            case SettingsCatalog.ActionCancel: Platform.UiTasks.Forget(CancelAsync(), "Cancel", text => _status.Text = text); break;
            case SettingsCatalog.ActionApply: Apply(); break;
            case SettingsCatalog.ActionOk: Apply(); Close(); break;   // Apply only takes the values; the owner's save and broadcast are deferred, so the window is gone in this frame
        }
    }

    private Task CancelAsync() => CloseAskingAsync();

    /// <summary>The footer band: Manage settings and the version on the left; the status line, Cancel, Apply and OK on the right.</summary>
    private Border BuildFooter()
    {
        _resetPage.Click += (_, _) => RunFooterAction(SettingsCatalog.ActionResetPage);
        _resetPage.Classes.Add("PrefsActionReset");
        ToolTip.SetTip(_resetPage, SettingsCatalog.Special(SettingsCatalog.ActionResetPage).Tip);
        // The label stays the button's content (the text the tests read); its template adds the menu chevron, as the BAND VIEW caption menu does.
        var manage = _manage;
        manage.ContentTemplate = new FuncDataTemplate<object>((text, _) => ManageContent(text as string ?? ""));
        manage.Classes.Add("PrefsActionButton");
        ToolTip.SetTip(manage, SettingsCatalog.Special(SettingsCatalog.ActionManage).Tip);
        manage.Flyout = PreferencesSpecialPages.ManageMenu(RunFooterAction);
        var cancel = new Button { Content = SettingsCatalog.Special(SettingsCatalog.ActionCancel).Label, IsCancel = true };
        cancel.Classes.Add("PrefsActionButton");
        ToolTip.SetTip(cancel, SettingsCatalog.Special(SettingsCatalog.ActionCancel).Tip);
        cancel.Click += (_, _) => RunFooterAction(SettingsCatalog.ActionCancel);
        var apply = _apply;
        apply.Classes.Add("PrefsActionButton");
        ToolTip.SetTip(apply, SettingsCatalog.Special(SettingsCatalog.ActionApply).Tip);
        apply.Click += (_, _) => RunFooterAction(SettingsCatalog.ActionApply);
        var ok = new Button { Content = SettingsCatalog.Special(SettingsCatalog.ActionOk).Label, IsDefault = true };
        ok.Classes.Add("PrefsActionButton");
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        ToolTip.SetTip(ok, SettingsCatalog.Special(SettingsCatalog.ActionOk).Tip);
        ok.Click += (_, _) => RunFooterAction(SettingsCatalog.ActionOk);
        var footer = new DockPanel { Margin = new Thickness(FooterMarginLeft, FooterMarginVertical, FooterMarginRight, FooterMarginVertical), LastChildFill = false };
        var version = new TextBlock { Text = AppInfo.VersionLine, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted") };
        var left = ToolWindowParts.Row(manage, version);
        // The status text belongs to the right-hand group, before the buttons, as in the WPF footer.
        _status.Margin = new Thickness(0, 0, FooterStatusGap, 0);
        _status.MaxWidth = FooterStatusMaxWidth;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _changes.View.Margin = new Thickness(0, 0, FooterStatusGap, 0);
        var right = ToolWindowParts.Row(_changes.View, _status, cancel, apply, ok);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(left);
        footer.Children.Add(right);
        _status.Text = SavedText;
        var band = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        band[!Border.BackgroundProperty] = new DynamicResourceExtension("WindowBrush");
        band[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");
        return band;
    }
}
