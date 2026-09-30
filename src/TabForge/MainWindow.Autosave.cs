using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Documents;
using TabForge.Services;

namespace TabForge;

// MainWindow, autosave (audit F-04): dirty songs are copied to the Recovery folder; see AutosaveService and AutosaveRunner.
public partial class MainWindow
{
    private readonly AutosavePlanner _autosavePlanner = new(DateTime.UtcNow);
    private readonly AutosaveRunner _autosave = new();
    private DispatcherTimer? _autosaveTimer;
    private Border? _autosaveBar;
    private TextBlock? _autosaveBarText;
    private bool _reconcileScheduled;

    /// <summary>Starts the autosave clock (a cheap tick every 20 s; the interval setting is read live).</summary>
    private void StartAutosave()
    {
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(20) };
        _autosaveTimer.Tick += (_, _) => AutosaveTick();
        _autosaveTimer.Start();
        // A closed tab's copy goes soon after (never during a tab move: the registry waits a grace period before it deletes).
        _documents.Changed += (_, _) => ScheduleAutosaveReconcile();
        Closed += (_, _) =>
        {
            _autosaveTimer?.Stop();
            // The documents still in this window are closed for good; a tab moved away was already handed over.
            AutosaveRegistry.Retire(_documents.Documents.ToArray());
        };
    }

    private static List<DocumentSession> DocumentsInAllWindows() =>
        Application.Current?.Windows.OfType<MainWindow>().SelectMany(w => w.OpenDocuments).ToList() ?? new List<DocumentSession>();

    private void ScheduleAutosaveReconcile()
    {
        if (_reconcileScheduled) return;
        _reconcileScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _reconcileScheduled = false;
            ReconcileAutosaveStates();
        });
    }

    private void ReconcileAutosaveStates()
    {
        try
        {
            if (AutosaveRegistry.Reconcile(DocumentsInAllWindows(), DateTime.UtcNow) > 0)
            {
                // A document is briefly missing (a tab move in progress): look again once the grace period is over.
                var recheck = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutosaveRegistry.MissingGrace + TimeSpan.FromMilliseconds(250) };
                recheck.Tick += (_, _) => { recheck.Stop(); ReconcileAutosaveStates(); };
                recheck.Start();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave reconcile failed: {ex}"); }
    }

    private void AutosaveTick()
    {
        try
        {
            ReconcileAutosaveStates();
            UpdateAutosaveBar();
            if (_autosave.Busy) return;
            var minutes = _settings.General.AutosaveMinutes;
            // Playing (or recording) only stretches the interval; a long session must still keep a recovery copy.
            var playing = _documents.Documents.Any(d => d.Playback.Engine.IsPlaying);
            if (!_autosavePlanner.Due(DateTime.UtcNow, minutes, playing, _autosave.Health.Failing)) return;
            _autosavePlanner.Ran(DateTime.UtcNow);
            _ = RunAutosavePass();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave failed: {ex}"); }
    }

    private async Task RunAutosavePass()
    {
        try { await _autosave.RunPassAsync(_documents.Documents.ToArray(), AutosaveService.DefaultFolder); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autosave pass failed: {ex}"); }
        UpdateAutosaveBar();
    }

    /// <summary>The non-modal notice above the status bar while autosave is failing (the reason, the last good copy, the retry).</summary>
    private void UpdateAutosaveBar()
    {
        var text = _autosave.Health.Describe(DateTime.UtcNow);
        if (text is null) { if (_autosaveBar is not null) _autosaveBar.Visibility = Visibility.Collapsed; return; }
        if (_autosaveBar is null)
        {
            if (MainStatusBar.Parent is not DockPanel dock) return;
            _autosaveBarText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _autosaveBarText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var retry = new Button { Content = "Retry now", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(10, 0, 0, 0) };
            retry.Click += (_, _) => { _autosavePlanner.RunSoon(); AutosaveTick(); };
            DockPanel.SetDock(retry, Dock.Right);
            var row = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 3, 8, 3) };
            row.Children.Add(retry);
            row.Children.Add(_autosaveBarText);
            _autosaveBar = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) };
            _autosaveBar.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            _autosaveBar.SetResourceReference(Border.BorderBrushProperty, "BorderSoftBrush");
            System.Windows.Automation.AutomationProperties.SetName(_autosaveBar, "Autosave warning");
            DockPanel.SetDock(_autosaveBar, Dock.Bottom);
            dock.Children.Insert(dock.Children.IndexOf(MainStatusBar) + 1, _autosaveBar);
        }
        _autosaveBarText!.Text = text;
        _autosaveBarText.ToolTip = text;
        _autosaveBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Start-up: offers songs a crashed or killed session left in the Recovery folder. Yes opens each as an unsaved tab (Save asks
    /// for a name; the user's own files are untouched); No discards the copies.
    /// </summary>
    internal void OfferAutosaveRecovery()
    {
        var files = AutosaveService.FindOrphans(AutosaveService.DefaultFolder);
        if (files.Count == 0) return;
        var answer = MessageBox.Show(this,
            $"TabForge found {files.Count} unsaved song(s) from a session that ended unexpectedly.\n\nOpen them now? Choosing No discards them.",
            "Recover unsaved songs", MessageBoxButton.YesNo, MessageBoxImage.Question);
        foreach (var file in files)
        {
            if (answer == MessageBoxResult.Yes) OpenRecoveredSong(file);
            else AutosaveService.Delete(file);
        }
    }

    private void OpenRecoveredSong(string file)
    {
        try
        {
            OpenDocumentFromPath(file);
            var doc = _documents.FindByPath(file);
            if (doc is null) return;   // the open failed and said so; the copy stays for the next start
            doc.Path = null;
            doc.IsNew = true;
            doc.MarkIncomplete();     // unsaved: closing asks, Save asks for a name
            _documents.Replace(_documents.IndexOf(doc), doc);
            UpdateTitle();
            // The copy is NOT deleted yet: it is the only durable copy of this work until the song is saved or this session has
            // autosaved it under its own name (the autosave then removes it); a crash in between offers it again.
            AutosaveRegistry.Adopt(doc, file);
            _autosavePlanner.RunSoon();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Recovered song could not be opened: {ex}"); }
    }
}
