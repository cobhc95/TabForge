using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, file commands: new, open, save, export, print.
public partial class MainWindow
{
    // ---------- file ----------

    private void New_Click(object sender, RoutedEventArgs e)
    {
        NewTab_Click(sender, e);
        StatusText.Text = "New score";
    }

    private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = GpDialogs.PickTemplate(UserTemplates.List());
        if (name is null) return;
        SongProject template;
        try { template = UserTemplates.Create(name); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open the template \"{name}\".\n\n{ex.Message}", "New from template", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var doc = DocumentSession.FromProject(template, null);
        ApplyPreferredScoreView(doc);
        doc.Project.IsDirty = true;
        _documents.Add(doc);
        ActivateDocument(doc, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        StatusText.Text = $"Created template: {name}";
    }

    /// <summary>File > Save as template: stores a copy of the active score in the templates folder.</summary>
    private void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = GpDialogs.Prompt("Save as template", "Template name", string.IsNullOrWhiteSpace(_project.Title) ? "My template" : _project.Title);
        if (UserTemplates.CleanName(name) is null) return;
        try
        {
            var stored = UserTemplates.Save(name!, _project);
            StatusText.Text = $"Saved template \"{stored}\" (File > New from template)";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not save the template.\n\n{ex.Message}", "Save as template", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Ctrl+O follows "Open projects in the current tab" (Settings > Tabs); Ctrl+Shift+O always opens a new tab.
    private void Open_Click(object sender, RoutedEventArgs e) => OpenFiles(inNewTab: !_settings.Tabs.OpenInCurrentTab);
    private void OpenInNewTab_Click(object sender, RoutedEventArgs e) => OpenFiles(inNewTab: true);

    private void OpenFiles(bool inNewTab)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open TabForge or Guitar Pro file",
            Filter = "Supported scores|*.tforge;*.gp3;*.gp4;*.gp5;*.gpx;*.gp|TabForge project (*.tforge)|*.tforge|Guitar Pro (*.gp3;*.gp4;*.gp5;*.gpx;*.gp)|*.gp3;*.gp4;*.gp5;*.gpx;*.gp",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true) return;
        var replaceCurrent = !inNewTab;
        // Guitar Pro files import in the background (A5-07); results open in the selected order. "Keep" on the replace prompt
        // stops the rest of this selection, as before.
        var batch = new List<ScoreImportJob>();
        var stopped = false;
        foreach (var file in dlg.FileNames)
        {
            if (stopped) break;
            var job = OpenScore(file, replaceCurrent, replaceAll: false, (opened, loaded) =>
            {
                if (!loaded)
                {
                    stopped = true;
                    foreach (var other in batch) Imports.Cancel(other);
                    return;
                }
                StatusText.Text = opened.ImportedFromGuitarPro
                    ? $"Imported {Path.GetFileName(file)}"
                    : $"Opened {Path.GetFileName(file)}";
                if (opened.Notice is { } notice) StatusText.Text += $" — {notice}";
            });
            if (job is not null) batch.Add(job);
            // With multi-select, replace the original active tab once; open remaining files in new tabs.
            replaceCurrent = false;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _ = SaveCurrentAsync(Doc);

    /// <summary>Save (or Save As when the song has no writable path yet) the given document, whichever tab is displayed; true when the song was written.</summary>
    private Task<bool> SaveCurrentAsync(DocumentSession doc)
    {
        // Only .tforge and .gp can be written; a song opened from .gp3/.gp4/.gp5/.gpx asks where to save.
        if (!DocumentSaveFlow.CanSaveInPlace(doc.Path)) return SaveAsAsync(doc);
        var current = doc.Path!;
        if (_degraded && File.Exists(current) && !_degradedConfirmedPaths.Contains(current))
        {
            // After an unexpected error the model may be damaged: overwriting the user's file is opt-in.
            var name = Path.GetFileName(current);
            var dialog = new ThemedConfirmDialog("TabForge",
                $"TabForge hit an unexpected error earlier, so this song may be damaged in memory.\n\nSave it as a new file instead of overwriting {name}?\n\n" +
                $"Yes: save as a new file (recommended).\nNo: overwrite {name}.\nCancel: do not save.",
                yesToolTip: "Save as a new file (recommended)", noToolTip: $"Overwrite {name}") { Owner = this };
            var answer = DialogHost.ShowModal(dialog) == true ? dialog.Result : MessageBoxResult.Cancel;
            if (answer == MessageBoxResult.Cancel) return Task.FromResult(false);
            if (answer == MessageBoxResult.Yes) return SaveAsAsync(doc);
            _degradedConfirmedPaths.Add(current);
        }
        return SaveToAsync(doc, current);
    }

    // ---------- degraded mode (after an unexpected error) ----------

    private bool _degraded;
    /// <summary>Files the user already chose to write while degraded (overwrite confirmed, or the new file saved as): no second prompt.</summary>
    private readonly HashSet<string> _degradedConfirmedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called by the crash handler after an unhandled UI exception: the model may be inconsistent, so the title says so and the next
    /// save of an existing file asks "save as a new file (recommended) / overwrite / cancel".
    /// </summary>
    internal void MarkDegraded()
    {
        _degraded = true;
        UpdateTitle();
    }

    private string DegradedTitleSuffix => _degraded ? "  [after an error: restart recommended]" : "";

    // ---------- saving vs. closing (no nested dispatcher frames) ----------

    /// <summary>Saves and multi-tab closes in progress; the window closes only when none is running.</summary>
    private int _documentOperations;
    /// <summary>A close was requested while an operation ran: close when the last one ends.</summary>
    private bool _closeWhenIdle;
    private bool _closeScheduled;
    private Cursor? _cursorBeforeSave;

    private void BeginDocumentOperation() => _documentOperations++;

    private void EndDocumentOperation()
    {
        _documentOperations = Math.Max(0, _documentOperations - 1);
        if (_documentOperations == 0 && _closeWhenIdle) { _closeWhenIdle = false; RequestClose(); }
    }

    /// <summary>Closes the window once no save / close operation runs (Closing then asks as usual).</summary>
    private void RequestClose()
    {
        if (_documentOperations > 0) { _closeWhenIdle = true; return; }
        if (_closeScheduled) return;
        _closeScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _closeScheduled = false;
            if (PresentationSource.FromVisual(this) is not null) Close();   // not when it already closed meanwhile
        }), DispatcherPriority.Background);
    }

    /// <summary>
    /// Window Closing: it needs a synchronous answer, so it never waits. While a save runs the close is cancelled and retried when the
    /// save ends; "Save" in the prompt cancels it, saves with a real await, then closes again (the prompt is skipped: the song is clean).
    /// </summary>
    private bool ConfirmWindowClose()
    {
        if (_documentOperations > 0 || _documentController.IsSaving)
        {
            _closeWhenIdle = true;
            StatusText.Text = "Closing when the save has finished…";
            return false;
        }
        if (!_confirmOnClose) return true;
        // Every document in this window with unsaved changes is asked about, not only the displayed tab. All answers are collected before
        // anything happens: Cancel at any point leaves every document and the window exactly as they were (DocumentCloseFlow).
        var plan = DocumentCloseFlow.Plan(_documents.Documents, _discardOnClose, (doc, asked) =>
        {
            var message = asked == 1 && ReferenceEquals(doc, Doc) ? "Save changes to the current project?" : $"Save changes to {doc.DisplayName}?";
            return ShowSaveChangesConfirmation(message) switch
            {
                MessageBoxResult.Cancel => DiscardAnswer.Keep,
                MessageBoxResult.Yes => DiscardAnswer.SaveFirst,
                _ => DiscardAnswer.Close,
            };
        });
        if (plan.Cancel) return false;
        foreach (var doc in plan.Discard) _discardOnClose.Add(doc);   // answered "don't save": the close that follows the saves does not ask again
        if (plan.Save.Count == 0) return true;
        Dispatcher.BeginInvoke(new Action(() => SaveThenCloseWindow(plan.Save)));
        return false;
    }

    /// <summary>Documents the user chose to discard in the window-close prompt (so the close that follows the saves of the others does not ask again).</summary>
    private readonly HashSet<DocumentSession> _discardOnClose = new();

    private async void SaveThenCloseWindow(IReadOnlyList<DocumentSession> documents)
    {
        if (!await DocumentCloseFlow.SaveAllAsync(documents, SaveDocumentAsync))
        {
            _discardOnClose.Clear();   // a save did not complete: the window stays open and every answer is asked again at the next close
            return;
        }
        if (!_isClosed) RequestClose();
    }

    private DiscardAnswer AskDiscardDocument(DocumentSession doc)
    {
        if (!doc.HasUnsavedChanges) return DiscardAnswer.Close;
        var result = ShowSaveChangesConfirmation($"Save changes to {doc.DisplayName}?");
        return result switch
        {
            MessageBoxResult.Cancel => DiscardAnswer.Keep,
            MessageBoxResult.Yes => DiscardAnswer.SaveFirst,
            _ => DiscardAnswer.Close,
        };
    }

    /// <summary>Asks before a tab closes; "Save" saves it with a real await. True when the document may close now.</summary>
    private async Task<bool> ConfirmDiscardDocumentAsync(DocumentSession doc)
    {
        if (_documentController.IsSaving && doc.HasUnsavedChanges)
        {
            StatusText.Text = "Saving… close that tab when the save has finished";
            return false;
        }
        switch (AskDiscardDocument(doc))
        {
            case DiscardAnswer.Keep: return false;
            case DiscardAnswer.Close: return true;
        }
        return await SaveDocumentAsync(doc) && !doc.HasUnsavedChanges;
    }

    /// <summary>
    /// Saves <paramref name="doc"/> even when it is not the displayed tab. The document is an argument all the way down (path, dialogs, the plug-in
    /// states, the write): the displayed tab is never switched to borrow the "current document" aliases, so nothing is re-read from whichever tab is shown.
    /// </summary>
    private async Task<bool> SaveDocumentAsync(DocumentSession doc)
    {
        BeginDocumentOperation();
        try
        {
            // The write may run after another tab became active: take the lyrics box into the song now, not from the box later.
            if (ReferenceEquals(_documents.Active, doc) && doc.Project.Lyrics != LyricsBox.Text) doc.Project.Lyrics = LyricsBox.Text;
            return await SaveCurrentAsync(doc);
        }
        finally { EndDocumentOperation(); }
    }

    /// <summary>"Save" when a song replaces this tab: the new song opened beside it; this tab closes once its save succeeded.</summary>
    private async void SaveThenCloseDocument(DocumentSession doc)
    {
        if (!await SaveDocumentAsync(doc) || doc.HasUnsavedChanges) return;   // not saved: the tab stays open with its changes
        var index = _documents.IndexOf(doc);
        if (index < 0 || _documents.Documents.Count <= 1) return;
        _documents.Close(index);
        doc.DisposePlayback();
        ActivateDocument(_documents.Active);
    }

    /// <summary>While a save runs the window takes no keyboard or mouse input (no edits, tab closes or commands mid-save); Alt+F4 still reaches Closing, which waits for the save.</summary>
    private int _saveGateDepth;

    private void BeginSaveInputGate()
    {
        if (_saveGateDepth++ > 0) return;
        PreviewKeyDown += SwallowKeyWhileSaving;
        PreviewTextInput += SwallowTextWhileSaving;
        PreviewMouseDown += SwallowMouseWhileSaving;
        _cursorBeforeSave = Cursor;
        Cursor = Cursors.AppStarting;
    }

    private void EndSaveInputGate()
    {
        if (_saveGateDepth == 0 || --_saveGateDepth > 0) return;
        PreviewKeyDown -= SwallowKeyWhileSaving;
        PreviewTextInput -= SwallowTextWhileSaving;
        PreviewMouseDown -= SwallowMouseWhileSaving;
        Cursor = _cursorBeforeSave;
    }

    private void SwallowKeyWhileSaving(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey == Key.F4) return;
        e.Handled = true;
    }

    private void SwallowTextWhileSaving(object sender, TextCompositionEventArgs e) => e.Handled = true;
    private void SwallowMouseWhileSaving(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void SaveAs_Click(object sender, RoutedEventArgs e) => _ = SaveAsAsync(Doc);

    private Task<bool> SaveAsAsync(DocumentSession doc)
    {
        var gpDefault = !string.Equals(_settings.General.DefaultSaveFormat, "tforge", StringComparison.OrdinalIgnoreCase);
        const string gp = "Guitar Pro 7/8 (*.gp) - opens in Guitar Pro, keeps every TabForge feature|*.gp";
        const string tf = "TabForge project (*.tforge)|*.tforge";
        var dlg = new SaveFileDialog
        {
            Title = "Save song", Filter = gpDefault ? $"{gp}|{tf}" : $"{tf}|{gp}",
            DefaultExt = gpDefault ? ".gp" : ".tforge", AddExtension = true,
            FileName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.Path) is { Length: > 0 } name ? name : doc.Project.Title),
        };
        return dlg.ShowDialog(this) == true ? SaveToAsync(doc, dlg.FileName) : Task.FromResult(false);
    }

    /// <summary>The questions a save may ask, answered with this window's dialogs.</summary>
    private sealed class SaveDialogs : ISaveInteractions
    {
        private readonly Window _owner;
        public SaveDialogs(Window owner) => _owner = owner;

        public AudioDataSaveChoice? AskAudioDataChoice(string fileName) => Views.PluginSaveDialog.Ask(_owner, fileName) switch
        {
            Views.PluginSaveDialog.TForge => AudioDataSaveChoice.TForgeFile,
            Views.PluginSaveDialog.GpPlusDataFile => AudioDataSaveChoice.GpPlusDataFile,
            null => null,
            _ => AudioDataSaveChoice.GpWithEmbeddedProject,
        };

        public GpExportChoice AskGpPreflight(GpPreflightReport report, GpExportKind kind, string fileName) =>
            Views.GpExportPreflightDialog.Ask(_owner, report, kind, fileName);
    }

    private DocumentSaveFlow? _saveFlow;
    private DocumentSaveFlow SaveFlow => _saveFlow ??= new DocumentSaveFlow(_documentController);

    /// <summary>The plug-in states of a document's tracks (and the song's buses and monitor chain), read from the engine; large states (Nexus) take a moment.</summary>
    private Task<Audio.StateCollection> CollectStatesAsync(DocumentSession doc) =>
        Audio.AudioEngineClient.Instance.CollectStatesAsync(doc.Project.Tracks.Concat(Models.MixerBuses.Active(doc.Project))
            .Concat(Models.MixerBuses.ActiveMonitor(doc.Project, _settings.Plugins.MonitorFx) is { } monitorFx ? new[] { monitorFx } : Array.Empty<Models.TrackModel>()).ToList(), 5000);

    /// <summary>
    /// Saves <paramref name="doc"/> to <paramref name="path"/> without blocking the window: plug-in states are awaited (status "Saving…", UI responsive,
    /// a second save is refused), then the model is written synchronously in one step, so the file is a snapshot taken after the states arrived. The
    /// sequence is <see cref="DocumentSaveFlow"/>'s, for this document; this method is the window's half (input gate, status, the failure message,
    /// the title). Returns true when the song was written and is clean.
    /// </summary>
    private async Task<bool> SaveToAsync(DocumentSession doc, string path)
    {
        if (_documentController.IsSaving) { StatusText.Text = "Already saving…"; return false; }
        BeginDocumentOperation();   // a window close waits for this save instead of interrupting it
        BeginSaveInputGate();       // no edits, tab closes or other commands in this window until it is done (dialogs are their own windows)
        try
        {
            // The lyrics as they are now: the box belongs to the displayed tab only (input is gated until the write is done).
            var lyrics = ReferenceEquals(_documents.Active, doc) ? LyricsBox.Text : doc.Project.Lyrics;
            var outcome = await SaveFlow.SaveAsync(doc, path, lyrics, new SaveDialogs(this), CollectStatesAsync, text => StatusText.Text = text);
            if (outcome.Message.Length > 0) StatusText.Text = outcome.Message;
            if (outcome.Cancelled) return false;
            if (_degraded && doc.Path is { } written) _degradedConfirmedPaths.Add(written);   // the file the user chose while degraded
            UpdateTitle();
            return outcome.Saved;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Save failed";
            DialogHost.ShowError(this, ex.Message, "Save failed");
            return false;
        }
        finally
        {
            EndSaveInputGate();
            EndDocumentOperation();
        }
    }

    /// <summary>File > Export compatible Guitar Pro file: a copy for other programs. Never changes this song's file or unsaved state.</summary>
    private void ExportGuitarPro_Click(object sender, RoutedEventArgs e) => _ = ExportGuitarProAsync();

    private async Task ExportGuitarProAsync()
    {
        if (_documentController.IsSaving) { StatusText.Text = "Already saving…"; return; }
        var doc = Doc;   // the song this command was started for, even if another tab is selected while the dialogs are open
        var dlg = new SaveFileDialog
        {
            Title = "Export compatible .gp file", Filter = "Guitar Pro 7/8 (*.gp)|*.gp", DefaultExt = ".gp", AddExtension = true,
            FileName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.Path ?? "") is { Length: > 0 } name ? name : doc.Project.Title),
        };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        BeginDocumentOperation();
        BeginSaveInputGate();
        try
        {
            var lyrics = ReferenceEquals(_documents.Active, doc) ? LyricsBox.Text : doc.Project.Lyrics;
            var outcome = await SaveFlow.ExportGuitarProAsync(doc, path, lyrics, new SaveDialogs(this), CollectStatesAsync, text => StatusText.Text = text);
            StatusText.Text = outcome.Message;
        }
        catch (Exception ex) { StatusText.Text = "Export failed"; DialogHost.ShowError(this, ex.Message, ".gp export failed"); }
        finally
        {
            EndSaveInputGate();
            EndDocumentOperation();
        }
    }

    private void ExportMidi_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Export MIDI", Filter = "MIDI (*.mid)|*.mid", DefaultExt = ".mid", AddExtension = true, FileName = SanitizeFileName(_project.Title) };
        if (dlg.ShowDialog(this) != true) return;
        try { MidiExportService.Export(_project, dlg.FileName); StatusText.Text = $"Exported MIDI {Path.GetFileName(dlg.FileName)}"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "MIDI export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Render_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.HasSelection ? Editor.SelectionCellRange : default;
        var selected = Editor.SelectedTrackIndex is >= 0 && Editor.SelectedTrackIndex < _project.Tracks.Count ? new[] { _project.Tracks[Editor.SelectedTrackIndex] } : Array.Empty<Models.TrackModel>();
        var ctx = new Views.RenderContext
        {
            Project = _project, Media = Doc.Media, Settings = _settings, SelectedTracks = selected,
            Selection = Editor.HasSelection ? (sel.StartMeasure, sel.StartCell, sel.EndMeasure, sel.EndCell) : null,
            StopPlayback = () => { if (_midi.IsPlaying) StopPlayback(); },
            Restore = () => { SyncAudioEngine(); _midi.RearmChannelSetup(); },
            SaveSettings = SaveSettings,
        };
        new Views.RenderWindow(ctx, this).ShowDialog();
    }

    private void ExportAscii_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Export ASCII tab", Filter = "Text (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true, FileName = SanitizeFileName(_project.Title) };
        if (dlg.ShowDialog(this) != true) return;
        try { AsciiExportService.Export(_project, dlg.FileName); StatusText.Text = $"Exported ASCII {Path.GetFileName(dlg.FileName)}"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ASCII export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportMusicXml_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Export MusicXML", Filter = "MusicXML (*.musicxml;*.xml)|*.musicxml;*.xml", DefaultExt = ".musicxml", AddExtension = true, FileName = SanitizeFileName(_project.Title) };
        if (dlg.ShowDialog(this) != true) return;
        try { MusicXmlExportService.Export(_project, dlg.FileName); StatusText.Text = $"Exported MusicXML {Path.GetFileName(dlg.FileName)}"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "MusicXML export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "Export PDF", Filter = "PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, FileName = SanitizeFileName(_project.Title) };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var pages = Views.ScorePdfExporter.Export(_project, Editor.SelectedTrackIndex, dlg.FileName);
            StatusText.Text = $"Exported PDF {Path.GetFileName(dlg.FileName)} ({pages} page{(pages == 1 ? "" : "s")})";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PDF export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CommandPalette_Click(object sender, RoutedEventArgs e)
    {
        var palette = new Views.CommandPalette(this, _settings.Hotkeys);
        palette.ShowDialog();
        // Run after the dialog is gone so the command sees the normal window focus.
        if (palette.ChosenId is { } id) Dispatcher.BeginInvoke(new Action(() => RunHotkey(id)), DispatcherPriority.Input);
    }

    private void Print_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "Printing uses the Windows print dialog on the score page in the full release. For now use Export ASCII or screenshot the page.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
    private void PrintPreview_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "Print preview is the white score page above (page mode, 4 bars per system). Zoom with the toolbar.", "Print preview", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ScoreInfo_Click(object sender, RoutedEventArgs e)
    {
        var capture = CaptureUndo();
        if (GpDialogs.EditScoreInfo(_project)) { RefreshStatus(); UpdateTitle(); StatusText.Text = "Score info updated"; }
        else if (capture is { } cancelled) _undo.Discard(cancelled);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
