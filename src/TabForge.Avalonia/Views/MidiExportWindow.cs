using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Audio.Contracts;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Rendering;
using TabForge.Services;
using Trace = TabForge.Services.Trace;

namespace TabForge.Avalonia.Views;

// Owns: the File > Export MIDI dialog: which tracks to write and which range (whole song, selected bars,
//   bar range or time range), the output file and the export run.
// Does not own: the range math (RenderSpecBuilder, RenderBarRange, MidiExportRange), the option lists and
//   rules (RenderDialogPresenter), the file encoding (MidiExportService) or the settings file (AppSettingsStore).
// Tests: MidiExportRangeTests (range math); this window is thin by design.
public sealed partial class MidiExportWindow : Window
{
    private readonly SongProject _project;
    private readonly MidiExportSettings _settings;
    private readonly SelectionSnapshot? _snip;
    private readonly (int StartBar, int StartCell, int EndBar, int EndCell)? _selection;
    private readonly Func<TabForge.Platform.FileDialogOptions, Window, string?>? _saveFile;
    private readonly Action _quietSave;
    private readonly Action<string> _report;
    private readonly Action<TrackModel>? _openArticulations;
    private readonly int _barCount;

    private readonly List<CheckBox> _trackBoxes = new();
    private readonly RadioButton[] _bounds = new RadioButton[4];
    // The body scrolls; the footer (status and buttons) is docked below it, so Cancel and Export are always in view.
    private readonly ScrollViewer _scroll = new() { Name = "MidiExportScroll", VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _barFrom = Field(DialogMetrics.MidiExportBarWidth), _barTo = Field(DialogMetrics.MidiExportBarWidth),
        _secFrom = Field(DialogMetrics.MidiExportSecWidth), _secTo = Field(DialogMetrics.MidiExportSecWidth);
    private readonly TextBlock _hint = ToolWindowParts.Small(""), _status = ToolWindowParts.Status("StatusText");
    private readonly Button _export = new Button { Content = "Export" }.WithPadding(DialogMetrics.MidiExportPrimaryPadding);
    private readonly Button _cancel = new Button { Name = "Cancel", Content = "Close" }.WithPadding(DialogMetrics.MidiExportSecondaryPadding);
    private bool _loading = true;

    /// <param name="project">The song to export.</param>
    /// <param name="settings">The last-used export options, read on open and written on close.</param>
    /// <param name="hasSelection">Whether the editor has a selection (the selected-bars choice needs one).</param>
    /// <param name="snip">The white selection when the dialog opened; "Selected bars" exports it, like Render.</param>
    /// <param name="selection">The score selection (bars and cells) bar ranges map from; null without one.</param>
    /// <param name="saveFile">Asks for the .mid path; null when the person cancels. None in the preview.</param>
    /// <param name="quietSave">Schedules a debounced save without the settings broadcast.</param>
    /// <param name="report">Shows the outcome or the failure to the person.</param>
    /// <param name="openArticulations">Opens the existing Articulations pane for a track (null: the link is unavailable).</param>
    public MidiExportWindow(SongProject project, MidiExportSettings settings, bool hasSelection, SelectionSnapshot? snip,
        (int StartBar, int StartCell, int EndBar, int EndCell)? selection,
        Func<TabForge.Platform.FileDialogOptions, Window, string?>? saveFile, Action quietSave, Action<string> report,
        Action<TrackModel>? openArticulations = null)
    {
        _project = project; _settings = settings;
        _snip = RenderSnip.Any(snip) ? snip : null;
        _selection = hasSelection ? selection : null;
        _saveFile = saveFile; _quietSave = quietSave; _report = report; _openArticulations = openArticulations;
        _barCount = TabForge.Services.BarRangeEditor.MaxMeasures(project);
        Title = "Export MIDI";
        Width = DialogMetrics.MidiExportWidth; Height = DialogMetrics.MidiExportHeight; MinWidth = DialogMetrics.MidiExportMinWidth; MinHeight = DialogMetrics.MidiExportMinHeight;
        DialogMetrics.CenterOnOwner(this);
        ShowInTaskbar = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        var stack = DialogMetrics.Inset(new StackPanel());
        stack.Children.Add(BuildTracksGroup());
        stack.Children.Add(BuildRangeGroup());
        stack.Children.Add(BuildTechniquesGroup());
        _scroll.Content = stack;
        var footer = DialogMetrics.Inset(new StackPanel());
        footer.Children.Add(_status);
        footer.Children.Add(BuildButtons());
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = new DockPanel { Children = { footer, _scroll } };

        LoadSettings();
        WireEvents();
        _loading = false;
        Changed();
        Closing += (_, _) => { SaveSettings(); _quietSave(); };
    }

    private Border BuildTracksGroup()
    {
        var all = new Button { Name = "TracksAll", Content = "All" }.WithPadding(DialogMetrics.MidiExportSmallPadding);
        var none = new Button { Name = "TracksNone", Content = "None" }.WithPadding(DialogMetrics.MidiExportSmallPadding);
        all.Click += (_, _) => SetAllTracks(true);
        none.Click += (_, _) => SetAllTracks(false);
        var list = new StackPanel();
        foreach (var track in _project.Tracks)
        {
            var label = new TextBlock { Text = track.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            var box = new CheckBox { Content = label, Tag = track }.WithMargin(DialogMetrics.MixRowGap);
            MidiSharedRows.Tip(box, track.Name);
            if (track.IsAudio)
            {
                box.IsEnabled = false;
                label.Text = track.Name + " (not exported)";
                MidiSharedRows.Tip(box, "Audio tracks are not written to a MIDI file.");
            }
            _trackBoxes.Add(box);
            list.Children.Add(box);
        }
        var content = new StackPanel();
        content.Children.Add(ToolWindowParts.Row(ToolWindowParts.Label("Tracks to export:"), all, none));
        content.Children.Add(new ScrollViewer { MaxHeight = DialogMetrics.MediaListMaxHeight, Content = list });
        return ToolWindowParts.Group("Tracks", content);
    }

    private Border BuildRangeGroup()
    {
        var names = new[] { "Whole song", "Selected bars", "Bar range", "Time range" };
        var range = new StackPanel();
        for (var i = 0; i < _bounds.Length; i++)
        {
            _bounds[i] = new RadioButton { Name = "Range" + i, Content = names[i], GroupName = "bounds" }.WithMargin(DialogMetrics.MixRowGap);
            range.Children.Add(_bounds[i]);
            if (i == 2) range.Children.Add(Wrap(ToolWindowParts.Label("from bar"), _barFrom, ToolWindowParts.Label("to bar"), _barTo,
                ToolWindowParts.Label($"(1 to {_barCount})")));
            if (i == 3) range.Children.Add(Wrap(ToolWindowParts.Label("from"), _secFrom, ToolWindowParts.Label("to"), _secTo,
                ToolWindowParts.Label("(mm:ss)")));
        }
        range.Children.Add(_hint);
        return ToolWindowParts.Group("Range", range);
    }

    /// <summary>A short time or bar text box with a floor, so it never shrinks to unreadable.</summary>
    private static TextBox Field(double width) => new() { Width = width, MinWidth = DialogMetrics.MidiExportFieldMinWidth };

    /// <summary>A row of parts that flows onto a second line when the window is narrow, instead of running past the edge.</summary>
    private static WrapPanel Wrap(params Control[] parts)
    {
        var panel = new WrapPanel();
        foreach (var part in parts) panel.Children.Add(part.WithMargin(DialogMetrics.MidiExportPartGap));
        return panel;
    }

    private StackPanel BuildButtons()
    {
        _export.WithMargin(DialogMetrics.MidiExportButtonGap);
        _export.Click += async (_, _) => await ExportPressed();
        _cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }.WithMargin(DialogMetrics.ColourPartGap);
        buttons.Children.Add(_export); buttons.Children.Add(_cancel);
        return buttons;
    }

    private void WireEvents()
    {
        foreach (var box in _trackBoxes) box.Click += (_, _) => { if (_useMap.IsChecked == true) EnsureTechniqueRows(); Changed(); };
        foreach (var radio in _bounds) radio.IsCheckedChanged += (_, _) => Changed();
        foreach (var text in new[] { _barFrom, _barTo, _secFrom, _secTo }) text.TextChanged += (_, _) => Changed();
    }

    private void SetAllTracks(bool isChecked)
    {
        foreach (var box in _trackBoxes) if (box.IsEnabled) box.IsChecked = isChecked;
        if (_useMap.IsChecked == true) EnsureTechniqueRows();
        Changed();
    }

    /// <summary>Exports the chosen tracks and range: the file dialog, then the write on a worker.</summary>
    public async Task ExportPressed()
    {
        SaveSettings();
        _quietSave();
        var (timeline, slice) = MidiExportRange.Resolve(_project, _settings, _snip, _selection);
        if (slice.Tracks.Count == 0) { _status.Text = "Nothing ticked: tick at least one track."; return; }
        if (slice.EndMs <= slice.StartMs + 1e-9) { _status.Text = "The range is empty."; return; }
        if (_saveFile is null) { _status.Text = ToolWindowParts.NeedsFullApp("Exporting MIDI"); return; }
        var path = _saveFile(new TabForge.Platform.FileDialogOptions
        {
            Key = DialogKeys.ExportMidi, Title = "Export MIDI", Filter = "MIDI file (*.mid)|*.mid",
            DefaultExt = ".mid", AddExtension = true, FileName = SafeFileNames.SafeFileName(_project.Title, "Untitled"),
        }, this);
        if (path is null) return;
        var articulationOptions = BuildArticulationOptions();
        _export.IsEnabled = false;
        _status.Text = "Exporting…";
        try
        {
            var copy = SongSnapshot.Take(_project, new ProjectStateEncoder(ProjectStateEncoder.PlaybackParallelEncodeBars));
            await Task.Run(() => MidiExportService.Export(copy.Song, path, slice, articulationOptions));
            _ = timeline;
            var report = ArticulationReport(articulationOptions);
            _status.Text = "Exported " + System.IO.Path.GetFileName(path) + report;
            _report("Exported " + System.IO.Path.GetFileName(path) + report);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Trace.Error(Trace.Ui, "midi export: " + ex.Message);
            _status.Text = "MIDI export failed: " + ex.Message;
            _report("MIDI export failed: " + ex.Message);
        }
        finally { _export.IsEnabled = true; Changed(); }
    }

    private void LoadSettings()
    {
        _bounds[MidiExportRange.BoundsOnLoad(_settings.Bounds, _snip is not null, _barCount)].IsChecked = true;
        var saved = _settings.TrackNames;
        foreach (var box in _trackBoxes)
        {
            if (!box.IsEnabled) { box.IsChecked = false; continue; }
            box.IsChecked = saved is null || saved.Contains(((TrackModel)box.Tag!).Name);
        }
        var (fromBar, toBar) = RenderDialogPresenter.BarRangeOnLoad(_settings.CustomFromBar, _settings.CustomToBar, _barCount);
        _barFrom.Text = fromBar.ToString(CultureInfo.InvariantCulture);
        _barTo.Text = toBar.ToString(CultureInfo.InvariantCulture);
        _secFrom.Text = MidiExportRange.FormatSeconds(_settings.CustomStartSec);
        _secTo.Text = MidiExportRange.FormatSeconds(_settings.CustomEndSec > 0 ? _settings.CustomEndSec : 60);
        ToolWindowParts.NotAvailable(_bounds[1], _snip is null);
        ToolTip.SetTip(_bounds[1], RenderDialogPresenter.SelectionTooltip(_snip is not null));
        if (_snip is not null)
        {
            var (tracks, clips) = RenderSnip.Counts(_project, _snip);
            _bounds[1].Content = RenderDialogPresenter.SelectedBarsLabel(tracks, _snip.HasRange ? _snip.StartBar : -1, _snip.EndBar, clips);
        }
        LoadTechniqueSettings();
    }

    private void SaveSettings()
    {
        _settings.TrackNames = _trackBoxes.Where(b => b.IsChecked == true && b.IsEnabled).Select(b => ((TrackModel)b.Tag!).Name).ToList();
        _settings.Bounds = Math.Max(0, Array.FindIndex(_bounds, b => b.IsChecked == true));
        if (int.TryParse(_barFrom.Text?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var fromBar)) _settings.CustomFromBar = Math.Max(0, fromBar);
        if (int.TryParse(_barTo.Text?.Trim() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var toBar)) _settings.CustomToBar = Math.Max(0, toBar);
        _settings.CustomStartSec = MidiExportRange.ParseSeconds(_secFrom.Text ?? "");
        _settings.CustomEndSec = MidiExportRange.ParseSeconds(_secTo.Text ?? "");
        SaveTechniqueSettings();
    }

    /// <summary>Applies the enabling, hint and Export rules after any change; every change saves quietly (debounced, no broadcast).</summary>
    private void Changed()
    {
        if (_loading) return;
        SaveSettings();
        _quietSave();
        for (var i = 0; i < _bounds.Length; i++)
            _bounds[i].IsEnabled = MidiExportRange.IsEnabled(i, _snip is not null, _barCount);
        var barRange = _bounds[2].IsChecked == true;
        var timeRange = _bounds[3].IsChecked == true;
        _barFrom.IsEnabled = _barTo.IsEnabled = barRange;
        _secFrom.IsEnabled = _secTo.IsEnabled = timeRange;
        string? problem = null;
        if (barRange) problem = RenderBarRange.Validate(_barFrom.Text ?? "", _barTo.Text ?? "", _barCount);
        else if (timeRange && MidiExportRange.ParseSeconds(_secTo.Text ?? "") <= MidiExportRange.ParseSeconds(_secFrom.Text ?? ""))
            problem = "The end time must come after the start time.";
        _hint.Text = problem ?? "";
        _hint.IsVisible = _hint.Text.Length > 0;
        var ticked = _trackBoxes.Any(b => b.IsChecked == true && b.IsEnabled);
        if (!ticked) { _hint.Text = "Tick at least one track to export."; _hint.IsVisible = true; }
        _export.IsEnabled = problem is null && ticked;
        ToolWindowParts.NotAvailable(_export, _saveFile is null);
    }
}
