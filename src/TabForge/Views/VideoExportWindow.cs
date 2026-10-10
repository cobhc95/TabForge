using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Views.Video;

namespace TabForge.Views;

/// <summary>
/// File > Export > Video (MP4): range, layout, tracks, theme, instrument, size and frame rate; exports with <see cref="VideoExportFlow"/>
/// and shows its progress with Cancel. The choices are remembered in <see cref="VideoSettings"/> when "Remember these settings" is on.
/// Owns: the options page, the range and file choice and the progress display.
/// Does not own: drawing or encoding (Services/Video) or the audio render.
/// Tests: TestVideoExport.
/// </summary>
public sealed class VideoExportWindow : Window
{
    private static readonly string[] Ranges = { "Entire song", "Selection", "Bars", "Section" };
    private static readonly string[] Layouts = { "Focus on one track (score and instrument)", "Score only", "Band view", "Score and Band view" };

    private readonly RenderContext _ctx;
    private readonly VideoSettings _s;
    private readonly List<MarkerModel> _sections;
    private readonly int _barCount;
    private readonly ComboBox _range = new() { MinWidth = 170 }, _section = new() { MinWidth = 260 }, _layout = new() { MinWidth = 360 }, _theme = new() { MinWidth = 100 },
        _size = new() { MinWidth = 210 }, _fps = new() { MinWidth = 70 };
    private readonly TextBox _from = new() { Width = 60 }, _to = new() { Width = 60 };
    private readonly CheckBox _instrument = new() { Content = "Show the instrument (fretboard, keyboard or drums)" }, _remember = new() { Content = "Remember these settings" };
    private readonly StackPanel _trackList = new();
    private readonly List<CheckBox> _trackBoxes = new();
    private readonly ProgressBar _bar = new() { Height = 16, Maximum = 1 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Button _export = new() { Content = "Export", Padding = new Thickness(18, 4, 18, 4), IsDefault = true };
    private readonly Button _cancel = new() { Content = "Close", Padding = new Thickness(14, 4, 14, 4) };
    private CancellationTokenSource? _cts;

    public VideoExportWindow(RenderContext ctx, Window? owner)
    {
        _ctx = ctx; _s = ctx.Settings.Video;
        Owner = owner; Title = "Export video (MP4)";
        Width = 640; Height = Math.Min(760, SystemParameters.WorkArea.Height - 40); MinWidth = 520; MinHeight = 480;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        _sections = SectionLayout.Sorted(ctx.Project);
        _barCount = BarRangeEditor.MaxMeasures(ctx.Project);

        foreach (var t in Ranges) _range.Items.Add(t);
        foreach (var m in _sections) _section.Items.Add(RenderBarRange.Label(m));
        foreach (var t in Layouts) _layout.Items.Add(t);
        foreach (var t in new[] { "Dark", "Light" }) _theme.Items.Add(t);
        foreach (var t in new[] { "1080p (1920 x 1080)", "4K (3840 x 2160)" }) _size.Items.Add(t);
        foreach (var t in new[] { "30", "60", "120" }) _fps.Items.Add(t);
        foreach (var track in ctx.Project.Tracks)
        {
            var box = new CheckBox { Content = track.Name, Tag = track, Margin = new Thickness(0, 1, 0, 1) };
            _trackBoxes.Add(box); _trackList.Children.Add(box);
        }

        var stack = new StackPanel { Margin = new Thickness(12) };
        Group(stack, "Range", Column(Row(Label("Range:"), _range), Row(Label("from bar"), _from, Label("to bar"), _to, Label($"(1 to {_barCount})")), Row(Label("Section:"), _section)));
        Group(stack, "View", Column(Row(Label("Layout:"), _layout), Row(Label("Theme:"), _theme), _instrument));
        Group(stack, "Tracks (the first is the score and instrument; all are shown in the Band view)", new ScrollViewer { MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _trackList });
        Group(stack, "Video", Column(Row(Label("Resolution:"), _size, Label("Frames per second:"), _fps), _remember));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_export); buttons.Children.Add(new Border { Width = 8 }); buttons.Children.Add(_cancel);
        var footer = new StackPanel { Margin = new Thickness(12) };
        footer.Children.Add(_bar); footer.Children.Add(_status); footer.Children.Add(buttons);
        var footerHost = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerHost.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        DockPanel.SetDock(footerHost, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footerHost);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
        Content = layout;

        _bar.SetResourceReference(StyleProperty, "ThemedProgressBar");   // no bright default strip on the dark theme
        Load();
        UiIds.Id(_range, "Video.Range", "Range"); UiIds.Id(_layout, "Video.Layout", "Layout"); UiIds.Id(_theme, "Video.Theme", "Theme");
        UiIds.Id(_size, "Video.Size", "Resolution"); UiIds.Id(_fps, "Video.Fps", "Frames per second"); UiIds.Id(_export, "Video.Export"); UiIds.Id(_cancel, "Video.Close");
        _range.SelectionChanged += (_, _) => ShowRange();
        _layout.SelectionChanged += (_, _) => ShowTracks();
        _export.Click += async (_, _) => await ExportAsync();
        _cancel.Click += (_, _) => { if (_cts is not null) _cts.Cancel(); else Close(); };
        Closing += (_, e) => { if (_cts is not null) e.Cancel = true; else Save(); };
        OwnerActivation.Attach(this);
    }

    public static void OpenDialog(RenderContext ctx, Window? owner) => new VideoExportWindow(ctx, owner).ShowDialog();

    private void Load()
    {
        _range.SelectedIndex = _ctx.Selection is null && _s.Range == 1 ? 0 : Math.Clamp(_s.Range, 0, 3);
        _from.Text = Math.Clamp(_s.FromBar, 1, Math.Max(1, _barCount)).ToString();
        _to.Text = Math.Clamp(_s.ToBar, 1, Math.Max(1, _barCount)).ToString();
        _section.SelectedIndex = _sections.Count > 0 ? Math.Clamp(_s.Section, 0, _sections.Count - 1) : -1;
        _layout.SelectedIndex = Math.Clamp(_s.Layout, 0, 3);
        _theme.SelectedIndex = _s.Dark ? 0 : 1;
        _instrument.IsChecked = _s.ShowInstrument;
        _size.SelectedIndex = _s.Height >= 2160 ? 1 : 0;
        _fps.SelectedItem = _s.Fps is 60 or 120 ? _s.Fps.ToString() : "30";
        _remember.IsChecked = _s.Remember;
        var names = _s.TrackNames ?? (_ctx.SelectedTracks.Count > 0 ? _ctx.SelectedTracks.Select(t => t.Name).ToList() : _ctx.Project.Tracks.Take(1).Select(t => t.Name).ToList());
        foreach (var box in _trackBoxes) box.IsChecked = names.Contains(((TrackModel)box.Tag).Name);
        if (_trackBoxes.Count > 0 && _trackBoxes.All(b => b.IsChecked != true)) _trackBoxes[0].IsChecked = true;
        ShowRange(); ShowTracks();
    }

    private void ShowRange()
    {
        var bars = _range.SelectedIndex == 2;
        _from.IsEnabled = _to.IsEnabled = bars;
        _section.IsEnabled = _range.SelectedIndex == 3 && _sections.Count > 0;
    }

    private void ShowTracks()
    {
        var focus = _layout.SelectedIndex is 0 or 1;
        foreach (var box in _trackBoxes) box.ToolTip = focus ? "Only the first checked track is shown" : null;
    }

    private VideoViewSpec Spec()
    {
        var tracks = _trackBoxes.Select((b, i) => (b, i)).Where(x => x.b.IsChecked == true).Select(x => x.i).ToList();
        if (tracks.Count == 0) tracks.Add(0);
        var tall = _size.SelectedIndex == 1;
        return new VideoViewSpec
        {
            Layout = (VideoLayout)_layout.SelectedIndex, Tracks = tracks, Dark = _theme.SelectedIndex == 0, ShowInstrument = _instrument.IsChecked == true,
            Width = tall ? 3840 : 1920, Height = tall ? 2160 : 1080, Notation = _ctx.Notation,
        };
    }

    private int Fps() => int.TryParse(_fps.SelectedItem as string, out var f) ? f : 30;

    private void Save()
    {
        _s.Remember = _remember.IsChecked == true;
        if (!_s.Remember) return;
        _s.Range = _range.SelectedIndex; _s.Layout = _layout.SelectedIndex; _s.Dark = _theme.SelectedIndex == 0; _s.ShowInstrument = _instrument.IsChecked == true;
        _s.Height = _size.SelectedIndex == 1 ? 2160 : 1080; _s.Fps = Fps();
        if (int.TryParse(_from.Text, out var f)) _s.FromBar = f;
        if (int.TryParse(_to.Text, out var t)) _s.ToBar = t;
        _s.Section = Math.Max(0, _section.SelectedIndex);
        _s.TrackNames = _trackBoxes.Where(b => b.IsChecked == true).Select(b => ((TrackModel)b.Tag).Name).ToList();
        _ctx.SaveSettings?.Invoke();
    }

    /// <summary>The range in song milliseconds, or why it cannot be exported.</summary>
    private (double Start, double End, string? Problem) Bounds(ScoreTimeline timeline)
    {
        switch (_range.SelectedIndex)
        {
            case 1:
                if (_ctx.Selection is not { } sel) return (0, 0, "Select some bars first.");
                var (a, b) = RenderSpecBuilder.Bounds(timeline, RenderBounds.Bars, sel.StartBar, sel.StartCell, sel.EndBar, sel.EndCell, 0, 0);
                return (a, b, null);
            case 2:
                var problem = RenderBarRange.Validate(_from.Text, _to.Text, _barCount);
                if (problem is not null) return (0, 0, problem);
                var (c, d) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, int.Parse(_from.Text) - 1, 0, int.Parse(_to.Text) - 1, -1, 0, 0);
                return (c, d, null);
            case 3:
                if (_section.SelectedIndex < 0 || RenderBarRange.SectionBars(_ctx.Project, _section.SelectedIndex, _section.SelectedIndex) is not { } bars) return (0, 0, "The song has no section to export.");
                var (e, f) = RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomSections, bars.FirstBar, 0, bars.LastBar, -1, 0, 0);
                return (e, f, null);
            default:
                var (g, h) = RenderSpecBuilder.Bounds(timeline, RenderBounds.Song, 0, 0, 0, -1, 0, 0);
                return (g, h, null);
        }
    }

    private async Task ExportAsync()
    {
        Save();
        try
        {
            var timeline = RenderSpecBuilder.Compile(_ctx.Project);
            var (start, end, problem) = Bounds(timeline);
            if (problem is not null) { _status.Text = problem; return; }
            if (end - start < 100) { _status.Text = "The chosen range is empty."; return; }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export video", Filter = "MP4 video (*.mp4)|*.mp4", DefaultExt = ".mp4", AddExtension = true, OverwritePrompt = true,
                FileName = TabForge.Audio.Contracts.SafeFileNames.SafeFileName(_ctx.Project.Title, "Untitled"), InitialDirectory = Directory.Exists(_s.Directory) ? _s.Directory : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            };
            if (dialog.ShowDialog(this) != true) return;
            if (_s.Remember) { _s.Directory = Path.GetDirectoryName(dialog.FileName) ?? ""; _ctx.SaveSettings?.Invoke(); }
            _ctx.StopPlayback?.Invoke();
            _export.IsEnabled = false; _cancel.Content = "Cancel export";
            _status.Text = "Rendering the audio…";
            var request = new VideoExportRequest
            {
                Project = _ctx.Project, Settings = _ctx.Settings, Engine = _ctx.Engine, Media = _ctx.Media, Timeline = timeline, StartMs = start, EndMs = end,
                Spec = Spec(), Path = dialog.FileName, Fps = Fps(), Restore = _ctx.Restore, Look = _ctx.Look,
                ConfirmIncomplete = missing => Dispatcher.InvokeAsync(() =>
                {
                    var ask = new ThemedConfirmDialog("Plug-ins not loaded",
                        "Some plug-ins are not loaded, so the audio would sound different from the song:\n\n" + string.Join("\n", missing.Select(m => "• " + m)) + "\n\nExport anyway without them?",
                        yesToolTip: "Export without the missing plug-ins", noToolTip: "Cancel the export", showCancel: false) { Owner = this };
                    return DialogHost.ShowModal(ask) == true && ask.Result == MessageBoxResult.Yes;
                }).Task,
            };
            _cts = new CancellationTokenSource();
            var progress = new Progress<VideoExportProgress>(p => { _bar.Value = p.Fraction; _status.Text = $"{p.Text} ({p.Fraction:P0})"; });
            var result = await VideoExportFlow.RunAsync(request, progress, _cts.Token);
            _bar.Value = 1;
            _status.Text = $"Done: {result.Frames} frames, {result.Seconds:0.0} s of video in {result.ElapsedSeconds:0.0} s.";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException) { _status.Text = "Export cancelled."; }
        catch (RenderException ex) { _status.Text = ex.Cancelled ? "Export cancelled." : "Export failed: " + ex.Message; }
        catch (Exception ex) { _status.Text = "Export failed: " + ex.Message; }
        finally { _cts?.Dispose(); _cts = null; _export.IsEnabled = true; _cancel.Content = "Close"; }
    }

    // ---- small layout helpers ----
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var i in items) { if (i is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, fe.Margin.Right + 6, fe.Margin.Bottom); row.Children.Add(i); }
        return row;
    }

    private static StackPanel Column(params UIElement[] items)
    {
        var column = new StackPanel();
        foreach (var i in items) column.Children.Add(i);
        return column;
    }

    private static void Group(StackPanel parent, string header, UIElement content)
    {
        var box = new GroupBox { Header = header, Padding = new Thickness(8, 4, 8, 6), Margin = new Thickness(0, 0, 0, 8), Content = content };
        box.SetResourceReference(ForegroundProperty, "TextBrush");
        box.SetResourceReference(BorderBrushProperty, "BorderSoftBrush");
        parent.Children.Add(box);
    }
}
