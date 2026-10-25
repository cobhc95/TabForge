using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the mixer's row builders (master, group and track rows) and shared row helpers.
// Does not own: the rest of the class (see the main file).
// Tests: no dedicated test class.
public sealed partial class MixerWindow
{
    private Grid RowGrid() => MixerColumnLayout.NewGrid(_compact);

    private static void Put(Grid grid, Control element, int column)
    {
        Grid.SetColumn(element, column);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (element.Margin == default) element.Margin = new Thickness(4, 0);
        grid.Children.Add(element);
    }

    private Control ColumnHeader()
    {
        var grid = RowGrid();
        grid.Name = "ColumnHeader";
        // The header loses the same 22 DIP of total width as a row grid (the panel and row borders), or pan and volume (the star
        // columns) split that extra width and the mute/solo header (the column after them) drifts off the rows.
        grid.Margin = new Thickness(MixerColumnLayout.HeaderMarginLeft, 0, MixerColumnLayout.HeaderMarginRight, 4);
        for (var i = 0; i < ColumnNames.Length; i++)
        {
            var header = new TextBlock { Text = _compact && i == MixerColumnLayout.Volume ? "VOL" : ColumnNames[i], FontSize = 11, FontWeight = FontWeight.SemiBold, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary") };
            if (_compact && (i == MixerColumnLayout.Midi || i == MixerColumnLayout.Pitch)) header.IsVisible = false;
            Put(grid, header, i);
        }
        return grid;
    }

    private static Border Panel(params Control[] rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows) stack.Children.Add(row);
        return new Border
        {
            BorderThickness = new Thickness(MixerColumnLayout.PanelBorderThickness), CornerRadius = new CornerRadius(6), Padding = new Thickness(MixerColumnLayout.PanelPadding), Margin = new Thickness(0, 0, 0, 8), Child = stack,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderSoftBrush"),
        };
    }

    internal static Border RowBorder(Grid grid, bool accent, string name)
    {
        var border = new Border
        {
            Name = name, Child = grid, Padding = new Thickness(MixerColumnLayout.RowBorderPadding, 5), Margin = new Thickness(0, 1), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(MixerColumnLayout.RowBorderThickness), BorderBrush = Brushes.Transparent,   // the selection outline lives here (no layout shift)
            [!Border.BackgroundProperty] = new DynamicResourceExtension(accent ? "Panel2Brush" : "WindowBrush"),
        };
        AutomationProperties.SetName(border, name);
        return border;
    }

    /// <summary>The colour key of group and Master names: the accent, or plain text where the accent reads below 4.5:1 on the row (the WPF readable-text fallback).</summary>
    public static string AccentNameKey(bool light) =>
        ThemeColours.Contrast(AvaloniaPalette.Get("Accent", light), AvaloniaPalette.Get("PanelBackground", light)) >= 4.5 ? "Accent" : "TextPrimary";

    private StackPanel NameBlock(string title, string subtitle, bool accent)
    {
        var subtitleBlock = new TextBlock { Text = subtitle, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = !_compact, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary") };
        return new StackPanel
        {
            Children =
            {
                new TextBlock { Name = "StripTitle", Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                    [!TextBlock.ForegroundProperty] = new DynamicResourceExtension(accent ? AccentNameKey(ActualThemeVariant == ThemeVariant.Light) : "TextPrimary") },
                subtitleBlock,
            },
        };
    }

    private Border MasterRow()
    {
        var grid = RowGrid();
        var name = NameBlock(MixerStripPresenter.MasterName, MixerStripPresenter.MasterSubtitle, accent: true);
        Put(grid, MasterHead(name), MixerColumnLayout.Name);
        Put(grid, BusButton(null), MixerColumnLayout.Fx);
        var monitor = new MonitorFxButton { Name = "MonitorFx", Active = MonitorActive(), Margin = new Thickness(2, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(monitor, "Monitoring effects (e.g. speaker calibration). Heard live only; never included in renders or exports.");
        AutomationProperties.SetName(monitor, "Monitor FX");
        monitor.Click += (_, _) => OpenMonitorFx();
        // Compact makes the MIDI column zero wide, so the button would spill over the pan slider; the Master row's free mute and solo column holds it there.
        Put(grid, monitor, _compact ? MixerColumnLayout.MuteSolo : MixerColumnLayout.Midi);
        Put(grid, Fader(() => _project.Mixer.MasterPan, -64, 63, MixerStripPresenter.PanDefault, v => _project.Mixer.MasterPan = v,
            $"Master pan: moves the whole mix left or right ({MixerStripPresenter.PanRange}; double-click: centre)"), MixerColumnLayout.Pan);
        Put(grid, Fader(MasterVolume, 0, 100, MixerStripPresenter.MasterVolumeDefault, SetMasterVolume,
            "Master volume for all tracks (0–100 %, the same as the main window's master knob; double-click: 100)", songEdit: false), MixerColumnLayout.Volume);
        return RowBorder(grid, accent: true, $"Mixer row: {MixerStripPresenter.MasterName}");
    }

    /// <summary>The window's own master volume, used only when the window has no playback to read (the preview).</summary>
    private int _masterVolume = 100;

    /// <summary>The master volume the Master slider shows: the process's (the transport knob's) when the window has one.</summary>
    private int MasterVolume() => _masterPercent?.Invoke() ?? _masterVolume;

    /// <summary>Sets the master volume everywhere (the playback and the transport knob), or the window's own value without playback.</summary>
    private void SetMasterVolume(int value)
    {
        if (_setMasterPercent is null) _masterVolume = value;
        else _setMasterPercent(value);
    }

    private Border GroupRow(string group, IReadOnlyList<TrackModel> members)
    {
        MixerGroupLevels Levels() => _project.Mixer.Levels(group);
        MixerGroupLevels Edit() => _project.Mixer.Edit(group);
        var collapsed = IsCollapsed(group);
        var chevron = Chevron(collapsed, group, collapsed ? "Expand the group (Right arrow)" : "Collapse the group (Left arrow)", () => SetCollapsed(new[] { group }, null));
        var colour = GroupColour(group, members);
        var swatch = new Button
        {
            Name = "GroupColour", Content = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = SwatchBrush(colour) },
            Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(swatch, $"Colour of the {group.ToLowerInvariant()}: click to choose (colours every track in the group)");
        AutomationProperties.SetName(swatch, $"Colour of the {group.ToLowerInvariant()}");
        swatch.Click += (_, _) => ColourMenuFor(swatch, colour, hex => { ClickEdit(() => TrackColouring.Group(_project, group, hex)); Rebuild(); });
        var head = new DockPanel();
        DockPanel.SetDock(chevron, Dock.Left);
        DockPanel.SetDock(swatch, Dock.Left);
        head.Children.Add(chevron);
        head.Children.Add(swatch);
        head.Children.Add(NameBlock(group, $"{members.Count} track{(members.Count == 1 ? "" : "s")}", accent: true));
        var grid = RowGrid();
        Put(grid, head, MixerColumnLayout.Name);
        Put(grid, BusButton(group), MixerColumnLayout.Fx);
        var pitch = Stepper(Levels().Pitch, v => ClickEdit(() => Edit().Pitch = v), "Shifts every track in the group by semitones");
        pitch.IsVisible = !_compact;
        Put(grid, pitch, MixerColumnLayout.Pitch);
        Put(grid, Fader(() => Levels().Pan, -64, 63, MixerStripPresenter.PanDefault, v => Edit().Pan = v, $"Moves every track in the group left or right ({MixerStripPresenter.PanRange})"), MixerColumnLayout.Pan);
        Put(grid, Fader(() => Levels().Volume, 0, 200, MixerStripPresenter.GroupVolumeDefault, v => Edit().Volume = v, "Group level in percent of each track's own volume (100 = unchanged)"), MixerColumnLayout.Volume);
        Put(grid, MuteSolo(Levels().Mute, Levels().Solo, v => ClickEdit(() => Edit().Mute = v), v => ClickEdit(() => Edit().Solo = v), null), MixerColumnLayout.MuteSolo);
        if (_compact)
            Put(grid, RowMenuButton(
                menu => FillGroupRowMenu(menu, () => Levels().Pitch, v => ClickEdit(() => Edit().Pitch = v), $"{members.Count} track{(members.Count == 1 ? "" : "s")}"),
                $"More controls for the {group}"), MixerColumnLayout.RowMenu);
        return RowBorder(grid, accent: true, $"Mixer group: {group}");
    }

    private Border TrackRow(TrackModel built)
    {
        // An undo replaces the track objects: each gesture resolves the track by id when it runs, never the one the row was built from.
        var id = built.Id;
        TrackModel? Now() => _project.Tracks.FirstOrDefault(t => t.Id == id);
        void OnTrack(Action<TrackModel> apply) { if (Now() is { } live) apply(live); }
        var track = built;   // the build-time values the row shows
        var grid = RowGrid();
        var swatch = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = SwatchBrush(track.ColorHex) };
        var chip = new Button
        {
            Name = "TrackColour", Content = swatch, Width = 16, Height = 16, Padding = new Thickness(0), Margin = new Thickness(22, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(chip, $"Colour of '{track.Name}': click to choose");
        AutomationProperties.SetName(chip, $"Colour of {track.Name}");
        chip.Click += (_, _) => OnTrack(live => PickTrackColour(live, chip));
        var head = new DockPanel();
        DockPanel.SetDock(chip, Dock.Left);
        head.Children.Add(chip);
        var title = NameBlock(track.Name, track.InstrumentName, accent: false);
        title.Margin = new Thickness(0, 0, 4, 0);
        head.Children.Add(title);
        Put(grid, head, MixerColumnLayout.Name);

        var fx = new FxSplitButton { Name = "TrackFx", ChainOn = track.SoundSource == SoundSources.Plugins, PluginCount = track.Rig.Plugins.Count, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(fx, $"FX chain for {track.Name}");
        ToolTip.SetTip(fx, "FX: open this track's plug-in chain · power: play through the chain (on) or plain MIDI (off)");
        var midi = new CheckBox
        {
            Name = "MidiSound", IsChecked = track.MidiSound, IsEnabled = MixerStripPresenter.MidiSoundEnabled(track.SoundSource), HorizontalAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(midi, "MIDI sound: the track's General MIDI instrument plays while the FX chain is on (the effects shape it when there is no VST instrument). Off: only a VST instrument sounds.");
        midi.IsCheckedChanged += (_, _) => { if (!_building) ClickEdit(() => OnTrack(live => { live.MidiSound = midi.IsChecked == true; live.MidiSoundAuto = false; live.MidiSoundManualOff = !live.MidiSound; })); };
        fx.OpenChain += (_, _) => OnTrack(OpenChain);
        fx.TogglePower += (_, _) =>
        {
            ClickEdit(() => OnTrack(live => live.SoundSource = live.SoundSource == SoundSources.Plugins ? SoundSources.Midi : SoundSources.Plugins));
            var source = Now()?.SoundSource ?? track.SoundSource;
            fx.ChainOn = source == SoundSources.Plugins;
            midi.IsEnabled = MixerStripPresenter.MidiSoundEnabled(source);
        };
        Put(grid, fx, MixerColumnLayout.Fx);
        midi.IsVisible = !_compact;
        Put(grid, midi, MixerColumnLayout.Midi);
        var pitch = Stepper(track.Transpose, v => ClickEdit(() => OnTrack(live => live.Transpose = v)), "Transposes this track by semitones");
        pitch.IsVisible = !_compact;
        Put(grid, pitch, MixerColumnLayout.Pitch);
        Put(grid, Fader(() => MixerStripPresenter.PanSlider(Now()?.Pan ?? track.Pan), -64, 63, MixerStripPresenter.PanDefault, v => OnTrack(live => live.Pan = MixerStripPresenter.PanFromSlider(v)),
            $"Track pan ({MixerStripPresenter.PanRange}; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: centre)"), MixerColumnLayout.Pan);
        // The volume cell also holds the track's reverb and chorus sends (0..SendMax), each an undoable song edit like the other faders.
        var volumeCell = new StackPanel();
        volumeCell.Children.Add(Fader(() => MixerStripPresenter.ClampVolume(Now()?.Volume ?? track.Volume), 0, MidiLimits.Max, MixerStripPresenter.VolumeDefault, v => OnTrack(live => live.Volume = MixerStripPresenter.ClampVolume(v)),
            "Track volume (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 100)"));
        volumeCell.Children.Add(Fader(() => Math.Clamp(Now()?.Reverb ?? track.Reverb, 0, SendMax), 0, SendMax, MixerStripPresenter.ReverbDefault, v => OnTrack(live => live.Reverb = Math.Clamp(v, 0, SendMax)),
            "Reverb send (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 24)"));
        volumeCell.Children.Add(Fader(() => Math.Clamp(Now()?.Chorus ?? track.Chorus, 0, SendMax), 0, SendMax, MixerStripPresenter.ChorusDefault, v => OnTrack(live => live.Chorus = Math.Clamp(v, 0, SendMax)),
            "Chorus send (0-127; mouse wheel: 1 step, Ctrl+wheel: 8; double-click: 0)"));
        Put(grid, volumeCell, MixerColumnLayout.Volume);
        var row = RowBorder(grid, accent: false, MixerStripPresenter.StripName(track.Name, track.Mute, track.Solo));
        Put(grid, MuteSolo(track.Mute, track.Solo, v => ClickEdit(() => OnTrack(live => live.Mute = v)), v => ClickEdit(() => OnTrack(live => live.Solo = v)),
            () => OnTrack(live => AutomationProperties.SetName(row, MixerStripPresenter.StripName(live.Name, live.Mute, live.Solo)))), MixerColumnLayout.MuteSolo);
        if (_compact)
            Put(grid, RowMenuButton(
                menu => FillTrackRowMenu(menu, midi, () => Now()?.Transpose ?? track.Transpose, v => ClickEdit(() => OnTrack(live => live.Transpose = v)), track.InstrumentName),
                $"More controls for {track.Name}"), MixerColumnLayout.RowMenu);
        AttachDrag(row, id, null, row);
        row.ContextMenu = TrackMenu(id, chip);
        return row;
    }

    internal static IBrush? SwatchBrush(string hex) =>
        ColourHex.TryParse(hex, out var colour) ? new SolidColorBrush(AvaloniaPalette.ColorOf(colour.ToUi())) : null;
}
