using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// "Find missing media": each missing file with Locate…, a Search folder… that matches the others by file name in the folder (and below) and fixes every
/// match, Leave missing (close, nothing changed) and Done (hand the fixes back). The search runs on a worker so the window stays live.
/// Owns: the rows, the file and folder pickers it opens, and the fixes it collects. Does not own: the search (MediaRelink), the model change and its undo step (the caller applies Fixes).
/// Tests: TabForge.Avalonia.Tests.MediaRelinkTests (the search); the window itself is checked by use.
/// </summary>
public sealed class MediaRelinkWindow : Window
{
    public const string TitleText = "Find missing media";
    public const string LeaveText = "Leave missing";
    public const string SearchText = "Search folder…";
    public const string DoneText = "Done";

    private readonly IReadOnlyList<MissingMedia> _missing;
    private readonly IFileDialogs _dialogs;
    private readonly Dictionary<string, string> _fixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TextBlock Status, Button Locate)> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _note = MediaWindowParts.Muted("Search finds files by name, then fixes every match in the folder you pick.");
    private readonly Button _search = new Button { Name = "SearchButton", Content = SearchText }.WithPadding(DialogMetrics.ActionButtonPadding);
    private readonly Button _done = new Button { Name = "DoneButton", Content = DoneText, IsDefault = true }.WithPadding(DialogMetrics.ActionButtonPadding);
    private CancellationTokenSource? _running;

    public MediaRelinkWindow(IReadOnlyList<MissingMedia> missing, IFileDialogs dialogs)
    {
        _missing = missing;
        _dialogs = dialogs;
        Title = TitleText;
        Width = DialogMetrics.MediaRelinkWidth;
        SizeToContent = SizeToContent.Height;
        MinWidth = DialogMetrics.MediaRelinkMinWidth;
        DialogMetrics.CenterOnOwner(this);
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var list = new StackPanel();
        foreach (var item in missing) list.Children.Add(Row(item));
        var frame = new Border { BorderThickness = DialogMetrics.MediaOutline, CornerRadius = new CornerRadius(DialogMetrics.MediaCorner), Child = new ScrollViewer { Content = list, MaxHeight = DialogMetrics.MediaRelinkListMaxHeight } }.WithMargin(DialogMetrics.MediaFrameGap);
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var leave = new Button { Name = "LeaveButton", Content = LeaveText, IsCancel = true }.WithPadding(DialogMetrics.ActionButtonPadding);
        leave.Click += (_, _) => { _fixes.Clear(); Close(); };
        _search.Click += (_, _) => _ = SearchFolderAsync();
        _done.Click += (_, _) => Close();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { leave, _search, _done } }.WithMargin(DialogMetrics.FooterGap);
        Content = new StackPanel { Children = { frame, _note, actions } }.WithMargin(DialogMetrics.ContentInset);
        Closing += (_, _) => _running?.Cancel();
    }

    /// <summary>The fixes chosen, keyed by the missing file's full path; empty after Leave missing.</summary>
    public IReadOnlyDictionary<string, string> Fixes => _fixes;

    private Control Row(MissingMedia item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") }.WithMargin(DialogMetrics.MediaRowInset);
        var name = new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        ToolTip.SetTip(name, item.FullPath);
        var status = MediaWindowParts.Muted(item.Folder);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
        status.WithMargin(DialogMetrics.MediaStatusGap);
        ToolTip.SetTip(status, item.Folder);
        var locate = new Button { Content = "Locate…" }.WithPadding(DialogMetrics.ActionButtonPadding);
        locate.Click += (_, _) => Locate(item);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(locate, 2);
        grid.Children.Add(name);
        grid.Children.Add(status);
        grid.Children.Add(locate);
        _rows[item.FullPath] = (status, locate);
        return grid;
    }

    private void Locate(MissingMedia item)
    {
        var picked = _dialogs.OpenFile(new FileDialogOptions { Title = "Locate " + item.Name, FileName = item.Name, CheckFileExists = true, Filter = "Audio files|*.wav;*.mp3;*.flac;*.aif;*.aiff;*.ogg;*.m4a;*.wma|All files|*.*" }, this);
        if (picked is not null) Fix(item.FullPath, picked);
    }

    private void Fix(string missingPath, string foundPath)
    {
        _fixes[missingPath] = foundPath;
        if (!_rows.TryGetValue(missingPath, out var row)) return;
        row.Status.Text = "fixed - " + (Path.GetDirectoryName(foundPath) ?? foundPath);
        row.Status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Accent");
        row.Locate.Content = "Change…";
    }

    private async Task SearchFolderAsync()
    {
        var folder = _dialogs.PickFolder(new FileDialogOptions { Title = "Search this folder (and below) for the missing files" }, this);
        if (folder is null) return;
        var open = _missing.Where(m => !_fixes.ContainsKey(m.FullPath)).ToList();
        if (open.Count == 0) return;
        _search.IsEnabled = false;
        _note.Text = "Searching " + folder + "…";
        _running = new CancellationTokenSource();
        var token = _running.Token;
        try
        {
            var found = await Task.Run(() => MediaRelink.Search(folder, open, token), token);
            foreach (var (missing, path) in found) Fix(missing, path);
            _note.Text = found.Count == 0 ? "Nothing with those names was found in that folder." : $"Fixed {found.Count} file{(found.Count == 1 ? "" : "s")} from that folder.";
        }
        catch (OperationCanceledException) { return; }   // the window closed while searching: nothing to show
        finally { _search.IsEnabled = true; }
    }
}
