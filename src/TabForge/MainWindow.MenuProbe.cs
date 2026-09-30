using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, menu probe: `TabForge.exe <song> --probe-menus <folder>` clicks every item of the main menu
// through its real handler (on a temporary copy of the song), and records whether the song, the view or
// the settings changed, which dialog or message appeared, and whether Undo restores an edit. Dialogs are
// photographed and cancelled; Windows file/print dialogs and message boxes are closed automatically.
public partial class MainWindow
{
    // Exit would close the app; Check for updates would contact GitHub (tested headlessly without network).
    private static readonly string[] MenuProbeSkip = { "E_xit", "Check for updates..." };

    public void RunMenuProbe(string folder)
    {
        var dir = FilePathPolicy.OutputDirectory(folder, "probe folder");
        Directory.CreateDirectory(dir);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            _probeNoSave = true;
            var report = new StringBuilder();
            var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            var nativeSeen = new List<string>();
            closer.Tick += (_, _) => CloseNativeDialogs(nativeSeen);
            closer.Start();
            string? song = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(File.Exists);
            var workCopy = Path.Combine(Path.GetTempPath(), "tabforge-menu-probe" + Path.GetExtension(song ?? ".gp5"));
            var original = JsonSerializer.Serialize(_settings);
            int ok = 0, noEffect = 0, failed = 0;
            try
            {
                await Task.Delay(1500);
                if (song is null) throw new InvalidOperationException("the probe needs a song file on the command line");
                File.Copy(song, workCopy, true);
                async Task Reset()
                {
                    foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w != this).ToList()) w.Close();
                    if (_midi.IsPlaying) StopPlayback();
                    File.Copy(song!, workCopy, true);
                    foreach (var d in _documents.Documents) d.MarkClean();
                    await OpenDocumentFromPath(workCopy, replaceCurrent: true, replaceAll: true);
                    _settings = JsonSerializer.Deserialize<AppSettings>(original)!;
                    _settings.Editing.ConfirmDeleteBar = false; // run the real delete, not just its prompt
                    _settings.General.ConfirmDeleteSection = false;
                    SyncFromSettings(applyWindowSize: false);
                    _dockWorkspace?.RestoreLayout(_settings.Workspace); // panels an earlier item hid or moved
                    InstrumentViewMenu.IsChecked = true; ArrangementMenu.IsChecked = true;
                    await Task.Delay(250);
                    PlaceCursorOnNote();
                    await Task.Delay(150);
                }
                await Reset();

                var paths = new List<string[]>();
                var menu = FindVisuals<Menu>(this).First(m => m.Items.Count > 3);
                foreach (var top in menu.Items.OfType<MenuItem>().ToList())
                {
                    top.IsSubmenuOpen = true;
                    await Task.Delay(150);
                    Collect(top, new List<string>(), paths);
                    top.IsSubmenuOpen = false;
                }

                DialogHost.Capture = dialog =>
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                    dialog.Left = SystemParameters.VirtualScreenLeft - dialog.Width - 200;
                    dialog.Top = 0;
                    dialog.ShowActivated = false;
                    dialog.Show();
                    Pump();
                    _probeDialog = $"{dialog.GetType().Name} \"{dialog.Title}\"";
                    ShootTo(dialog, Path.Combine(dir, Slug(string.Join("-", _probePath)) + "-dialog.png"));
                    dialog.Close();
                    return false;
                };

                foreach (var path in paths)
                {
                    var label = string.Join(" > ", path.Select(p => p.Replace("_", "")));
                    if (MenuProbeSkip.Contains(path[^1])) { report.AppendLine($"SKIPPED   {label} (would close the app)"); continue; }
                    var item = Find(menu, path);
                    if (item is null) { report.AppendLine($"MISSING   {label}"); failed++; continue; }
                    if (!item.IsEnabled) { report.AppendLine($"DISABLED  {label} (greyed out with a note selected)"); continue; }

                    _probePath = path; _probeDialog = null; nativeSeen.Clear();
                    var projectBefore = Convert.ToHexString(ProjectService.ContentHash(_project));
                    var settingsBefore = JsonSerializer.Serialize(_settings);
                    var tabsBefore = _documents.Documents.Count;
                    var statusBefore = StatusText.Text;
                    var screenBefore = WindowHash();
                    var cursorBefore = (Editor.SelectedMeasure, Editor.SelectedString);
                    string? error = null;
                    try
                    {
                        if (item.IsCheckable) item.IsChecked = !item.IsChecked; // a real click ticks it first
                        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                        await Task.Delay(450);
                    }
                    catch (Exception ex) { error = ex.GetBaseException().Message; }

                    var projectAfter = Convert.ToHexString(ProjectService.ContentHash(_project));
                    var effects = new List<string>();
                    if (projectAfter != projectBefore) effects.Add("song edited");
                    if (_documents.Documents.Count != tabsBefore) effects.Add($"tabs {tabsBefore}->{_documents.Documents.Count}");
                    if (JsonSerializer.Serialize(_settings) != settingsBefore) effects.Add("settings changed");
                    if ((Editor.SelectedMeasure, Editor.SelectedString) != cursorBefore) effects.Add("cursor moved");
                    if (_midi.IsPlaying) effects.Add("playing");
                    var extra = Application.Current.Windows.OfType<Window>().Where(w => w != this && w.IsVisible).ToList();
                    foreach (var w in extra)
                    {
                        effects.Add($"window \"{w.Title}\"");
                        ShootTo(w, Path.Combine(dir, Slug(string.Join("-", path)) + "-window.png"));
                    }
                    if (_probeDialog is not null) effects.Add("dialog " + _probeDialog);
                    if (nativeSeen.Count > 0) effects.Add("windows dialog/message: " + string.Join(" | ", nativeSeen));
                    if (StatusText.Text != statusBefore) effects.Add($"status \"{StatusText.Text}\"");
                    if (effects.Count == 0 && WindowHash() != screenBefore) effects.Add("screen changed");
                    SaveWindowPng(Path.Combine(dir, Slug(string.Join("-", path)) + ".png"));

                    if (projectAfter != projectBefore && error is null)
                    {
                        Undo_Click(this, new RoutedEventArgs());
                        await Task.Delay(200);
                        effects.Add(Convert.ToHexString(ProjectService.ContentHash(_project)) == projectBefore ? "undo restores" : "UNDO DOES NOT RESTORE");
                    }

                    if (error is not null) { failed++; report.AppendLine($"ERROR     {label}: {error}"); }
                    else if (effects.Count == 0) { noEffect++; report.AppendLine($"NO-EFFECT {label}"); }
                    else { ok++; report.AppendLine($"OK        {label}: {string.Join("; ", effects)}"); }
                    await Reset();
                }
                report.Insert(0, $"{paths.Count} menu items: {ok} did something, {noEffect} no visible effect, {failed} errors\n");
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); }
            finally
            {
                closer.Stop();
                DialogHost.Capture = null;
            }
            DiagnosticFileService.WriteText(Path.Combine(dir, "menu-report.txt"), report.ToString());
            foreach (var d in _documents.Documents) d.MarkClean();
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }

    private string[] _probePath = Array.Empty<string>();
    private string? _probeDialog;

    private static void Collect(MenuItem item, List<string> trail, List<string[]> into)
    {
        var header = item.Header?.ToString() ?? "";
        var here = new List<string>(trail) { header };
        var children = item.Items.OfType<MenuItem>().ToList();
        if (children.Count == 0) { if (trail.Count > 0) into.Add(here.ToArray()); return; }
        foreach (var child in children) Collect(child, here, into);
    }

    private static MenuItem? Find(ItemsControl root, string[] path)
    {
        ItemsControl? node = root;
        MenuItem? found = null;
        foreach (var header in path)
        {
            found = node?.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header?.ToString() ?? "") == header);
            if (found is null) return null;
            node = found;
        }
        return found;
    }

    private void PlaceCursorOnNote()
    {
        for (var measure = 0; measure < 4; measure++)
            for (var cell = 0; cell < 16; cell++)
                for (var s = 0; s < 6; s++)
                {
                    Editor.SelectForEdit(measure, cell, s);
                    if (Editor.HasEditableNotes) return;
                }
    }

    private void ShootTo(Window window, string file)
    {
        try
        {
            window.UpdateLayout();
            var w = Math.Max(1, (int)window.ActualWidth); var h = Math.Max(1, (int)window.ActualHeight);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
    }

    // ---- native dialogs (file pickers, print, MessageBox): note their title and text, then cancel them ----

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    private static void CloseNativeDialogs(List<string> seen)
    {
        var pid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(64); GetClassName(hwnd, cls, 64);
            if (cls.ToString() != "#32770") return true;
            var title = new StringBuilder(256); GetWindowText(hwnd, title, 256);
            var text = new StringBuilder();
            EnumChildWindows(hwnd, (child, _) =>
            {
                var c = new StringBuilder(32); GetClassName(child, c, 32);
                if (c.ToString() == "Static")
                {
                    var t = new StringBuilder(512); GetWindowText(child, t, 512);
                    if (t.Length > 0) text.Append(t).Append(' ');
                }
                return true;
            }, IntPtr.Zero);
            var entry = $"\"{title}\" {text.ToString().Trim()}".Trim();
            if (!seen.Contains(entry)) seen.Add(entry);
            const uint WM_COMMAND = 0x0111;
            PostMessage(hwnd, WM_COMMAND, (IntPtr)2, IntPtr.Zero); // IDCANCEL
            PostMessage(hwnd, WM_COMMAND, (IntPtr)7, IntPtr.Zero); // IDNO (Yes/No boxes have no Cancel)
            return true;
        }, IntPtr.Zero);
    }
}
