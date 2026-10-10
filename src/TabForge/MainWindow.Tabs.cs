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

// MainWindow, document tabs in the title bar, moving tabs between windows, window chrome.
// Owns: the window's wiring of the document tabs (DocumentTabsController does the work), moving tabs between windows (tear-off, adopt) and the window chrome.
// Does not own: document lifetime (DocumentCloseFlow).
// Tests: listed in docs/feature-map/windows-tabs-and-documents.md.
public partial class MainWindow
{
    // ---------- MIDI / VST ----------

    // ---------- document tabs (in the title bar) ----------

    private TabSettings _tabSettings = new();

    private void WireTabs()
    {
        Tabs.Settings = _tabSettings;
        Tabs.Bind(_documents);
        Tabs.TabActivated += (_, index) => ActivateTabAt(index);
        Tabs.CloseRequested += (_, index) => CloseDocument(index);
        Tabs.CloseOthersRequested += (_, index) => CloseOtherDocuments(index);
        Tabs.CloseRightRequested += (_, index) => CloseDocumentsToTheRight(index);
        Tabs.DuplicateRequested += (_, index) => DuplicateDocument(index);
        Tabs.DetachRequested += (_, index) => DetachDocumentToNewWindow(index);
        Tabs.HeldTearOffRequested += (_, drag) => DetachHeldDocumentToNewWindow(drag.Index, drag.ScreenPoint);
        Tabs.Reordered += (_, move) => MoveDocument(move.from, move.to);
        Tabs.NewTabRequested += (_, _) => NewTab();
        Tabs.TabDropped += (_, e) => AdoptDroppedDocument(e.Session, e.Index);
        Tabs.CaptionDragRequested += (_, _) => NativeCaptionDrag(this);
        Tabs.CaptionDoubleClickRequested += (_, _) => ToggleMaximise();
    }

    private void ActivateTabAt(int index) => DocTabs.ActivateAt(index);

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();

    private void NewTab()
    {
        var at = _tabSettings.NewTabPosition == NewTabPositions.AtEnd
            ? _documents.Documents.Count
            : _documents.ActiveIndex + 1;
        var blank = DocumentSession.Blank();
        ApplyPreferredScoreView(blank);
        blank.Notation = PreferredNotation;
        blank.Project.Mixer.ShowGroupsInTrackList = _settings.Timeline.ShowGroupsInNewSongs;   // Settings > Timeline & sections > Track list
        var doc = _documents.Insert(blank, at);
        ActivateDocument(doc, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        StatusText.Text = "New tab";
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseActiveTab();

    private void CloseActiveTab() => CloseDocument(_documents.ActiveIndex);

    private DocumentTabsController DocTabs => new(new TabsHost(this));

    private sealed class TabsHost : IDocumentTabsHost
    {
        private readonly MainWindow _window;
        public TabsHost(MainWindow window) => _window = window;
        public DocumentManager Documents => _window._documents;
        public TabSettings TabSettings => _window._tabSettings;
        public bool IsSaving => _window._documentController.IsSaving;
        public void CaptureDocumentState() => _window.CaptureDocumentState();
        public void RefreshTabs() => _window.RefreshTabs();
        public void Activate(DocumentSession session, bool focusTabSelection, bool applyPlaybackSwitchPolicy) =>
            _window.ActivateDocument(session, focusTabSelection: focusTabSelection, applyPlaybackSwitchPolicy: applyPlaybackSwitchPolicy);
        public DiscardAnswer AskDiscard(DocumentSession doc) => _window.AskDiscardDocument(doc);
        public Task<bool> SaveDocumentAsync(DocumentSession doc) => _window.SaveDocumentAsync(doc);
        public void BeginDocumentOperation() => _window.BeginDocumentOperation();
        public void EndDocumentOperation() => _window.EndDocumentOperation();
        public void CloseWindow() => _window.Close();
        public void SetStatus(string text) => _window.StatusText.Text = text;
    }

    private void DuplicateDocument(int index) => DocTabs.Duplicate(index);
    private void MoveDocument(int from, int to) => DocTabs.Move(from, to);
    // The closes await the save (no nested dispatcher frame); the handlers stay async void like any event handler.
    private async void CloseDocument(int index) => await DocTabs.CloseAsync(index);
    private async void CloseOtherDocuments(int keep) => await DocTabs.CloseOthersAsync(keep);
    private async void CloseDocumentsToTheRight(int from) => await DocTabs.CloseToTheRightAsync(from);

    // ---------- moving tabs between windows (TabTransferController; these are its entry points) ----------

    private void DetachDocumentToNewWindow(int index) => _tabTransfer.DetachToNewWindow(index);

    private void DetachHeldDocumentToNewWindow(int index, Point screenPoint) => _tabTransfer.DetachHeldToNewWindow(index, screenPoint);

    /// <summary>Moves this window's only document into <paramref name="target"/> at <paramref name="index"/> and closes this window.</summary>
    internal bool MergeSoleDocumentInto(MainWindow target, int index) => _tabTransfer.MergeSoleDocumentInto(target._tabTransfer, index);

    /// <summary>Adopts a document dropped onto this window's tab strip.</summary>
    private void AdoptDroppedDocument(DocumentSession session, int index) => _tabTransfer.TryAdopt(session, index);

    /// <summary>Replaces the freshly-created blank tab with a document handed over from another window.</summary>
    public void AdoptSession(DocumentSession session) => _tabTransfer.AdoptAsOnlyDocument(session);

    /// <summary>Applies the "last tab closed" setting after a drag removed the final tab.</summary>
    private void HandleSourceWindowEmptied()
    {
        if (_tabSettings.LastTabClosed == LastTabActions.CloseWindow) { Close(); return; }
        var blank = DocumentSession.Blank();
        ApplyPreferredScoreView(blank);
        blank.Notation = PreferredNotation;
        _documents.Insert(blank, 0);
        ActivateDocument(_documents.Active);
    }

    /// <summary>
    /// The whole window accepts a dropped tab (browser behaviour): dropping anywhere merges the
    /// document into this window. Drops on the tab strip are handled by the tab bar itself, which
    /// marks the event handled so this fallback only runs for the rest of the window.
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format))
        {
            // Files from Windows: songs open in new tabs anywhere on the window; audio and MIDI only on the timeline (it handles them first).
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DroppedSongs.In(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>Song files dropped on the window (anywhere, the timeline and tab strip included): each opens in a new tab.</summary>
    private void OpenDroppedSongs(string[] songs)
    {
        foreach (var file in songs)
            OpenScore(file, replaceCurrent: false, replaceAll: false, (opened, loaded) =>
            {
                if (!loaded) return;
                StatusText.Text = opened.ImportedFromGuitarPro ? $"Imported {Path.GetFileName(file)}" : $"Opened {Path.GetFileName(file)}";
                if (opened.Notice is { } notice) StatusText.Text += $" — {notice}";
            });
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format))
        {
            var songs = DroppedSongs.In(e.Data);
            if (songs.Length == 0) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            // After the drag returns: opening a song can show a dialog, which must not run inside the drag source's drag loop.
            Dispatcher.BeginInvoke(() => OpenDroppedSongs(songs));
            return;
        }
        _tabTransfer.DropTab(e);
    }

    /// <summary>The window as the host of its <see cref="TabTransferController"/>.</summary>
    private sealed class TabTransferHost : ITabTransferHost
    {
        private readonly MainWindow _window;
        public TabTransferHost(MainWindow window) => _window = window;

        public DocumentManager Documents => _window._documents;
        public bool IsOpen => !_window._isClosed && _window.IsVisible;
        public bool IsEnabled => _window.IsEnabled;
        public Rect TabStripScreenRect => _window.Tabs.GetScreenRect();
        public double DpiScaleY => VisualTreeHelper.GetDpi(_window).DpiScaleY;
        public int TabInsertIndexAt(Point screen) => _window.Tabs.GetInsertionIndexAtScreen(screen);

        public void SetAttachHighlight(bool highlighted)
        {
            var titleBar = _window.TitleBar;
            if (titleBar is null) return;
            // Whole-strip attach treatment: a full accent outline and a soft fill across the complete
            // title row, including the tab buttons and unused caption space.
            titleBar.BorderBrush = (Brush)_window.FindResource(highlighted ? "AccentBrush" : "ChromeBorderBrush");
            titleBar.Background = (Brush)_window.FindResource(highlighted ? "AccentSoftBrush" : "ChromeStripBrush");
            titleBar.BorderThickness = highlighted ? new Thickness(2) : new Thickness(0, 0, 0, 1);
            titleBar.CornerRadius = highlighted ? new CornerRadius(4) : new CornerRadius(0);
        }

        public bool TryGetCursor(out Point screen) => NativeWindowDrag.TryGetCursor(out screen);

        // Screen pixels -> this window's DIPs (per-monitor DPI; also correct below 100 % scaling).
        public Point ScreenToDip(Point screen) =>
            (PresentationSource.FromVisual(_window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(screen);

        public Point NewWindowPositionNearPointer()
        {
            var position = Mouse.GetPosition(_window);
            return new Point(_window.Left + position.X - 60, _window.Top + position.Y + 24);
        }

        public void CaptureDocumentState() => _window.CaptureDocumentState();
        public void ShowActiveDocument() => _window.ActivateDocument(_window._documents.Active);
        public void ActivateAdopted(DocumentSession session) => _window.ActivateDocument(session, focusTabSelection: true);
        public void LastDocumentLeft() => _window.HandleSourceWindowEmptied();
        public void CloseWindow() => _window.Close();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public void PrepareRestoredDocument(DocumentSession session) => _window.ApplyPreferredScoreView(session);

        public TabTransferController OpenWindowWith(DocumentSession session, Point? dipPosition)
        {
            var window = new MainWindow(_window._engine, _window._options);
            // A newly detached document window is a normal, positioned child window. The primary
            // application window is maximised by default, but that is not appropriate for tear-off.
            window.WindowState = WindowState.Normal;
            window.AdoptSession(session);
            if (dipPosition is { } p)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = p.X;
                window.Top = p.Y;
            }
            window.Show();
            window.Activate();
            return window._tabTransfer;
        }

        public bool DragWindowWithHeldPointer(Point screen, Action moved)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero || !NativeWindowDrag.LeftButtonDown) return false;
            if (_window.WindowState != WindowState.Normal)
            {
                var drop = ScreenToDip(screen);
                _window.WindowState = WindowState.Normal;
                _window.Left = drop.X - 120;
                _window.Top = drop.Y - 22;
            }
            NativeWindowDrag.ReleaseMouseCapture();
            EventHandler onMoved = (_, _) => moved();
            _window.LocationChanged += onMoved;
            try { NativeWindowDrag.BeginCaptionDrag(hwnd); }
            finally { _window.LocationChanged -= onMoved; }
            return true;
        }
    }

    private void ToggleMaximise() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---------- window chrome (client-drawn) ----------

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxButton_Click(object sender, RoutedEventArgs e) => ToggleMaximise();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Swaps the maximise glyph for the restore glyph while the window is maximised.</summary>
    private void UpdateMaximiseGlyph()
    {
        if (MaxIcon is null) return;
        MaxIcon.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximise");
    }

    /// <summary>
    /// Starts a real caption drag through the non-client path, so Aero Snap, the double-click
    /// maximise and the right-click system menu keep working even though the caption is hit-testable.
    /// </summary>
    private static void NativeCaptionDrag(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        NativeWindowDrag.BeginCaptionDrag(handle);
    }
}
