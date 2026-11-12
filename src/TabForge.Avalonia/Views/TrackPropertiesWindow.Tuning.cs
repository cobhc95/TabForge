using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>
/// The Tuning card: the controls column beside the string rows (string count, preset menu, All -1, All +1, Frets) and the keep-frets box below.
/// Removing a string is done with the count box. Every string edit goes through the working strings (TrackTuningDraft) and is written on OK as the same one undo step.
/// Owns: the header controls, the searchable preset flyout (built-ins plus the person's saved tunings, with inline save and rename), the warning before a string with notes is removed and the warning before OK
///     when a later retune makes more removed-string notes change pitch; a pending answer is ignored when a later edit or a close won.
/// Does not own: the string rows (TrackPropertiesWindow.Strings.cs), the edit list (TrackTuningDraft), the preset list rules (TuningPresetMenu) or the preset data (TuningPresets).
/// Tests: TabForge.Avalonia.Tests.TrackPropertiesWindowTests, TrackPropertiesWindowConcurrencyTests.
/// </summary>
public sealed partial class TrackPropertiesWindow
{
    private ComboBox _countBox = null!;
    private Button _presetButton = null!;
    private Flyout _presetFlyout = null!;
    private TextBox _presetSearch = null!;
    private CheckBox _presetAllCounts = null!;
    private StackPanel _presetList = null!;
    private Button _saveShowButton = null!;
    private StackPanel _saveEditor = null!;
    private TextBox _saveNameBox = null!;
    private TextBlock _saveError = null!;
    private bool _saveEditing;
    private string? _renameTarget;
    private string? _renameDraft;
    private string? _renameError;
    private TextBox? _renameBox;
    private readonly Func<AppSettings>? _settingsGetter;
    private readonly Action? _saveSettings;
    private readonly List<TuningUserPreset> _memoryUsers = new();
    private bool _syncingHeader;
    private int _pendingTuningDialogs;
    private readonly HashSet<TabNote> _disclosedUnplayable = new();

    /// <summary>Asks whether the notes of a string about to be removed move to other strings; given the notes at stake. Replaceable so tests need no dialog.</summary>
    internal Func<TuningRemovalImpact, Task<bool>> ConfirmNoteMove { get; set; }

    /// <summary>Asks before OK when retuning after a removal makes notes change pitch that the removal question did not mention. Replaceable so tests need no dialog.</summary>
    internal Func<TuningRemovalImpact, Task<bool>> ConfirmUndisclosedPitchChange { get; set; }

    private bool IsBass => _track.Kind == TrackKind.Bass;

    private AppSettings? CurrentSettings() => _settingsGetter?.Invoke();

    private List<TuningUserPreset> LoadUsers()
    {
        var settings = CurrentSettings();
        return settings is null ? _memoryUsers : TuningUserSettings.LoadUserEntries(settings);
    }

    private IReadOnlyList<TuningMenuEntry> CurrentUserEntries() => TuningPresetMenu.UserEntries(LoadUsers());

    private void PersistUsers(List<TuningUserPreset> users)
    {
        var settings = CurrentSettings();
        if (settings is null) return;
        settings.TuningUserPresets = TuningUserSettings.ToEntries(users);
        _saveSettings?.Invoke();
    }

    private string NextMyTuningName()
    {
        var taken = new HashSet<string>(TuningUserRules.Names(LoadUsers()), StringComparer.OrdinalIgnoreCase);
        var builtIn = new HashSet<string>(TuningPresets.Catalog.Select(p => p.Preset.Name), StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = $"My tuning {n}";
            if (!taken.Contains(candidate) && !builtIn.Contains(candidate)) return candidate;
        }
    }

    // Drum and keys tracks have no tuning. The controls column (count, preset, All -1 / All +1, Frets) sits beside the string rows; the keep-frets box is full width below.
    private Border TuningCard()
    {
        _countBox = new ComboBox { Name = "StringCountBox", HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_countBox, "String count");
        for (var n = TrackPropertiesPresenter.FewestStrings; n <= TrackPropertiesPresenter.MostStrings; n++) _countBox.Items.Add($"{n} strings");
        _countBox.SelectionChanged += (_, _) => { if (!_syncingHeader) ChangeCount(TrackPropertiesPresenter.FewestStrings + _countBox.SelectedIndex); };
        _presetButton = new Button { Name = "TuningPresetButton", MinWidth = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningCountBoxWidth, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Flyout = BuildPresetFlyout(), HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(_presetButton, "Tuning preset");
        ToolTip.SetTip(_presetButton, "Pick a tuning preset; editing a string makes it Custom");
        var all = new UniformGrid { Columns = 2 };
        foreach (var step in new[] { HeaderButton("AllDownButton", "All −1", "Tune every string down a semitone", () => { _draft.StepAll(-1); ShowAllRows(); }), HeaderButton("AllUpButton", "All +1", "Tune every string up a semitone", () => { _draft.StepAll(1); ShowAllRows(); }) })
        {
            step.HorizontalAlignment = HorizontalAlignment.Stretch;
            step.HorizontalContentAlignment = HorizontalAlignment.Center;
            var arrow = step.Name == "AllDownButton" ? DownArrow : UpArrow;
            step.ContentTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<object>((text, _) => ArrowContent(arrow, text as string ?? ""));
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(step, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningHeaderPartGap);
            all.Children.Add(step);
        }
        var frets = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("Frets", 0), _frets } };
        var controls = new StackPanel { Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningControlsWidth, Children = { _countBox, _presetButton, all, frets } };
        foreach (var part in new Control[] { _countBox, _presetButton, all }) global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(part, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningControlGap);
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(controls, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningColumnGap);
        var columns = new DockPanel();
        DockPanel.SetDock(controls, Dock.Left);
        columns.Children.Add(controls);
        columns.Children.Add(_stringRows);
        var card = Card("Tuning", new StackPanel { Children = { columns, KeepFretsBox() } });
        card.Name = "TuningCard";
        _tuningCard = card;
        return card;
    }


    private const string DownArrow = "M 6,1 L 6,11 M 2,7 L 6,11 L 10,7", UpArrow = "M 6,11 L 6,1 M 2,5 L 6,1 L 10,5";

    /// <summary>A retune button's look: its arrow in the accent colour, then its label. The label stays the button's content.</summary>
    private static StackPanel ArrowContent(string arrow, string text)
    {
        var glyph = new global::Avalonia.Controls.Shapes.Path
        {
            Data = global::Avalonia.Media.Geometry.Parse(arrow), Stretch = global::Avalonia.Media.Stretch.None,
            StrokeLineCap = global::Avalonia.Media.PenLineCap.Round, StrokeJoin = global::Avalonia.Media.PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.Width = glyph.Height = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningArrowSize;
        glyph.StrokeThickness = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningArrowStroke;
        glyph[!global::Avalonia.Controls.Shapes.Shape.StrokeProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AccentBrush");
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, IsHitTestVisible = false,
            Children = { glyph, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
        };
    }

    private static Button HeaderButton(string name, string text, string tip, Action click)
    {
        var button = new Button { Name = name, Content = text, VerticalAlignment = VerticalAlignment.Center };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(button, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    // The count box and the preset label follow the working strings.
    private void SyncTuningHeader()
    {
        _syncingHeader = true;
        _countBox.SelectedIndex = _draft.Count - TrackPropertiesPresenter.FewestStrings;
        _syncingHeader = false;
        _presetButton.Content = TuningPresetMenu.Label(_draft.Tunings, IsBass, CurrentUserEntries()) + " ▾";
    }

    private void ChangeCount(int count) => Platform.UiTasks.Forget(EditStructure(_draft.SlotsDroppedBy(count), draft => draft.ResizeTo(count)), "Changing the string count", text => _error.Text = text);

    // A change in how many strings there are. The edit runs on a copy, so the warning is judged against the final strings (a preset can retune the kept strings too);
    // strings that hold notes ask first (Yes moves notes and keeps the copy, No leaves the open draft as it was); nothing is dropped silently.
    // With no notes at stake the copy replaces the draft at once, without waiting.
    private async Task EditStructure(IReadOnlyList<int> dropped, Action<TrackTuningDraft> edit)
    {
        var signature = DraftSignature();
        var origins = dropped.Where(s => s >= 0 && s < _draft.Count).Select(s => _draft.Origin[s]).Where(o => o >= 0).ToHashSet();
        var next = _draft.Clone();
        edit(next);
        var impact = next.NotesAtStakeForOrigins(_track, origins);
        if (impact.Total > 0)
        {
            bool keep;
            _pendingTuningDialogs++;
            try { keep = await ConfirmNoteMove(impact); }
            finally { _pendingTuningDialogs--; }
            // A later edit (or a close) wins: the answer to the old question must not replace it.
            if (_closed) return;
            if (signature != DraftSignature()) { RebuildStrings(); return; }
            if (!keep) { RebuildStrings(); return; }
        }
        foreach (var note in impact.UnplayableNotes) _disclosedUnplayable.Add(note);
        _draft = next;
        RebuildStrings();
    }

    // The working strings that a pending question is about; any later edit changes it, so the stale answer is ignored.
    private string DraftSignature() => string.Join(",", _draft.Origin) + ":" + string.Join(",", _draft.Tunings);

    private Task<bool> AskMoveNotes(TuningRemovalImpact impact) =>
        AskYesNo(TrackPropertiesPresenter.RemovalWarningText(impact.Total, impact.Unplayable), "Remove string");

    private Task<bool> AskUndisclosedPitchChange(TuningRemovalImpact impact) =>
        AskYesNo(TrackPropertiesPresenter.UndisclosedPitchChangeWarningText(impact.Unplayable), "Apply tuning change");

    private async Task<bool> AskYesNo(string text, string caption) =>
        await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes;

    // Before OK writes anything: the old removal question may have been answered before later retuning made more removed-string notes unplayable.
    // Only notes not already disclosed ask again; No leaves the window open with the draft and the song untouched.
    private async Task<bool> ConfirmUnannouncedPitchChanges()
    {
        while (true)
        {
            var signature = DraftSignature();
            var impact = _draft.RemovedNotesAtStake(_track);
            var newly = impact.UnplayableNotes.Where(note => !_disclosedUnplayable.Contains(note)).ToList();
            if (newly.Count == 0) return true;
            if (!await ConfirmUndisclosedPitchChange(new TuningRemovalImpact(impact.Total, newly))) return false;
            // A close, another open question or a later tuning edit means this answer is stale: judge the final draft again.
            if (_closed) return false;
            if (_pendingTuningDialogs > 0) return false;
            if (signature == DraftSignature())
            {
                foreach (var note in newly) _disclosedUnplayable.Add(note);
                return true;
            }
        }
    }

    /// <summary>Takes a preset from the menu: its strings replace the working ones (a preset with another string count resizes at the low end).</summary>
    internal void ApplyPreset(TuningMenuEntry preset) => Platform.UiTasks.Forget(EditStructure(_draft.SlotsDroppedBy(preset.Strings), draft => draft.Apply(preset.HighToLow)), "Applying the tuning preset", text => _error.Text = text);

    // The preset menu: a search box, the all-counts switch, the presets grouped by instrument, and the save row for the person's own tunings.
    private Flyout BuildPresetFlyout()
    {
        _presetSearch = new TextBox { Name = "PresetSearchBox", Watermark = "Search tunings" };
        AutomationProperties.SetName(_presetSearch, "Search tunings");
        _presetAllCounts = new CheckBox { Name = "PresetAllCountsBox", Content = "Show all string counts" };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(_presetAllCounts, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetAllCountsGap);
        _presetList = new StackPanel { Name = "PresetList" };
        _presetSearch.TextChanged += (_, _) => FillPresetList();
        _presetAllCounts.IsCheckedChanged += (_, _) => FillPresetList();
        var scroller = new ScrollViewer { MaxHeight = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetListHeight, Content = _presetList };
        _saveShowButton = new Button { Name = "SavePresetButton", Content = "Save current tuning…", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(_saveShowButton, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(_saveShowButton, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetAllCountsGap);
        AutomationProperties.SetName(_saveShowButton, "Save current tuning");
        ToolTip.SetTip(_saveShowButton, "Save the current tuning as your own preset");
        _saveShowButton.Click += (_, _) => ShowSaveEditor();
        _saveNameBox = new TextBox { Name = "SavePresetNameBox", Watermark = "Preset name" };
        AutomationProperties.SetName(_saveNameBox, "Preset name");
        var saveSave = new Button { Name = "SavePresetSave", Content = "Save", VerticalAlignment = VerticalAlignment.Center };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(saveSave, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        saveSave.Click += (_, _) => CommitSave();
        var saveCancel = new Button { Name = "SavePresetCancel", Content = "Cancel", VerticalAlignment = VerticalAlignment.Center };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(saveCancel, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(saveCancel, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningHeaderPartGap);
        saveCancel.Click += (_, _) => HideSaveEditor();
        var saveButtons = new StackPanel { Orientation = Orientation.Horizontal, Children = { saveSave, saveCancel } };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(saveButtons, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
        _saveError = new TextBlock { Name = "SavePresetError", TextWrapping = TextWrapping.Wrap };
        _saveError[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(_saveError, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
        _saveEditor = new StackPanel { Name = "SavePresetEditor", Children = { _saveNameBox, saveButtons, _saveError }, IsVisible = false };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(_saveNameBox, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
        var content = new StackPanel { Width = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetMenuWidth, Children = { _presetSearch, _presetAllCounts, scroller, _saveShowButton, _saveEditor } };
        _presetFlyout = new Flyout { Content = content };
        _presetFlyout.Opened += (_, _) => { _presetSearch.Text = ""; _saveEditing = false; _renameTarget = null; _renameDraft = null; _renameError = null; SyncSaveEditor(); FillPresetList(); _presetSearch.Focus(); };
        return _presetFlyout;
    }

    private void SyncSaveEditor() => _saveEditor.IsVisible = _saveEditing;

    private void ShowSaveEditor()
    {
        _saveEditing = true;
        _saveNameBox.Text = NextMyTuningName();
        _saveError.Text = "";
        SyncSaveEditor();
        _saveNameBox.Focus();
    }

    private void HideSaveEditor()
    {
        _saveEditing = false;
        _saveError.Text = "";
        SyncSaveEditor();
    }

    private void CommitSave()
    {
        var users = LoadUsers();
        var name = _saveNameBox.Text ?? "";
        if (!TuningUserRules.TrySave(users, name, _draft.Tunings, out var error))
        {
            _saveError.Text = error ?? "";
            return;
        }
        PersistUsers(users);
        HideSaveEditor();
        FillPresetList();
        SyncTuningHeader();
    }

    private void BeginRename(string name)
    {
        _renameTarget = name;
        _renameDraft = name;
        _renameError = null;
        FillPresetList();
    }

    private void CancelRename()
    {
        _renameTarget = null;
        _renameDraft = null;
        _renameError = null;
        _renameBox = null;
        FillPresetList();
    }

    private void CommitRename()
    {
        if (_renameTarget is null) return;
        var users = LoadUsers();
        var name = _renameBox?.Text ?? _renameDraft ?? "";
        if (!TuningUserRules.TryRename(users, _renameTarget, name, out var error))
        {
            _renameError = error ?? "";
            _renameDraft = name;
            FillPresetList();
            return;
        }
        PersistUsers(users);
        _renameTarget = null;
        _renameDraft = null;
        _renameError = null;
        _renameBox = null;
        FillPresetList();
        SyncTuningHeader();
    }

    private void DeleteUser(string name)
    {
        var users = LoadUsers();
        if (!TuningUserRules.Delete(users, name)) return;
        PersistUsers(users);
        if (string.Equals(_renameTarget, name, StringComparison.OrdinalIgnoreCase))
        {
            _renameTarget = null;
            _renameDraft = null;
            _renameError = null;
        }
        FillPresetList();
        SyncTuningHeader();
    }

    internal void FillPresetList()
    {
        _presetList.Children.Clear();
        var all = _presetAllCounts.IsChecked == true;
        var users = CurrentUserEntries();
        var groups = TuningPresetMenu.Groups(_presetSearch.Text ?? "", _draft.Count, all, IsBass, users);
        if (groups.Count == 0)
        {
            var none = new TextBlock { Name = "PresetNone", Text = "No tunings match", Opacity = 0.7 };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(none, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
            _presetList.Children.Add(none);
        }
        foreach (var group in groups)
        {
            var header = new TextBlock { Text = group.Name.ToUpperInvariant(), FontSize = global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningGroupHeaderFontSize, FontWeight = FontWeight.SemiBold, Opacity = 0.75 };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(header, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningGroupHeaderGap);
            _presetList.Children.Add(header);
            foreach (var entry in group.Entries)
            {
                if (string.Equals(group.Name, TuningPresetMenu.MyTuningsGroup, StringComparison.Ordinal)) _presetList.Children.Add(BuildUserRow(entry, all));
                else
                {
                    var item = new Button { Name = "PresetItem", Tag = entry, Content = entry.Text(all), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                    item.Click += (_, _) => { _presetFlyout.Hide(); ApplyPreset(entry); };
                    _presetList.Children.Add(item);
                }
            }
        }
    }

    // One of the person's rows: the preset applies on click, with instant rename and delete beside it; the row being renamed edits inline.
    private Control BuildUserRow(TuningMenuEntry entry, bool showCount)
    {
        if (string.Equals(_renameTarget, entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            var box = new TextBox { Name = "RenamePresetBox", Text = _renameDraft ?? entry.Name };
            _renameBox = box;
            AutomationProperties.SetName(box, "Rename tuning");
            box.TextChanged += (_, _) => { _renameDraft = box.Text ?? ""; };
            var save = new Button { Name = "RenamePresetSave", Content = "Save", VerticalAlignment = VerticalAlignment.Center };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(save, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
            save.Click += (_, _) => CommitRename();
            var cancel = new Button { Name = "RenamePresetCancel", Content = "Cancel", VerticalAlignment = VerticalAlignment.Center };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(cancel, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(cancel, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningHeaderPartGap);
            cancel.Click += (_, _) => CancelRename();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { save, cancel } };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(row, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
            var error = new TextBlock { Name = "RenamePresetError", Text = _renameError ?? "", TextWrapping = TextWrapping.Wrap };
            error[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Danger");
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(error, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
            var editor = new StackPanel { Name = "UserPresetEditor", Children = { box } };
            global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(box, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningPresetNoneInset);
            editor.Children.Add(row);
            if (!string.IsNullOrEmpty(_renameError)) editor.Children.Add(error);
            return editor;
        }
        var apply = new Button { Name = "PresetItem", Tag = entry, Content = entry.Text(showCount), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        apply.Click += (_, _) => { _presetFlyout.Hide(); ApplyPreset(entry); };
        AutomationProperties.SetName(apply, entry.Name);
        var rename = new Button { Name = "RenamePresetButton", Tag = entry, Content = "✎", VerticalAlignment = VerticalAlignment.Center };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(rename, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        AutomationProperties.SetName(rename, "Rename " + entry.Name);
        ToolTip.SetTip(rename, "Rename this tuning");
        rename.Click += (_, _) => BeginRename(entry.Name);
        var delete = new Button { Name = "DeletePresetButton", Tag = entry, Content = "🗑", VerticalAlignment = VerticalAlignment.Center };
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithPadding(delete, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningButtonPadding);
        global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.WithMargin(delete, global::TabForge.Avalonia.Views.Dialogs.DialogMetrics.TrackTuningHeaderPartGap);
        AutomationProperties.SetName(delete, "Delete " + entry.Name);
        ToolTip.SetTip(delete, "Delete this tuning");
        delete.Click += (_, _) => DeleteUser(entry.Name);
        var panel = new DockPanel { Name = "UserPresetRow", LastChildFill = true };
        DockPanel.SetDock(delete, Dock.Right);
        DockPanel.SetDock(rename, Dock.Right);
        panel.Children.Add(delete);
        panel.Children.Add(rename);
        panel.Children.Add(apply);
        return panel;
    }
}
