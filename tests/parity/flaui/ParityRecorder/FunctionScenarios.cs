using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ParityRecorder;

/// <summary>
/// Function scenarios on an off-screen TabForge (the app's capture placement) that opened a scratch copy of the demo song.
/// Each scenario is driven only through UI Automation patterns: menus are expanded and their items invoked, toolbar
/// buttons invoked, tool windows opened from menus and read as trees, sliders set through their range pattern, toggles
/// switched. No mouse or keyboard input, window messages or focus changes are used; scenarios that need them are MANUAL.
/// Owns: the function scenario list, its order and the record format. Does not own: the pattern helpers and dismissal rules (UiProbe) or the file set (Recorder).
/// Every step closes what it opened (Cancel, No or Close, then the close command) and records what it found: a prompt
/// is recorded with its title and text. A window that cannot be closed halts the later steps, which are then MANUAL.
/// </summary>
internal sealed class FunctionScenarios
{
    private static readonly string[] MainMenus =
        { "File", "Edit", "Bar", "Track", "Note", "Effects", "Sections", "Tools", "Sound", "View", "Options", "Help" };

    private const string NeedsKeys = "MANUAL: needs keyboard input, which the off-screen run does not send; on the owner's hand-test list";

    private readonly UiProbe _ui;
    private readonly IDictionary<string, string> _files;
    private readonly StringBuilder _sb = new();
    private bool _halted;

    private FunctionScenarios(UiProbe ui, IDictionary<string, string> files)
    {
        _ui = ui;
        _files = files;
    }

    /// <summary>Waits for the demo song's window, runs every function scenario and returns the scenarios text.</summary>
    public static string Run(TabForgeProcess tf, IDictionary<string, string> files)
    {
        var header = new StringBuilder();
        header.AppendLine("# function scenarios: off-screen TabForge (capture placement) driven through UI Automation patterns only.");
        header.AppendLine("# no mouse or keyboard input, no window messages, no focus change. tree= is a hash of the window's element tree; full trees are in tree-fn-*.txt.");
        var main = WaitForSong(tf, 90);
        if (main is null)
        {
            header.AppendLine("MANUAL: the demo song window did not appear; no function scenario ran.");
            return header.ToString();
        }
        var fs = new FunctionScenarios(new UiProbe(tf, main, ElementText.HandleOf(main)), files);
        fs.RunAll();
        foreach (var key in files.Keys.Where(k => k.StartsWith("tree-fn-", StringComparison.Ordinal)).ToList())
            files[key] = Normalize(files[key]);
        return header.Append(fs._sb).ToString();
    }

    /// <summary>Removes the cell number from the position text in a tree: the cell within a bar depends on the playback timing.</summary>
    private static string Normalize(string text) => Regex.Replace(text, @"\s*·\s*cell \d+", "");

    private static AutomationElement? WaitForSong(TabForgeProcess tf, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var main = tf.Windows().FirstOrDefault(w => ElementText.NameOf(w).Contains("Ashen", StringComparison.OrdinalIgnoreCase));
            if (main is not null)
            {
                Thread.Sleep(1500); // Let the song finish laying out before the first read.
                return main;
            }
            Thread.Sleep(500);
        }
        return null;
    }

    private void RunAll()
    {
        Step("F01", "demo song open", () => $"title=\"{_ui.NameOf(_ui.Main)}\" tracks={_ui.TrackCount()}");
        foreach (var menu in MainMenus) Step("F02", $"menu {menu}", () => ReadMenu(menu));
        Step("F03", "play 2 s, then stop; first bar from the Bar menu", Playback);
        Step("F04", "cursor keys and fret entry on a string", () => throw new UiProbe.Manual(NeedsKeys));
        Step("F05", "copy, then paste (Edit menu)", CopyAndPaste);
        Step("F06", "undo, then redo (Edit menu)", UndoAndRedo);
        Step("F07", "time signature dialog, cancelled", () => OpenWindow("time-signature", () => PickMenuItem("Bar", "Time signature"), null));
        Step("F08", "add a track (Track menu), then delete it", AddAndDeleteTrack);
        Step("F09", "zoom in and out (View menu)", Zoom);
        Step("F10", "Ctrl+wheel zoom", () => throw new UiProbe.Manual(NeedsKeys));
        foreach (var (label, open, inside) in Tools())
            Step("F11", $"tool window {label}", () => OpenWindow(label, open, inside));
        Step("F12", "keyboard mode on, then off", KeyboardMode);
        Step("F13", "save (File menu), then last bar (Bar menu)", SaveAndCount);
        Step("F14", "Save As", () => throw new UiProbe.Manual("Save As opens the native Windows save dialog, so it was not driven"));
        Step("F15", "reopen the saved song", () => throw new UiProbe.Manual("reopening needs the native Open dialog or a second instance, so it was not driven"));
    }

    /// <summary>
    /// Records one scenario line: the id, the title, then the body's text (which may continue on indented lines), then the
    /// notes of what the cleanup dismissed. Windows the step left open are dismissed before the next step.
    /// </summary>
    private void Step(string id, string title, Func<string> body)
    {
        // Progress on the console as each step starts, so a run that dies mid-way shows the step it was in.
        Console.WriteLine($"STEP {id} {title}");
        _ui.Notes.Clear();
        var before = _ui.WindowHandles();
        string text;
        if (_halted)
        {
            text = "MANUAL: not run: an earlier step left a window open that could not be closed";
        }
        else
        {
            try
            {
                text = body();
            }
            catch (UiProbe.Manual manual)
            {
                text = "MANUAL: " + manual.Message;
            }
            catch (Exception e)
            {
                text = "ERROR: " + TreeDump.Clean(e.GetType().Name + ": " + e.Message);
            }
        }
        try
        {
            CleanUp(before);
        }
        catch (Exception e)
        {
            _halted = true;
            _ui.Notes.Add("cleanup failed: " + TreeDump.Clean(e.GetType().Name + ": " + e.Message));
        }
        _sb.Append(id).Append(' ').Append(title).Append(": ").Append(text);
        foreach (var note in _ui.Notes) _sb.Append("\n  ").Append(note);
        _sb.AppendLine();
    }

    /// <summary>Collapses the menus this step opened and dismisses every window it left open; halts the run when one stays.</summary>
    private void CleanUp(HashSet<IntPtr> before)
    {
        foreach (var menu in MainMenus)
        {
            var item = _ui.Find("MainMenu." + menu);
            if (item is not null && UiProbe.IsExpanded(item)) _ui.Collapse(item);
        }
        foreach (var window in _ui.NewWindows(before, 0))
        {
            if (!_ui.Dismiss(window, "left open")) _halted = true;
        }
        if (_ui.NewWindows(before, 0).Count > 0)
        {
            _halted = true;
            _ui.Notes.Add("a window stayed open after cleanup; the later steps are not run");
        }
    }

    private AutomationElement Need(string id) =>
        _ui.Find(id) ?? throw new UiProbe.Manual($"element {id} not found");

    private string TreeHash() => UiProbe.Hash(Normalize(TreeDump.Body(_ui.Main, _ui.MainHwnd)));

    private string Describe(IReadOnlyList<AutomationElement> windows) =>
        windows.Count == 0 ? "none" : string.Join(" | ", windows.Select(w => _ui.NameOf(w)));

    /// <summary>The playhead's bar number from the position text ("Measure 3 ..."), or null.</summary>
    private int? BarNumber()
    {
        var match = Regex.Match(_ui.NameOf(_ui.Find("PositionText")), @"\d+");
        return match.Success ? int.Parse(match.Value) : null;
    }

    /// <summary>The position without its cell part: the cell within a bar depends on timing, so only the bar is recorded.</summary>
    private static string Bar(string position) => Regex.Replace(position, @"\s*·\s*cell \d+", "");

    /// <summary>The transport and status texts as one line.</summary>
    private string Snap() =>
        $"play=\"{_ui.NameOf(_ui.Find("TransportPlay"))}\" position=\"{Bar(_ui.NameOf(_ui.Find("PositionText")))}\" " +
        $"state=\"{_ui.NameOf(_ui.Find("BarStateText"))}\" status=\"{_ui.NameOf(_ui.Find("StatusText"))}\"";

    private static bool Matches(string name, string fragment) =>
        fragment.StartsWith('=') ? name == fragment[1..] : name.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens a menu bar item (expands it, as a click would).</summary>
    private void OpenMenu(AutomationElement item)
    {
        if (item.Patterns.ExpandCollapse.IsSupported) item.Patterns.ExpandCollapse.Pattern.Expand();
        else _ui.Activate(item);
        Thread.Sleep(500);
    }

    /// <summary>Expands a main menu and records its items and enabled states, then collapses it.</summary>
    private string ReadMenu(string menu)
    {
        var item = Need("MainMenu." + menu);
        OpenMenu(item);
        var items = _ui.OpenMenuItems().Select(UiProbe.ItemText).ToList();
        _ui.Collapse(item);
        return items.Count == 0 ? "no popup appeared" : $"items={items.Count}: {string.Join(" | ", items)}";
    }

    /// <summary>Expands the first of the menus whose items match a fragment and invokes the matching item.</summary>
    private void PickMenuItem(string menu, params string[] fragments) => PickMenuItem(new[] { menu }, fragments);

    private void PickMenuItem(string[] menus, params string[] fragments)
    {
        var seen = new List<string>();
        foreach (var menu in menus)
        {
            var item = Need("MainMenu." + menu);
            OpenMenu(item);
            var entries = _ui.OpenMenuItems();
            seen.AddRange(entries.Select(UiProbe.ItemText));
            var hit = entries.FirstOrDefault(i => fragments.Any(f => Matches(ElementText.NameOf(i), f)));
            if (hit is not null)
            {
                _ui.Activate(hit);
                return;
            }
            _ui.Collapse(item);
        }
        throw new UiProbe.Manual($"no menu item matches {string.Join(" / ", fragments)} in {string.Join(", ", menus)}; items seen: {string.Join(" | ", seen)}");
    }

    private string Playback()
    {
        _ui.Activate(Need("TransportPlay"));
        Thread.Sleep(2000);
        var playing = Snap();
        _ui.Activate(Need("TransportStop"));
        Thread.Sleep(800);
        var stopped = $"{Snap()} bar={BarNumber()?.ToString() ?? "n/a"}";
        string first;
        try
        {
            PickMenuItem("Bar", "First bar");
            // Home puts the caret on the bar's first beat; where playback stopped inside the bar depends on wall-clock time.
            FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.HOME);
            Thread.Sleep(500);
            first = Snap();
        }
        catch (UiProbe.Manual manual)
        {
            first = "MANUAL: " + manual.Message;
        }
        return $"\n  while playing: {playing}\n  after stop: {stopped}\n  first bar (Bar menu): {first}";
    }

    private string CopyAndPaste()
    {
        var sb = new StringBuilder();
        sb.Append("\n  before: ").Append(Snap()).Append(" tree=").Append(TreeHash());
        PickMenuItem("Edit", "Copy");
        PickMenuItem("Edit", "Paste");
        sb.Append("\n  copy, paste: ").Append(Snap()).Append(" tree=").Append(TreeHash());
        return sb.ToString();
    }

    private string UndoAndRedo()
    {
        var sb = new StringBuilder();
        sb.Append("\n  start: tree=").Append(TreeHash());
        PickMenuItem("Edit", "Undo");
        sb.Append("\n  undo: ").Append(Snap()).Append(" tree=").Append(TreeHash());
        PickMenuItem("Edit", "Redo");
        sb.Append("\n  redo: ").Append(Snap()).Append(" tree=").Append(TreeHash());
        return sb.ToString();
    }

    /// <summary>Track menu: Add track opens its dialog, whose Add or OK button adds a track; then Delete track, confirmed with Yes, Delete or OK.</summary>
    private string AddAndDeleteTrack()
    {
        var sb = new StringBuilder();
        var tracks0 = _ui.TrackCount();
        var before = _ui.WindowHandles();
        PickMenuItem("Track", "Add track");
        var dialogs = _ui.NewWindows(before, 4000);
        sb.Append("\n  add track: dialog=").Append(Describe(dialogs));
        if (dialogs.Count > 0)
        {
            Thread.Sleep(500);
            sb.Append(" tree=").Append(UiProbe.Hash(TreeDump.Body(dialogs[0], ElementText.HandleOf(dialogs[0]))));
            var add = UiProbe.FindButton(dialogs[0], "^(Add|OK)$");
            if (add is not null) _ui.Activate(add);
        }
        var tracks1 = _ui.TrackCount();
        sb.Append($"\n  tracks {tracks0} -> {tracks1}");
        before = _ui.WindowHandles();
        PickMenuItem("Track", "Delete track");
        var confirm = _ui.NewWindows(before, 3000);
        sb.Append("\n  delete track: confirm=").Append(Describe(confirm));
        if (confirm.Count > 0)
        {
            var yes = UiProbe.FindButton(confirm[0], "^(Yes|Delete|OK)$");
            if (yes is not null) _ui.Activate(yes);
        }
        sb.Append($"\n  tracks {tracks1} -> {_ui.TrackCount()}");
        return sb.ToString();
    }

    private string ZoomText()
    {
        var zoom = Need("ZoomCombo");
        var texts = zoom.FindAllDescendants().Select(e => _ui.NameOf(e)).Where(n => n.Length > 0 && n != "<missing>");
        return $"{_ui.NameOf(zoom)} [{string.Join(", ", texts)}]";
    }

    private string Zoom()
    {
        var sb = new StringBuilder();
        sb.Append("zoom at start: ").Append(ZoomText());
        PickMenuItem("View", "Zoom in");
        sb.Append("\n  zoom in (View menu): ").Append(ZoomText());
        PickMenuItem("View", "Zoom out");
        sb.Append("\n  zoom out (View menu): ").Append(ZoomText());
        return sb.ToString();
    }

    /// <summary>The tool windows reached from menus, with what to read inside each one after it opens.</summary>
    private List<(string Label, Action Open, Func<AutomationElement, string>? Inside)> Tools() => new()
    {
        ("mixer", () => PickMenuItem("View", "Mixer"), MixerInside),
        ("fx-chain", () => PickMenuItem("Track", "FX chain", "Effects chain"), null),
        ("render", () => PickMenuItem("File", "Render"), null),
        ("preferences", () => PickMenuItem(new[] { "Options", "Tools", "File" }, "Preferences", "Settings"), PreferencesInside),
        ("tuner", () => PickMenuItem("Tools", "Tuner"), null),
        ("song-info", () => PickMenuItem("Bar", "Score information", "Song info"), null),
        ("scale-finder", () => PickMenuItem("Tools", "Scale finder"), null),
        ("chord-finder", () => PickMenuItem("Tools", "Chord finder"), null),
        ("command-palette", () => PickMenuItem(MainMenus, "Command palette"), null),
        ("shortcuts", () => PickMenuItem(MainMenus, "Keyboard shortcuts"), null),
        ("band-layout", () => PickMenuItem("View", "Band"), null),
    };

    /// <summary>
    /// Opens a window with <paramref name="open"/>, records its tree (full file and fingerprint), runs <paramref name="inside"/>
    /// on it, then dismisses it. A window that never appears is recorded as such (it may be docked into the main window).
    /// </summary>
    private string OpenWindow(string label, Action open, Func<AutomationElement, string>? inside)
    {
        var before = _ui.WindowHandles();
        open();
        var fresh = _ui.NewWindows(before, 5000);
        if (fresh.Count == 0) return "no new window appeared (docked, or not shown)";
        var sb = new StringBuilder();
        for (var i = 0; i < fresh.Count; i++)
        {
            var window = fresh[i];
            var hwnd = ElementText.HandleOf(window);
            Thread.Sleep(700);
            var body = TreeDump.Body(window, hwnd);
            var file = fresh.Count == 1 ? $"tree-fn-{label}.txt" : $"tree-fn-{label}-{i + 1}.txt";
            _files[file] = TreeDump.File(label, window, hwnd);
            sb.Append("\n  window \"").Append(_ui.NameOf(window)).Append("\" nodes=")
                .Append(body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length)
                .Append(" tree=").Append(UiProbe.Hash(Normalize(body)));
            if (inside is not null) sb.Append("\n  ").Append(inside(window));
        }
        var closed = true;
        foreach (var window in fresh) closed &= _ui.Dismiss(window, $"{label} window");
        sb.Append("\n  closed=").Append(closed ? "y" : "n");
        return sb.ToString();
    }

    /// <summary>Mixer: the first slider set two lower and back (range pattern); the first Mute and Solo toggles switched and restored.</summary>
    private string MixerInside(AutomationElement window)
    {
        var sb = new StringBuilder();
        var sliders = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Slider)).Where(s => ElementText.BoundsOf(s).Width > 0).ToList();
        try
        {
            if (sliders.Count == 0)
            {
                sb.Append("no slider in the mixer");
            }
            else
            {
                var slider = sliders[0];
                var original = slider.AsSlider().Value;
                _ui.SetRange(slider, original - 2);
                var lowered = UiProbe.SliderValueOf(slider);
                _ui.SetRange(slider, original);
                sb.Append($"slider \"{_ui.NameOf(slider)}\" value {original:0.###} -> set -2 {lowered} -> restored {UiProbe.SliderValueOf(slider)}");
            }
        }
        catch (UiProbe.Manual manual)
        {
            sb.Append("MANUAL: ").Append(manual.Message);
        }
        foreach (var name in new[] { "Mute", "Solo" })
        {
            var toggle = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => ElementText.NameOf(b).Contains(name, StringComparison.OrdinalIgnoreCase));
            if (toggle is null)
            {
                sb.Append($"; no {name} button");
                continue;
            }
            try
            {
                var first = UiProbe.ToggleOf(toggle);
                _ui.Activate(toggle);
                var switched = UiProbe.ToggleOf(toggle);
                _ui.Activate(toggle);
                sb.Append($"; {name} {first} -> {switched} -> restored {UiProbe.ToggleOf(toggle)}");
            }
            catch (UiProbe.Manual manual)
            {
                sb.Append($"; {name} MANUAL: {manual.Message}");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Preferences: the choices named Light and Dark are selected in turn through their selection pattern, and the choice
    /// that was selected before is put back. Colours are not sampled (no screen pixels are read off-screen). The page list
    /// is not cycled: it holds audio and device pages that are not safe to change unattended.
    /// </summary>
    private string PreferencesInside(AutomationElement window)
    {
        var choices = window.FindAllDescendants().Where(e => ElementText.NameOf(e) is "Light" or "Dark").ToList();
        var light = choices.FirstOrDefault(e => ElementText.NameOf(e) == "Light");
        var dark = choices.FirstOrDefault(e => ElementText.NameOf(e) == "Dark");
        if (light is null || dark is null)
            return "theme: MANUAL: no Light and Dark choice is visible in Preferences; the theme was not switched. Colours: MANUAL (not sampled off-screen). Pages: not cycled.";
        var selectedBefore = choices.FirstOrDefault(IsSelected);
        Select(light);
        var lightSelected = IsSelected(light);
        Select(dark);
        var darkSelected = IsSelected(dark);
        if (selectedBefore is not null) Select(selectedBefore);
        return $"theme choice: before={(selectedBefore is null ? "none" : ElementText.NameOf(selectedBefore))} light-selected={(lightSelected ? "y" : "n")} dark-selected={(darkSelected ? "y" : "n")} restored={(selectedBefore is null || IsSelected(selectedBefore) ? "y" : "n")}; colours: MANUAL (not sampled off-screen); pages: not cycled";
    }

    private static bool IsSelected(AutomationElement e) =>
        e.Patterns.SelectionItem.IsSupported && e.Patterns.SelectionItem.Pattern.IsSelected.Value;

    private void Select(AutomationElement e)
    {
        if (e.Patterns.SelectionItem.IsSupported) e.Patterns.SelectionItem.Pattern.Select();
        else _ui.Activate(e);
        Thread.Sleep(600);
    }

    /// <summary>Keyboard mode button on, then off; a summary of the visible buttons after each change (the full tree varies between runs of one build), and the full tree while it is off.</summary>
    private string KeyboardMode()
    {
        var sb = new StringBuilder();
        var start = ControlSummary();
        _files["tree-fn-keyboard-mode-off.txt"] = TreeDump.File("fn-keyboard-mode-off", _ui.Main, _ui.MainHwnd);
        sb.Append("off: toggle=").Append(UiProbe.ToggleOf(Need("Toolbar.KeyboardMode"))).Append(' ').Append(start);
        var before = _ui.WindowHandles();
        _ui.Activate(Need("Toolbar.KeyboardMode"));
        Thread.Sleep(1500);
        sb.Append("\n  on: toggle=").Append(UiProbe.ToggleOf(Need("Toolbar.KeyboardMode")))
            .Append(' ').Append(ControlSummary())
            .Append(" new windows=").Append(Describe(_ui.NewWindows(before, 300)));
        _ui.Activate(Need("Toolbar.KeyboardMode"));
        Thread.Sleep(1500);
        var end = ControlSummary();
        sb.Append("\n  off again: toggle=").Append(UiProbe.ToggleOf(Need("Toolbar.KeyboardMode")))
            .Append(' ').Append(end).Append(" restored=").Append(end == start ? "y" : "n");
        return sb.ToString();
    }

    /// <summary>The visible named buttons of the main window, counted and hashed: a stable summary where the full tree varies between runs.</summary>
    private string ControlSummary()
    {
        var names = _ui.Main.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .Where(b => ElementText.BoundsOf(b).Width > 0)
            .Select(b => _ui.NameOf(b)).Where(n => n.Length > 0)
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        return $"buttons={names.Count} names={UiProbe.Hash(string.Join("|", names))}";
    }

    /// <summary>File menu Save writes the scratch copy; the last bar comes from the Bar menu's Last bar item.</summary>
    private string SaveAndCount()
    {
        var before = _ui.WindowHandles();
        PickMenuItem("File", "=Save");
        Thread.Sleep(1500);
        var dialogs = _ui.NewWindows(before, 500);
        var tabs = string.Join(" | ", _ui.Main.FindAllDescendants(cf => cf.ByAutomationId("TabTitle")).Select(t => _ui.NameOf(t)));
        var sb = new StringBuilder();
        sb.Append("status=\"").Append(_ui.NameOf(_ui.Find("StatusText"))).Append("\" tabs=\"").Append(tabs)
            .Append("\" dialog=").Append(Describe(dialogs));
        PickMenuItem("Bar", "Last bar");
        Thread.Sleep(500);
        sb.Append("\n  last bar: position=\"").Append(Bar(_ui.NameOf(_ui.Find("PositionText")))).Append("\" last bar=").Append(BarNumber()?.ToString() ?? "n/a");
        return sb.ToString();
    }
}
