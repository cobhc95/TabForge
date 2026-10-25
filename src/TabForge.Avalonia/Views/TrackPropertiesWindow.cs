using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Track properties for a MIDI track, the twin of the WPF TrackPropertiesDialog: the details card (the owner's instrument
/// picture, track name, colour and tint), the instrument card (the Change button runs the track row icon's picker, InstrumentPickerFlow, whose OK commits at once as its own undo step; MIDI program and channel), the tuning card (strings, string tools, keep frets,
/// frets and capo) or the drum notation card, and the OK / Cancel footer. OK is one undo step through DocumentEdits.Run when a document is given.
/// The window edits working copies; OK writes the track only when every typed field is in range, else it shows the field's message and stays open.
/// Closing with edits pending (Cancel, Esc, the close box) asks the WPF discard question; Yes closes, No returns to editing.
/// Owns: the controls, layout and the working copies; the list of pending changes for the discard question.
/// Does not own: the rules and texts (TrackPropertiesPresenter), the capo shift and retune (TrackSetup), the question window (SavePromptWindow);
///     the picture, colour, keep-frets and drum sections are in TrackPropertiesWindow.Sections.cs.
/// Not a full twin: the FX button in the Mixer card,
///     the custom drum map editor, the typed note on a string chip, the Add track position row and the audio track variant.
/// Deviation: the cover falls back to the owner's track-row icon, not the WPF vector instrument art, when an instrument has no badge; a string chip is the palette
///     tuning pill, not the track colour; "Don't ask me again" is shown but not stored (no settings here).
/// Over 300 lines: the field cards, string rows, footer and discard question of one window; the further sections live in the partial file; the Mixer card is in TrackPropertiesWindow.Mixer.cs.
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests.
/// </summary>
public sealed partial class TrackPropertiesWindow : Window
{
    private static readonly SavePrompt DiscardQuestion =
        new("Unsaved track changes", "Discard the changes made to this track?", "Close without applying", "Return to the track properties");

    private readonly TrackModel _track;
    private readonly IEditableDocument? _document;
    private readonly List<int> _tunings;
    private readonly TextBox _name, _performer, _notes, _program, _channel, _frets, _capo;
    private readonly TextBlock _channelHint, _stringsLabel = new() { Name = "StringsLabel", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Name = "ErrorText", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _stringRows = new() { Name = "StringRows", Margin = new Thickness(0, 8, 0, 0) };
    private bool _discardConfirmed, _askingDiscard;
    private readonly bool _adding;
    private System.Threading.Tasks.Task _committed = System.Threading.Tasks.Task.CompletedTask;
    private readonly InstrumentPickerFlow _picker;

    /// <summary>
    /// <paramref name="document"/> holds <paramref name="track"/>; with it OK is one undo step, without it (the preview) OK writes the track directly.
    /// <paramref name="adding"/>: the track is new (the Add track window), so OK also adds it at the end of the song.
    /// <paramref name="picker"/>: how the Change instrument button sounds and syncs the choice (none: silent).
    /// </summary>
    public TrackPropertiesWindow(TrackModel track, IEditableDocument? document = null, bool adding = false, InstrumentPickerFlow? picker = null)
    {
        _track = track;
        _picker = picker ?? new InstrumentPickerFlow();
        _document = document;
        _adding = adding;
        _tunings = track.StringTunings.ToList();
        Title = TrackPropertiesPresenter.Title(track.Name, adding);
        Width = 800;
        SizeToContent = SizeToContent.Height;
        MinWidth = 640;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("WindowBackground");
        this[!ForegroundProperty] = new DynamicResourceExtension("TextPrimary");
        FontSize = DialogMetrics.FontSize;

        _name = Box("NameBox", "Track name", track.Name, double.NaN);
        _performer = Box("PerformerBox", "Played by", track.Performer, double.NaN);
        _notes = Box("NotesBox", "Notes", track.TrackNotes, double.NaN);
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.MinHeight = NotesMinHeight;
        _notes.VerticalContentAlignment = VerticalAlignment.Top;
        _program = Box("ProgramBox", "MIDI program", track.MidiProgram.ToString(), 60);
        _channel = Box("ChannelBox", "Channel", track.MidiChannel.ToString(), 60);
        _frets = Box("FretsBox", "Frets", track.NumberOfFrets.ToString(), 50);
        _capo = Box("CapoBox", "Capo", track.Capo.ToString(), 50);
        _channelHint = Hint("Channel 9 (the 10th) is the General MIDI drum channel.");
        _channelHint.Name = "ChannelHint";
        _error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");

        var left = new StackPanel { Width = 250, Margin = new Thickness(0, 0, 14, 0) };
        var details = Card("Details", new StackPanel { Children = { Caption("Track name"), _name, PlayedBy(), PictureAndColour(), NotesBox() } });
        left.Children.Add(_cover);
        left.Children.Add(_caption);
        left.Children.Add(details);
        var right = new StackPanel { Children = { InstrumentCard(), DrumCard(), MixerCard(), TuningCard() } };
        var columns = new DockPanel { Margin = new Thickness(16, 16, 16, 0) };
        DockPanel.SetDock(left, Dock.Left);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var footer = Footer();
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = columns, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Content = root;

        _channel.TextChanged += (_, _) => UpdateChannelHint();
        UpdateChannelHint();
        RebuildStrings();
        ShowFamilyCards();
    }

    /// <summary>True after OK passed the field checks and wrote the edits to the track.</summary>
    public bool Applied { get; private set; }

    private Border InstrumentCard()
    {
        var soundRow = Pair("MIDI program", _program, "Channel", _channel, top: 8);
        return Card("Instrument", new StackPanel { Children = { InstrumentChooser(), soundRow, _channelHint } });
    }

    // Drum and keys tracks have no tuning, as in WPF.
    private Border TuningCard()
    {
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        tools.Children.Add(Wide("AllDownButton", "All −1", "Tune every string down a semitone", () => StepAll(-1)));
        tools.Children.Add(Wide("AllUpButton", "All +1", "Tune every string up a semitone", () => StepAll(1)));
        tools.Children.Add(Wide("AddLowStringButton", "+ Low string", "Add a string a fourth below the lowest", () => TrackPropertiesPresenter.WithLowString(_tunings)));
        tools.Children.Add(Wide("RemoveLowStringButton", "− Low string", "Remove the lowest string", () => TrackPropertiesPresenter.WithoutLowString(_tunings)));
        var fretRow = Pair("Frets", _frets, "Capo", _capo, top: 10);
        var card = Card("Tuning", new StackPanel { Children = { PresetRow(), _stringRows, tools, KeepFretsBox(), fretRow } });
        card.Name = "TuningCard";
        _tuningCard = card;
        return card;
    }

    private void StepAll(int delta)
    {
        for (var i = 0; i < _tunings.Count; i++) _tunings[i] = TrackPropertiesPresenter.Step(_tunings[i], delta);
    }

    // Redraws the string rows and the preset box for the working strings.
    private void RebuildStrings()
    {
        RebuildStringRows();
        RefreshPresets();
    }

    // Only the rows: a preset pick runs inside the preset box's own selection change, which must not refill that box.
    private void RebuildStringRows()
    {
        _stringRows.Children.Clear();
        _stringsLabel.Text = $"{_tunings.Count} strings";
        for (var i = 0; i < _tunings.Count; i++) _stringRows.Children.Add(StringRow(i));
    }

    // Alpha of the note chip's fill: the track colour, dimmed, as in the WPF chip.
    private const byte TrackChipTintAlpha = 0x40;

    // Per string: its number, the drawn string (thicker for lower pitches, like wound strings), − / note chip / +.
    private Grid StringRow(int index)
    {
        var row = new Grid { Height = 30, ColumnDefinitions = new ColumnDefinitions("26,*,Auto") };
        var number = new TextBlock { Text = (index + 1).ToString(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        var thickness = TrackPropertiesPresenter.StringThickness(_tunings[index]);
        var line = new global::Avalonia.Controls.Shapes.Rectangle
        {
            Name = $"StringLine{index}", Height = thickness, RadiusX = thickness / 2, RadiusY = thickness / 2, VerticalAlignment = VerticalAlignment.Center,
        };
        line.Fill = AvaloniaPalette.TuningStringBrush(ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);
        var note = new TextBlock
        {
            Name = $"NoteLabel{index}", Text = MusicTheoryService.NoteName(_tunings[index]), FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        note[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextStrong");
        var chip = new Border { Name = $"NoteChip{index}", Width = 52, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(2, 0), BorderThickness = new Thickness(1), Child = note };
        if (ColourHex.TryParseStrict(_colour.Value.Trim(), out var trackColour))
        {
            var track = AvaloniaPalette.ColorOf(trackColour);
            chip.Background = new SolidColorBrush(Color.FromArgb(TrackChipTintAlpha, track.R, track.G, track.B));
            chip.BorderBrush = new SolidColorBrush(track);
        }
        ToolTip.SetTip(chip, $"MIDI {_tunings[index]} - double-click to type a note (e.g. D2, Eb3, F#4)");
        chip.DoubleTapped += (_, e) => { e.Handled = true; TypeNote(chip, index); };
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                StepButton($"StringDown{index}", "−", -1, "Tune this string down a semitone", index),
                chip,
                StepButton($"StringUp{index}", "+", 1, "Tune this string up a semitone", index),
            },
        };
        Grid.SetColumn(line, 1);
        Grid.SetColumn(controls, 2);
        row.Children.Add(number);
        row.Children.Add(line);
        row.Children.Add(controls);
        return row;
    }

    // Double-click on a note chip: type the note name. Enter accepts; Esc or leaving the box cancels (WPF BuildNoteChip).
    internal void TypeNote(Border chip, int index)
    {
        if (chip.Child is TextBox) return;
        var label = chip.Child;
        var box = new TextBox
        {
            Name = $"NoteEntry{index}", Text = MusicTheoryService.NoteName(_tunings[index]), BorderThickness = new Thickness(0), FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 0, Padding = new Thickness(0),
        };
        box[!TextBox.BackgroundProperty] = new DynamicResourceExtension("Transparent");
        box[!TextBox.ForegroundProperty] = new DynamicResourceExtension("TextStrong");
        var finished = false;
        void Finish(bool accept)
        {
            if (finished) return;
            finished = true;
            if (accept && TrackPropertiesPresenter.TryParseNote(box.Text ?? "", out var midi))
            {
                _tunings[index] = System.Math.Clamp(midi, TrackPropertiesPresenter.LowestNote, TrackPropertiesPresenter.HighestNote);
                RebuildStrings();
                return;
            }
            chip.Child = label;
        }
        box.KeyDown += (_, k) =>
        {
            if (k.Key == global::Avalonia.Input.Key.Enter) { Finish(true); k.Handled = true; }
            else if (k.Key == global::Avalonia.Input.Key.Escape) { Finish(false); k.Handled = true; }
        };
        box.LostFocus += (_, _) => Finish(false);
        chip.Child = box;
        box.AttachedToVisualTree += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    private Button StepButton(string name, string text, int delta, string tip, int index)
    {
        var button = new Button { Name = name, Content = text, Width = 26, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => { _tunings[index] = TrackPropertiesPresenter.Step(_tunings[index], delta); RebuildStrings(); };
        return button;
    }

    // A string tool: runs the edit on the working strings, then redraws them.
    private Button Wide(string name, string text, string tip, System.Action edit)
    {
        var button = new Button { Name = name, Content = text, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => { edit(); RebuildStrings(); };
        return button;
    }

    private DockPanel Footer()
    {
        var ok = new Button { Name = "OkButton", Content = "OK", Width = 92, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", Width = 92, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(16, 10, 16, 14), LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        footer.Children.Add(_error);
        return footer;
    }

    private void UpdateChannelHint() => _channelHint.IsVisible = _track.Kind == TrackKind.Drums || Typed(_channel) == "9";

    private void OnOk()
    {
        if ((TrackPropertiesPresenter.FieldError(_program.Text ?? "", _channel.Text ?? "", _frets.Text ?? "", _capo.Text ?? "", _track.IsDrumTrack, _track.IsAudio) ?? ColourError()) is { } fieldError)
        {
            _error.Text = fieldError;
            return;
        }
        var chains = _adding ? TabForge.Avalonia.Views.Fx.FxChainWindows.For(this) : null;
        Applied = true;
        Close();
        // The window is gone first; the edit (one undo step, the relayout it triggers) runs after the frame that hides it.
        var written = new System.Threading.Tasks.TaskCompletionSource();
        _committed = written.Task;
        AvaloniaUiThread.ForApplication().Post(() =>
        {
            try
            {
                if (_adding) chains?.ApplyDefaultChain(_track);
                if (_document is null) Write();
                else DocumentEdits.Run(_document, project => { Write(); if (_adding) project.Tracks.Add(_track); return true; });
            }
            catch (System.Exception ex)
            {
                // Reported, not swallowed: the window is closed by now, so the failure goes to the trace.
                Trace.Error(Trace.Engine, "Track properties: save failed: " + ex.Message);
            }
            written.SetResult();
        }, UiPriority.Background);
    }

    /// <summary>Shows the window modally; returns once it is closed and, after OK, once its edit is written.</summary>
    public async System.Threading.Tasks.Task ShowAndWaitAsync(Window owner)
    {
        await ShowDialog(owner);
        await _committed;
    }

    private void Write()
    {
        _track.Name = TrackPropertiesPresenter.ResolvedName(_name.Text ?? "", _track.Name);
        _track.Performer = (_performer.Text ?? "").Trim();
        _track.TrackNotes = _notes.Text ?? "";
        if (!_track.IsAudio)   // an audio track has no MIDI channel, program, frets or capo: whatever the boxes hold is not written
        {
            _track.MidiProgram = int.Parse(_program.Text!);
            _track.MidiChannel = int.Parse(_channel.Text!);
            _track.NumberOfFrets = int.Parse(_frets.Text!);
            // The capo moves the sounding pitch of the notes (frets are relative to it); the written frets stay.
            TrackSetup.SetCapo(_track, int.Parse(_capo.Text!));
        }
        WriteMix();
        WriteSections();
    }

    /// <summary>What the user changed so far, for the discard question (the WPF labels and arrow).</summary>
    private List<string> PendingChanges()
    {
        var list = new List<string>();
        void Diff(string label, string before, string after)
        {
            if (!string.Equals(before, after, System.StringComparison.Ordinal)) list.Add($"{label}: {before} → {after}");
        }
        Diff("Track name", _track.Name, Typed(_name));
        Diff("MIDI program", _track.MidiProgram.ToString(), Typed(_program));
        Diff("MIDI channel", _track.MidiChannel.ToString(), Typed(_channel));
        Diff("Tuning", string.Join(" ", _track.StringTunings.Select(MusicTheoryService.NoteName)), string.Join(" ", _tunings.Select(MusicTheoryService.NoteName)));
        Diff("Frets", _track.NumberOfFrets.ToString(), Typed(_frets));
        Diff("Capo", _track.Capo.ToString(), Typed(_capo));
        Diff("Volume", _track.Volume.ToString(), ((int)_volumeKnob!.Value).ToString());
        Diff("Pan", _track.Pan.ToString(), ((int)_panKnob!.Value).ToString());
        Diff("Colour", _track.ColorHex, _colour.Value.Trim());
        Diff("Played by", _track.Performer, (_performer.Text ?? "").Trim());
        if (!string.Equals(_track.TrackNotes, _notes.Text ?? "", System.StringComparison.Ordinal)) list.Add("Notes edited");
        return list;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Only the user closing this window asks; the owner or the app closing does not wait on a question.
        if (Applied || _discardConfirmed || e.CloseReason is not WindowCloseReason.WindowClosing) return;
        var changes = PendingChanges();
        if (changes.Count == 0) return;
        e.Cancel = true;
        if (!_askingDiscard) AskDiscard(changes);
    }

    // async void: the question is answered after the close was cancelled; an exception reaches the UI thread's handler instead of vanishing.
    private async void AskDiscard(IReadOnlyList<string> changes)
    {
        _askingDiscard = true;
        PromptButton? answer;
        try
        {
            answer = await DialogLauncher.AskFrom<SavePromptWindow, PromptButton?>(this,
                () => new SavePromptWindow(DiscardQuestion, showCancel: false, details: changes, rememberText: "Don't ask me again"), window => window.Result);
        }
        finally { _askingDiscard = false; }
        if (answer != PromptButton.Yes) return;
        _discardConfirmed = true;
        Close();
    }

    private static string Typed(TextBox box) => (box.Text ?? "").Trim();

    private static TextBox Box(string name, string automationName, string text, double width)
    {
        var box = new TextBox { Name = name, Text = text, Width = width, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, automationName);
        return box;
    }

    private static StackPanel Pair(string firstLabel, TextBox first, string secondLabel, TextBox second, double top) => new()
    {
        Orientation = Orientation.Horizontal, Margin = new Thickness(0, top, 0, 0),
        Children = { Label(firstLabel, 0), first, Label(secondLabel, 16), second },
    };

    private static TextBlock Label(string text, double left) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };

    private static TextBlock Caption(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return hint;
    }

    private static Border Card(string title, Control content)
    {
        var heading = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 2) };
        var card = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { heading, content } },
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return card;
    }
}
