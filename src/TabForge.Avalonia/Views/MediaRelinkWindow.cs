using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// "Find missing audio": says how many audio files cannot be found, takes a folder (typed or Browse…), searches it and every subfolder by file name on a worker
/// (the list fills as files turn up; Stop ends it), shows per file "found - subfolder", "N matches - picked ..." or "not found" with Locate…, and hands the
/// fixes back when Relink is pressed. Locate… on one file also fixes the other missing files found in the same folder.
/// Owns: the rows, the pickers it opens, the search task and the fixes it collects. Does not own: the search and the pick among several matches (MediaRelink),
/// the model change and its one undo step (the caller applies Fixes).
/// Tests: TabForge.Avalonia.Tests.MediaRelinkWindowTests (the window), MediaRelinkTests (the search).
/// </summary>
public sealed class MediaRelinkWindow : Window
{
    public const string TitleText = "Find missing audio";
    public const string CancelText = "Cancel";
    public const string SearchText = "Search", StopText = "Stop", BrowseText = "Browse\u2026", LocateText = "Locate\u2026";
    private const string Tick = "\u2713", Cross = "\u2717", Dot = " \u00B7 ";

    private readonly IReadOnlyList<MissingMedia> _missing;
    private readonly IFileDialogs _dialogs;
    private readonly Dictionary<string, string> _fixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _result = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (TextBlock Status, Button Locate)> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _folder = new() { Name = "FolderBox", Watermark = "Folder to search" };
    private readonly TextBlock _summary = MediaWindowParts.Muted("");
    private readonly Button _browse = new Button { Name = "BrowseButton", Content = BrowseText }.WithPadding(DialogMetrics.ActionButtonPadding);
    private readonly Button _search = new Button { Name = "SearchButton", Content = SearchText }.WithPadding(DialogMetrics.ActionButtonPadding);
    private readonly Button _relink = PromptChrome.Action("RelinkButton", "", primary: true);
    private CancellationTokenSource? _running;

    public MediaRelinkWindow(IReadOnlyList<MissingMedia> missing, IFileDialogs dialogs)
    {
        _missing = missing;
        _dialogs = dialogs;
        PromptChrome.Setup(this, TitleText, DialogMetrics.MediaRelinkWidth);
        CanResize = true;
        MinWidth = DialogMetrics.MediaRelinkMinWidth;
        _summary.Name = "SummaryText";
        _search.Classes.Add(AvaloniaDefaultLooks.AccentLook);

        var intro = PromptChrome.Muted("IntroText", Intro(missing.Count));
        var pick = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") }.WithMargin(DialogMetrics.MediaListGap);
        _browse.WithMargin(DialogMetrics.MediaStatusGap);
        Grid.SetColumn(_browse, 1);
        Grid.SetColumn(_search, 2);
        pick.Children.Add(_folder);
        pick.Children.Add(_browse);
        pick.Children.Add(_search);
        var list = new StackPanel();
        foreach (var item in missing) list.Children.Add(Row(item));
        var frame = new Border { BorderThickness = DialogMetrics.MediaOutline, CornerRadius = new CornerRadius(DialogMetrics.MediaCorner), Child = new ScrollViewer { Content = list, MaxHeight = DialogMetrics.MediaRelinkListMaxHeight } }.WithMargin(DialogMetrics.MediaFrameGap);
        frame[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush");

        var cancel = PromptChrome.Action("CancelButton", CancelText, primary: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        _browse.Click += (_, _) => Browse();
        _search.Click += (_, _) => { if (_running is null) _ = SearchAsync(_folder.Text ?? ""); else _running.Cancel(); };
        _relink.Click += (_, _) => { foreach (var (path, found) in _fixes) _result[path] = found; Close(); };
        Content = new StackPanel { Children = { PromptChrome.Heading(TitleText), intro, pick, frame, _summary, PromptChrome.Footer(null, null, cancel, _relink) } }.WithMargin(DialogMetrics.PromptInset);
        _folder.KeyDown += (_, e) => { if (e.Key == Key.Enter && _running is null) { _ = SearchAsync(_folder.Text ?? ""); e.Handled = true; } };
        Closing += (_, _) => _running?.Cancel();
        Update();
    }

    /// <summary>The fixes to apply, keyed by the missing file full path; empty unless Relink was pressed.</summary>
    public IReadOnlyDictionary<string, string> Fixes => _result;

    /// <summary>How many files have a fix chosen now.</summary>
    public int FixCount => _fixes.Count;

    internal static string Intro(int count) =>
        $"{count} audio file{(count == 1 ? "" : "s")} can\u0027t be found. Pick a folder; TabForge searches it and every subfolder by file name.";

    /// <summary>The file picker options for locating <paramref name="item"/> by hand (the window and the clip menu share them).</summary>
    public static FileDialogOptions LocateOptions(MissingMedia item) =>
        new() { Title = "Locate " + item.Name, FileName = item.Name, CheckFileExists = true, Filter = "Audio files|*.wav;*.mp3;*.flac;*.aif;*.aiff;*.ogg;*.m4a;*.wma|All files|*.*" };

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
        var locate = new Button { Content = LocateText }.WithPadding(DialogMetrics.ActionButtonPadding);
        locate.Click += (_, _) => Locate(item);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(locate, 2);
        grid.Children.Add(name);
        grid.Children.Add(status);
        grid.Children.Add(locate);
        _rows[item.FullPath] = (status, locate);
        return grid;
    }

    private void Browse()
    {
        var picked = _dialogs.PickFolder(new FileDialogOptions { Title = "Search this folder (and below) for the missing audio" }, this);
        if (picked is null) return;
        _folder.Text = picked;
        _ = SearchAsync(picked);
    }

    // One file located by hand: it is fixed, and so is every other missing file found beside it.
    private void Locate(MissingMedia item)
    {
        var picked = _dialogs.OpenFile(LocateOptions(item), this);
        if (picked is null) return;
        Fix(item.FullPath, picked, Tick + " located" + Dot + (Path.GetDirectoryName(picked) ?? picked));
        foreach (var (path, found) in MediaRelink.SiblingsOf(picked, _missing.Where(m => !string.Equals(m.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase) && !_fixes.ContainsKey(m.FullPath))))
            Fix(path, found, Tick + " found beside it" + Dot + (Path.GetDirectoryName(found) ?? found));
        Update();
    }

    private void Fix(string missingPath, string foundPath, string text)
    {
        _fixes[missingPath] = foundPath;
        if (!_rows.TryGetValue(missingPath, out var row)) return;
        row.Status.Text = text;
        row.Status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Accent");
        ToolTip.SetTip(row.Status, foundPath);
        row.Locate.Content = "Change\u2026";
    }

    /// <summary>
    /// Searches <paramref name="folder"/> (and every folder below) for the files still missing, on a worker; matches appear in the list as they turn up.
    /// Files fixed earlier are kept, so a second search only looks for the rest.
    /// </summary>
    public async Task SearchAsync(string folder)
    {
        var open = _missing.Where(m => !_fixes.ContainsKey(m.FullPath)).ToList();
        if (_running is not null || open.Count == 0) return;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { _summary.Text = "That folder does not exist."; return; }
        _running = new CancellationTokenSource();
        var token = _running.Token;
        _search.Content = StopText;
        _summary.Text = "Searching " + folder + "\u2026";
        var progress = new Progress<MediaMatch>(OnMatch);
        try
        {
            var all = await Task.Run(() => MediaRelink.Find(folder, open, token, progress), token);
            foreach (var match in all) OnMatch(match);
            foreach (var item in open.Where(m => !_fixes.ContainsKey(m.FullPath)))
                if (_rows.TryGetValue(item.FullPath, out var row)) { row.Status.Text = Cross + " not found"; row.Status[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger"); }
            Update();
        }
        catch (OperationCanceledException) { Update("Search stopped. "); }
        finally { _running = null; _search.Content = SearchText; }
    }

    private void OnMatch(MediaMatch match)
    {
        if (match.FoundPath is null) return;
        var where = match.Subfolder.Length == 0 ? "this folder" : match.Subfolder;
        Fix(match.MissingPath, match.FoundPath, match.Matches > 1 ? $"{Tick} {match.Matches} matches{Dot}picked {match.Pick}" : $"{Tick} found{Dot}{where}");
        Update();
    }

    private void Update(string prefix = "")
    {
        var n = _fixes.Count;
        _summary.Text = prefix + (n == _missing.Count ? $"{n} of {_missing.Count} found." : $"{n} of {_missing.Count} found. Search another folder for the rest, or relink these now.");
        _relink.Content = "Relink " + n;
        _relink.IsEnabled = n > 0;
    }
}
