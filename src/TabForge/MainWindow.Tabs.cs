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

    private void RefreshTabsAndActivate(DocumentSession session)
    {
        CaptureDocumentState();
        ActivateDocument(session, applyPlaybackSwitchPolicy: true);
    }

    private void ActivateTabAt(int index)
    {
        if (index < 0 || index >= _documents.Documents.Count || index == _documents.ActiveIndex) return;
        CaptureDocumentState();
        ActivateDocument(_documents.Documents[index], applyPlaybackSwitchPolicy: true);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTab();

    private void NewTab()
    {
        CaptureDocumentState();
        var at = _tabSettings.NewTabPosition == NewTabPositions.AtEnd
            ? _documents.Documents.Count
            : _documents.ActiveIndex + 1;
        var blank = DocumentSession.Blank();
        ApplyPreferredScoreView(blank);
        blank.Notation = PreferredNotation;
        var doc = _documents.Insert(blank, at);
        ActivateDocument(doc, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        StatusText.Text = "New tab";
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e) => CloseActiveTab();

    private void CloseActiveTab() => CloseDocument(_documents.ActiveIndex);

    private void DuplicateDocument(int index)
    {
        CaptureDocumentState();
        var copy = _documents.Duplicate(index);
        if (copy is null) return;
        ActivateDocument(copy, focusTabSelection: true, applyPlaybackSwitchPolicy: true);
        StatusText.Text = $"Duplicated {copy.DisplayName}";
    }

    private void MoveDocument(int from, int to)
    {
        if (!_documents.Move(from, to)) return;
        CaptureDocumentState();
        RefreshTabs();
    }

    // Closing a tab with "Save" awaits the save (no nested dispatcher frame); the tab is found again by reference afterwards.
    private async void CloseDocument(int index)
    {
        if (index < 0 || index >= _documents.Documents.Count) return;
        CaptureDocumentState();
        var doc = _documents.Documents[index];
        if (!await ConfirmDiscardDocumentAsync(doc)) return;
        index = _documents.IndexOf(doc);
        if (index < 0) return;   // it went away while saving
        if (_documents.Documents.Count == 1 && _tabSettings.LastTabClosed == LastTabActions.CloseWindow)
        {
            doc.DisposePlayback();
            _documents.Detach(index);
            Close();
            return;
        }
        _documents.Close(index);
        doc.DisposePlayback();
        ActivateDocument(_documents.Active);
        StatusText.Text = $"Closed {doc.DisplayName}";
    }

    private async void CloseOtherDocuments(int keep)
    {
        if (keep < 0 || keep >= _documents.Documents.Count) return;
        CaptureDocumentState();
        var kept = _documents.Documents[keep];
        var others = _documents.Documents.Where(d => !ReferenceEquals(d, kept)).ToArray();
        await CloseDocumentsAsync(others);
        var at = _documents.IndexOf(kept);
        ActivateDocument(_documents.Documents[Math.Clamp(at < 0 ? _documents.ActiveIndex : at, 0, _documents.Documents.Count - 1)]);
        StatusText.Text = "Closed other tabs";
    }

    private async void CloseDocumentsToTheRight(int from)
    {
        CaptureDocumentState();
        await CloseDocumentsAsync(_documents.Documents.Skip(from + 1).ToArray());
        ActivateDocument(_documents.Documents[Math.Clamp(_documents.ActiveIndex, 0, _documents.Documents.Count - 1)]);
        StatusText.Text = "Closed tabs to the right";
    }

    /// <summary>Closes <paramref name="targets"/> last-first, asking for each; one operation, so a window close waits for all of it.</summary>
    private async Task CloseDocumentsAsync(IReadOnlyList<DocumentSession> targets)
    {
        BeginDocumentOperation();
        try
        {
            for (var k = targets.Count - 1; k >= 0; k--)
            {
                var doc = targets[k];
                if (!await ConfirmDiscardDocumentAsync(doc)) continue;
                var i = _documents.IndexOf(doc);
                if (i < 0) continue;
                _documents.Close(i);
                doc.DisposePlayback();
            }
        }
        finally { EndDocumentOperation(); }
    }

    // ---------- moving tabs between windows ----------

    /// <summary>Opens the document in a brand-new top-level window (drag-out or the tab menu).</summary>
    private void DetachDocumentToNewWindow(int index)
    {
        if (index < 0 || index >= _documents.Documents.Count) return;
        var doc = _documents.Documents[index];
        _documents.Detach(index);
        if (_documents.Documents.Count == 0) HandleSourceWindowEmptied();
        else ActivateDocument(_documents.Active);
        var position = Mouse.GetPosition(this);
        OpenWindowWith(doc, new Point(Left + position.X - 60, Top + position.Y + 24));
        StatusText.Text = $"Moved {doc.DisplayName} to a new window";
    }

    /// <summary>
    /// Held tear-off path: transfer the live session, place a real top-level window under the held
    /// pointer, then hand movement to the native caption loop so Aero Snap and monitor-edge behaviour
    /// remain owned by Windows. The editor itself stays in the workspace.
    /// </summary>
    private void DetachHeldDocumentToNewWindow(int index, Point screenPoint)
    {
        if (index < 0 || index >= _documents.Documents.Count) return;
        var session = _documents.Documents[index];
        CaptureDocumentState();
        _documents.Detach(index);
        if (_documents.Documents.Count == 0) HandleSourceWindowEmptied();
        else ActivateDocument(_documents.Active);

        // Screen pixels -> this window's DIPs (per-monitor DPI; also correct below 100 % scaling).
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var dropDip = fromDevice.Transform(screenPoint);
        var detached = new MainWindow();
        detached.AdoptSession(session);
        // Detached document windows should open at the dropped position rather than inheriting
        // the maximised startup state of the primary application window.
        detached.WindowState = WindowState.Normal;
        detached.WindowStartupLocation = WindowStartupLocation.Manual;
        detached.Left = dropDip.X - 120;
        detached.Top = dropDip.Y - 22;
        detached.Show();
        detached.Activate();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(detached).Handle;
        if (hwnd != IntPtr.Zero && (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0)
        {
            ReleaseCapture();
            detached.LocationChanged += detached.TearOffWindow_LocationChanged;
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            detached.LocationChanged -= detached.TearOffWindow_LocationChanged;
            detached.CompleteHeldTearOff();
        }
    }

    private void TearOffWindow_LocationChanged(object? sender, EventArgs e) => UpdateAttachTargetFromCursor();

    private void UpdateAttachTargetFromCursor()
    {
        if (!TryGetScreenCursor(out var cursor)) return;
        var next = TabWindowRegistry.FindTarget(this, cursor);
        if (!ReferenceEquals(next, _attachTarget))
        {
            _attachTarget?.SetTabAttachHighlight(false);
            _attachTarget = next;
            _attachTarget?.SetTabAttachHighlight(true);
        }
        _attachTargetIndex = _attachTarget?.GetTabAttachInsertIndex(cursor) ?? -1;
    }

    private void CompleteHeldTearOff()
    {
        if (!TryGetScreenCursor(out var cursor)) cursor = new Point(Left, Top);
        UpdateAttachTargetFromCursor();
        var target = _attachTarget;
        var index = _attachTargetIndex;
        ClearAttachTarget();
        if (target is null || !target.IsVisible || !target.CanAcceptTabAttachAt(cursor)) return;
        var session = _documents.Documents.Count > 0 ? _documents.Detach(0) : null;
        if (session is null) return;
        target.AdoptDroppedDocument(session, Math.Max(0, index));
        Close();
    }

    private void ClearAttachTarget()
    {
        _attachTarget?.SetTabAttachHighlight(false);
        _attachTarget = null;
        _attachTargetIndex = -1;
    }

    internal bool CanAcceptTabAttachAt(Point screenPoint)
    {
        if (!IsVisible || !IsEnabled) return false;
        var strip = Tabs.GetScreenRect();
        var scale = Math.Max(1.0, VisualTreeHelper.GetDpi(this).DpiScaleY);
        var horizontal = Math.Max(24, 28 * scale);
        var top = Math.Max(4, 6 * scale);
        var bottom = Math.Max(56, 78 * scale);
        var zone = new Rect(strip.Left - horizontal, strip.Top - top,
            strip.Width + horizontal * 2, strip.Height + top + bottom);
        return zone.Contains(screenPoint);
    }

    internal int GetTabAttachInsertIndex(Point screenPoint) => Tabs.GetInsertionIndexAtScreen(screenPoint);

    internal void SetTabAttachHighlight(bool highlighted)
    {
        if (TitleBar is null) return;
        // Whole-strip attach treatment: a full accent outline and a soft fill across the complete
        // title row, including the tab buttons and unused caption space.
        TitleBar.BorderBrush = (Brush)FindResource(highlighted ? "AccentBrush" : "ChromeBorderBrush");
        TitleBar.Background = (Brush)FindResource(highlighted ? "AccentSoftBrush" : "ChromeStripBrush");
        TitleBar.BorderThickness = highlighted ? new Thickness(2) : new Thickness(0, 0, 0, 1);
        TitleBar.CornerRadius = highlighted ? new CornerRadius(4) : new CornerRadius(0);
    }

    private static bool TryGetScreenCursor(out Point point)
    {
        if (GetCursorPos(out var native)) { point = new Point(native.X, native.Y); return true; }
        point = default;
        return false;
    }

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

    /// <summary>Adopts a document dropped onto this window's tab strip.</summary>
    private void AdoptDroppedDocument(DocumentSession session, int index)
    {
        CaptureDocumentState();
        _documents.Insert(session, Math.Clamp(index, 0, _documents.Documents.Count));
        ActivateDocument(session, focusTabSelection: true);
        StatusText.Text = $"Opened {session.DisplayName} here";
    }

    /// <summary>
    /// The whole window accepts a dropped tab (browser behaviour): dropping anywhere merges the
    /// document into this window. Drops on the tab strip are handled by the tab bar itself, which
    /// marks the event handled so this fallback only runs for the rest of the window.
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format)) return;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(TabDragService.Format)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.Move;
        var index = _documents.Documents.Count;

        if (TabDragService.Current is { } incoming)
        {
            if (_documents.IndexOf(incoming.Session) >= 0) return;   // our own tab: the strip handled it
            incoming.Consumed = true;
            AdoptDroppedDocument(incoming.Session, index);
            return;
        }

        var json = e.Data.GetData(TabDragService.Format) as string;
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var project = ProjectService.Restore(json);
            var doc = DocumentSession.FromProject(project, null);
            ApplyPreferredScoreView(doc);
            doc.Project.IsDirty = true;
            AdoptDroppedDocument(doc, index);
        }
        catch
        {
            // Corrupt payload: ignore the drop rather than crashing.
        }
    }

    /// <summary>Creates a window that starts with the given document instead of a blank tab.</summary>
    private void OpenWindowWith(DocumentSession session, Point? screenPosition)
    {
        var window = new MainWindow();
        // A newly detached document window is a normal, positioned child window. The primary
        // application window is maximised by default, but that is not appropriate for tear-off.
        window.WindowState = WindowState.Normal;
        window.AdoptSession(session);
        if (screenPosition is { } p)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = p.X;
            window.Top = p.Y;
        }
        window.Show();
        window.Activate();
    }

    /// <summary>Replaces the freshly-created blank tab with a document handed over from another window.</summary>
    public void AdoptSession(DocumentSession session)
    {
        while (_documents.Documents.Count > 0)
            _documents.Detach(0)?.DisposePlayback();
        _documents.Insert(session, 0);
        ActivateDocument(session, focusTabSelection: true);
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

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;
    private const int VK_LBUTTON = 0x01;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativeScreenPoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeScreenPoint { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    /// <summary>
    /// Starts a real caption drag through the non-client path, so Aero Snap, the double-click
    /// maximise and the right-click system menu keep working even though the caption is hit-testable.
    /// </summary>
    private static void NativeCaptionDrag(Window window)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }
}
