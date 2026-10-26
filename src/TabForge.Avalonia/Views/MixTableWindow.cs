using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Mix Table (F10): ticks the parameters that change from the selected beat on, their values, the transition and whether the change applies
/// to every track. OK closes with the presenter's result, Clear with <see cref="MixTablePresenter.Cleared"/>, Cancel and Esc with null.
/// Owns: the window's controls, their layout and their enabling from the ticks. Does not own: the start values, labels and result (MixTablePresenter).
/// Sample state: the preview has no document, so <see cref="SamplePresenter"/> stands for the selected beat (no mix point, track values).
/// Tests: no dedicated test class.
/// </summary>
public sealed class MixTableWindow : Window
{
    private readonly MixTablePresenter _presenter;
    private readonly CheckBox _instrumentCheck = new();
    private readonly ComboBox _instrument = new() { Width = 180 };
    private readonly List<(CheckBox Check, Slider Slider)> _scales = [];
    private readonly CheckBox _tempoCheck = new();
    private readonly TextBox _tempo = new() { Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _transition = new() { Width = 130 };
    private readonly CheckBox _allTracks = new();

    public MixTableWindow(MixTablePresenter presenter)
    {
        _presenter = presenter;
        Title = MixTablePresenter.Title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Header());
        root.Children.Add(Sections());
        root.Children.Add(OptionsRow());
        root.Children.Add(ButtonRow());
        Content = root;
    }

    /// <summary>The sample beat the preview opens with: no mix point, the full demo song's first track and tempo. Built once, on first use (never at startup).</summary>
    public static MixTablePresenter SamplePresenter() => _sample ??= CreateSample();

    private static MixTablePresenter? _sample;

    private static MixTablePresenter CreateSample() =>
        FullDemoSongFactory.Create() is { } song && song.Tracks[0] is { } track
            ? new(null, track.MidiProgram, track.Volume, track.Pan, null, song.Tempo, Enumerable.Range(0, 128).Select(p => (p, GeneralMidi.NameOf(p), false)))
            : throw new InvalidOperationException("The demo song has no tracks.");

    private Control Header() => new StackPanel
    {
        Margin = new Thickness(0, 0, 0, 8),
        Children =
        {
            new TextBlock { Text = MixTablePresenter.Title, FontSize = 18, FontWeight = FontWeight.SemiBold },
            ToolWindowParts.Small(MixTableRows.Hint),
        },
    };

    private Control Sections()
    {
        _instrumentCheck.Content = MixTablePresenter.InstrumentLabel;
        foreach (var choice in _presenter.InstrumentChoices) _instrument.Items.Add(choice.Label);
        _instrument.SelectedIndex = _presenter.InstrumentStartIndex;
        _instrument.HorizontalAlignment = HorizontalAlignment.Stretch;
        var instrumentRow = MixTableRows.Row(_instrumentCheck, _instrument, MixTableRows.Readout(), _presenter.InstrumentOn);

        var scaleFields = new (string Label, MixFieldValue Field)[]
        {
            (MixTablePresenter.VolumeLabel, _presenter.Volume), (MixTablePresenter.PanLabel, _presenter.Pan),
            (MixTablePresenter.ChorusLabel, _presenter.Chorus), (MixTablePresenter.ReverbLabel, _presenter.Reverb),
            (MixTablePresenter.PhaserLabel, _presenter.Phaser), (MixTablePresenter.TremoloLabel, _presenter.Tremolo),
        };
        var rows = new List<Control>();
        foreach (var (label, field) in scaleFields)
        {
            var isPan = label == MixTablePresenter.PanLabel;
            var readout = MixTableRows.Readout();
            var slider = MixTableRows.Scale(isPan ? MixTablePresenter.PanMin : MixTablePresenter.ScaleMin, isPan ? MixTablePresenter.PanMax : MixTablePresenter.ScaleMax,
                field.Value, readout, isPan ? MixTableRows.PanText : MixTablePresenter.ValueText);
            // Pan's centre and the effects' off are 0; the volume has no fixed default (it is the track's own), so it keeps none.
            if (label != MixTablePresenter.VolumeLabel) AvaloniaSliderReset.OnDoubleTap(slider, 0);
            var check = new CheckBox { Content = label };
            rows.Add(MixTableRows.Row(check, slider, readout, field.On));
            _scales.Add((check, slider));
        }

        _tempoCheck.Content = MixTablePresenter.TempoLabel;
        _tempo.Text = _presenter.TempoStartText;
        var tempoReadout = MixTableRows.Readout();
        tempoReadout.Text = MixTableRows.TempoText(_tempo.Text);
        _tempo.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) tempoReadout.Text = MixTableRows.TempoText(_tempo.Text ?? ""); };
        var tempoRow = MixTableRows.Row(_tempoCheck, _tempo, tempoReadout, _presenter.TempoOn);

        return new StackPanel
        {
            Children =
            {
                MixTableRows.Section("Sound", instrumentRow, rows[0], rows[1]),
                MixTableRows.Section("Effects", rows[2], rows[3], rows[4], rows[5]),
                MixTableRows.Section("Tempo", tempoRow),
            },
        };
    }

    private StackPanel OptionsRow()
    {
        foreach (var label in _presenter.TransitionLabels) _transition.Items.Add(label);
        _transition.SelectedIndex = _presenter.TransitionStartIndex;
        _allTracks.Content = MixTablePresenter.AllTracksLabel;
        _allTracks.IsChecked = _presenter.AllTracksStart;
        _allTracks.Margin = new Thickness(16, 0, 0, 0);
        _allTracks.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(3, 12, 0, 0),
            Children = { ToolWindowParts.Label(MixTablePresenter.TransitionLabel), _transition, _allTracks },
        };
    }

    private DockPanel ButtonRow()
    {
        var clear = new Button { Content = MixTablePresenter.ClearButton, Width = 80 };
        var ok = new Button { Content = MixTablePresenter.OkButton, Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Classes.Add(AvaloniaDefaultLooks.AccentLook);
        var cancel = new Button { Content = MixTablePresenter.CancelButton, Width = 80, IsCancel = true };
        clear.Click += (_, _) => Close(MixTablePresenter.Cleared);
        ok.Click += (_, _) => Close(_presenter.Build(ReadEdits()));
        cancel.Click += (_, _) => Close(null);
        DockPanel.SetDock(clear, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        return new DockPanel { Margin = new Thickness(3, 16, 3, 0), Children = { clear, right } };
    }

    private MixTableEdits ReadEdits() => new(
        InstrumentOn: _instrumentCheck.IsChecked == true, InstrumentIndex: _instrument.SelectedIndex,
        Volume: Picked(0), Pan: Picked(1), Chorus: Picked(2), Reverb: Picked(3), Phaser: Picked(4), Tremolo: Picked(5),
        TempoOn: _tempoCheck.IsChecked == true, TempoText: _tempo.Text ?? "", TransitionIndex: _transition.SelectedIndex,
        AllTracks: _allTracks.IsChecked == true);

    private MixFieldValue Picked(int index) => new(_scales[index].Check.IsChecked == true, (int)_scales[index].Slider.Value);
}
