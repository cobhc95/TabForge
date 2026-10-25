using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Rendering;
using TabForge.Services;
using Trace = TabForge.Services.Trace;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// File > Render (Tools > Render to file… in the preview): source, bounds and tail, output folder and file name, sample rate, format and options.
/// Render runs the given job (AvaloniaRenderRunner) with progress and cancel; without one (the preview) it reports that it needs the full app. Browse asks for the output folder
/// through the given folder picker (the preview passes none and reports that it needs the full app). Video (MP4)… closes this window and
/// opens the video export window.
/// Owns: the controls (their order, texts and automation names as in the WPF dialog), loading and saving RenderSettings, and the enabling,
///     hint and file-preview rules applied from RenderDialogPresenter and RenderBarRange. It is one class because the controls share their state.
/// Does not own: the render (RenderJob and the engine), the bounds and file-name rules (RenderDialogPresenter), the custom range check
///     (RenderBarRange), or the video dialog (VideoExportWindow).
/// The MP3 formats follow the encoder check the caller passes (RenderJob.Mp3Available); the preview enables them.
/// Over 300 lines: the controls share their state (sections, bounds, stems and the file plan), so they stay in one class.
/// Tests: TabForge.Avalonia.Tests.RenderVideoExportTutorialTests.
/// </summary>
public sealed class RenderWindow : Window
{
    private const string CancelLabel = "Cancel", CancelRenderLabel = "Cancel render", CloseLabel = "Close";
    private const string Mp3MissingTip = "MP3 needs the Windows Media Foundation encoder, which this Windows edition does not have.";

    private readonly SongProject _project;
    private readonly RenderSettings _settings;
    private readonly bool _hasSelection;
    private readonly Action? _openVideoExport;
    private readonly List<MarkerModel> _sections;
    private readonly int _barCount;
    private readonly List<CheckBox> _stemBoxes = new();
    private readonly RadioButton[] _bounds = new RadioButton[6];
    private readonly ComboBox _source = new() { MinWidth = 260 }, _tail = new() { MinWidth = 180 }, _rate = new() { MinWidth = 120 },
        _format = new() { MinWidth = 180 }, _threads = new() { MinWidth = 140 }, _secFrom = new() { MinWidth = 170 }, _secTo = new() { MinWidth = 170 };
    private readonly TextBox _tailMs = new() { Width = 70 }, _start = new() { Width = 70 }, _end = new() { Width = 70 }, _dir = new(),
        _pattern = new() { MinWidth = 300 }, _barFrom = new() { Width = 60 }, _barTo = new() { Width = 60 };
    private readonly CheckBox _mono = new() { Content = "Mono (L+R average)" }, _realtime = new() { Content = "Realtime pace (for streaming samplers)" },
        _open = new() { Content = "Open the folder when done" };
    private readonly StackPanel _stemPanel = new() { Margin = new Thickness(0, 6, 0, 0), IsVisible = false }, _stemList = new();
    private readonly ListBox _preview = new() { MinHeight = 90 };
    private readonly ProgressBar _bar = new() { Height = 10, Maximum = 1, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _hint = ToolWindowParts.Small(""), _status = ToolWindowParts.Status("StatusText");
    private readonly Button _render = new() { Content = "Render", Padding = new Thickness(18, 4, 18, 4) };
    private readonly Button _cancel = new() { Name = "Cancel", Content = CloseLabel, Padding = new Thickness(14, 4, 14, 4) };
    private readonly Func<string, string?>? _pickFolder;
    private readonly Func<RenderPlan, IProgress<RenderProgressInfo>, CancellationToken, Task<RenderResult>>? _run;
    private readonly bool _mp3Ok;
    private readonly (int StartBar, int StartCell, int EndBar, int EndCell)? _selection;   // the score's note selection when the dialog opened
    private CancellationTokenSource? _cts;   // set while a render runs
    private bool _loading = true;
    private bool _fillingSections;

    /// <param name="project">The song to render; the preview passes its demo song.</param>
    /// <param name="settings">The last-used render options, read on open and written on close.</param>
    /// <param name="hasSelection">Whether the editor has a selection (the preview has none, so the selection choices are disabled).</param>
    /// <param name="openVideoExport">Opens the video export window; Video (MP4)… closes this window first.</param>
    /// <param name="pickFolder">Asks for the output folder, starting at the current one; null when the person cancels. None in the preview.</param>
    /// <param name="run">Renders the plan with progress and cancel (AvaloniaRenderRunner); null in the preview, where Render reports that it needs the full app.</param>
    /// <param name="selection">The score selection (bars and cells) the selection choices render; null without one.</param>
    /// <param name="mp3Available">Whether the MP3 encoder is there (RenderJob.Mp3Available); the MP3 formats are disabled without it.</param>
    public RenderWindow(SongProject project, RenderSettings settings, bool hasSelection, Action? openVideoExport, Func<string, string?>? pickFolder = null,
        Func<RenderPlan, IProgress<RenderProgressInfo>, CancellationToken, Task<RenderResult>>? run = null, bool mp3Available = true,
        (int StartBar, int StartCell, int EndBar, int EndCell)? selection = null)
    {
        _project = project; _settings = settings; _hasSelection = hasSelection; _openVideoExport = openVideoExport; _pickFolder = pickFolder;
        _run = run; _mp3Ok = mp3Available; _selection = selection;
        Title = "Render to file";
        Width = 760; Height = 700; MinWidth = 620; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;
        _sections = SectionLayout.Sorted(project);
        _barCount = BarRangeEditor.MaxMeasures(project);

        var stack = new StackPanel { Margin = DialogMetrics.ContentInset };
        stack.Children.Add(BuildSourceGroup());
        stack.Children.Add(BuildBoundsGroup());
        stack.Children.Add(BuildOutputGroup());
        stack.Children.Add(BuildOptionsGroup());
        stack.Children.Add(_open);

        // Progress, status and the Video / Render / Cancel buttons sit in a footer outside the scroll area, as in the WPF dialog.
        var footer = new StackPanel { Margin = new Thickness(14, 0, 14, 12) };
        footer.Children.Add(_bar); footer.Children.Add(_status); footer.Children.Add(BuildButtons());
        var footerHost = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0), [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush") };
        DockPanel.SetDock(footerHost, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footerHost);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
        Content = layout;

        LoadSettings();
        WireEvents();
        _loading = false;
        Changed();
        Closing += (_, _) => SaveSettings();
    }

    private Border BuildSourceGroup()
    {
        foreach (var name in RenderDialogPresenter.SourceNames) _source.Items.Add(name);
        var stemButtons = new Button { Name = "StemsAll", Content = "All", Padding = new Thickness(10, 1, 10, 1) };
        var stemNone = new Button { Name = "StemsNone", Content = "None", Padding = new Thickness(10, 1, 10, 1) };
        stemButtons.Click += (_, _) => SetAllStems(true);
        stemNone.Click += (_, _) => SetAllStems(false);
        foreach (var track in _project.Tracks)
        {
            var box = new CheckBox { Content = track.Name, Tag = track, Margin = new Thickness(0, 1, 0, 1) };
            _stemBoxes.Add(box);
            _stemList.Children.Add(box);
        }
        _stemPanel.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Tracks to render as stems:"), stemButtons, stemNone));
        _stemPanel.Children.Add(new ScrollViewer { MaxHeight = 150, Content = _stemList });
        var content = new StackPanel();
        content.Children.Add(_source);
        content.Children.Add(_stemPanel);
        return ToolWindowParts.Group("Source", content);
    }

    private Border BuildBoundsGroup()
    {
        var bounds = new StackPanel();
        for (var i = 0; i < _bounds.Length; i++)
        {
            _bounds[i] = new RadioButton { Name = "Range" + i, Content = RenderDialogPresenter.BoundsNames[i], GroupName = "bounds", Margin = new Thickness(0, 1, 0, 1) };
            bounds.Children.Add(_bounds[i]);
            if (i == 3) bounds.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("from"), _start, ToolWindowParts.Label("to"), _end));
            if (i == 4) bounds.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("from bar"), _barFrom, ToolWindowParts.Label("to bar"), _barTo,
                ToolWindowParts.Label($"(1 to {_barCount})")));
            if (i == 5) bounds.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("from"), _secFrom, ToolWindowParts.Label("to"), _secTo));
        }
        bounds.Children.Add(_hint);
        foreach (var tail in RenderDialogPresenter.TailNames) _tail.Items.Add(tail);
        bounds.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Tail:"), _tail, _tailMs, ToolWindowParts.Label("ms (reverb / delay ring-out)")));
        return ToolWindowParts.Group("Bounds", bounds);
    }

    private Border BuildOutputGroup()
    {
        var browse = new Button { Name = "Browse", Content = "Browse…", Padding = new Thickness(10, 2, 10, 2) };
        browse.Click += (_, _) => BrowsePressed();
        ToolWindowParts.NotAvailable(browse, _pickFolder is null);
        var output = new StackPanel();
        output.Children.Add(ToolWindowParts.Row(_dir, browse));
        output.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("File name:"), _pattern));
        output.Children.Add(ToolWindowParts.Small("Wildcards: $project $track $tracknumber $date $time $bpm. Existing files are never overwritten: a number is added."));
        output.Children.Add(_preview);
        return ToolWindowParts.Group("Output", output);
    }

    private Border BuildOptionsGroup()
    {
        foreach (var rate in RenderDialogPresenter.SampleRates)
            _rate.Items.Add(new ComboBoxItem { Content = rate == 0 ? "Engine rate" : $"{rate / 1000.0:0.#} kHz" });
        for (var i = 0; i < RenderDialogPresenter.FormatNames.Count; i++)
        {
            var isMp3 = RenderDialogPresenter.IsMp3(i);
            var item = new ComboBoxItem { Content = RenderDialogPresenter.FormatNames[i], IsEnabled = _mp3Ok || !isMp3 };
            if (!_mp3Ok && isMp3) ToolTip.SetTip(item, Mp3MissingTip);
            _format.Items.Add(item);
        }
        foreach (var threads in RenderDialogPresenter.ThreadNames) _threads.Items.Add(threads);
        var options = new StackPanel();
        options.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Sample rate:"), _rate, ToolWindowParts.Label("Format:"), _format, _threads));
        options.Children.Add(_mono);
        options.Children.Add(_realtime);
        options.Children.Add(ToolWindowParts.Small("MP3: 44.1 / 48 kHz only. The engine's General MIDI synth may sound slightly different from the Windows MIDI synth used for normal playback."));
        return ToolWindowParts.Group("Options and format", options);
    }

    private StackPanel BuildButtons()
    {
        var video = new Button { Name = "Video", Content = "Video (MP4)…", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(video, "Export the song as an MP4 video instead");
        video.Click += (_, _) => { Close(); _openVideoExport?.Invoke(); };
        _cancel.Content = CancelLabel;
        _cancel.Click += (_, _) => { if (_cts is { } running) running.Cancel(); else Close(); };
        _render.Margin = new Thickness(0, 0, 8, 0);
        _render.Click += async (_, _) => await RenderPressed();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(video); buttons.Children.Add(_render); buttons.Children.Add(_cancel);
        return buttons;
    }

    private void WireEvents()
    {
        foreach (var combo in new[] { _source, _tail, _rate, _format, _threads }) combo.SelectionChanged += (_, _) => Changed();
        foreach (var text in new[] { _tailMs, _start, _end, _dir, _pattern, _barFrom, _barTo }) text.TextChanged += (_, _) => Changed();
        foreach (var box in _stemBoxes) box.Click += (_, _) => Changed();
        foreach (var radio in _bounds) radio.IsCheckedChanged += (_, _) => Changed();
        _secFrom.SelectionChanged += (_, _) => { if (!_fillingSections) { FillSectionTo(null); Changed(); } };
        _secTo.SelectionChanged += (_, _) => { if (!_fillingSections) Changed(); };
        _mono.Click += (_, _) => Changed(); _realtime.Click += (_, _) => Changed(); _open.Click += (_, _) => Changed();
    }

    private void SetAllStems(bool isChecked)
    {
        foreach (var box in _stemBoxes) box.IsChecked = isChecked;
        Changed();
    }

    /// <summary>Puts the picked folder in the output box; a cancel leaves it as it was.</summary>
    public void BrowsePressed()
    {
        if (_pickFolder is null) { _status.Text = ToolWindowParts.NeedsFullApp("Choosing a folder"); return; }
        if (_pickFolder(_dir.Text ?? "") is { Length: > 0 } folder) _dir.Text = folder;
    }

    /// <summary>The output folder box's text.</summary>
    public string OutputFolder => _dir.Text ?? "";

    /// <summary>Renders the plan as the WPF dialog does: progress on the bar and status line, the Cancel button cancels the job while it runs.</summary>
    public async Task RenderPressed()
    {
        SaveSettings();
        var files = Plan();
        if (files.Count == 0) { _status.Text = "Nothing to render."; return; }
        if (_run is null) { _status.Text = ToolWindowParts.NeedsFullApp("Rendering"); return; }
        try
        {
            Directory.CreateDirectory(FilePathPolicy.OutputDirectory(ToolWindowParts.TextOf(_dir), "render output folder"));
            if (RenderPlanNow() is not { } plan) return;
            _status.Text = "Preparing…";
            _render.IsEnabled = false; _cancel.Content = CancelRenderLabel;
            _cts = new CancellationTokenSource();
            var progress = new Progress<RenderProgressInfo>(p => { _bar.Value = p.Fraction; _status.Text = $"Rendering… {p.Fraction:P0}, {p.Seconds:0.0} s of audio, {p.Speed:0.#}x realtime"; });
            var result = await _run(plan, progress, _cts.Token);
            _bar.Value = 1;
            var clipped = result.Files.Sum(f => f.ClippedSamples);
            _status.Text = $"Done: {files.Count} file(s), {result.Seconds:0.0} s of audio in {result.ElapsedSeconds:0.0} s ({result.Seconds / Math.Max(0.001, result.ElapsedSeconds):0.#}x realtime)" + (clipped > 0 ? $"; {clipped} samples clipped." : ".");
            if (_open.IsChecked == true) FolderLauncher.Open(ToolWindowParts.TextOf(_dir));
        }
        catch (RenderException ex)
        {
            if (!ex.Cancelled) Trace.Error(Trace.Ui, "render: " + ex.Message);
            _status.Text = ex.Cancelled ? "Render cancelled." : "Render failed: " + ex.Message + (string.IsNullOrEmpty(ex.PluginPath) ? "" : $" (plug-in: {Path.GetFileName(ex.PluginPath)}; try Threads: 1)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Trace.Error(Trace.Ui, "render: " + ex.Message);
            _status.Text = "Render failed: " + ex.Message;
        }
        finally { _cts?.Dispose(); _cts = null; _render.IsEnabled = true; _cancel.Content = CloseLabel; Changed(); }
    }

    /// <summary>The compiled song, the range and the output paths for the current choices (as the WPF dialog computes them); null with the reason shown.</summary>
    private RenderPlan? RenderPlanNow()
    {
        var timeline = RenderSpecBuilder.Compile(_project);
        var mode = (RenderBounds)Math.Max(0, _settings.Bounds);
        int startBar = _selection?.StartBar ?? 0, startCell = _selection?.StartCell ?? 0, endBar = _selection?.EndBar ?? 0, endCell = _selection?.EndCell ?? -1;
        if (mode is RenderBounds.CustomBars or RenderBounds.CustomSections)
        {
            var range = CustomBarRange();
            if (range.Problem is not null) { _status.Text = range.Problem; return null; }
            startBar = range.First; startCell = 0; endBar = range.Last; endCell = -1;
        }
        var (startMs, endMs) = RenderSpecBuilder.Bounds(timeline, mode, startBar, startCell, endBar, endCell, _settings.CustomStartSec, _settings.CustomEndSec);
        var dir = ToolWindowParts.TextOf(_dir).Trim();
        string? master = null;
        var stems = new Dictionary<TrackModel, string>();
        foreach (var file in PlannedFiles())
        {
            var path = Path.Combine(dir, file.FileName);
            if (file.Track is null) master = path; else stems[file.Track] = path;
        }
        return new RenderPlan(timeline, startMs, endMs, master, stems);
    }

    private void LoadSettings()
    {
        _source.SelectedIndex = Math.Clamp(_settings.Source, 0, 3);
        _bounds[RenderDialogPresenter.BoundsOnLoad(_settings.Bounds, _hasSelection, _sections.Count, _barCount)].IsChecked = true;
        var saved = _settings.StemTrackNames;
        foreach (var box in _stemBoxes) box.IsChecked = saved is null || saved.Contains(((TrackModel)box.Tag!).Name);
        var (fromBar, toBar) = RenderDialogPresenter.BarRangeOnLoad(_settings.CustomFromBar, _settings.CustomToBar, _barCount);
        _barFrom.Text = fromBar.ToString(CultureInfo.InvariantCulture);
        _barTo.Text = toBar.ToString(CultureInfo.InvariantCulture);
        _fillingSections = true;
        for (var i = 0; i < _sections.Count; i++) _secFrom.Items.Add(new ComboBoxItem { Content = RenderBarRange.Label(_sections[i]), Tag = i });
        var fromIndex = _sections.FindIndex(m => m.MeasureIndex == _settings.CustomFromSectionBar);
        _secFrom.SelectedIndex = _sections.Count == 0 ? -1 : Math.Max(0, fromIndex);
        _fillingSections = false;
        FillSectionTo(_settings.CustomToSectionBar);
        ToolTip.SetTip(_bounds[5], RenderDialogPresenter.SectionsTooltip(_sections.Count));
        ToolTip.SetTip(_bounds[1], RenderDialogPresenter.SelectionTooltip(_hasSelection));
        ToolTip.SetTip(_bounds[2], RenderDialogPresenter.SelectionTooltip(_hasSelection));
        _start.Text = _settings.CustomStartSec.ToString("0.###", CultureInfo.InvariantCulture);
        _end.Text = (_settings.CustomEndSec > 0 ? _settings.CustomEndSec : 60).ToString("0.###", CultureInfo.InvariantCulture);
        _tail.SelectedIndex = Math.Clamp(_settings.TailMode, 0, 2);
        _tailMs.Text = _settings.TailMs.ToString(CultureInfo.InvariantCulture);
        _dir.Text = string.IsNullOrWhiteSpace(_settings.Directory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) : _settings.Directory;
        _pattern.Text = string.IsNullOrWhiteSpace(_settings.Pattern) ? "$project" : _settings.Pattern;
        var rateIndex = RenderDialogPresenter.SampleRates.ToList().IndexOf(_settings.SampleRate);
        _rate.SelectedIndex = rateIndex < 0 ? 0 : rateIndex;
        _format.SelectedIndex = RenderDialogPresenter.FormatIndex(_settings.Format, _settings.Mp3Kbps, mp3Available: _mp3Ok);
        _threads.SelectedIndex = _settings.SingleThread ? 1 : 0;
        _mono.IsChecked = _settings.Mono; _realtime.IsChecked = _settings.RealtimePace; _open.IsChecked = _settings.OpenFolder;
    }

    /// <summary>Fills the "to" list with the sections at or after the chosen "from" one, keeping the choice (or the bar given, or the last section).</summary>
    private void FillSectionTo(int? wantBar)
    {
        var keep = wantBar ?? (_secTo.SelectedItem is ComboBoxItem current && current.Tag is int currentIndex ? _sections[currentIndex].MeasureIndex : -1);
        _fillingSections = true;
        _secTo.Items.Clear();
        var from = _secFrom.SelectedItem is ComboBoxItem fromItem && fromItem.Tag is int fromIndex ? fromIndex : -1;
        var select = -1;
        if (from >= 0)
            for (var i = from; i < _sections.Count; i++)
            {
                _secTo.Items.Add(new ComboBoxItem { Content = RenderBarRange.Label(_sections[i]), Tag = i });
                if (_sections[i].MeasureIndex == keep) select = _secTo.Items.Count - 1;
            }
        _secTo.SelectedIndex = select >= 0 ? select : _secTo.Items.Count - 1;
        _fillingSections = false;
    }

    /// <summary>The bars (0-based, inclusive) of the custom-bars / custom-sections choice, or why they cannot be rendered.</summary>
    private (int First, int Last, string? Problem) CustomBarRange()
    {
        if (_bounds[4].IsChecked == true)
        {
            var problem = RenderBarRange.Validate(ToolWindowParts.TextOf(_barFrom), ToolWindowParts.TextOf(_barTo), _barCount);
            return problem is not null ? (0, 0, problem)
                : (int.Parse(ToolWindowParts.TextOf(_barFrom).Trim(), CultureInfo.InvariantCulture) - 1, int.Parse(ToolWindowParts.TextOf(_barTo).Trim(), CultureInfo.InvariantCulture) - 1, null);
        }
        if (_secFrom.SelectedItem is ComboBoxItem from && from.Tag is int fromIndex && _secTo.SelectedItem is ComboBoxItem to && to.Tag is int toIndex
            && RenderBarRange.SectionBars(_project, fromIndex, toIndex) is { } bars)
            return (bars.FirstBar, bars.LastBar, null);
        return (0, 0, RenderDialogPresenter.SectionsTooltip(_sections.Count) ?? "Choose the first and last section.");
    }

    private void SaveSettings()
    {
        _settings.Source = Math.Max(0, _source.SelectedIndex);
        _settings.StemTrackNames = _stemBoxes.Where(b => b.IsChecked == true).Select(b => ((TrackModel)b.Tag!).Name).ToList();
        _settings.Bounds = Math.Max(0, Array.FindIndex(_bounds, b => b.IsChecked == true));
        _settings.CustomStartSec = RenderDialogPresenter.ParseNumber(ToolWindowParts.TextOf(_start));
        _settings.CustomEndSec = RenderDialogPresenter.ParseNumber(ToolWindowParts.TextOf(_end));
        if (int.TryParse(ToolWindowParts.TextOf(_barFrom).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromBar)) _settings.CustomFromBar = Math.Max(0, fromBar);
        if (int.TryParse(ToolWindowParts.TextOf(_barTo).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var toBar)) _settings.CustomToBar = Math.Max(0, toBar);
        if (_secFrom.SelectedItem is ComboBoxItem from && from.Tag is int fromIndex) _settings.CustomFromSectionBar = _sections[fromIndex].MeasureIndex;
        if (_secTo.SelectedItem is ComboBoxItem to && to.Tag is int toIndex) _settings.CustomToSectionBar = _sections[toIndex].MeasureIndex;
        _settings.TailMode = Math.Max(0, _tail.SelectedIndex);
        _settings.TailMs = (int)Math.Clamp(RenderDialogPresenter.ParseNumber(ToolWindowParts.TextOf(_tailMs)), 0, 30000);
        _settings.Directory = ToolWindowParts.TextOf(_dir);
        _settings.Pattern = ToolWindowParts.TextOf(_pattern);
        _settings.SampleRate = RenderDialogPresenter.SampleRates[Math.Max(0, _rate.SelectedIndex)];
        var (format, mp3Kbps) = RenderDialogPresenter.SavedFormat(Math.Max(0, _format.SelectedIndex));
        _settings.Format = format;
        if (mp3Kbps is int kbps) _settings.Mp3Kbps = kbps;
        _settings.SingleThread = _threads.SelectedIndex == 1;
        _settings.Mono = _mono.IsChecked == true;
        _settings.RealtimePace = _realtime.IsChecked == true;
        _settings.OpenFolder = _open.IsChecked == true;
    }

    /// <summary>Applies the enabling, hint and file-preview rules after any change of a control.</summary>
    private void Changed()
    {
        if (_loading) return;
        var formatIndex = _format.SelectedIndex;
        for (var i = 0; i < _rate.Items.Count; i++)
            if (_rate.Items[i] is ComboBoxItem item) item.IsEnabled = RenderDialogPresenter.IsSampleRateAllowed(formatIndex, RenderDialogPresenter.SampleRates[i]);
        _rate.SelectedIndex = RenderDialogPresenter.RateIndexAfterFormat(formatIndex, _rate.SelectedIndex);
        for (var i = 0; i < _bounds.Length; i++)
            _bounds[i].IsEnabled = RenderDialogPresenter.IsBoundsEnabled(i, _hasSelection, _sections.Count, _barCount);
        var custom = _bounds[3].IsChecked == true;
        _start.IsEnabled = _end.IsEnabled = custom;
        var customBars = _bounds[4].IsChecked == true;
        var customSections = _bounds[5].IsChecked == true;
        _barFrom.IsEnabled = _barTo.IsEnabled = customBars;
        _secFrom.IsEnabled = _secTo.IsEnabled = customSections && _sections.Count > 0;
        var problem = customBars || customSections ? CustomBarRange().Problem : null;
        _hint.Text = RenderDialogPresenter.HintText(problem, _sections.Count);
        _hint.IsVisible = _hint.Text.Length > 0;
        _render.IsEnabled = problem is null && _run is not null;
        ToolWindowParts.NotAvailable(_render, _run is null);
        _tailMs.IsEnabled = _tail.SelectedIndex != 0;
        _stemPanel.IsVisible = _source.SelectedIndex == 1;
        _preview.Items.Clear();
        foreach (var name in Plan()) _preview.Items.Add(name);
    }

    /// <summary>The output file names a render would write, in order.</summary>
    private List<string> Plan() => PlannedFiles().Select(file => file.FileName).ToList();

    private IReadOnlyList<RenderFileName> PlannedFiles()
    {
        var checkedTracks = _stemBoxes.Where(b => b.IsChecked == true).Select(b => (TrackModel)b.Tag!).ToList();
        var extension = RenderDialogPresenter.IsMp3(_format.SelectedIndex) ? ".mp3" : ".wav";
        return RenderDialogPresenter.PlanFiles(ToolWindowParts.TextOf(_pattern), ToolWindowParts.TextOf(_dir).Trim(), _project.Title, _project.Tempo, DateTime.Now,
            extension, Math.Max(0, _source.SelectedIndex), _project.Tracks, checkedTracks);
    }
}
