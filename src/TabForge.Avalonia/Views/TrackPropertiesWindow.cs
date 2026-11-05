using System;
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
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views;

/// <summary>
/// Track properties for a MIDI track, the track properties dialog (the v7 layout: left column Details, Mixer; right column Instrument beside MIDI, Tuning, Capo): the details card
/// (track name, colour and tint), the instrument card (the Change button runs the track row icon's picker, InstrumentPickerFlow, whose pick is kept with no step and committed with everything else on OK; bank select and channel are the MIDI card), the tuning card (strings, string tools, keep frets,
/// frets and capo) or the drum notation card, and the OK / Cancel footer. OK is one undo step through DocumentEdits.Run when a document is given.
/// The window edits working copies; OK writes the track only when every typed field is in range, else it shows the field's message and stays open.
/// Closing with edits pending (Cancel, Esc, the close box) asks the discard question; Yes closes, No returns to editing.
/// Owns: the controls, layout and the working copies; the list of pending changes for the discard question.
/// Does not own: the rules and texts (TrackPropertiesPresenter), the capo shift and retune (TrackSetup), the question window (SavePromptWindow);
///     the colour, keep-frets and drum sections are in TrackPropertiesWindow.Sections.cs.
/// Not a full twin: the FX button in the Mixer card,
///     the custom drum map editor, the typed note on a string chip, the Add track position row and the audio track variant.
/// Deviation: a string chip is the palette
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
    private TrackTuningDraft _draft;
    private readonly TextBox _name, _performer, _notes, _program, _bankMsb, _bankLsb, _channel, _frets;
    private readonly TextBlock _channelHint;
    private readonly TextBlock _error = new TextBlock { Name = "ErrorText", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }.WithMargin(DialogMetrics.TrackErrorGap);
    private bool _discardConfirmed, _askingDiscard, _committing, _closed, _pickerRunning;
    private readonly bool _adding;
    private System.Threading.Tasks.Task _committed = System.Threading.Tasks.Task.CompletedTask;
    private readonly InstrumentPickerFlow _picker;
    // The song as the window opened it (a copy, never in the song): the discard question compares the
    // working copies against it, Cancel restores the instrument previews to it, and OK restores first so
    // its one step runs from the original. The picker previews mutate the real track live (with sound but
    // no undo step); only OK writes the working copies, as one step.
    private readonly TrackModel _original;
    // The instrument the picker kept last (null: no pick yet): re-applied inside OK's one step after the restore.
    private string? _pendingInstrument;

    /// <summary>
    /// <paramref name="document"/> holds <paramref name="track"/>; with it OK is one undo step, without it (the preview) OK writes the track directly.
    /// <paramref name="adding"/>: the track is new (the Add track window), so OK also adds it at the end of the song.
    /// <paramref name="picker"/>: how the Change instrument button sounds and syncs the choice (none: silent).
    /// <paramref name="ringer"/>: how a string rings through the track's sound (none: silent; the host wires the playback engine).
    /// <paramref name="settingsGetter"/>: the settings whose TuningUserPresets keep the person's tunings; null keeps them in this window only.
    /// <paramref name="saveSettings"/>: writes the settings after a tuning preset is saved, renamed or deleted; null writes nothing.
    /// </summary>
    public TrackPropertiesWindow(TrackModel track, IEditableDocument? document = null, bool adding = false, InstrumentPickerFlow? picker = null, IStringRinger? ringer = null, Func<AppSettings>? settingsGetter = null, Action? saveSettings = null)
    {
        _track = track;
        _picker = picker ?? new InstrumentPickerFlow();
        _document = document;
        _adding = adding;
        _settingsGetter = settingsGetter;
        _saveSettings = saveSettings;
        _original = SnapshotTrack(track);
        _draft = new TrackTuningDraft(track.StringTunings);
        _capoFret = track.Capo;
        _ringer = ringer;
        ConfirmNoteMove = AskMoveNotes;
        ConfirmUndisclosedPitchChange = AskUndisclosedPitchChange;
        Title = TrackPropertiesPresenter.Title(track.Name, adding);
        Width = 800;
        SizeToContent = SizeToContent.Height;
        MinWidth = 640;
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
        _program = Box("ProgramBox", "MIDI program", track.MidiProgram.ToString(), double.NaN);
        _bankMsb = Box("BankMsbBox", "Bank MSB (-1 is off)", track.MidiBankMsb.ToString(), double.NaN);
        _bankLsb = Box("BankLsbBox", "Bank LSB (-1 is off)", track.MidiBankLsb.ToString(), double.NaN);
        _channel = Box("ChannelBox", "Channel", track.MidiChannel.ToString(), double.NaN);
        _frets = Box("FretsBox", "Frets", track.NumberOfFrets.ToString(), 50);
        _channelHint = Hint("Channel 9 (the 10th) is the General MIDI drum channel.");
        _channelHint.Name = "ChannelHint";
        _error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");

        var left = new StackPanel { Width = DialogMetrics.TrackLeftColumnWidth }.WithMargin(DialogMetrics.TrackLeftColumnGap);
        var details = Card("Details", new StackPanel { Children = { Caption("Track name"), _name, PlayedBy(), PictureAndColour(), NotesBox() } });
        left.Children.Add(details);
        left.Children.Add(MixerCard());
        var right = new StackPanel { Children = { InstrumentRow(), DrumCard(), TuningCard(), CapoCard(), Footer() } };
        var columns = new DockPanel().WithMargin(DialogMetrics.TrackColumnsInset);
        DockPanel.SetDock(left, Dock.Left);
        columns.Children.Add(left);
        columns.Children.Add(right);

        // Keep the actions directly below the instrument cards, beside (not below) the taller
        // Details/Mixer column, so that column doesn't add an empty full-width footer row.
        Content = new ScrollViewer { Content = columns, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        _channel.TextChanged += (_, _) => UpdateChannelHint();
        UpdateChannelHint();
        RebuildStrings();
        ShowFamilyCards();
    }

    /// <summary>True after OK passed the field checks and wrote the edits to the track.</summary>
    public bool Applied { get; private set; }

    // Row 1 of the right column: the Instrument card (picture, name, Change...) beside the MIDI card (program, banks, channel).
    private Grid InstrumentRow()
    {
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(1.2, GridUnitType.Star) } };
        var instrument = Card("Instrument", InstrumentChooser());
        var midi = MidiCard();
        DialogMetrics.WithMargin(instrument, DialogMetrics.TrackInstrumentCardGap);
        Grid.SetColumn(midi, 1);
        row.Children.Add(instrument);
        row.Children.Add(midi);
        return row;
    }

    // The MIDI card: program, bank select and channel in one row of small boxes.
    private Border MidiCard()
    {
        ToolTip.SetTip(_bankMsb, "Bank select MSB (CC0): –1 is off, else sent before the program change");
        ToolTip.SetTip(_bankLsb, "Bank select LSB (CC32): –1 is off, else sent before the program change");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        var cells = new (string Label, TextBox Box)[] { ("Program", _program), ("Bank MSB", _bankMsb), ("Bank LSB", _bankLsb), ("Channel", _channel) };
        for (var i = 0; i < cells.Length; i++)
        {
            var (label, box) = cells[i];
            box.MinHeight = box.Height = DialogMetrics.TrackMidiBoxHeight;
            box.FontSize = DialogMetrics.TrackMidiFontSize;
            DialogMetrics.WithPadding(box, DialogMetrics.TrackMidiBoxPadding);
            var caption = new TextBlock { Text = label, FontSize = DialogMetrics.TrackMidiFontSize };
            DialogMetrics.WithMargin(caption, DialogMetrics.TrackMidiLabelGap);
            var cell = new StackPanel { Children = { caption, box } };
            DialogMetrics.WithMargin(cell, DialogMetrics.TrackMidiCellGap);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        var card = Card("MIDI", new StackPanel { Children = { grid, _channelHint } });
        card.Name = "MidiCard";
        return card;
    }

    private DockPanel Footer()
    {
        var ok = new Button { Name = "OkButton", Content = "OK", Width = 92, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Name = "CancelButton", Content = "Cancel", Width = 92, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(12, 6, 12, 8), LastChildFill = true };
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        footer.Children.Add(_error);
        return footer;
    }

    private void UpdateChannelHint() => _channelHint.IsVisible = _track.Kind == TrackKind.Drums || Typed(_channel) == "9";

    private async void OnOk()
    {
        // A question or the instrument picker still owns the working copies; OK must not commit behind it.
        if (_committing || _pendingTuningDialogs > 0 || _pickerRunning || _askingDiscard) return;
        _committing = true;
        try
        {
            if ((TrackPropertiesPresenter.FieldError(_program.Text ?? "", _bankMsb.Text ?? "", _bankLsb.Text ?? "", _channel.Text ?? "", _frets.Text ?? "", _capoFret.ToString(), _track.IsDrumTrack, _track.IsAudio) ?? ColourError()) is { } fieldError)
            {
                _error.Text = fieldError;
                return;
            }
            // The final draft decides, not the tuning when the string was removed: a later retune may have made more notes unplayable.
            if (!await ConfirmUnannouncedPitchChanges() || _closed || _askingDiscard) return;
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
                    else if (_adding) DocumentEdits.Run(_document, project => { Write(); project.Tracks.Add(_track); return true; });
                    else
                    {
                        // One step from the window-open song: drop the live previews first, then apply the kept
                        // instrument (if any) and every working copy together. Unrelated tracks are untouched.
                        var pending = _pendingInstrument;
                        RestoreInstrumentFields();
                        DocumentEdits.Run(_document, project =>
                        {
                            if (pending is not null && !string.Equals(pending, _original.InstrumentName, System.StringComparison.OrdinalIgnoreCase))
                                AudioToInstrumentConversion.ApplyInstrument(project, _track, pending);
                            Write();
                            return true;
                        });
                    }
                }
                catch (System.Exception ex)
                {
                    // Reported, not swallowed: the window is closed by now, so the failure goes to the trace.
                    Trace.Error(Trace.Engine, "Track properties: save failed: " + ex.Message);
                }
                written.SetResult();
            }, UiPriority.Background);
        }
        finally { _committing = false; }
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
        if (!_track.IsAudio)   // an audio track has no MIDI channel, program, bank, frets or capo: whatever the boxes hold is not written
        {
            _track.MidiProgram = int.Parse(_program.Text!);
            _track.MidiBankMsb = int.Parse(_bankMsb.Text!);
            _track.MidiBankLsb = int.Parse(_bankLsb.Text!);
            _track.MidiChannel = int.Parse(_channel.Text!);
            _track.NumberOfFrets = int.Parse(_frets.Text!);
            // The capo moves the sounding pitch of the notes (frets are relative to it); the written frets stay.
            // Full and partial capos are written in one pass, so every note moves by its own string's capo change.
            TrackSetup.SetCapoAndPartials(_track, _capoFret, CurrentPartialModels(), _draft.Count);
        }
        WriteMix();
        WriteSections();
        if (_track.PartialCapos is not null) _track.PartialCapos = PartialCapoRules.Normalize(_track.PartialCapos, _track.StringTunings.Count);
        TrackColouring.FollowInstrument(_track);   // still on the default colour: it follows a changed program
    }

    /// <summary>What the user changed so far, for the discard question: every working copy against the window-open song.</summary>
    private List<string> PendingChanges()
    {
        var list = new List<string>();
        void Diff(string label, string before, string after)
        {
            if (!string.Equals(before, after, System.StringComparison.Ordinal)) list.Add($"{label}: {before} → {after}");
        }
        Diff("Track name", _original.Name, Typed(_name));
        Diff("MIDI program", _original.MidiProgram.ToString(), Typed(_program));
        Diff("Bank MSB", _original.MidiBankMsb.ToString(), Typed(_bankMsb));
        Diff("Bank LSB", _original.MidiBankLsb.ToString(), Typed(_bankLsb));
        Diff("MIDI channel", _original.MidiChannel.ToString(), Typed(_channel));
        Diff("Tuning", string.Join(" ", _original.StringTunings.Select(MusicTheoryService.NoteName)), string.Join(" ", _draft.Tunings.Select(MusicTheoryService.NoteName)));
        Diff("Frets", _original.NumberOfFrets.ToString(), Typed(_frets));
        Diff("Capo", _original.Capo.ToString(), _capoFret.ToString());
        if (!PartialCapoRules.Same(_original.PartialCapos, CurrentPartialModels()))
            list.Add($"Partial capos: {PartialCapoRules.Describe(_original.PartialCapos)} → {PartialCapoRules.Describe(CurrentPartialModels())}");
        Diff("Volume", _original.Volume.ToString(), ((int)_volumeKnob!.Value).ToString());
        Diff("Pan", _original.Pan.ToString(), ((int)_panKnob!.Value).ToString());
        Diff("Colour", TabForge.Platform.Theme.NamedColourSwatches.LabelOf(_original.ColorHex), TabForge.Platform.Theme.NamedColourSwatches.LabelOf(_colour.Value));
        if (_original.TintRow != (_tint.IsChecked == true))
            list.Add($"Tint: {(_original.TintRow ? "On" : "Off")} → {(_tint.IsChecked == true ? "On" : "Off")}");
        if (!string.Equals(_original.InstrumentName, _track.InstrumentName, System.StringComparison.OrdinalIgnoreCase))
            list.Add($"Instrument: {_original.InstrumentName} → {_track.InstrumentName}");
        var drumBefore = CurrentDrumPreset();
        // The working drum preset lives in the box; the original lives in the snapshot.
        if (!string.Equals(_original.DrumMapPreset, drumBefore, System.StringComparison.Ordinal) || IsCustomDrumEdited())
            list.Add($"Drum notation: {DrumMaps.DisplayName(_original.DrumMapPreset)} → {DrumMaps.DisplayName(drumBefore)}");
        Diff("Played by", _original.Performer, (_performer.Text ?? "").Trim());
        if (!string.Equals(_original.TrackNotes, _notes.Text ?? "", System.StringComparison.Ordinal)) list.Add("Notes edited");
        return list;
    }

    private bool IsCustomDrumEdited()
    {
        if (!string.Equals(CurrentDrumPreset(), DrumMaps.Custom, System.StringComparison.Ordinal)) return false;
        return !DrumMapsEqual(_original.CustomDrumMap, _customMap);
    }

    private static bool DrumMapsEqual(List<DrumMapEntry>? a, List<DrumMapEntry>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Midi != b[i].Midi || a[i].TabLine != b[i].TabLine || a[i].StaffStep != b[i].StaffStep
                || !string.Equals(a[i].Label, b[i].Label, System.StringComparison.Ordinal)
                || !string.Equals(a[i].Head, b[i].Head, System.StringComparison.Ordinal))
                return false;
        return true;
    }

    // A copy of the track as the window opened it: never in the song, only the discard question and the Cancel/OK restore read it.
    private static TrackModel SnapshotTrack(TrackModel track) => new()
    {
        Id = track.Id,
        Name = track.Name,
        Kind = track.Kind,
        ColorHex = track.ColorHex,
        ColorFollowsInstrument = track.ColorFollowsInstrument,
        InstrumentName = track.InstrumentName,
        DrumMapPreset = track.DrumMapPreset,
        CustomDrumMap = track.CustomDrumMap?.Select(e => e.Clone()).ToList(),
        Performer = track.Performer,
        TrackNotes = track.TrackNotes,
        NumberOfFrets = track.NumberOfFrets,
        MidiChannel = track.MidiChannel,
        MidiProgram = track.MidiProgram,
        MidiBankMsb = track.MidiBankMsb,
        MidiBankLsb = track.MidiBankLsb,
        Capo = track.Capo,
        PartialCapos = PartialCapoRules.Copy(track.PartialCapos),
        Volume = track.Volume,
        Pan = track.Pan,
        TintRow = track.TintRow,
        StringTunings = track.StringTunings.ToList(),
        Measures = track.Measures.Select(m => m.Clone()).ToList(),
        Rig = new RigPreset { Name = track.Rig.Name, ArticulationMap = track.Rig.ArticulationMap },
    };

    // Back to the window-open instrument state (what the live previews may have changed): no undo step, the redo stack untouched.
    private void RestoreInstrumentFields()
    {
        _track.InstrumentName = _original.InstrumentName;
        _track.MidiProgram = _original.MidiProgram;
        _track.MidiChannel = _original.MidiChannel;
        _track.Capo = _original.Capo;
        _track.PartialCapos = PartialCapoRules.Copy(_original.PartialCapos);
        _track.Kind = _original.Kind;
        _track.ColorHex = _original.ColorHex;
        _track.ColorFollowsInstrument = _original.ColorFollowsInstrument;
        _track.StringTunings = _original.StringTunings.ToList();
        _track.Measures = _original.Measures.Select(m => m.Clone()).ToList();
        _track.Rig.Name = _original.Rig.Name;
        _track.Rig.ArticulationMap = _original.Rig.ArticulationMap;
        _track.DrumMapPreset = _original.DrumMapPreset;
        _track.CustomDrumMap = _original.CustomDrumMap?.Select(e => e.Clone()).ToList();
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
        if (!Applied) RestoreInstrumentFields();   // discard every live instrument preview: the song is untouched
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

    private static TextBlock Label(string text, double left) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };

    private static TextBlock Caption(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = ChromeMetrics.TextSmall, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMuted");
        return hint;
    }

    private static Border Card(string title, Control content) => Card(Heading(title), content);

    private static TextBlock Heading(string title) =>
        new() { Text = title.ToUpperInvariant(), FontSize = ChromeMetrics.TextSmall, FontWeight = FontWeight.SemiBold, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };

    // A card whose heading is a control (the Tuning card's header row) or a plain title, then the content.
    private static Border Card(Control heading, Control content)
    {
        heading.Margin = new Thickness(0, 0, 0, 1);
        var card = new Border
        {
            CornerRadius = new CornerRadius(ChromeMetrics.RadiusLarge), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { heading, content } },
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SoftBorder");
        return card;
    }
}
