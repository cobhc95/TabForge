using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Rendering;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the render dialog needs from the app.</summary>
public sealed class RenderContext
{
    public required SongProject Project { get; init; }
    public required AppSettings Settings { get; init; }
    /// <summary>Tracks selected in the arrangement / mixer (stems "selected tracks").</summary>
    public IReadOnlyList<TrackModel> SelectedTracks { get; init; } = Array.Empty<TrackModel>();
    /// <summary>The editor's selection (source bars and grid cells), or null.</summary>
    public (int StartBar, int StartCell, int EndBar, int EndCell)? Selection { get; init; }
    public Action? StopPlayback { get; init; }
    /// <summary>Re-sync the engine after the render.</summary>
    public Action? Restore { get; init; }
    public Action? SaveSettings { get; init; }
}

/// <summary>File > Render (Ctrl+Alt+R): source, bounds + tail, output, options, format.</summary>
public sealed class RenderWindow : Window
{
    private readonly RenderContext _ctx;
    private readonly RenderSettings _s;
    private readonly ComboBox _source = new(), _tail = new(), _rate = new(), _format = new(), _threads = new();
    private readonly RadioButton[] _bounds = new RadioButton[4];
    private readonly TextBox _tailMs = new() { Width = 70 }, _start = new() { Width = 70 }, _end = new() { Width = 70 }, _dir = new(), _pattern = new();
    private readonly CheckBox _mono = new() { Content = "Mono (L+R average)" }, _realtime = new() { Content = "Realtime pace (for streaming samplers)" },
        _open = new() { Content = "Open the folder when done" };
    private readonly ListBox _preview = new() { MinHeight = 90 };
    private readonly ProgressBar _bar = new() { Height = 16, Maximum = 1 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _render = new() { Content = "Render", Padding = new Thickness(18, 4, 18, 4), IsDefault = true };
    private readonly Button _cancel = new() { Content = "Cancel", Padding = new Thickness(14, 4, 14, 4), IsCancel = false };
    private readonly StackPanel _stemPanel = new() { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed }, _stemList = new();
    private readonly List<CheckBox> _stemBoxes = new();
    private readonly Button _stemAll = new() { Content = "All", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(6, 0, 0, 0) },
        _stemNone = new() { Content = "None", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(6, 0, 0, 0) };
    private readonly bool _mp3Ok;
    private CancellationTokenSource? _cts;
    private bool _loading = true;
    private static readonly int[] Rates = { 0, 44100, 48000, 88200, 96000 };
    private static readonly string[] Formats = { "WAV 16-bit", "WAV 24-bit", "WAV 32-bit float", "MP3 128 kbps", "MP3 160 kbps", "MP3 192 kbps", "MP3 320 kbps" };
    private static readonly int[] Kbps = { 128, 160, 192, 320 };

    public RenderWindow(RenderContext ctx, Window? owner)
    {
        _ctx = ctx; _s = ctx.Settings.Render;
        Owner = owner; Title = "Render to file";
        Width = 760; Height = 700; MinWidth = 620; MinHeight = 520;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        _mp3Ok = RenderJob.Mp3Available();

        var stack = new StackPanel { Margin = new Thickness(12) };
        var sourceStack = new StackPanel();
        sourceStack.Children.Add(_source);
        var stemButtons = Row(Label("Tracks to render as stems:"), _stemAll, _stemNone);
        _stemAll.Click += (_, _) => { foreach (var c in _stemBoxes) c.IsChecked = true; };
        _stemNone.Click += (_, _) => { foreach (var c in _stemBoxes) c.IsChecked = false; };
        _stemPanel.Children.Add(stemButtons);
        _stemPanel.Children.Add(new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stemList });
        sourceStack.Children.Add(_stemPanel);
        Group(stack, "Source", sourceStack);
        var saved = _s.StemTrackNames ?? (_ctx.SelectedTracks.Count > 0 ? _ctx.SelectedTracks.Select(t => t.Name).ToList() : null);
        foreach (var track in _ctx.Project.Tracks)
        {
            var box = new CheckBox { Content = track.Name, Tag = track, IsChecked = saved is null || saved.Contains(track.Name), Margin = new Thickness(0, 1, 0, 1) };
            box.Click += (_, _) => Changed();
            _stemBoxes.Add(box); _stemList.Children.Add(box);
        }
        _stemAll.Click += (_, _) => Changed(); _stemNone.Click += (_, _) => Changed();
        foreach (var t in new[] { "Master mix", "Stems: checked tracks", "Stems: all tracks", "Master mix + stems (all tracks)" }) _source.Items.Add(t);

        var bounds = new StackPanel();
        var names = new[] { "Entire song", "Time selection", "Selected bars", "Custom range (seconds)" };
        for (var i = 0; i < 4; i++)
        {
            _bounds[i] = new RadioButton { Content = names[i], GroupName = "bounds", Margin = new Thickness(0, 1, 0, 1) };
            _bounds[i].Checked += (_, _) => Changed();
            bounds.Children.Add(_bounds[i]);
        }
        bounds.Children.Add(Row(Label("from"), _start, Label("to"), _end));
        var tailRow = Row(Label("Tail:"), _tail, _tailMs, Label("ms (reverb / delay ring-out)"));
        foreach (var t in new[] { "Off", "Fixed", "Auto (until silent)" }) _tail.Items.Add(t);
        bounds.Children.Add(tailRow);
        Group(stack, "Bounds", bounds, "Render.Range");
        string[] rangeIds = { "Entire", "Selection", "Bars", "Custom" };
        for (var i = 0; i < 4; i++) UiIds.Id(_bounds[i], "Render.Range." + rangeIds[i]);

        var output = new DockPanel();
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        output.Children.Add(browse); output.Children.Add(_dir);
        var outStack = new StackPanel();
        outStack.Children.Add(output);
        outStack.Children.Add(Row(Label("File name:"), _pattern));
        _pattern.MinWidth = 300;
        outStack.Children.Add(Small("Wildcards: $project $track $tracknumber $date $time $bpm. Existing files are never overwritten: a number is added."));
        outStack.Children.Add(_preview);
        Group(stack, "Output", outStack);

        var options = new StackPanel();
        foreach (var r in Rates) _rate.Items.Add(new ComboBoxItem { Content = r == 0 ? "Engine rate" : $"{r / 1000.0:0.#} kHz" });
        foreach (var f in Formats) _format.Items.Add(new ComboBoxItem { Content = f, IsEnabled = _mp3Ok || !f.StartsWith("MP3"), ToolTip = !_mp3Ok && f.StartsWith("MP3") ? "MP3 needs the Windows Media Foundation encoder, which this Windows edition does not have." : null });
        _threads.Items.Add("Threads: auto"); _threads.Items.Add("Threads: 1");
        options.Children.Add(Row(Label("Sample rate:"), _rate, Label("Format:"), _format, _threads));
        options.Children.Add(_mono); options.Children.Add(_realtime);
        options.Children.Add(Small("MP3: 44.1 / 48 kHz only. The engine's General MIDI synth may sound slightly different from the Windows MIDI synth used for normal playback."));
        Group(stack, "Options and format", options);
        stack.Children.Add(_open);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        _render.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_render); buttons.Children.Add(_cancel);
        _bar.Margin = new Thickness(0, 8, 0, 4);
        _bar.SetResourceReference(StyleProperty, "ThemedProgressBar");   // no bright default strip on the dark theme
        _bar.Height = 10;
        // Progress, status and the Render / Close buttons sit in a footer outside the scroll area, so they are always in view.
        var footer = new StackPanel { Margin = new Thickness(14, 0, 14, 12) };
        footer.Children.Add(_bar); footer.Children.Add(_status); footer.Children.Add(buttons);
        var footerHost = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerHost.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
        DockPanel.SetDock(footerHost, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footerHost);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
        Content = layout;
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);

        LoadSettings();
        foreach (var c in new[] { _source, _tail, _rate, _format, _threads }) c.SelectionChanged += (_, _) => Changed();
        foreach (var t in new[] { _tailMs, _start, _end, _dir, _pattern }) t.TextChanged += (_, _) => Changed();
        _mono.Click += (_, _) => Changed(); _realtime.Click += (_, _) => Changed(); _open.Click += (_, _) => Changed();
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Render into folder", InitialDirectory = _dir.Text };
            if (dlg.ShowDialog(this) == true) _dir.Text = dlg.FolderName;
        };
        // Stable automation ids (the folder box's own name is its path, so it gets a fixed name).
        UiIds.Id(_dir, "Render.Folder", "Output folder");
        UiIds.Id(_pattern, "Render.FileName", "File name pattern");
        UiIds.Id(_format, "Render.Format", "Format");
        UiIds.Id(_source, "Render.Source", "Source");
        UiIds.Id(browse, "Render.Browse");
        UiIds.Id(_render, "Render.Start");
        UiIds.Id(_cancel, "Render.Cancel");
        _render.Click += async (_, _) => await RenderAsync();
        _cancel.Click += (_, _) => { if (_cts is not null) _cts.Cancel(); else Close(); };
        Closing += (_, e) => { _cts?.Cancel(); SaveSettings(); };
        OwnerActivation.Attach(this);
        _loading = false;
        Changed();
    }

    private void LoadSettings()
    {
        _source.SelectedIndex = Math.Clamp(_s.Source, 0, 3);
        var hasSel = _ctx.Selection is not null;
        var b = Math.Clamp(_s.Bounds, 0, 3);
        if (!hasSel && b is 1 or 2) b = 0;
        _bounds[b].IsChecked = true;
        _bounds[1].IsEnabled = _bounds[2].IsEnabled = hasSel;
        if (!hasSel) { _bounds[1].ToolTip = _bounds[2].ToolTip = "Select bars in the score first."; }
        _start.Text = _s.CustomStartSec.ToString("0.###", CultureInfo.InvariantCulture);
        _end.Text = (_s.CustomEndSec > 0 ? _s.CustomEndSec : 60).ToString("0.###", CultureInfo.InvariantCulture);
        _tail.SelectedIndex = Math.Clamp(_s.TailMode, 0, 2);
        _tailMs.Text = _s.TailMs.ToString(CultureInfo.InvariantCulture);
        _dir.Text = string.IsNullOrWhiteSpace(_s.Directory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) : _s.Directory;
        _pattern.Text = string.IsNullOrWhiteSpace(_s.Pattern) ? "$project" : _s.Pattern;
        var ri = Array.IndexOf(Rates, _s.SampleRate); _rate.SelectedIndex = ri < 0 ? 0 : ri;
        var fi = _s.Format == 3 ? 3 + Math.Max(0, Array.IndexOf(Kbps, _s.Mp3Kbps)) : Math.Clamp(_s.Format, 0, 2);
        if (!_mp3Ok && fi >= 3) fi = 1;
        _format.SelectedIndex = fi;
        _threads.SelectedIndex = _s.SingleThread ? 1 : 0;
        _mono.IsChecked = _s.Mono; _realtime.IsChecked = _s.RealtimePace; _open.IsChecked = _s.OpenFolder;
    }

    private void SaveSettings()
    {
        _s.Source = Math.Max(0, _source.SelectedIndex);
        _s.StemTrackNames = _stemBoxes.Where(b => b.IsChecked == true).Select(b => ((TrackModel)b.Tag).Name).ToList();
        _s.Bounds = Array.FindIndex(_bounds, b => b.IsChecked == true);
        if (_s.Bounds < 0) _s.Bounds = 0;
        _s.CustomStartSec = Num(_start.Text); _s.CustomEndSec = Num(_end.Text);
        _s.TailMode = Math.Max(0, _tail.SelectedIndex); _s.TailMs = (int)Math.Clamp(Num(_tailMs.Text), 0, 30000);
        _s.Directory = _dir.Text; _s.Pattern = _pattern.Text;
        _s.SampleRate = Rates[Math.Max(0, _rate.SelectedIndex)];
        var f = Math.Max(0, _format.SelectedIndex);
        _s.Format = f >= 3 ? 3 : f; if (f >= 3) _s.Mp3Kbps = Kbps[f - 3];
        _s.SingleThread = _threads.SelectedIndex == 1;
        _s.Mono = _mono.IsChecked == true; _s.RealtimePace = _realtime.IsChecked == true; _s.OpenFolder = _open.IsChecked == true;
        _ctx.SaveSettings?.Invoke();
    }

    private static double Num(string text) => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void Changed()
    {
        if (_loading) return;
        var mp3 = _format.SelectedIndex >= 3;
        for (var i = 0; i < _rate.Items.Count; i++) if (_rate.Items[i] is ComboBoxItem item) item.IsEnabled = !mp3 || Rates[i] is 0 or 44100 or 48000;
        if (mp3 && Rates[Math.Max(0, _rate.SelectedIndex)] is 88200 or 96000) _rate.SelectedIndex = 0;
        var custom = _bounds[3].IsChecked == true;
        _start.IsEnabled = _end.IsEnabled = custom;
        _tailMs.IsEnabled = _tail.SelectedIndex != 0;
        _stemPanel.Visibility = _source.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _preview.Items.Clear();
        try { foreach (var f in Plan().Files) _preview.Items.Add(f); } catch (Exception) { }
    }

    private (List<string> Files, string? Master, Dictionary<TrackModel, string> Stems) Plan()
    {
        var src = Math.Max(0, _source.SelectedIndex);
        var ext = _format.SelectedIndex >= 3 ? ".mp3" : ".wav";
        var dir = _dir.Text.Trim();
        var now = DateTime.Now;
        var project = _ctx.Project;
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Name(string track, int number)
        {
            var n = RenderNaming.Expand(_pattern.Text, project.Title ?? "song", track, number, now, project.Tempo) + ext;
            n = RenderNaming.Unique(dir, n, reserved); reserved.Add(n); return n;
        }
        var files = new List<string>(); string? master = null; var stems = new Dictionary<TrackModel, string>();
        if (src is 0 or 3) { var n = Name("master", 0); master = Path.Combine(dir, n); files.Add(n); }
        if (src != 0)
        {
            var tracks = src == 1 ? _stemBoxes.Where(b => b.IsChecked == true).Select(b => (TrackModel)b.Tag).ToList() : project.Tracks.ToList();
            foreach (var t in tracks)
            {
                var n = Name(t.Name, project.Tracks.IndexOf(t) + 1);
                if (!_pattern.Text.Contains("$track", StringComparison.Ordinal)) { reserved.Remove(n); n = RenderNaming.Unique(dir, Path.GetFileNameWithoutExtension(n) + "_" + RenderNaming.Sanitize(t.Name) + ext, reserved); reserved.Add(n); }
                stems[t] = Path.Combine(dir, n); files.Add(n);
            }
        }
        return (files, master, stems);
    }

    private async Task RenderAsync()
    {
        SaveSettings();
        try
        {
            if (!Directory.Exists(_dir.Text)) Directory.CreateDirectory(_dir.Text);
            var plan = Plan();
            if (plan.Files.Count == 0) { _status.Text = "Nothing to render."; return; }
            _ctx.StopPlayback?.Invoke();
            _status.Text = "Preparing…";
            _render.IsEnabled = false; IsEnabled = true; _cancel.Content = "Cancel render";
            var tl = RenderSpecBuilder.Compile(_ctx.Project);
            var sel = _ctx.Selection;
            var (startMs, endMs) = RenderSpecBuilder.Bounds(tl, (RenderBounds)Math.Max(0, _s.Bounds), sel?.StartBar ?? 0, sel?.StartCell ?? 0, sel?.EndBar ?? 0, sel?.EndCell ?? -1, _s.CustomStartSec, _s.CustomEndSec);
            var request = new RenderRequest
            {
                Project = _ctx.Project, Plugins = _ctx.Settings.Plugins, MasterPercent = _ctx.Settings.Audio.MasterVolume, Settings = _s, Timeline = tl,
                StartMs = startMs, EndMs = endMs, MasterFile = plan.Master, Stems = plan.Stems, Restore = _ctx.Restore,
                ConfirmIncomplete = missing => Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new ThemedConfirmDialog("Plug-ins not loaded",
                        "Some plug-ins are not loaded, so the render would sound different from the song:\n\n" + string.Join("\n", missing.Select(m => "• " + m)) + "\n\nRender anyway without them?",
                        yesToolTip: "Render without the missing plug-ins", noToolTip: "Cancel the render", showCancel: false) { Owner = this };
                    return DialogHost.ShowModal(dialog) == true && dialog.Result == MessageBoxResult.Yes;
                }).Task,
            };
            _cts = new CancellationTokenSource();
            var progress = new Progress<RenderProgressInfo>(p =>
            {
                _bar.Value = p.Fraction;
                _status.Text = $"Rendering… {p.Fraction:P0}, {p.Seconds:0.0} s of audio, {p.Speed:0.#}x realtime";
            });
            var result = await RenderJob.RunAsync(request, progress, _cts.Token);
            _bar.Value = 1;
            var clipped = result.Files.Sum(f => f.ClippedSamples);
            _status.Text = $"Done: {plan.Files.Count} file(s), {result.Seconds:0.0} s of audio in {result.ElapsedSeconds:0.0} s ({result.Seconds / Math.Max(0.001, result.ElapsedSeconds):0.#}x realtime)" + (clipped > 0 ? $"; {clipped} samples clipped." : ".");
            if (_open.IsChecked == true) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_dir.Text}\"") { UseShellExecute = true });
        }
        catch (RenderException ex) { _status.Text = ex.Cancelled ? "Render cancelled." : "Render failed: " + ex.Message + (string.IsNullOrEmpty(ex.PluginPath) ? "" : $" (plug-in: {Path.GetFileName(ex.PluginPath)}; try Threads: 1)"); }
        catch (Exception ex) { _status.Text = "Render failed: " + ex.Message; }
        finally { _cts?.Dispose(); _cts = null; _render.IsEnabled = true; _cancel.Content = "Close"; UiIds.Id(_cancel, "Render.Close"); Changed(); }
    }

    // ---- small layout helpers ----
    private TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        return t;
    }

    private TextBlock Small(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        foreach (var i in items) { if (i is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, fe.Margin.Right + 6, fe.Margin.Bottom); row.Children.Add(i); }
        return row;
    }

    private static void Group(StackPanel parent, string header, UIElement content, string? automationId = null)
    {
        var box = new GroupBox { Header = header, Padding = new Thickness(8, 4, 8, 6), Margin = new Thickness(0, 0, 0, 8), Content = content };
        if (automationId is not null) UiIds.Id(box, automationId, header);
        box.SetResourceReference(ForegroundProperty, "TextBrush");
        box.SetResourceReference(BorderBrushProperty, "BorderSoftBrush");
        parent.Children.Add(box);
    }
}
