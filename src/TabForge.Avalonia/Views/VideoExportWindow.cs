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
using TabForge.Models;
using TabForge.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Rendering;
using TabForge.Services;
using TabForge.Services.Video;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Export video (MP4) (Tools > Export video (MP4)… in the preview): range, layout, theme, instrument, tracks, resolution and frame rate.
/// Export checks the range, asks for the file and runs the given export (AvaloniaVideoExporter) with progress; Close cancels it while it runs. The preview, with none, reports that it needs the full app.
/// Owns: the controls (their order, texts and automation names as in the window), loading and saving VideoSettings, and the enabling
///     and tooltip rules applied from VideoExportPresenter. Does not own: the choice lists and range rules (VideoExportPresenter), the
///     bar check (RenderBarRange), the export (VideoExportFlow and the encoder), or the song (the caller passes it).
/// Tests: TabForge.Avalonia.Tests.RenderVideoExportTutorialTests.
/// </summary>
public sealed class VideoExportWindow : Window
{
    private readonly SongProject _project;
    private readonly VideoSettings _settings;
    private readonly bool _hasSelection;
    private readonly List<MarkerModel> _sections;
    private readonly int _barCount;
    private readonly List<CheckBox> _trackBoxes = new();
    private readonly StackPanel _trackList = new();
    private readonly ComboBox _range = new() { MinWidth = 170 }, _section = new() { MinWidth = 260 }, _layout = new() { MinWidth = 360 },
        _theme = new() { MinWidth = 100 }, _size = new() { MinWidth = 210 }, _fps = new() { MinWidth = 70 }, _encoder = new();
    private readonly TextBox _from = new() { Width = 60 }, _to = new() { Width = 60 };
    private readonly CheckBox _instrument = new() { Content = "Show the instrument (fretboard, keyboard or drums)" }, _remember = new() { Content = "Remember these settings" };
    private readonly ScrollViewer _trackScroll = new() { MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ProgressBar _bar = new() { Height = 10, Maximum = 1, Margin = new Thickness(0, 8, 0, 4) };
    private readonly TextBlock _rangeHint = ToolWindowParts.Small(""), _status = ToolWindowParts.Status("StatusText");
    private readonly Button _export = new() { Content = VideoExportPresenter.ExportButton, Padding = new Thickness(18, 4, 18, 4) };
    private readonly Button _close = new() { Name = "Close", Content = VideoExportPresenter.CloseButton, Padding = new Thickness(14, 4, 14, 4) };
    private readonly Func<VideoExportPlan, IProgress<VideoExportProgress>, CancellationToken, Task<VideoExportResult>>? _run;
    private readonly Func<FileDialogOptions, string?>? _saveFile;
    private readonly (int StartBar, int StartCell, int EndBar, int EndCell)? _selection;
    private readonly NotationMode _notation;
    private CancellationTokenSource? _cts;   // set while an export runs
    private bool _loading = true;

    /// <param name="project">The song to export; the preview passes its demo song.</param>
    /// <param name="settings">The remembered video options, read on open and written on close when "Remember these settings" is ticked.</param>
    /// <param name="hasSelection">Whether the editor has a selection (the preview has none).</param>
    /// <param name="run">Exports the plan with progress and cancel (AvaloniaVideoExporter); null in the preview, where Export reports that it needs the full app.</param>
    /// <param name="saveFile">Asks where to save the MP4 (null when the person cancels).</param>
    /// <param name="selection">The score selection (bars and cells) the selection range exports; null without one.</param>
    /// <param name="notation">The score's notation, which the video's score follows.</param>
    public VideoExportWindow(SongProject project, VideoSettings settings, bool hasSelection,
        Func<VideoExportPlan, IProgress<VideoExportProgress>, CancellationToken, Task<VideoExportResult>>? run = null, Func<FileDialogOptions, string?>? saveFile = null,
        (int StartBar, int StartCell, int EndBar, int EndCell)? selection = null, NotationMode notation = NotationMode.TabAndStaff)
    {
        _project = project; _settings = settings; _hasSelection = hasSelection; _run = run; _saveFile = saveFile; _selection = selection; _notation = notation;
        _sections = SectionLayout.Sorted(project);
        _barCount = BarRangeEditor.MaxMeasures(project);
        Title = VideoExportPresenter.WindowTitle;
        Width = 640; Height = 760; MinWidth = 520; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var stack = new StackPanel { Margin = DialogMetrics.ContentInset };
        stack.Children.Add(BuildRangeGroup());
        stack.Children.Add(BuildViewGroup());
        stack.Children.Add(BuildTracksGroup());
        stack.Children.Add(BuildVideoGroup());

        _close.Click += (_, _) => { if (_cts is { } running) running.Cancel(); else Close(); };
        _export.Click += async (_, _) => await ExportPressed();
        ToolWindowParts.NotAvailable(_export, _run is null || _saveFile is null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_export); buttons.Children.Add(new Border { Width = 8 }); buttons.Children.Add(_close);
        var footer = new StackPanel { Margin = DialogMetrics.ContentInset };
        footer.Children.Add(_bar); footer.Children.Add(_status); footer.Children.Add(buttons);
        var footerHost = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0), [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush") };
        DockPanel.SetDock(footerHost, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footerHost);
        layout.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
        Content = layout;

        LoadSettings();
        _loading = false;
        Changed();
        Closing += (_, _) => VideoExportPresenter.Remember(_settings, Choices());
    }

    private Border BuildRangeGroup()
    {
        foreach (var choice in VideoExportPresenter.RangeChoices) _range.Items.Add(choice);
        foreach (var section in _sections) _section.Items.Add(RenderBarRange.Label(section));
        _range.SelectionChanged += (_, _) => Changed();
        _from.TextChanged += (_, _) => Changed();
        _to.TextChanged += (_, _) => Changed();
        var content = new StackPanel();
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Range:"), _range));
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("from bar"), _from, ToolWindowParts.Label("to bar"), _to,
            ToolWindowParts.Label($"(1 to {_barCount})")));
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Section:"), _section));
        content.Children.Add(_rangeHint);
        return ToolWindowParts.Group("Range", content);
    }

    private Border BuildViewGroup()
    {
        foreach (var choice in VideoExportPresenter.LayoutChoices) _layout.Items.Add(choice);
        foreach (var choice in VideoExportPresenter.ThemeChoices) _theme.Items.Add(choice);
        _layout.SelectionChanged += (_, _) => Changed();
        var content = new StackPanel();
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Layout:"), _layout));
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Theme:"), _theme));
        content.Children.Add(_instrument);
        return ToolWindowParts.Group("View", content);
    }

    private Border BuildTracksGroup()
    {
        foreach (var track in _project.Tracks)
        {
            var box = new CheckBox { Content = track.Name, Tag = track, Margin = new Thickness(0, 1, 0, 1) };
            _trackBoxes.Add(box);
            _trackList.Children.Add(box);
        }
        _trackScroll.Content = _trackList;
        return ToolWindowParts.Group("Tracks (the first is the score and instrument; all are shown in the Band view)", _trackScroll);
    }

    private Border BuildVideoGroup()
    {
        foreach (var choice in VideoExportPresenter.ResolutionChoices) _size.Items.Add(choice);
        foreach (var choice in VideoExportPresenter.FpsChoices) _fps.Items.Add(choice);
        foreach (var choice in VideoExportPresenter.EncoderChoices) _encoder.Items.Add(choice);
        ToolTip.SetTip(_encoder, VideoExportPresenter.EncoderToolTip);
        var content = new StackPanel();
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Resolution:"), _size, ToolWindowParts.Label("Frames per second:"), _fps));
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Encoder:"), _encoder));
        content.Children.Add(_remember);
        return ToolWindowParts.Group("Video", content);
    }

    private void LoadSettings()
    {
        var names = _project.Tracks.Select(t => t.Name).ToList();
        _range.SelectedIndex = VideoExportPresenter.RangeOnOpen(_settings.Range, _hasSelection);
        _from.Text = VideoExportPresenter.ClampBar(_settings.FromBar, _barCount).ToString();
        _to.Text = VideoExportPresenter.ClampBar(_settings.ToBar, _barCount).ToString();
        _section.SelectedIndex = VideoExportPresenter.SectionOnOpen(_settings.Section, _sections.Count);
        _layout.SelectedIndex = VideoExportPresenter.LayoutOnOpen(_settings.Layout);
        _theme.SelectedIndex = VideoExportPresenter.ThemeIndex(_settings.Dark);
        _instrument.IsChecked = _settings.ShowInstrument;
        _size.SelectedIndex = VideoExportPresenter.ResolutionIndex(_settings.Height);
        _fps.SelectedItem = VideoExportPresenter.FpsChoice(_settings.Fps);
        _encoder.SelectedItem = VideoExportPresenter.EncoderChoice(_settings.Encoder);
        _remember.IsChecked = _settings.Remember;
        var checks = VideoExportPresenter.InitialTrackChecks(names, _settings.TrackNames, Array.Empty<string>());
        for (var i = 0; i < _trackBoxes.Count; i++) _trackBoxes[i].IsChecked = checks[i];
    }

    /// <summary>Applies the enabling and tooltip rules of the range and layout choices.</summary>
    private void Changed()
    {
        if (_loading) return;
        var range = _range.SelectedIndex;
        _from.IsEnabled = _to.IsEnabled = VideoExportPresenter.BarBoxesEnabled(range);
        _section.IsEnabled = VideoExportPresenter.SectionListEnabled(range, _sections.Count);
        var barProblem = range == VideoExportPresenter.RangeBars ? RenderBarRange.Validate(ToolWindowParts.TextOf(_from), ToolWindowParts.TextOf(_to), _barCount) : null;
        var problem = VideoExportPresenter.RangeProblem(range, _hasSelection, barProblem, _sections.Count > 0);
        _rangeHint.Text = problem ?? "";
        _rangeHint.IsVisible = problem is not null;
        ToolTip.SetTip(_trackScroll, VideoExportPresenter.TrackToolTip(_layout.SelectedIndex));
    }

    /// <summary>Checks the range, asks for the file and exports: progress on the bar, Close cancels while it runs.</summary>
    public async Task ExportPressed()
    {
        var choices = Choices();
        var barProblem = choices.Range == VideoExportPresenter.RangeBars ? RenderBarRange.Validate(choices.FromText, choices.ToText, _barCount) : null;
        var problem = VideoExportPresenter.RangeProblem(choices.Range, _hasSelection, barProblem, _sections.Count > 0);
        if (problem is not null || _run is null || _saveFile is null) { _status.Text = problem ?? ToolWindowParts.NeedsFullApp("Video export"); return; }
        VideoExportPresenter.Remember(_settings, choices);
        var timeline = RenderSpecBuilder.Compile(_project);
        var (start, end) = RangeOf(timeline, choices);
        if (VideoExportPresenter.EmptyRangeProblemFor(start, end) is { } empty) { _status.Text = empty; return; }
        var chosen = _saveFile(new FileDialogOptions
        {
            Title = VideoExportPresenter.SaveDialogTitle, Filter = VideoExportPresenter.SaveDialogFilter, DefaultExt = VideoExportPresenter.DefaultExtension,
            AddExtension = true, OverwritePrompt = true, FileName = VideoExportPresenter.SuggestedFileName(_project.Title),
            InitialDirectory = VideoExportPresenter.InitialDirectory(_settings.Directory, Directory.Exists(_settings.Directory), TabForge.Platform.AppFolders.Current.Videos),
        });
        if (chosen is null) return;
        if (_settings.Remember) _settings.Directory = VideoExportPresenter.DirectoryOf(chosen);
        _export.IsEnabled = false; _close.Content = VideoExportPresenter.CancelExportButton;
        _status.Text = VideoExportPresenter.RenderingAudioText;
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<VideoExportProgress>(p => { _bar.Value = p.Fraction; _status.Text = VideoExportPresenter.ProgressText(p.Text, p.Fraction); });
            var plan = new VideoExportPlan(timeline, start, end, VideoExportPresenter.ViewSpec(choices, _notation), chosen, VideoExportPresenter.FpsFrom(choices.FpsChoice), VideoExportPresenter.EncoderChoice(choices.Encoder));
            var result = await _run(plan, progress, _cts.Token);
            _bar.Value = 1;
            _status.Text = VideoExportPresenter.DoneText(result.Frames, result.Seconds, result.ElapsedSeconds);
        }
        catch (OperationCanceledException) { _status.Text = VideoExportPresenter.CancelledText; }
        catch (RenderException ex) { _status.Text = VideoExportPresenter.FailureText(ex.Cancelled, ex.Message); }
        catch (Exception ex) when (ex is VideoEncoderException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = VideoExportPresenter.FailureText(false, ex.Message);
        }
        finally { _cts?.Dispose(); _cts = null; _export.IsEnabled = true; _close.Content = VideoExportPresenter.CloseButton; }
    }

    /// <summary>The range in song milliseconds for the choices; the range was checked before.</summary>
    private (double Start, double End) RangeOf(ScoreTimeline timeline, VideoExportChoices choices)
    {
        switch (choices.Range)
        {
            case VideoExportPresenter.RangeSelection when _selection is { } sel:
                return RenderSpecBuilder.Bounds(timeline, RenderBounds.Bars, sel.StartBar, sel.StartCell, sel.EndBar, sel.EndCell, 0, 0);
            case VideoExportPresenter.RangeBars:
                return RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomBars, int.Parse(choices.FromText, CultureInfo.InvariantCulture) - 1, 0, int.Parse(choices.ToText, CultureInfo.InvariantCulture) - 1, -1, 0, 0);
            case VideoExportPresenter.RangeSection when choices.Section >= 0 && RenderBarRange.SectionBars(_project, choices.Section, choices.Section) is { } bars:
                return RenderSpecBuilder.Bounds(timeline, RenderBounds.CustomSections, bars.FirstBar, 0, bars.LastBar, -1, 0, 0);
            default:
                return RenderSpecBuilder.Bounds(timeline, RenderBounds.Song, 0, 0, 0, -1, 0, 0);
        }
    }

    private VideoExportChoices Choices() => new(
        Remember: _remember.IsChecked == true,
        Range: _range.SelectedIndex,
        FromText: ToolWindowParts.TextOf(_from),
        ToText: ToolWindowParts.TextOf(_to),
        Section: _section.SelectedIndex,
        Layout: _layout.SelectedIndex,
        Dark: _theme.SelectedIndex == VideoExportPresenter.ThemeIndex(dark: true),
        ShowInstrument: _instrument.IsChecked == true,
        ResolutionIndex: _size.SelectedIndex,
        FpsChoice: _fps.SelectedItem as string,
        TrackNames: _project.Tracks.Select(t => t.Name).ToList(),
        TrackChecks: _trackBoxes.Select(b => b.IsChecked == true).ToList(),
        Encoder: VideoExportPresenter.EncoderChoice(_encoder.SelectedItem as string));
}
