using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Plugins;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the editor of the common plug-in folders: one folder per line, Save, Reset to defaults and Cancel. It closes with the lines (Save) or null.
// Does not own: where the folders are kept (PluginSettings through PluginBrowserSession) or what they scan (VstScannerService).
// Tests: TabForge.Avalonia.Tests.PluginBrowserWindowTests.
public sealed class CommonFoldersWindow : Window
{
    private const double WindowWidth = 560, WindowHeight = 380, Gap = 6;
    private readonly TextBox _box = new()
    {
        AcceptsReturn = true, Name = "FolderLines", Margin = new Thickness(0, 4, 0, 8), FontFamily = new global::Avalonia.Media.FontFamily("Consolas"),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    public CommonFoldersWindow(IEnumerable<string> lines)
    {
        Title = "Common plug-in folders";
        Width = WindowWidth; Height = WindowHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        _box.Text = string.Join(Environment.NewLine, lines);
        var save = new Button { Content = "Save", Name = "SaveButton", IsDefault = true, Padding = new Thickness(14, 3), Margin = new Thickness(0, 0, Gap, 0) };
        var reset = new Button { Content = "Reset to defaults", Name = "ResetButton", Padding = new Thickness(14, 3), Margin = new Thickness(0, 0, Gap, 0) };
        var cancel = new Button { Content = "Cancel", Name = "CancelButton", IsCancel = true, Padding = new Thickness(14, 3) };
        save.Click += (_, _) => Close(Lines());
        reset.Click += (_, _) => _box.Text = string.Join(Environment.NewLine, VstScannerService.DefaultCommonFolders);
        cancel.Click += (_, _) => Close(null);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, reset, cancel } };
        var caption = new TextBlock { Text = "Common plug-in folders, one per line (%ENV% variables work; missing folders are skipped)" };
        var dock = new DockPanel { Margin = DialogMetrics.ContentInset };
        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(row, Dock.Bottom);
        dock.Children.Add(caption);
        dock.Children.Add(row);
        dock.Children.Add(_box);
        Content = dock;
    }

    private string[] Lines() => (_box.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
}
