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
public sealed record HotkeyAction(string Id, string Category, string Name, string DefaultGesture, string Description = "");

/// <summary>
/// The catalogue of every command that can be bound to a key. The settings store only the bindings the
/// user has changed; everything else falls back to <see cref="HotkeyAction.DefaultGesture"/>.
/// </summary>
public static class HotkeyCatalog
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

    /// <summary>Two commands can share a key when exactly one of them is a clip command.</summary>
    public static bool SameContext(string a, string b) => IsClipAction(a) == IsClipAction(b);

    private static readonly List<HotkeyAction> Actions = new()
    {
        new("File.New", CategoryFile, "New score", "Ctrl+N", "Create a new score in a new tab."),
        new("File.NewFromTemplate", CategoryFile, "New from template", "", "Create a new score from a built-in or saved template (File > New from template)."),
        new("File.SaveAsTemplate", CategoryFile, "Save as template", "", "Save a copy of the active score as a template in the templates folder."),
        new("File.Open", CategoryFile, "Open score", "Ctrl+O", "Open a .tforge or Guitar Pro file in the current tab."),
        new("File.OpenInNewTab", CategoryFile, "Open score in new tab", "Ctrl+Shift+O", "Open a .tforge or Guitar Pro file in a new tab."),
        new("File.Save", CategoryFile, "Save", "Ctrl+S", "Save the active score."),
        new("File.SaveAs", CategoryFile, "Save as", "Ctrl+Shift+S", "Save the active score under a new name."),
        new("File.Print", CategoryFile, "Print", "Ctrl+P", "Print the active score."),
        new("File.PrintPreview", CategoryFile, "Print preview", "Ctrl+Shift+P", "Preview the printed page."),
        new("File.CancelImport", CategoryFile, "Cancel import", "", "Cancel the Guitar Pro import(s) running in the background (same as the status-bar Cancel button)."),
        new("File.ExportPdf", CategoryFile, "Export PDF", "", "Export the engraved score (notation and tab) as a PDF file."),
        new("File.ExportMusicXml", CategoryFile, "Export MusicXML", "", "Export the song as uncompressed MusicXML (.musicxml): a part per track with notation and a tab staff."),
        new("App.CommandPalette", CategoryFile, "Command palette", "Ctrl+Shift+A", "Search every command by name, see its key and run it."),
        new("File.Render", CategoryFile, "Render to audio file", "Ctrl+Alt+R", "Render the song to WAV / MP3 (master mix and / or stems) faster than realtime."),
        new("Tab.New", CategoryFile, "New tab", "Ctrl+T", "Open another score in a new tab."),
        new("Tab.Close", CategoryFile, "Close tab", "Ctrl+W", "Close the active tab."),
        new("Tab.Duplicate", CategoryFile, "Duplicate tab", "Ctrl+Shift+D", "Duplicate the active tab as an unsaved copy."),
        new("App.Preferences", CategoryFile, "Preferences", "F12", "Open this settings window."),
        new("App.Shortcuts", CategoryFile, "Keyboard shortcuts", "F1", "Show the shortcut reference."),

        new("Edit.Undo", CategoryEdit, "Undo", "Ctrl+Z", "Undo the last edit."),
        new("Edit.Redo", CategoryEdit, "Redo", "Ctrl+Y", "Redo the last undone edit."),
        new("Edit.Copy", CategoryEdit, "Copy beat", "Ctrl+C", "Copy the current beat."),
        new("Edit.Cut", CategoryEdit, "Cut beat", "Ctrl+X", "Cut the current beat."),
        new("Edit.Paste", CategoryEdit, "Paste beat", "Ctrl+V", "Paste the copied beat."),
        new("Edit.SelectAll", CategoryEdit, "Select whole track", "Ctrl+A", "Select every bar of the track."),
        new("Edit.RepeatSelection", CategoryEdit, "Repeat selection", "Ctrl+Shift+R", "Repeat the selected bars."),

        new("Transport.PlayPause", CategoryTransport, "Play / pause", "Space", "Start or pause playback."),
        new("Transport.PlayFromStart", CategoryTransport, "Play from the start", "Shift+Space", "Restart playback from the beginning."),
        new("Playback.SpeedUp", CategoryTransport, "Speed up", "Ctrl+Alt+Up", "Playback speed to the next preset (50, 75, 100, 125, 150, 200 %)."),
        new("Playback.SpeedDown", CategoryTransport, "Slow down", "Ctrl+Alt+Down", "Playback speed to the previous preset (50, 75, 100, 125, 150, 200 %)."),
        new("Playback.SpeedReset", CategoryTransport, "Reset speed to 100 %", "Ctrl+Alt+D0", "Play at the song's own tempo (100 %)."),
        new("Transport.Stop", CategoryTransport, "Stop", "Ctrl+OemPeriod", "Stop playback and return to the edit cursor."),
        new("Transport.Loop", CategoryTransport, "Loop", "F9", "Play the loop range repeatedly."),

        new("Bar.Insert", CategoryBar, "Insert bar", "Ctrl+Insert", "Insert an empty bar before the cursor."),
        new("Bar.Delete", CategoryBar, "Delete bar", "Ctrl+Delete", "Delete the bar at the cursor."),
        new("Bar.TimeSignature", CategoryBar, "Time signature", "Ctrl+Shift+T", "Change the time signature."),
        new("Beat.MixTable", CategoryBar, "Mix table", "F10", "Change instrument, volume, pan, effects or tempo from the selected beat (mix table)."),
        new("Bar.KeySignature", CategoryBar, "Key signature", "Ctrl+K", "Change the key signature."),
        new("Bar.Clef", CategoryBar, "Clef", "K", "Change the clef of the track."),
        new("Bar.Directions", CategoryBar, "Directions", "D", "Edit repeat/DC/DS directions."),
        new("Bar.GoTo", CategoryBar, "Go to bar", "Ctrl+G", "Jump to a bar number."),
        new("Bar.First", CategoryBar, "First bar", "Ctrl+Home", "Jump to the first bar."),
        new("Bar.Last", CategoryBar, "Last bar", "Ctrl+End", "Jump to the last bar."),
        new("Bar.Check", CategoryBar, "Check bars", "F4", "Report bars that do not fill their time signature."),
        new("Bar.ScoreInfo", CategoryBar, "Score information", "F5", "Edit title, artist and other score information."),
        new("Section.Add", CategoryBar, "Add section", "M", "Add a new section (marker) at the cursor bar."),
        new("Section.Edit", CategoryBar, "Section editor", "Shift+Insert", "Add or edit a section marker."),
        new("Section.Previous", CategoryBar, "Previous section", "Alt+Shift+Left", "Jump to the previous section."),
        new("Section.Next", CategoryBar, "Next section", "Alt+Shift+Right", "Jump to the next section."),

        new("Note.Chord", CategoryNote, "Chord name", "A", "Attach a chord name to the beat."),
        new("Note.Text", CategoryNote, "Beat text", "T", "Attach text to the beat."),
        new("Track.Add", CategoryNote, "Add track", "Ctrl+Shift+Insert", "Add a new track."),
        new("Track.Delete", CategoryNote, "Delete track", "Ctrl+Shift+Delete", "Delete the selected track."),
        new("Track.Properties", CategoryNote, "Track properties", "F6", "Edit the selected track's properties."),
        new("Track.Next", CategoryNote, "Next track", "Ctrl+Shift+Down", "Select the next track."),
        new("Track.Previous", CategoryNote, "Previous track", "Ctrl+Shift+Up", "Select the previous track."),

        new("View.Multitrack", CategoryView, "Multitrack view", "F3", "Show every track in the score."),
        new("View.Global", CategoryView, "Global view", "F8", "Show all tracks together."),
        new("View.Fullscreen", CategoryView, "Fullscreen", "F11", "Toggle fullscreen."),
        new("View.HorizontalScroll", CategoryView, "Horizontal score scrolling", "", "Switch the score between wrapped lines (scroll down) and one line (scroll right)."),
        new("View.InstrumentView", CategoryView, "Switch instrument view", "", "Cycle the instrument panel between fretboard, keyboard and drum pads (this session; the default is in Settings > Fretboard)."),
        new("Help.CheckForUpdates", CategoryView, "Check for updates", "", "Ask GitHub whether a newer TabForge release exists (one anonymous HTTPS request)."),
        new("Transport.Record", CategoryTransport, "Record", "Ctrl+R", "Record every armed track while the song plays (press again to stop)."),
        new("Clip.Delete", CategoryClips, "Delete clip", "Delete", "Delete the selected clip."),
        new("Clip.Deselect", CategoryClips, "Deselect clip", "Escape", "Deselect the clip (Esc again clears the score selection)."),
        new("Clip.NudgeLeft", CategoryClips, "Move clip left", "Left", "Move the selected clip one beat earlier."),
        new("Clip.NudgeRight", CategoryClips, "Move clip right", "Right", "Move the selected clip one beat later."),
        new("Clip.NudgeLeftFine", CategoryClips, "Move clip left (fine)", "Shift+Left", "Move the selected clip 10 ms earlier."),
        new("Clip.NudgeRightFine", CategoryClips, "Move clip right (fine)", "Shift+Right", "Move the selected clip 10 ms later."),
        new("Clip.LaneUp", CategoryClips, "Move clip to the lane above", "Up", "Move the selected clip one lane up."),
        new("Clip.LaneDown", CategoryClips, "Move clip to the lane below", "Down", "Move the selected clip one lane down (a new lane under the last one)."),
        new("Clip.Copy", CategoryClips, "Copy clip", "Ctrl+C", "Copy the selected clip."),
        new("Clip.Cut", CategoryClips, "Cut clip", "Ctrl+X", "Cut the selected clip."),
        new("Clip.Paste", CategoryClips, "Paste clip", "Ctrl+V", "Paste the copied clip at the lane position last clicked."),
        new("Clip.Duplicate", CategoryClips, "Duplicate clip", "Ctrl+D", "Duplicate the selected clip right after itself."),
        new("Clip.Mute", CategoryClips, "Mute clip", "Ctrl+M", "Mute or unmute the selected clip."),
        new("Clip.Properties", CategoryClips, "Clip properties", "F2", "Volume, pitch, speed and name of the selected clip."),
        new("Timeline.Snap", CategoryView, "Snap clips on / off", "Alt+S", "Turn snapping of audio and MIDI clips on or off (right-click the snap button for its settings)."),
        new("Track.Arm", CategoryTransport, "Arm track for recording", "", "Monitor the audio input through the selected track (and record it with Record)."),
        new("View.SidePanel", CategoryView, "Show / hide side panel", "", "Hide the side panel (tools, sections, practice) for more score space, or bring it back."),
        new("View.InstrumentPanel", CategoryView, "Show / hide fretboard / keyboard", "", "Hide the instrument panel (fretboard, keyboard or drum map) for more score space, or bring it back (same as View > Instrument view)."),
        new("View.LockInstrumentSize", CategoryView, "Lock fretboard size", "", "Lock or unlock the fretboard / keyboard pane's height. Unlocked, dragging its edge resizes it and the drawing scales to fit; locked, it keeps its size."),
        new("View.LayoutCompose", CategoryView, "Layout: Compose", "Ctrl+D1", "Switch to the Compose workspace layout (score and tab editor large, fretboard and tools, small arrangement)."),
        new("View.LayoutPractice", CategoryView, "Layout: Practice", "Ctrl+D2", "Switch to the Practice workspace layout (score and fretboard large, transport and practice panels)."),
        new("View.LayoutMix", CategoryView, "Layout: Mix", "Ctrl+D3", "Switch to the Mix workspace layout (arrangement and mixer large, small score)."),
        new("View.Mixer", CategoryView, "Mixer", "", "Open the mixer: track and group levels, pan, pitch, sound source and FX chains."),
        new("Track.FxChain", CategoryView, "Track FX chain", "", "Open the selected track's FX (plug-in) chain."),
        new("View.AutoFitTrackList", CategoryView, "Auto-resize track list to fit", "", "Turn on / off growing and shrinking the track list to fit all tracks and group rows."),
        new("Track.Wiring", CategoryView, "Plug-in wiring", "", "Open the wiring window (audio pins, MIDI input and channel filter) of the selected track's selected plug-in."),
        new("Track.MoveUp", CategoryNote, "Move track up", "Alt+Up", "Move the selected track up one place in the track list. In the Mixer window it moves the selected mixer row (a track, or a whole group) up; a track passing the top of its group joins the group above."),
        new("Track.MoveDown", CategoryNote, "Move track down", "Alt+Down", "Move the selected track down one place in the track list. In the Mixer window it moves the selected mixer row (a track, or a whole group) down; a track passing the end of its group joins the group below."),
        new("View.ShowTrackGroups", CategoryView, "Show tracks in groups", "", "Show or hide a header per mixer group (guitars, basses...) in the track list. The same setting as the Mixer's \"Groups in track list\" box and the track list's right-click menu."),
        new("Mixer.MasterFx", CategoryView, "Master FX chain", "", "Open the master effects chain (applied to the whole mix after the group buses)."),
        new("Mixer.MonitorFx", CategoryView, "Mixer: Monitor FX", "", "Open the monitoring effects chain (e.g. speaker calibration). It plays after the master, live only: never included in renders or exports."),
        new("Mixer.GroupFx", CategoryView, "Group bus FX chain", "", "Open the effects bus chain of the selected track's mixer group (its tracks sum into it before the master)."),
        new("Track.MidiProcessing", CategoryView, "Plug-in MIDI processing", "", "Open the MIDI processing window (filter, transpose, drum map, velocity, humanize, delay, program / CC, log) of the selected track's selected plug-in."),
        new("View.ClearScale", CategoryView, "Clear scale highlight", "", "Remove the highlighted scale from the fretboard and keyboard."),
        new("View.CycleStringSpacing", CategoryView, "Cycle fretboard string spacing", "", "Switch the fretboard string spacing between Compact, Natural (default) and Wide (at most 1.5x natural). The same setting as right-click the fretboard > Appearance > String spacing."),
        new("Tools.ScaleFinder", CategoryView, "Scale finder", "", "Find which scales the selected notes (or the whole song) fit, or pick any scale, and highlight it on the fretboard."),
        new("Tools.Tuner", CategoryView, "Tuner", "", "Open the chromatic tuner: the note and cents of the sound on the armed input, with the selected track's string tunings."),
        new("View.SmoothFollow", CategoryView, "Smooth page-turn follow", "", "Switch playback follow between instant page turns and smooth (glided) page turns."),
        new("View.ZoomIn", CategoryView, "Zoom in", "Ctrl+OemPlus", "Increase the score zoom."),
        new("View.ZoomOut", CategoryView, "Zoom out", "Ctrl+OemMinus", "Decrease the score zoom."),

        // Single-key note shortcuts (classic layout). Previously hard-wired in the editor.
        new("Note.RepeatBeat", CategoryNote, "Repeat previous beat", "C", "Copy the previous beat onto the cursor."),
        new("Note.Rest", CategoryNote, "Rest", "R", "Turn the beat into a rest (or back)."),
        new("Note.Tie", CategoryNote, "Tie note", "L", "Tie the note to the previous one."),
        new("Note.Fermata", CategoryNote, "Fermata", "F", "Hold the beat."),
        new("Note.Accent", CategoryEffects, "Accent", "Oem1", "Cycle none / accent / heavy accent."),
        new("Note.Staccato", CategoryEffects, "Staccato", "Shift+D1", "Play the note short."),
        new("Note.Tenuto", CategoryEffects, "Tenuto", "Shift+OemMinus", "Hold the note for its full value."),
        new("Note.Bend", CategoryEffects, "Bend", "B", "Add or remove a bend."),
        new("Note.HammerPull", CategoryEffects, "Hammer-on / pull-off", "H", "Legato to the next note."),
        new("Note.Vibrato", CategoryEffects, "Vibrato", "V", "Left-hand vibrato."),
        new("Note.Slide", CategoryEffects, "Legato slide", "S", "Slide into the next note."),
        new("Note.LetRing", CategoryEffects, "Let ring", "I", "Let the note ring over."),
        new("Note.Dead", CategoryEffects, "Dead note", "X", "Muted, percussive note."),
        new("Note.Ghost", CategoryEffects, "Ghost note", "O", "Very soft note in brackets."),
        new("Note.Harmonic", CategoryEffects, "Natural harmonic", "Y", "Natural harmonic."),
        new("Note.Trill", CategoryEffects, "Trill", "N", "Trill with another fret."),
        new("Note.TremoloBar", CategoryEffects, "Tremolo bar", "W", "Whammy bar effect."),
        new("Note.Grace", CategoryEffects, "Grace note", "G", "Add a grace note before the beat."),
        new("Note.PalmMute", CategoryEffects, "Palm mute", "P", "Palm-muted note."),
        new("Note.FadeIn", CategoryEffects, "Fade in", "Shift+OemComma", "Volume swell in."),
        new("Note.FadeOut", CategoryEffects, "Fade out", "Shift+OemPeriod", "Volume swell out."),
        new("Note.Dot", CategoryDuration, "Dotted", "OemPeriod", "Add or remove a dot."),
        new("Note.DoubleDot", CategoryDuration, "Double dotted", "", "Make the beat double-dotted."),
        new("Note.Triplet", CategoryDuration, "Triplet", "OemQuestion", "Toggle triplet (numpad / also works)."),
        new("Bar.RepeatOpen", CategoryBar, "Repeat start", "OemOpenBrackets", "Start a repeated passage."),
        new("Bar.RepeatClose", CategoryBar, "Repeat end", "OemCloseBrackets", "End a repeated passage."),
    };

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
    public static Dictionary<string, string> BuildMap(HotkeySettings settings, bool clipContext = false)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in All)
        {
            if (IsClipAction(action.Id) != clipContext) continue;
            var gesture = GestureFor(settings, action.Id);
            if (!string.IsNullOrWhiteSpace(gesture)) map[gesture] = action.Id;
        }
        return map;
    }

    public static string GestureFor(HotkeySettings settings, string id)
    {
        if (settings.IsDisabled(id)) return "";
        var custom = settings[id];
        if (!string.IsNullOrWhiteSpace(custom)) return custom;
        if (HotkeyPresets.Override(settings.Preset, id) is { } preset) return preset;
        return ById(id)?.DefaultGesture ?? "";
    }

    // ---------- gesture formatting / parsing ----------

    public static string Format(string key, HotkeyModifiers mods)
    {
        var parts = new List<string>(4);
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
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
            _ => key
        };
        return Format(text, mods);
    }

    /// <summary>"(Ctrl+S)" for tooltips, or "" when the action has no gesture.</summary>
    public static string TooltipSuffix(HotkeySettings settings, string id)
    {
        var display = Display(GestureFor(settings, id));
        return string.IsNullOrWhiteSpace(display) ? "" : $" ({display})";
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
    public static string DisplayName(string id) => id == GuitarPro5 ? "Classic (influenced by Guitar Pro 5)" : id;

    /// <summary>Tooltip of a preset in the picker.</summary>
    public static string Tooltip(string id) => id == GuitarPro5
        ? "A shortcut layout familiar to Guitar Pro 5 users. TabForge is not affiliated with Arobas Music."
        : "";

    // TabForge's defaults already follow the classic layout, so both presets are currently identical.
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
    };

    /// <summary>The preset's key for an action, or null when the preset uses the catalogue default.</summary>
    public static string? Override(string? preset, string id)
    {
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
    }

    /// <summary>What the preset box should show: the base preset, or Custom once the user changed keys.</summary>
    public static string Describe(HotkeySettings settings) =>
        settings.Bindings.Count > 0 || settings.DisabledActions.Count > 0 ? Custom : (settings.Preset ?? TabForge);
}
