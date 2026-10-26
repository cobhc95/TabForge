namespace TabForge.Platform.Theme;

// Owns: the path data of every icon that the Avalonia app draws (16 x 16 design box unless the name says otherwise),
//       in the path mini-language both toolkits parse. The Avalonia app reads them at start-up;
//       Avalonia passes them to Geometry.Parse.
// Does not own: how an icon is drawn (stroke, fill, size: IconPath and IconFillPath in App.xaml, and the views), or icons only one app
//       draws (the transport icons in App.xaml, Avalonia's caption minimise bar and the 9 px close glyph of the instrument strip).
// Tests: TabForge.Avalonia.Tests.IconGeometryTests pins each constant.
public static class IconGeometry
{
    public const string Maximise = "M 3.5,3.5 L 12.5,3.5 L 12.5,12.5 L 3.5,12.5 Z";
    public const string Restore = "M 3,6 L 10,6 L 10,13 L 3,13 Z M 6,6 L 6,3 L 13,3 L 13,10 L 10,10";
    public const string Close = "M 4,4 L 12,12 M 12,4 L 4,12";
    public const string Plus = "M 8,3.5 L 8,12.5 M 3.5,8 L 12.5,8";
    public const string ChevronLeft = "M 10,3.5 L 5.5,8 L 10,12.5";
    public const string ChevronRight = "M 6,3.5 L 10.5,8 L 6,12.5";
    public const string Speaker ="M 3,6 L 5.5,6 L 9,3.5 L 9,12.5 L 5.5,10 L 3,10 Z M 11,6 C 12.3,7 12.3,9 11,10 M 12.5,4.5 C 14.5,6.5 14.5,9.5 12.5,11.5";

    /// <summary>The cogwheel (the settings gear): a filled solid gear with eight teeth and a round centre hole; "F0" is the fill rule.</summary>
    public const string Cog = "F0 M 6.65,2.57 L 6.95,0.37 L 9.05,0.37 L 9.35,2.57 A 5.6,5.6 0 0 1 10.89,3.20 L 10.89,3.20 L 12.65,1.86 L 14.14,3.35 L 12.80,5.11 A 5.6,5.6 0 0 1 13.43,6.65 L 13.43,6.65 L 15.63,6.95 L 15.63,9.05 L 13.43,9.35 A 5.6,5.6 0 0 1 12.80,10.89 L 12.80,10.89 L 14.14,12.65 L 12.65,14.14 L 10.89,12.80 A 5.6,5.6 0 0 1 9.35,13.43 L 9.35,13.43 L 9.05,15.63 L 6.95,15.63 L 6.65,13.43 A 5.6,5.6 0 0 1 5.11,12.80 L 5.11,12.80 L 3.35,14.14 L 1.86,12.65 L 3.20,10.89 A 5.6,5.6 0 0 1 2.57,9.35 L 2.57,9.35 L 0.37,9.05 L 0.37,6.95 L 2.57,6.65 A 5.6,5.6 0 0 1 3.20,5.11 L 3.20,5.11 L 1.86,3.35 L 3.35,1.86 L 5.11,3.20 A 5.6,5.6 0 0 1 6.65,2.57 Z M 10.6,8 A 2.6,2.6 0 1 0 5.4,8 A 2.6,2.6 0 1 0 10.6,8 Z";

    /// <summary>The fretboard: headstock, nut, two frets and three strings (the show / hide fretboard and keyboard button).</summary>
    public const string Fretboard = "M 2,8 L 6,6.5 L 6,17.5 L 2,16 Z M 6,6.5 L 22,6.5 L 22,17.5 L 6,17.5 M 11,6.5 L 11,17.5 M 16,6.5 L 16,17.5 M 6,10.2 L 22,10.2 M 6,13.8 L 22,13.8";

    /// <summary>The sliders of the project settings.</summary>
    public const string Prefs = "M 3,5.5 L 13,5.5 M 3,10.5 L 13,10.5 M 5.5,4 L 8,4 L 8,7 L 5.5,7 Z M 9,9 L 11.5,9 L 11.5,12 L 9,12 Z";

    /// <summary>The layout toggles of the menu row (24 x 24 grid, drawn at 14 px): the side panel.</summary>
    public const string SidePanel = "M3,4 H21 V20 H3 Z M15,4 V20";

    /// <summary>The keyboard mode toggle: a keyboard with its black keys.</summary>
    public const string LearnMode = "M3,5 H21 V19 H3 Z M9,12.5 V19 M15,12.5 V19 M7.5,5 V12.5 H10.5 V5 M13.5,5 V12.5 H16.5 V5";

    /// <summary>The band view toggle: a head and shoulders per track.</summary>
    public const string BandView = "M9.3,7 a2.7,2.7 0 1 0 5.4,0 a2.7,2.7 0 1 0 -5.4,0 M7,19.5 c0,-3.4 2.1,-5.6 5,-5.6 s5,2.2 5,5.6 M2.8,8.8 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M1.8,18.5 c0,-2.4 1.1,-4 3,-4.4 M17.2,8.8 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M22.2,18.5 c0,-2.4 -1.1,-4 -3,-4.4";

    /// <summary>The compact mode toggle: arrows pointing at a bar.</summary>
    public const string CompactMode = "M12,2.5 V8.5 M9,5.5 L12,8.5 L15,5.5 M12,21.5 V15.5 M9,18.5 L12,15.5 L15,18.5 M3.5,12 H20.5";

    /// <summary>The record-video camera.</summary>
    public const string Camera = "M2,6 H15 V18 H2 Z M15,10 L22,6 V18 L15,14";

    /// <summary>The magnifier of the tempo bar's zoom control.</summary>
    public const string ZoomGlass = "M7,4 A3,3 0 1 1 1,4 A3,3 0 1 1 7,4 M6.3,6.3 L9.5,9.5";

    /// <summary>The rising steps of the scale finder button.</summary>
    public const string ScaleSteps = "M0,11 L3,11 L3,8 L6,8 L6,5 L9,5 L9,2 L12,2";

    /// <summary>The piano roll's Select tool: the arrow pointer.</summary>
    public const string ToolSelect = "M4,2.5 L4,13.2 L6.7,10.6 L8.6,14.2 L10.4,13.4 L8.5,9.8 L12.2,9.6 Z";

    /// <summary>The piano roll's Draw tool: a pencil.</summary>
    public const string ToolDraw = "M3,13 L3.7,10.2 L10.8,3.1 L12.9,5.2 L5.8,12.3 Z M9.5,4.4 L11.6,6.5";

    /// <summary>The piano roll's Erase tool: an eraser block.</summary>
    public const string ToolErase = "M2.8,10.2 L8.4,4.6 L13.4,9.6 L9.6,13.4 L6,13.4 Z M5.6,7.4 L10.6,12.4 M9.6,13.4 L14,13.4";

    /// <summary>The piano roll's Split tool: scissors.</summary>
    public const string ToolSplit = "M2.8,11.6 A2,2 0 1 1 6.8,11.6 A2,2 0 1 1 2.8,11.6 M9.2,11.6 A2,2 0 1 1 13.2,11.6 A2,2 0 1 1 9.2,11.6 M6.2,10.2 L11.5,2.8 M9.8,10.2 L4.5,2.8";

    /// <summary>The piano roll's Mute tool: a note with a cross.</summary>
    public const string ToolMute = "M2.5,6 L10.5,6 L10.5,10.5 L2.5,10.5 Z M11.5,3.5 L14.5,7 M14.5,3.5 L11.5,7";

    /// <summary>The piano roll's Quantize button: notes pulled onto grid lines.</summary>
    public const string Quantize = "M3,2.5 L3,13.5 M8,2.5 L8,13.5 M13,2.5 L13,13.5 M3,4.5 L7,4.5 L7,6.9 L3,6.9 Z M8,9.2 L11.2,9.2 L11.2,11.6 L8,11.6 Z";

    /// <summary>Zoom in: a magnifier with a plus.</summary>
    public const string ZoomIn = "M2.7,7 A4.3,4.3 0 1 1 11.3,7 A4.3,4.3 0 1 1 2.7,7 M10.2,10.2 L13.8,13.8 M5,7 L9,7 M7,5 L7,9";

    /// <summary>Zoom out: a magnifier with a minus.</summary>
    public const string ZoomOut = "M2.7,7 A4.3,4.3 0 1 1 11.3,7 A4.3,4.3 0 1 1 2.7,7 M10.2,10.2 L13.8,13.8 M5,7 L9,7";

    /// <summary>Zoom to fit: corner marks round a note.</summary>
    public const string ZoomFit = "M2.5,5.5 L2.5,2.5 L5.5,2.5 M10.5,2.5 L13.5,2.5 L13.5,5.5 M13.5,10.5 L13.5,13.5 L10.5,13.5 M5.5,13.5 L2.5,13.5 L2.5,10.5 M5.5,6.5 L10.5,6.5 L10.5,9.5 L5.5,9.5 Z";

    /// <summary>Follow the playhead: an arrow against a line.</summary>
    public const string Follow = "M2.5,8 L11.5,8 M8.5,5 L11.5,8 L8.5,11 M13.5,3 L13.5,13";

    /// <summary>The snap magnet (U shape with its poles).</summary>
    public const string Magnet = "M3.5,2.5 L3.5,8.5 A4.5,4.5 0 0 0 12.5,8.5 L12.5,2.5 L9.8,2.5 L9.8,8.5 A1.8,1.8 0 0 1 6.2,8.5 L6.2,2.5 Z M3.5,5 L6.2,5 M9.8,5 L12.5,5";

    /// <summary>A warning triangle (the tab prompt).</summary>
    public const string Warning = "M8,2 L14.5,13.5 L1.5,13.5 Z M8,6.5 L8,9.5 M8,11.6 L8,11.7";

    /// <summary>The piano roll's Glue tool (a later version): two notes joined.</summary>
    public const string ToolGlue = "M2.5,8 L6.5,8 M9.5,8 L13.5,8 M6.5,5.5 L6.5,10.5 M9.5,5.5 L9.5,10.5 M7.2,8 L8.8,8";

    /// <summary>The piano roll's Velocity tool (a later version): three lollipops.</summary>
    public const string ToolVelocity = "M3.5,13.5 L3.5,8 M8,13.5 L8,4.5 M12.5,13.5 L12.5,7 M2.2,7 A1.3,1.3 0 1 1 4.8,7 A1.3,1.3 0 1 1 2.2,7 M6.7,3.5 A1.3,1.3 0 1 1 9.3,3.5 A1.3,1.3 0 1 1 6.7,3.5 M11.2,6 A1.3,1.3 0 1 1 13.8,6 A1.3,1.3 0 1 1 11.2,6";

    /// <summary>The piano roll's Legato command (a later version): a note stretched to the next.</summary>
    public const string ToolLegato = "M2.5,5 L7,5 L7,7.5 L2.5,7.5 Z M7.5,9 L13.5,9 L13.5,11.5 L7.5,11.5 Z M7,6.2 L9.5,6.2";

    /// <summary>The piano roll's scale highlight: rows with one picked out.</summary>
    public const string ScaleRows = "M2.5,4 L13.5,4 M2.5,8 L13.5,8 M2.5,12 L13.5,12 M2.5,5.6 L13.5,5.6 L13.5,6.6 L2.5,6.6 Z";

    /// <summary>The piano roll's step input: a note and an arrow to the next step.</summary>
    public const string StepInput = "M2.5,6 L7,6 L7,9.5 L2.5,9.5 Z M9,7.75 L13.5,7.75 M11.5,5.5 L13.5,7.75 L11.5,10";
}
