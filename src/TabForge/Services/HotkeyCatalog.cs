using System.Linq;

namespace TabForge.Services;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>One customisable command: a stable id, where it appears, its default gesture and what it does.</summary>
public sealed record HotkeyAction(string Id, string Category, string Name, string DefaultGesture, string Description = "", string DefaultGesture2 = "");

// Owns: the catalogue of every bindable command with its default key, and the key presets.
// Does not own: running the commands and key routing (WindowKeyRouter).
// Tests: TestNewBindableCommands, TestKeyRoutingOrder.
/// <summary>
/// The catalogue of every command that can be bound to a key. The settings store only the bindings the
/// user has changed; everything else falls back to <see cref="HotkeyAction.DefaultGesture"/>.
/// </summary>
public static partial class HotkeyCatalog
{
    private static readonly IReadOnlyDictionary<string, string> KeyNames = BuildKeyNames();
    public const string CategoryFile = "File and tabs";
    public const string CategoryEdit = "Editing";
    public const string CategoryTransport = "Transport";
    public const string CategoryBar = "Bars and sections";
    public const string CategoryNote = "Notes and tracks";
    public const string CategoryView = "View";
    public const string CategoryEffects = "Note effects";
    public const string CategoryDuration = "Durations";
    /// <summary>Clip commands: they only act while an audio/MIDI clip (or a clip lane) is selected, so they may
    /// share keys with score commands (Delete, arrows, Ctrl+C...).</summary>
    public const string CategoryClips = "Audio and MIDI clips (while a clip is selected)";

    /// <summary>Clip-context commands (see <see cref="CategoryClips"/>).</summary>
    public static bool IsClipAction(string id) => id.StartsWith("Clip.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Track-row commands: they only act while a track row of the track list has the focus, so they may share keys with
    /// score commands (Ctrl+C, Delete...).</summary>
    public const string CategoryTrackRows = "Tracks (while a track row is focused)";

    /// <summary>Track-row context commands (see <see cref="CategoryTrackRows"/>).</summary>
    public static bool IsTrackRowAction(string id) => id.StartsWith("TrackRow.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bar-range commands: they only act while bars are selected and the timeline has the focus, so they may share keys with score commands (Delete, Ctrl+Delete).</summary>
    public const string CategoryRange = "Selected bars (while the timeline has the focus)";

    /// <summary>Bar-range context commands (see <see cref="CategoryRange"/>).</summary>
    public static bool IsRangeAction(string id) => id.StartsWith("Range.", StringComparison.OrdinalIgnoreCase);

    /// <summary>0 = the global map, 1 = clip context, 2 = track-row context, 3 = bar-range context: keys only collide inside one context.</summary>
    public static int ContextOf(string id) => IsClipAction(id) ? 1 : IsTrackRowAction(id) ? 2 : IsRangeAction(id) ? 3 : 0;

    /// <summary>Two commands can share a key when they act in different contexts.</summary>
    public static bool SameContext(string a, string b) => ContextOf(a) == ContextOf(b);

    private static readonly List<HotkeyAction> Actions = BuildActions();

    /// <summary>Every bindable command: the fixed catalogue plus palette tools registered at start-up.</summary>
    public static IReadOnlyList<HotkeyAction> All => Actions;

    private static Dictionary<string, HotkeyAction> ByIdMap =
        Actions.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds bindable actions (e.g. every tool-palette icon). Ids already present are ignored, so new
    /// palette tools become mappable automatically without duplicating existing commands.
    /// </summary>
    public static void Register(IEnumerable<HotkeyAction> actions)
    {
        foreach (var action in actions)
            if (!ByIdMap.ContainsKey(action.Id)) { Actions.Add(action); ByIdMap[action.Id] = action; }
    }

    public static HotkeyAction? ById(string id) =>
        ByIdMap.TryGetValue(id, out var action) ? action : null;

    /// <summary>Commands that still belong to the app while a text box has focus.</summary>
    private static readonly HashSet<string> TextBoxSafe = new(StringComparer.OrdinalIgnoreCase)
    {
        "File.New", "File.Open", "File.OpenInNewTab", "File.Save", "File.SaveAs", "File.Print", "File.PrintPreview",
        "Tab.New", "Tab.Close", "Tab.Duplicate", "App.Preferences", "App.Shortcuts", "App.CommandPalette"
    };

    public static bool AllowsInTextBox(string id) => TextBoxSafe.Contains(id);

    /// <summary>The effective gesture for an action: the user's binding, else the catalogue default.</summary>
    /// <summary>Every effective gesture mapped to its command id (what the window dispatches on).</summary>
    public static Dictionary<string, string> BuildMap(HotkeySettings settings, bool clipContext = false, bool trackRowContext = false, bool rangeContext = false)
    {
        var context = clipContext ? 1 : trackRowContext ? 2 : rangeContext ? 3 : 0;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in All)
        {
            if (ContextOf(action.Id) != context) continue;
            var gesture = GestureFor(settings, action.Id);
            if (string.IsNullOrWhiteSpace(gesture)) continue;
            // Keys are the canonical spelling (what the window builds from a key event), whatever order a binding was written in.
            map[Canonical(gesture)] = action.Id;
        }
        // Hotkey 2 never displaces a Hotkey 1: a gesture some action has as Hotkey 1 keeps that action.
        foreach (var action in All)
        {
            if (ContextOf(action.Id) != context) continue;
            var gesture = GestureFor(settings, action.Id, 2);
            if (string.IsNullOrWhiteSpace(gesture)) continue;
            map.TryAdd(Canonical(gesture), action.Id);
        }
        return map;
    }

    /// <summary>Hotkey 1 of an action: the user's binding, else the preset's, else the catalogue default.</summary>
    public static string GestureFor(HotkeySettings settings, string id) => GestureFor(settings, id, 1);

    /// <summary>One slot of an action (1 = Hotkey 1, 2 = Hotkey 2); the same precedence in both.</summary>
    public static string GestureFor(HotkeySettings settings, string id, int slot)
    {
        if (settings.IsDisabled(id, slot)) return "";
        var custom = settings.Get(id, slot);
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        if (HotkeyPresets.Override(settings.Preset, id, slot) is { } preset) return preset;
        // The "Reverse + / - duration keys" setting swaps the two default keys (a user's own binding is never swapped).
        if (settings.ReverseDurationKeys && id == NoteLonger) id = NoteShorter;
        else if (settings.ReverseDurationKeys && id == NoteShorter) id = NoteLonger;
        var action = ById(id);
        return (slot == 2 ? action?.DefaultGesture2 : action?.DefaultGesture) ?? "";
    }

    /// <summary>The catalogue default of one slot.</summary>
    public static string DefaultFor(HotkeyAction action, int slot) => slot == 2 ? action.DefaultGesture2 : action.DefaultGesture;

    /// <summary>The canonical spelling of a gesture (modifier order fixed); an unparsable text is returned as is.</summary>
    public static string Canonical(string gesture) => TryParse(gesture, out var key, out var mods) ? Format(key, mods) : gesture.Trim();

    public const string NoteLonger ="Note.Longer", NoteShorter = "Note.Shorter";

    // ---------- gesture formatting / parsing ----------

    public static string Format(string key, HotkeyModifiers mods)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        // Ctrl, Alt, Shift: the one canonical order (the catalogue writes "Alt+Shift+Left"); a gesture written in another
        // order still matches because BuildMap and the settings validator normalise through here.
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(key.Trim());
        return string.Join("+", parts);
    }

    public static bool TryParse(string gesture, out string key, out HotkeyModifiers mods)
    {
        key = "";
        mods = HotkeyModifiers.None;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "win": case "windows": mods |= HotkeyModifiers.Windows; continue;
            }
            if (key.Length > 0 || !KeyNames.TryGetValue(part, out var canonical)) return false;
            key = canonical;
        }
        return key.Length > 0;
    }

    /// <summary>Human-readable form, e.g. "Ctrl+Shift+T" or "Ctrl++" for the plus key.</summary>
    public static string Display(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return "";
        if (!TryParse(gesture, out var key, out var mods)) return gesture;
        var text = key.ToLowerInvariant() switch
        {
            "oemplus" => "+",
            "oemminus" => "-",
            "oemperiod" => ".",
            "oemcomma" => ",",
            "add" => "Num +",
            "subtract" => "Num -",
            "decimal" => "Num .",
            "multiply" => "Num *",
            "divide" => "Num /",
            // The punctuation keys as they are printed on a US keyboard (the layout the key names come from).
            "oem1" => ";",
            "oemquestion" => "/",
            "oemopenbrackets" => "[",
            "oemclosebrackets" => "]",
            "oemtilde" => "`",
            "oemquotes" => "'",
            "oempipe" => "\\",
            "d0" or "d1" or "d2" or "d3" or "d4" or "d5" or "d6" or "d7" or "d8" or "d9" => key[1..],
            _ => key
        };
        return Format(text, mods);
    }

    /// <summary>The live keys of a command for menus and tooltips: Hotkey 1, then Hotkey 2 when it is set ("Ctrl+Alt+T, Ctrl+Shift+Insert"); "" when unbound.</summary>
    public static string DisplayAll(HotkeySettings settings, string id)
    {
        var first = Display(GestureFor(settings, id));
        var second = Display(GestureFor(settings, id, 2));
        return first.Length == 0 ? second : second.Length == 0 ? first : first + ", " + second;
    }

    /// <summary>"(Ctrl+S)" for tooltips, or "" when the action has no gesture.</summary>
    public static string TooltipSuffix(HotkeySettings settings, string id)
    {
        var display = DisplayAll(settings, id);
        return string.IsNullOrWhiteSpace(display) ? "" : $" ({display})";
    }

    /// <summary>
    /// A tooltip with the command's key last: "Save the score (Ctrl+S)", or just the text when the command has no key.
    /// A text that already ends in a bracket gets the key inside it ("Zoom (50-200%; Ctrl++)") so there are never two bracket groups.
    /// </summary>
    public static string TooltipWithKey(HotkeySettings settings, string text, string id) =>
        TooltipWithDisplay(text, DisplayAll(settings, id));

    public static string TooltipWithDisplay(string text, string display)
    {
        if (string.IsNullOrWhiteSpace(display)) return text;
        var trimmed = text.TrimEnd();
        if (trimmed.EndsWith(')'))
        {
            var open = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && trimmed.IndexOf('\n', open) < 0)
                return trimmed[..^1] + "; " + display + ")";
        }
        return trimmed + " (" + display + ")";
    }

    private static IReadOnlyDictionary<string, string> BuildKeyNames()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name) => keys[name] = name;
        for (var letter = 'A'; letter <= 'Z'; letter++) Add(letter.ToString());
        for (var digit = 0; digit <= 9; digit++)
        {
            Add("D" + digit);
            Add("NumPad" + digit);
        }
        for (var function = 1; function <= 24; function++) Add("F" + function);
        foreach (var key in new[]
                 {
                     "Cancel", "Back", "Tab", "LineFeed", "Clear", "Return", "Pause", "Capital", "KanaMode",
                     "HangulMode", "JunjaMode", "FinalMode", "HanjaMode", "KanjiMode", "Escape", "ImeConvert",
                     "ImeNonConvert", "ImeAccept", "ImeModeChange", "Space", "Prior", "Next", "End", "Home",
                     "Left", "Up", "Right", "Down", "Select", "Print", "Execute", "Snapshot", "Insert", "Delete",
                     "Help", "LWin", "RWin", "Apps", "Sleep", "Multiply", "Add", "Separator", "Subtract",
                     "Decimal", "Divide", "NumLock", "Scroll", "LeftShift", "RightShift", "LeftCtrl", "RightCtrl",
                     "LeftAlt", "RightAlt", "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop",
                     "BrowserSearch", "BrowserFavorites", "BrowserHome", "VolumeMute", "VolumeDown", "VolumeUp",
                     "MediaNextTrack", "MediaPreviousTrack", "MediaStop", "MediaPlayPause", "LaunchMail",
                     "SelectMedia", "LaunchApplication1", "LaunchApplication2", "Oem1", "OemPlus", "OemComma",
                     "OemMinus", "OemPeriod", "OemQuestion", "OemTilde", "OemOpenBrackets", "OemPipe",
                     "OemCloseBrackets", "OemQuotes", "Oem8", "Oem102", "DeadCharProcessed"
                 }) Add(key);
        return keys;
    }
}

/// <summary>
/// Named key layouts. A preset is a set of differences from the catalogue defaults, so any command
/// added later automatically gets the default key in every preset until a preset says otherwise.
/// The user's own changes (Bindings / DisabledActions) are layered on top: that is "Custom".
/// </summary>
public static class HotkeyPresets
{
    public const string TabForge = "TabForge";
    public const string GuitarPro5 = "Guitar Pro 5";
    public const string TuxGuitar = "TuxGuitar";
    public const string Custom = "Custom";

    public static readonly IReadOnlyList<string> Names = new[] { TabForge, GuitarPro5, TuxGuitar };

    /// <summary>Display name of a preset (its internal id never changes).</summary>
    public static string DisplayName(string id) => id == GuitarPro5 ? "Classic" : id == TuxGuitar ? "Alternative" : id;

    /// <summary>Tooltip of a preset in the picker.</summary>
    public static string Tooltip(string id) => id == GuitarPro5
        ? "A classic tab-editor shortcut layout."
        : "";

    // TabForge's defaults follow the classic layout except Add track (see Override), so this table is empty.
    private static readonly Dictionary<string, string> GuitarPro5Overrides = new(StringComparer.OrdinalIgnoreCase);

    // TuxGuitar 1.6 defaults (Tools → Shortcuts). "" = not bound in TuxGuitar.
    private static readonly Dictionary<string, string> TuxGuitarOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["File.SaveAs"] = "F12",
        ["App.Preferences"] = "F7",
        ["Bar.First"] = "Ctrl+Shift+Left",
        ["Bar.Last"] = "Ctrl+Shift+Right",
        ["Section.Previous"] = "Alt+Left",
        ["Section.Next"] = "Alt+Right",
        ["Note.Dot"] = "Multiply",
        ["Note.FadeIn"] = "F",
        ["Note.Fermata"] = "",
        ["Note.RepeatBeat"] = "",
        ["Transport.Loop"] = "F9",
        ["Tab.Duplicate"] = "",
        ["Edit.PasteSpecial"] = "",
    };

    /// <summary>The preset's Hotkey 1 for an action, or null when the preset uses the catalogue default.</summary>
    public static string? Override(string? preset, string id) => Override(preset, id, 1);

    /// <summary>The preset's key for one slot of an action, or null when the preset uses the catalogue default. The Classic layout keeps Add track on its familiar key as Hotkey 1.</summary>
    public static string? Override(string? preset, string id, int slot)
    {
        if (preset == GuitarPro5 && id == "Track.Add") return slot == 2 ? "Ctrl+Alt+T" : "Ctrl+Shift+Insert";
        if (slot == 2) return null;
        var map = preset switch
        {
            TuxGuitar => TuxGuitarOverrides,
            GuitarPro5 => GuitarPro5Overrides,
            _ => null
        };
        return map is not null && map.TryGetValue(id, out var gesture) ? gesture : null;
    }

    /// <summary>Switches to a preset, dropping the user's own changes.</summary>
    public static void Apply(HotkeySettings settings, string preset)
    {
        settings.Preset = Names.Contains(preset) ? preset : TabForge;
        settings.Bindings.Clear();
        settings.DisabledActions.Clear();
        settings.Bindings2.Clear();
        settings.DisabledActions2.Clear();
    }

    /// <summary>What the preset box should show: the base preset, or Custom once the user changed keys.</summary>
    public static string Describe(HotkeySettings settings) =>
        settings.Bindings.Count > 0 || settings.DisabledActions.Count > 0 ||
        settings.Bindings2.Count > 0 || settings.DisabledActions2.Count > 0 ? Custom : (settings.Preset ?? TabForge);
}
