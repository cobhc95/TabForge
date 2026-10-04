namespace TabForge.Views;

// Owns: the tool definitions of the four palettes and the command each one runs.
// Does not own: the palette buttons (ToolPaletteController.cs).
// Tests: TestRuntimeIconAndResourceKeys.
internal sealed partial class ToolPaletteController
{
    internal sealed record PaletteTool(string Group, string Id, string Icon, string Label, bool Supported = true);

    internal static readonly PaletteTool[] PaletteTools =
    {
        new("Edit", "edit:pointer", "Edit/select_pointer", "Selection cursor"),
        new("Edit", "edit:erase_note", "Edit/erase_note", "Erase note"),
        new("Edit", "edit:change_accidental", "Edit/change_accidental", "Change accidental", false),
        new("Composition", "composition:time_signature", "Composition/time_signature", "Time signature"),
        new("Composition", "composition:tempo", "Composition/tempo", "Tempo change"),
        new("Composition", "composition:repeat_open", "Composition/repeat_open", "Repeat start"),
        new("Composition", "composition:repeat_close", "Composition/repeat_close", "Repeat end / count"),
        new("Composition", "composition:alternate_ending", "Composition/alternate_ending", "Alternate ending"),

        new("Duration", "duration:whole", "Duration/whole_note", "Whole note"),
        new("Duration", "duration:half", "Duration/half_note", "Half note"),
        new("Duration", "duration:quarter", "Duration/quarter_note", "Quarter note"),
        new("Duration", "duration:eighth", "Duration/eighth_note", "Eighth note"),
        new("Duration", "duration:sixteenth", "Duration/sixteenth_note", "16th note"),
        new("Duration", "duration:thirtysecond", "Duration/thirty_second_note", "32nd note"),
        new("Duration", "duration:sixtyfourth", "Duration/sixty_fourth_note", "64th note"),
        new("Duration", "duration:dotted", "Duration/dotted_note", "Dotted duration"),
        new("Duration", "duration:double-dotted", "Duration/double_dotted_note", "Double-dotted duration"),
        new("Duration", "duration:tie", "Duration/tied_note", "Tie"),
        new("Duration", "duration:tuplet", "Duration/tuplet", "Triplet"),
        new("Duration", "duration:tuplet-menu", "Duration/tuplet_menu", "Choose tuplet ratio"),

        new("Dynamic", "dynamic:ppp", "Dynamic/ppp", "ppp"),
        new("Dynamic", "dynamic:pp", "Dynamic/pp", "pp"),
        new("Dynamic", "dynamic:p", "Dynamic/p", "p"),
        new("Dynamic", "dynamic:mp", "Dynamic/mp", "mp"),
        new("Dynamic", "dynamic:mf", "Dynamic/mf", "mf"),
        new("Dynamic", "dynamic:f", "Dynamic/f", "f"),
        new("Dynamic", "dynamic:ff", "Dynamic/ff", "ff"),
        new("Dynamic", "dynamic:fff", "Dynamic/fff", "fff"),

        new("Effects", "effect:vibrato", "Effects/vibrato", "Vibrato"),
        new("Effects", "effect:bend", "Effects/bend", "Bend"),
        new("Effects", "effect:tremolo_bar", "Effects/tremolo_bar", "Tremolo bar"),
        new("Effects", "effect:slides", "Effects/slides", "Slide"),
        new("Effects", "effect:dead_note", "Effects/dead_note", "Dead note"),
        new("Effects", "effect:hammer_on_pull_off", "Effects/hammer_on_pull_off", "Hammer-on / pull-off"),
        new("Effects", "effect:ghost_note", "Effects/ghost_note", "Ghost note"),
        new("Effects", "effect:accent", "Effects/accent", "Accent"),
        new("Effects", "effect:heavy_accent", "Effects/heavy_accent", "Heavy accent"),
        new("Effects", "effect:let_ring", "Effects/let_ring", "Let ring"),
        new("Effects", "effect:natural_harmonic", "Effects/natural_harmonic", "Natural harmonic"),
        new("Effects", "effect:grace_note", "Effects/grace_note", "Grace note"),
        new("Effects", "effect:trill", "Effects/trill", "Trill"),
        new("Effects", "effect:tremolo_picking", "Effects/tremolo_picking", "Tremolo picking"),
        new("Effects", "effect:palm_mute", "Effects/palm_mute", "Palm mute"),
        new("Effects", "effect:staccato", "Effects/staccato", "Staccato"),
        new("Effects", "effect:tapping", "Effects/tapping", "Tapping"),
        new("Effects", "effect:slapping", "Effects/slapping", "Slapping"),
        new("Effects", "effect:popping", "Effects/popping", "Popping"),
        new("Effects", "effect:fade_in", "Effects/fade_in", "Fade in"),

        new("Beat", "effect:chord", "Beat/chord", "Chord"),
        new("Beat", "effect:chord_menu", "Beat/chord_menu", "Choose chord"),
        new("Beat", "effect:text", "Beat/text", "Text annotation"),
        new("Beat", "effect:stroke_down", "Beat/stroke_down", "Brush down"),
        new("Beat", "effect:stroke_up", "Beat/stroke_up", "Brush up"),
        new("Beat", "effect:pickstroke_down", "Beat/pickstroke_down", "Pickstroke down"),
        new("Beat", "effect:pickstroke_up", "Beat/pickstroke_up", "Pickstroke up")
    };

    internal static readonly PaletteTool[] StructurePaletteTools =
    {
        new("Bar editing", "gp:insert_bar", "More/insert_bar", "Insert bar before the cursor"),
        new("Bar editing", "gp:append_bar", "More/append_bar", "Add bar at the end"),
        new("Bar editing", "gp:duplicate_bar", "More/duplicate_bar", "Duplicate bar"),
        new("Bar editing", "gp:delete_bar", "More/delete_bar", "Delete bar"),
        new("Bar editing", "gp:check_bars", "More/check_bar", "Check bar durations"),
        new("Step through", "gp:step_back", "More/step_back", "Step back one beat"),
        new("Step through", "gp:step_forward", "More/step_forward", "Step forward one beat"),
        new("Bars", "composition:time_signature", "Composition/time_signature", "Time signature"),
        new("Bars", "composition:tempo", "Composition/tempo", "Tempo change"),
        new("Bars", "composition:repeat_open", "Composition/repeat_open", "Repeat start"),
        new("Bars", "composition:repeat_close", "Composition/repeat_close", "Repeat end / count"),
        new("Key and bars", "gp:key_signature", "More/key_signature", "Key signature"),
        new("Key and bars", "gp:triplet_feel", "More/triplet_feel", "Triplet feel"),
        new("Key and bars", "gp:free_time", "More/free_time", "Free time"),
        new("Key and bars", "gp:double_barline", "More/double_barline", "Double barline"),
        new("Repeats and directions", "gp:repeat_one_bar", "More/repeat_one_bar", "One-bar repeat"),
        new("Repeats and directions", "gp:repeat_two_bars", "More/repeat_two_bars", "Two-bar repeat"),
        new("Repeats and directions", "gp:directions", "More/directions", "Score directions"),
        new("Markers", "gp:add_marker", "More/add_marker", "Add marker"),
        new("Markers", "gp:marker_list", "More/marker_list", "Marker list"),
        new("Markers", "gp:previous_marker", "More/previous_marker", "Previous marker"),
        new("Markers", "gp:next_marker", "More/next_marker", "Next marker")
    };

    internal static readonly PaletteTool[] RhythmPaletteTools =
    {
        new("Tuplets and ties", "gp:custom_ntuplet", "More/custom_ntuplet", "N-tuplet"),
        new("Tuplets and ties", "gp:tie_note", "More/tie_note", "Tie note"),
        new("Tuplets and ties", "gp:tie_beat", "More/tie_beat", "Tie beat / chord"),
        new("Sounding pitch and duration", "gp:sound_duration", "More/sound_duration", "Sound duration"),
        new("Sounding pitch and duration", "gp:octave_8va", "More/octave_8va", "8va — octave above"),
        new("Sounding pitch and duration", "gp:octave_8vb", "More/octave_8vb", "8vb — octave below"),
        new("Sounding pitch and duration", "gp:octave_15ma", "More/octave_15ma", "15ma — two octaves above"),
        new("Sounding pitch and duration", "gp:octave_15mb", "More/octave_15mb", "15mb — two octaves below")
    };

    internal static readonly PaletteTool[] LayoutPaletteTools =
    {
        new("Voices", "gp:voice_1", "More/voice_lead", "Voice 1"),
        new("Voices", "gp:voice_2", "More/voice_bass", "Voice 2"),
        new("Voices", "gp:inactive_voice_gray", "More/inactive_voice_gray", "Gray inactive voice"),
        new("System layout", "gp:force_line_break", "More/force_line_break", "Force line break"),
        new("System layout", "gp:prevent_line_break", "More/prevent_line_break", "Prevent line break"),
        new("Beaming", "gp:beam_auto", "More/beam_auto", "Automatic beaming"),
        new("Beaming", "gp:beam_force", "More/beam_force", "Force beam group"),
        new("Beaming", "gp:beam_break", "More/beam_break", "Break primary beam"),
        new("Beaming", "gp:beam_break_secondary", "More/beam_break_secondary", "Break secondary beam"),
        new("Stems", "gp:stem_auto", "More/stem_auto", "Automatic stem direction"),
        new("Stems", "gp:stem_invert", "More/stem_invert", "Invert stem direction")
    };


    /// <summary>The command a palette tool runs: a shared command where one exists, else its own Tool.* id.</summary>
    private static string PaletteHotkeyId(string toolId) => toolId switch
    {
        "gp:key_signature" => "Bar.KeySignature",
        "gp:directions" => "Bar.Directions",
        "gp:add_marker" => "Section.Add",
        "gp:previous_marker" => "Section.Previous",
        "gp:next_marker" => "Section.Next",
        "gp:insert_bar" => "Bar.Insert",
        "gp:delete_bar" => "Bar.Delete",
        "gp:check_bars" => "Bar.Check",
        "composition:time_signature" => "Bar.TimeSignature",
        "composition:repeat_open" => "Bar.RepeatOpen",
        "composition:repeat_close" => "Bar.RepeatClose",
        "effect:vibrato" => "Note.Vibrato",
        "effect:bend" => "Note.Bend",
        "effect:slides" => "Note.Slide",
        "effect:dead_note" => "Note.Dead",
        "effect:hammer_on_pull_off" => "Note.HammerPull",
        "effect:ghost_note" => "Note.Ghost",
        "effect:let_ring" => "Note.LetRing",
        "effect:natural_harmonic" => "Note.Harmonic",
        "effect:grace_note" => "Note.Grace",
        "effect:trill" => "Note.Trill",
        "effect:palm_mute" => "Note.PalmMute",
        "effect:staccato" => "Note.Staccato",
        "effect:accent" => "Note.Accent",
        "gp:tie_note" => "Note.Tie",
        "effect:tremolo_bar" => "Note.TremoloBar",
        "effect:fade_in" => "Note.FadeIn",
        "effect:chord" => "Note.Chord",
        "effect:text" => "Note.Text",
        "duration:dotted" => "Note.Dot",
        "duration:double-dotted" => "Note.DoubleDot",
        "duration:tie" => "Note.Tie",
        "duration:tuplet" => "Note.Triplet",
        _ => "Tool." + toolId,
    };

    // Palette tools that are the same command as an existing hotkey action are not registered twice.
    private static readonly HashSet<string> PaletteToolsCoveredByCommands = new(StringComparer.Ordinal)
    {
        "effect:vibrato", "effect:bend", "effect:slides", "effect:dead_note", "effect:hammer_on_pull_off",
        "effect:ghost_note", "effect:let_ring", "effect:natural_harmonic", "effect:grace_note", "effect:trill",
        "effect:palm_mute", "effect:staccato", "effect:tremolo_bar", "effect:fade_in", "effect:chord", "effect:text",
        "duration:dotted", "duration:double-dotted", "duration:tie", "duration:tuplet",
        "composition:time_signature", "composition:repeat_open", "composition:repeat_close",
        "gp:key_signature", "gp:directions", "gp:insert_bar", "gp:delete_bar", "gp:check_bars",
        "gp:previous_marker", "gp:next_marker"
    };
}
