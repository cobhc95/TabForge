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
public partial class MainWindow : IWindowCloseHost
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
            Title = "Open TabForge or .gp file",
            Filter = "Supported scores|*.tforge;*.gp3;*.gp4;*.gp5;*.gpx;*.gp|TabForge project (*.tforge)|*.tforge|GP files (*.gp3;*.gp4;*.gp5;*.gpx;*.gp)|*.gp3;*.gp4;*.gp5;*.gpx;*.gp",
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
                    foreach (var other in batch) Imports.Queue.Cancel(other);
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
    private Task<bool> SaveCurrentAsync(DocumentSession doc, DocumentController.SaveHold? hold = null)
    {
        // The save is claimed before its first dialog (the degraded-mode question, the file dialog), so no other save starts and no import replaces a tab meanwhile.
        if (hold is null) return SaveFlow.RunClaimedAsync(claim => SaveCurrentAsync(doc, claim), ShowSaveBusy);
        // Only .tforge and .gp can be written; a song opened from .gp3/.gp4/.gp5/.gpx asks where to save.
        if (!DocumentSaveFlow.CanSaveInPlace(doc.Path)) return SaveAsAsync(doc, hold);
        var current = doc.Path!;
        if (CloseFlow.MustConfirmOverwrite(current) && File.Exists(current))
        {
            // After an unexpected error the model may be damaged: overwriting the user's file is opt-in.
            var name = Path.GetFileName(current);
            var dialog = new ThemedConfirmDialog("TabForge",
                $"TabForge hit an unexpected error earlier, so this song may be damaged in memory.\n\nSave it as a new file instead of overwriting {name}?\n\n" +
                $"Yes: save as a new file (recommended).\nNo: overwrite {name}.\nCancel: do not save.",
                yesToolTip: "Save as a new file (recommended)", noToolTip: $"Overwrite {name}") { Owner = this };
            var answer = DialogHost.ShowModal(dialog) == true ? dialog.Result : MessageBoxResult.Cancel;
            if (answer == MessageBoxResult.Cancel) return Task.FromResult(false);
            if (answer == MessageBoxResult.Yes) return SaveAsAsync(doc, hold);
            CloseFlow.ConfirmedWhileDegraded(current);
        }
        return SaveToAsync(doc, current, hold);
    }

    // ---------- degraded mode, saving vs. closing (WindowCloseFlow) ----------

    private WindowCloseFlow? _closeFlow;
    private WindowCloseFlow CloseFlow => _closeFlow ??= new WindowCloseFlow(this);
    IReadOnlyList<DocumentSession> IWindowCloseHost.Documents => _documents.Documents;
    DocumentSession IWindowCloseHost.Shown => Doc;
    bool IWindowCloseHost.IsSaving => _documentController.IsSaving;
    bool IWindowCloseHost.ConfirmOnClose => _confirmOnClose;
    bool IWindowCloseHost.IsClosed => _isClosed;
    DiscardAnswer IWindowCloseHost.AskSaveChanges(string message) => ShowSaveChangesConfirmation(message) switch
    {
        MessageBoxResult.Cancel => DiscardAnswer.Keep,
        MessageBoxResult.Yes => DiscardAnswer.SaveFirst,
        _ => DiscardAnswer.Close,
    };
    Task<bool> IWindowCloseHost.SaveDocumentAsync(DocumentSession doc) => SaveDocumentAsync(doc);
    void IWindowCloseHost.PostClose(Action closed) => Dispatcher.BeginInvoke(new Action(() =>
    {
        closed();
        if (PresentationSource.FromVisual(this) is not null) Close();   // not when it already closed meanwhile
    }), DispatcherPriority.Background);
    void IWindowCloseHost.Post(Action work) => Dispatcher.BeginInvoke(work);
    Cursor? IWindowCloseHost.Cursor { get => Cursor; set => Cursor = value; }
    void IWindowCloseHost.SetStatus(string text) => StatusText.Text = text;

    /// <summary>
    /// Called by the crash handler after an unhandled UI exception: the model may be inconsistent, so the title says so and the next
    /// save of an existing file asks "save as a new file (recommended) / overwrite / cancel".
    /// </summary>
    internal void MarkDegraded()
    {
        CloseFlow.MarkDegraded();
        UpdateTitle();
    }

    private void BeginDocumentOperation() => CloseFlow.BeginDocumentOperation();
    private void EndDocumentOperation() => CloseFlow.EndDocumentOperation();
    private void RequestClose() => CloseFlow.RequestClose();

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

    // The gate is the window's own class handlers for the preview events: they run before every handler added to the window (the key handler
    // of the window itself included), so a key or click the gate swallows reaches nothing.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (CloseFlow.InputGated && !(e.Key == Key.System && e.SystemKey == Key.F4)) e.Handled = true;
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (CloseFlow.InputGated) e.Handled = true;
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (CloseFlow.InputGated) e.Handled = true;
        base.OnPreviewMouseDown(e);
    }

    /// <summary>A save or export was refused because another one is running: the status bar says so.</summary>
    private void ShowSaveBusy(string text) => StatusText.Text = text;

    private void SaveAs_Click(object sender, RoutedEventArgs e) => _ = SaveAsAsync(Doc);

    private Task<bool> SaveAsAsync(DocumentSession doc, DocumentController.SaveHold? hold = null)
    {
        if (hold is null) return SaveFlow.RunClaimedAsync(claim => SaveAsAsync(doc, claim), ShowSaveBusy);
        var gpDefault = !string.Equals(_settings.General.DefaultSaveFormat, "tforge", StringComparison.OrdinalIgnoreCase);
        const string gp = ".gp file (*.gp) - keeps every TabForge feature|*.gp";
        const string tf = "TabForge project (*.tforge)|*.tforge";
        var dlg = new SaveFileDialog
        {
            Title = "Save song", Filter = gpDefault ? $"{gp}|{tf}" : $"{tf}|{gp}",
            DefaultExt = gpDefault ? ".gp" : ".tforge", AddExtension = true,
            FileName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.Path) is { Length: > 0 } name ? name : doc.Project.Title),
        };
        return dlg.ShowDialog(this) == true ? SaveToAsync(doc, dlg.FileName, hold) : Task.FromResult(false);
    }

    private DocumentSaveFlow? _saveFlow;
    private DocumentSaveFlow SaveFlow => _saveFlow ??= new DocumentSaveFlow(_documentController);

    /// <summary>The plug-in states of a document's tracks (and the song's buses and monitor chain), read from the engine; large states (Nexus) take a moment.</summary>
    private Task<Audio.StateCollection> CollectStatesAsync(DocumentSession doc) =>
        _engine.CollectStatesAsync(Models.SongRigs.Running(doc.Project, _settings.Plugins.MonitorFx).Select(r => r.AsTrack()).ToList(), 5000);

    /// <summary>
    /// Saves <paramref name="doc"/> to <paramref name="path"/> without blocking the window: plug-in states are awaited (status "Saving…", UI responsive,
    /// a second save is refused), then the model is written synchronously in one step, so the file is a snapshot taken after the states arrived. The
    /// sequence is <see cref="DocumentSaveFlow"/>'s, for this document; this method is the window's half (input gate, status, the failure message,
    /// the title). Returns true when the song was written and is clean.
    /// </summary>
    private async Task<bool> SaveToAsync(DocumentSession doc, string path, DocumentController.SaveHold hold)
    {
        BeginDocumentOperation();   // a window close waits for this save instead of interrupting it
        CloseFlow.BeginSaveInputGate();       // no edits, tab closes or other commands in this window until it is done (dialogs are their own windows)
        try
        {
            // The lyrics as they are now: the box belongs to the displayed tab only (input is gated until the write is done).
            var lyrics = ReferenceEquals(_documents.Active, doc) ? LyricsBox.Text : doc.Project.Lyrics;
            var outcome = await SaveFlow.SaveAsync(doc, path, lyrics, new SaveDialogs(this), CollectStatesAsync, text => StatusText.Text = text, hold);
            if (outcome.Message.Length > 0) StatusText.Text = outcome.Message;
            if (outcome.Cancelled) return false;
            if (doc.Path is { } written) CloseFlow.ConfirmedWhileDegraded(written);   // the file the user chose while degraded
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
            CloseFlow.EndSaveInputGate();
            EndDocumentOperation();
        }
    }

    /// <summary>File > Export compatible Guitar Pro file: a copy for other programs. Never changes this song's file or unsaved state.</summary>
    private void ExportGuitarPro_Click(object sender, RoutedEventArgs e) => _ = SaveFlow.RunClaimedAsync(hold => ExportGuitarProAsync(Doc, hold), ShowSaveBusy);   // claimed before the file dialog, like a save; the song is the one this command started for, even if another tab is selected while the dialogs are open

    private async Task<bool> ExportGuitarProAsync(DocumentSession doc, DocumentController.SaveHold hold)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export compatible .gp file", Filter = ".gp file (*.gp)|*.gp", DefaultExt = ".gp", AddExtension = true,
            FileName = SanitizeFileName(Path.GetFileNameWithoutExtension(doc.Path ?? "") is { Length: > 0 } name ? name : doc.Project.Title),
        };
        if (dlg.ShowDialog(this) != true) return false;
        var path = dlg.FileName;
        BeginDocumentOperation();
        CloseFlow.BeginSaveInputGate();
        try
        {
            var lyrics = ReferenceEquals(_documents.Active, doc) ? LyricsBox.Text : doc.Project.Lyrics;
            var outcome = await SaveFlow.ExportGuitarProAsync(doc, path, lyrics, new SaveDialogs(this), CollectStatesAsync, text => StatusText.Text = text, hold);
            StatusText.Text = outcome.Message;
            return true;
        }
        catch (Exception ex) { StatusText.Text = "Export failed"; DialogHost.ShowError(this, ex.Message, ".gp export failed"); return false; }
        finally
        {
            CloseFlow.EndSaveInputGate();
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
            Project = _project, Media = Doc.Media, Settings = _settings, Engine = _engine, SelectedTracks = selected,
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
        var capture = CheckpointUndo();
        if (GpDialogs.EditScoreInfo(_project)) { RefreshStatus(); UpdateTitle(); StatusText.Text = "Score info updated"; }
        else if (capture is { } cancelled) _undo.Discard(cancelled);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
