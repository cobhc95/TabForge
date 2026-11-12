using System.Collections.Generic;
using System.Linq;
using TabForge.Platform.Theme;
using TabForge.Menus;

namespace TabForge.Avalonia.Features.LeftStrip;

// Owns: the catalog of icons the left strip can show (id, label, group, the command it runs, its glyph), the default strip, the Separator entry,
//   and the strip's own glyphs (24 x 24 stroked path data, the Open / Save style); shared glyphs are taken from IconGeometry.
// Does not own: the commands themselves (MainWindowCommands), drawing the icons (MenuRowToggles.Icon), the saved list (Core LeftStripSettings).
// Tests: TabForge.Avalonia.Tests.LeftStripSettingsTests.
public static class LeftStripItems
{
    /// <summary>The id of a separator line on the strip (it may appear more than once).</summary>
    public const string SeparatorId = "Separator";

    /// <summary>One icon the strip can show; <see cref="IsToggle"/> marks the Composer toggle.</summary>
    public sealed record Item(string Id, string Label, string Group, string Icon, bool IsToggle = false);

    private const string Open = "M3,6 H9 L11,8 H21 V19 H3 Z";
    private const string Save = "M4,3 H17 L21,7 V21 H4 Z M8,3 V8 H15 V3 M8,21 V14 H17 V21";
    private const string SaveAs = "M4,3 H15 L18,6 V11 M4,3 V21 H11 M8,3 V8 H13 V3 M13,21 L20,14 L22,16 L15,23 H13 Z";
    private const string Render = "M2,12 H5 L7,7 L10,18 L13,4 L16,16 L18,12 H22";
    private const string Composer = "M9,18 V5 L19,3 V16 M9,18 A3,3 0 1 1 3,18 A3,3 0 1 1 9,18 M19,16 A3,3 0 1 1 13,16 A3,3 0 1 1 19,16";
    private const string New = "M5,3 H14 L19,8 V21 H5 Z M14,3 V8 H19 M12,11 V17 M9,14 H15";
    private const string Import = "M4,15 V20 H20 V15 M12,3 V15 M7,10 L12,15 L17,10";
    private const string Print = "M7,9 V3 H17 V9 M4,9 H20 V17 H4 Z M7,14 H17 V21 H7 Z";
    private const string Pdf = "M5,3 H14 L19,8 V21 H5 Z M14,3 V8 H19 M8,17 V12 H10 A1.5,1.5 0 0 1 10,15 H8";
    private const string Midi = "M3,5 H21 V19 H3 Z M7,19 V12 M12,19 V12 M17,19 V12 M5.5,5 V12 H8.5 V5 M15.5,5 V12 H18.5 V5";
    private const string Undo = "M9,5 L4,10 L9,15 M4,10 H15 A5,5 0 0 1 15,20 H11";
    private const string Redo = "M15,5 L20,10 L15,15 M20,10 H9 A5,5 0 0 0 9,20 H13";
    private const string Copy = "M8,8 H20 V20 H8 Z M4,16 V4 H16";
    private const string Paste = "M8,4 H5 V21 H19 V4 H16 M8,2 H16 V6 H8 Z";
    private const string AddBar = "M4,6 V18 M14,6 V18 M18,9 V15 M15,12 H21 M4,9 H14 M4,15 H14";
    private const string AddTrack = "M3,6 H15 M3,12 H15 M3,18 H11 M18,15 V21 M15,18 H21";
    private const string GoTo = "M4,12 H18 M13,7 L18,12 L13,17 M21,5 V19";
    private const string Play = "M7,4 L19,12 L7,20 Z";
    private const string Stop = "M6,6 H18 V18 H6 Z";
    private const string Loop = "M17,3 L20,6 L17,9 M20,6 H8 A4,4 0 0 0 4,10 V11 M7,21 L4,18 L7,15 M4,18 H16 A4,4 0 0 0 20,14 V13";
    private const string Metronome = "M8,3 H16 L20,21 H4 Z M12,17 L17,6 M7,17 H17";
    private const string CountIn = "M12,3 A9,9 0 1 0 12.01,3 M12,7 V12 L15,14";
    private const string Record = "M12,5 A7,7 0 1 0 12.01,5 M12,9 A3,3 0 1 0 12.01,9";
    private const string Mixer = "M6,3 V21 M12,3 V21 M18,3 V21 M4,15 H8 M10,8 H14 M16,12 H20";
    private const string Tuner = "M3,17 A9,9 0 0 1 21,17 M12,17 L16,9 M12,8 V6 M6.5,10.5 L5,9 M17.5,10.5 L19,9";
    private const string Tempo = "M12,3 A9,9 0 1 0 12.01,3 M12,12 L16,8 M12,12 V7";
    private const string TimeSig = "M8,3 V10 M8,14 V21 M14,4 H18 L14,10 H18 M14,14 H18 V17 H14 V21 H18";
    private const string KeySig = "M9,4 V20 M15,4 V20 M5,10 L19,7 M5,17 L19,14";
    private const string Transpose = "M7,20 V4 M3,8 L7,4 L11,8 M17,4 V20 M13,16 L17,20 L21,16";
    private const string Tuning = "M6,4 V20 M10,4 V20 M14,4 V20 M18,4 V20 M4,8 H8 M8,13 H12 M12,10 H16 M16,15 H20";
    private const string Lyrics = "M4,6 H20 M4,11 H20 M4,16 H14 M17,14 V20 A1.5,1.5 0 1 1 15.5,18.5";
    private const string ChordFinder = "M5,3 V21 M10,3 V21 M15,3 V21 M5,7 H15 M5,11 H15 M10,9 A1,1 0 1 0 10.01,9 M19,15 A2.5,2.5 0 1 0 19.01,15 M20.8,16.8 L22.5,18.5";
    private const string FocusTrack = "M0,0 H16.8 V22.8 H0 Z M0,6 H16.8 M4.2,3 H4.3 M8.4,3 H8.5 M12.6,3 H12.7 M8.4,9.96 A5.04,5.04 0 1 0 8.41,9.96 M8.4,15 H8.5";   // an upright combo amp: three knobs above one round speaker. Drawn from the origin: the strip centres a glyph by its bounds from (0,0), so any offset would push it off-centre
    private const string About = "M12,3 A9,9 0 1 0 12.01,3 M12,11 V17 M12,7.5 V8";

    /// <summary>Every icon the strip can show, in the Available list's order.</summary>
    public static readonly IReadOnlyList<Item> All = new Item[]
    {
        new(CommandIds.File.Open, "Open", "File", Open), new(CommandIds.File.Save, "Save", "File", Save), new(CommandIds.File.SaveAs, "Save as", "File", SaveAs),
        new(CommandIds.File.Render, "Render", "File", Render), new(CommandIds.File.New, "New", "File", New), new("Insert.AudioFile", "Import audio", "File", Import),
        new("File.Print", "Print", "File", Print), new("File.ExportPdf", "Export PDF", "File", Pdf), new("File.ExportMidi", "Export MIDI", "File", Midi),
        new(CommandIds.Edit.Undo, "Undo", "Edit", Undo), new(CommandIds.Edit.Redo, "Redo", "Edit", Redo), new(CommandIds.Edit.Copy, "Copy", "Edit", Copy), new(CommandIds.Edit.Paste, "Paste", "Edit", Paste),
        new("Bar.Append", "Add bar", "Edit", AddBar), new(CommandIds.Track.Add, "Add track", "Edit", AddTrack), new("Bar.GoTo", "Go to bar", "Edit", GoTo),
        new(CommandIds.Transport.PlayPause, "Play", "Playback", Play), new(CommandIds.Transport.Stop, "Stop", "Playback", Stop), new(CommandIds.Transport.Loop, "Loop", "Playback", Loop),
        new("Transport.Metronome", "Metronome", "Playback", Metronome), new("Transport.CountIn", "Count-in", "Playback", CountIn),
        new(CommandIds.Transport.Record, "Record", "Playback", Record), new(CommandIds.View.Mixer, "Mixer", "Playback", Mixer),
        new(MainWindowFocusTrack.CommandId, "Focus track", "Playback", FocusTrack, IsToggle: true),new(CommandIds.Tools.Tuner, "Tuner", "Playback", Tuner),
        new("Tool.composition:tempo", "Tempo", "Song", Tempo), new(CommandIds.Bar.TimeSignature, "Time signature", "Song", TimeSig),
        new(CommandIds.Bar.KeySignature, "Key signature", "Song", KeySig), new("Tools.Transpose", "Transpose", "Song", Transpose),
        new(CommandIds.Tools.GlobalTuning, "Tuning", "Song", Tuning), new("Insert.Lyrics", "Lyrics", "Song", Lyrics),
        new(CommandIds.View.ZoomIn, "Zoom in", "View", IconGeometry.ZoomIn), new(CommandIds.View.ZoomOut, "Zoom out", "View", IconGeometry.ZoomOut),
        new(CommandIds.View.BandView, "Band view", "View", IconGeometry.BandView, IsToggle: true), new(CommandIds.View.CompactMode, "Compact mode", "View", IconGeometry.CompactMode, IsToggle: true),
        new(CommandIds.View.LearnMode, "Keyboard mode (experimental)", "View", IconGeometry.LearnMode, IsToggle: true),
        new(CommandIds.View.InstrumentPanel, "Fretboard", "View", IconGeometry.Fretboard),
        new(CommandIds.Tools.ChordFinder, "Chord finder", "App", ChordFinder), new("Tools.ScaleFinder", "Scale finder", "App", IconGeometry.ScaleSteps),
        new(CommandIds.App.Preferences, "Preferences", "App", IconGeometry.Prefs), new("Help.About", "About", "App", About),
        new(LeftStripFeature.ToggleComposerId, "Composer", "App", Composer, IsToggle: true),
    };

    /// <summary>The strip as it comes: New, Open, Save, Save as, Render | Undo, Redo | Composer, Focus track | Band view, Compact mode, Keyboard mode.</summary>
    public static readonly IReadOnlyList<string> Default = new[]
    {
        CommandIds.File.New, CommandIds.File.Open, CommandIds.File.Save, CommandIds.File.SaveAs, CommandIds.File.Render, SeparatorId,
        CommandIds.Edit.Undo, CommandIds.Edit.Redo, SeparatorId,
        LeftStripFeature.ToggleComposerId, MainWindowFocusTrack.CommandId, SeparatorId,
        CommandIds.View.BandView, CommandIds.View.CompactMode, CommandIds.View.LearnMode,
    };

    /// <summary>The catalog item for <paramref name="id"/>, or null (a separator, or an id no longer in the catalog).</summary>
    public static Item? Find(string id) => All.FirstOrDefault(i => i.Id == id);

    /// <summary>The saved list with unknown ids dropped, or the default when nothing was saved.</summary>
    public static IReadOnlyList<string> Resolve(IReadOnlyList<string>? saved) =>
        saved is null ? Default : saved.Where(id => id == SeparatorId || Find(id) is not null).ToList();}
