using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Avalonia.Views.Midi;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Export MIDI dialog's Techniques section: the two choices, the lazy per-track map rows (the shared articulation preset selector, the mapped count
//   and the View / edit link) and the unmapped-techniques choice. It only reads maps and picks presets for this export; it never edits a track.
// Does not own: the map and its presets (ArticulationMap, ArticulationPresets), the selector (ArticulationPresetSelector), the editor (ArticulationsPane) or
//   the export (MidiExportService).
// Tests: MidiTechniqueExportTests (the export service); the compile is covered by TestArticulationKeyswitches.
public sealed partial class MidiExportWindow
{
    private readonly RadioButton _asPlayed = new() { Content = "As played in TabForge", GroupName = "articulations", Name = "AsPlayed" };
    private readonly RadioButton _useMap = new() { Content = "Use an articulation map", GroupName = "articulations", Name = "UseMap" };
    private readonly ComboBox _unmapped = new() { Name = "UnmappedTechniques", SelectedIndex = 0, ItemsSource = new[] { "Keep the TabForge sound", "Skip them" } };
    private readonly CheckBox _keepTechniques = new()
    {
        Name = "KeepTechniques", IsChecked = false,
        Content = "Keep techniques for TabForge",
    };
    private readonly CheckBox _sectionMarkers = new() { Name = "WriteSectionMarkers", IsChecked = true, Content = "Write sections as markers" };
    private const string BuiltInNote = "Built-in sound: no map by default. Pick a map to write its keyswitches for another program.";
    private const string NoMapText = "None (keep the TabForge sound)";
    private const string TrackMapText = "Track map (attached)";
    private const string FileMapText = "Open from file…";
    private readonly AvaloniaFileDialogs _mapFiles = new();
    private readonly StackPanel _unmappedRow = new();
    private readonly StackPanel _techniqueRows = new();
    private readonly List<TechniqueRow> _techniqueRowViews = new();
    private bool _rowsBuilt;

    /// <summary>One exported track's map row: its selector, its mapped count and the link into the existing Articulations pane.</summary>
    private sealed class TechniqueRow
    {
        public required int Index { get; init; }
        public required TrackModel Track { get; init; }
        public required Articulations.ArticulationPresetSelector Selector { get; init; }
        public required TextBlock Count { get; init; }
        public required Button Link { get; init; }
        public required TextBlock Status { get; init; }
        public bool BuiltIn { get; init; }
        public ArticulationMap? Map { get; set; }
        public string? PresetName { get; set; }
    }

    private Border BuildTechniquesGroup()
    {
        var choices = new StackPanel();
        _asPlayed.WithMargin(DialogMetrics.MixRowGap);
        _useMap.WithMargin(DialogMetrics.MixRowGap);
        choices.Children.Add(_asPlayed);
        choices.Children.Add(_useMap);
        _unmappedRow.Children.Add(ToolWindowParts.Label("Unmapped techniques:"));
        _unmappedRow.Children.Add(_unmapped);
        _keepTechniques.WithMargin(DialogMetrics.MixRowGap);
        ToolTip.SetTip(_keepTechniques, "Adds small text markers so TabForge can read the techniques back when it imports this file. Other programs ignore them.");
        _sectionMarkers.WithMargin(DialogMetrics.MixRowGap);
        ToolTip.SetTip(_sectionMarkers, "Adds each section as a MIDI marker so other programs show it (default on)");
        _sectionMarkers.IsCheckedChanged += (_, _) => { if (!_loading) Changed(); };
        var body = new StackPanel { Children = { choices, _unmappedRow, _keepTechniques, _sectionMarkers, _techniqueRows } };
        _asPlayed.IsCheckedChanged += (_, _) => TechniquesChanged();
        _useMap.IsCheckedChanged += (_, _) => TechniquesChanged();
        _unmapped.SelectionChanged += (_, _) => { if (!_loading) Changed(); };
        _keepTechniques.IsCheckedChanged += (_, _) => { if (!_loading) Changed(); };
        return ToolWindowParts.Group("Techniques", body);
    }

    private void TechniquesChanged()
    {
        if (_loading) return;
        if (_useMap.IsChecked == true) EnsureTechniqueRows();
        UpdateTechniqueVisibility();
        Changed();
    }

    private void UpdateTechniqueVisibility()
    {
        var on = _useMap.IsChecked == true;
        _unmappedRow.IsEnabled = on;
        _techniqueRows.IsVisible = on;
    }

    /// <summary>Builds the track rows the first time the map choice is used (lazy: no selector reads the preset folder before that).</summary>
    private void EnsureTechniqueRows()
    {
        if (_rowsBuilt) { if (TickedTracksChanged()) RebuildTechniqueRows(); return; }
        _rowsBuilt = true;
        RebuildTechniqueRows();
    }

    private bool TickedTracksChanged()
    {
        var ticked = _trackBoxes.Where(b => b.IsChecked == true && b.IsEnabled).Select(b => (TrackModel)b.Tag!).ToList();
        return ticked.Count != _techniqueRowViews.Count || ticked.Where((track, i) => !ReferenceEquals(track, _techniqueRowViews[i].Track)).Any();
    }

    private void RebuildTechniqueRows()
    {
        var saved = _techniqueRowViews.ToDictionary(row => row.Track, row => row.PresetName);
        var savedByName = _settings.TrackPresetOverrides;
        _techniqueRowViews.Clear();
        _techniqueRows.Children.Clear();
        for (var index = 0; index < _project.Tracks.Count; index++)
        {
            var track = _project.Tracks[index];
            if (track.IsAudio || !_trackBoxes.Any(b => ReferenceEquals(b.Tag, track) && b.IsChecked == true && b.IsEnabled)) continue;
            var builtIn = ArticulationExportOptions.InstrumentSlotOf(track) is null;
            var selector = new Articulations.ArticulationPresetSelector
            {
                DefaultText = builtIn ? NoMapText : TrackMapText, FileText = FileMapText,
                Width = DialogMetrics.MidiExportTechniqueSelectorWidth,
            };
            var count = ToolWindowParts.Label("");
            count.TextWrapping = TextWrapping.Wrap;
            var link = new Button { Content = "Create", Name = "TechniqueEdit" }.WithPadding(DialogMetrics.MidiExportSmallPadding);
            var status = ToolWindowParts.Small(BuiltInNote);
            status.TextWrapping = TextWrapping.Wrap;
            status.Name = "TechniqueStatus";
            var row = new TechniqueRow { Index = index, Track = track, Selector = selector, Count = count, Link = link, Status = status, BuiltIn = builtIn };
            if (row.BuiltIn) ToolTip.SetTip(selector, BuiltInNote);
            _techniqueRowViews.Add(row);
            var name = ToolWindowParts.Label(track.Name);
            name.Width = DialogMetrics.MidiExportTechniqueNameWidth;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            MidiSharedRows.Tip(name, track.Name);
            // The row is as tall as its content: the name, then the selector, count and link centred on one line (wrapping when narrow), and the status text wrapping below.
            foreach (var part in new Control[] { selector, count, link }) part.VerticalAlignment = VerticalAlignment.Center;
            var controls = Wrap(selector, count, link);
            var right = new StackPanel { Children = { controls, status } };
            Grid.SetColumn(right, 1);
            var line = new Grid { Name = "TechniqueRow", ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"), Children = { name, right } }.WithMargin(DialogMetrics.MixRowGap);
            MidiSharedRows.ConstrainToViewport(line, _scroll, DialogMetrics.MidiExportTechniqueReserve);
            selector.PresetSelected += preset => { row.Map = preset.ToMap(); row.PresetName = preset.Name; ShowRow(row); };
            selector.DefaultPicked += () => { row.Map = row.BuiltIn ? null : ArticulationExportOptions.DefaultMapOf(track); row.PresetName = null; ShowRow(row); };
            selector.FilePicked += () => _ = OpenMapFromFileAsync(row);
            link.Click += (_, _) => _openArticulations?.Invoke(track);
            if (_openArticulations is null) ToolWindowParts.NotAvailable(link);
            saved.TryGetValue(track, out var presetName);
            if (presetName is null && savedByName is not null) savedByName.TryGetValue(track.Name, out presetName);
            if (presetName is not null && ArticulationPresets.Find(presetName) is { } preset)
            {
                row.Map = preset.ToMap();
                row.PresetName = presetName;
                selector.Reload(presetName);
            }
            else
            {
                row.Map = row.BuiltIn ? null : ArticulationExportOptions.DefaultMapOf(track);
                selector.Reload(null);
            }
            ShowRow(row);
            _techniqueRows.Children.Add(line);
        }
    }

    /// <summary>Reads a preset file off the UI thread, keeps it with the user's presets and picks it for this track.</summary>
    private async Task OpenMapFromFileAsync(TechniqueRow row)
    {
        var picked = _mapFiles.OpenFile(new TabForge.Platform.FileDialogOptions
        {
            Key = DialogKeys.ArticulationsImport, Title = "Open an articulation preset",
            Filter = "Articulation preset (*.json)|*.json",
        }, this);
        if (picked is null) { row.Selector.Reload(row.PresetName); return; }
        var preset = await Task.Run(() => ArticulationPresets.ReadFile(picked));
        if (preset is null)
        {
            _report("That file is not an articulation preset this version can read.");
            row.Selector.Reload(row.PresetName);
            return;
        }
        var name = PresetRules.Error(preset.Name) is null ? preset.Name : Path.GetFileNameWithoutExtension(picked).Trim();
        if (ArticulationPresets.FindBuiltIn(name) is not null) name += " (imported)";
        if (PresetRules.Error(name) is not null) name = "Imported articulation";
        preset.Name = name;
        try { await Task.Run(() => ArticulationPresets.WriteFile(ArticulationPresets.UserStore().PathOf(name), preset)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _report("The preset could not be saved: " + ex.Message);
            row.Selector.Reload(row.PresetName);
            return;
        }
        row.Map = preset.ToMap();
        row.PresetName = name;
        row.Selector.Reload(name);
        ShowRow(row);
        Changed();
    }

    private void ShowRow(TechniqueRow row)
    {
        var total = Math.Max(1, ArticulationTechniques.All.Count - 1);
        var noMap = row.BuiltIn && row.Map is null;
        row.Status.IsVisible = noMap;
        row.Count.IsVisible = !noMap;
        row.Count.Text = noMap ? "" : $"mapped {(row.Map?.Rules.Count ?? 0)} of {total}";
        var has = row.Map is { IsEmpty: false } || (row.BuiltIn && !ArticulationMapStore.TrackMapOf(row.Track).IsEmpty);
        row.Link.Content = has ? "View / edit" : "Create";
    }

    /// <summary>The map choices for this export: null on the default option, so the old export path is untouched.</summary>
    private ArticulationExportOptions? BuildArticulationOptions()
    {
        var keep = _keepTechniques.IsChecked == true;
        if (_useMap.IsChecked != true) return keep ? new ArticulationExportOptions { MapsEnabled = false, KeepTechniques = true } : null;
        EnsureTechniqueRows();
        var options = new ArticulationExportOptions { SkipUnmapped = _unmapped.SelectedIndex == 1, KeepTechniques = keep };
        foreach (var row in _techniqueRowViews)
            if (row.PresetName is { } name && ArticulationPresets.Find(name) is { } preset)
                options.Overrides[row.Index] = preset.ToMap();
        return options;
    }

    private string ArticulationReport(ArticulationExportOptions? options) =>
        options is null ? "" : $" (techniques mapped {options.Mapped}, kept {options.Kept}, skipped {options.Skipped})";

    private void LoadTechniqueSettings()
    {
        _asPlayed.IsChecked = !_settings.UseArticulationMap;
        _useMap.IsChecked = _settings.UseArticulationMap;
        _unmapped.SelectedIndex = Math.Clamp(_settings.UnmappedTechniques, 0, 1);
        _keepTechniques.IsChecked = _settings.KeepTechniques;
        _sectionMarkers.IsChecked = _settings.WriteSectionMarkers;
        if (_settings.UseArticulationMap) EnsureTechniqueRows();
        UpdateTechniqueVisibility();
    }

    private void SaveTechniqueSettings()
    {
        _settings.UseArticulationMap = _useMap.IsChecked == true;
        _settings.UnmappedTechniques = Math.Max(0, _unmapped.SelectedIndex);
        _settings.KeepTechniques = _keepTechniques.IsChecked == true;
        _settings.WriteSectionMarkers = _sectionMarkers.IsChecked == true;
        if (!_rowsBuilt) return;   // keep the last per-track picks until the map option lists its rows
        var overrides = _techniqueRowViews.Where(row => row.PresetName is not null)
            .ToDictionary(row => row.Track.Name, row => row.PresetName!);
        _settings.TrackPresetOverrides = overrides.Count == 0 ? null : overrides;
    }
}
