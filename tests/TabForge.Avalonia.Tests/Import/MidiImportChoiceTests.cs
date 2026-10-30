using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Platform.Controls;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the MIDI import choice: tab tracks or MIDI tracks (never both lanes for the same notes), the default pick, the question (asked, skipped when remembered,
//   cancel, remember), the Settings row and the report line, and the question window's theme.
// Does not own: the quantising of the notes (MidiImportTests) or the section and technique data (MidiSectionTechniqueKindTests).
public sealed class MidiImportChoiceTests : IDisposable
{
    private const int Div = 480;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"tf-midi-5b-{Guid.NewGuid():N}");

    public MidiImportChoiceTests() { Directory.CreateDirectory(_folder); AlphaTabWarmUp.EnsureReady(); }
    public void Dispose() { try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
    private static void Vlq(List<byte> o, int v)
    {
        var parts = new Stack<byte>(); parts.Push((byte)(v & 0x7F));
        for (v >>= 7; v > 0; v >>= 7) parts.Push((byte)(v & 0x7F | 0x80));
        o.AddRange(parts);
    }
    private static byte[] Chunk(IEnumerable<(int Tick, byte[] Bytes)> events)
    {
        var body = new List<byte>(); var last = 0;
        foreach (var (tick, bytes) in events.OrderBy(e => e.Tick)) { Vlq(body, tick - last); last = tick; body.AddRange(bytes); }
        body.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
        return Ascii("MTrk").Concat(new byte[] { 0, 0, (byte)(body.Count >> 8), (byte)body.Count }).Concat(body).ToArray();
    }
    private static byte[] File1(params byte[][] chunks) =>
        Ascii("MThd").Concat(new byte[] { 0, 0, 0, 6, 0, 1, 0, (byte)chunks.Length, Div >> 8, Div & 0xFF }).Concat(chunks.SelectMany(c => c)).ToArray();
    private static (int, byte[]) Tempo(int tick, int bpm) { var mpq = 60_000_000 / bpm; return (tick, new byte[] { 0xFF, 0x51, 3, (byte)(mpq >> 16), (byte)(mpq >> 8), (byte)mpq }); }
    private static (int, byte[]) Marker(int tick, string text) { var b = Encoding.UTF8.GetBytes(text); return (tick, new byte[] { 0xFF, 0x06, (byte)b.Length }.Concat(b).ToArray()); }
    private static (int, byte[]) Name(string text) { var b = Ascii(text); return (0, new byte[] { 0xFF, 0x03, (byte)b.Length }.Concat(b).ToArray()); }
    private static (int, byte[]) Prog(int program, int ch) => (0, new byte[] { (byte)(0xC0 | ch), (byte)program });
    private static (int, byte[]) Cc(int tick, int controller, int value, int ch = 0) => (tick, new byte[] { (byte)(0xB0 | ch), (byte)controller, (byte)value });
    private static (int, byte[]) Bend(int tick, int value, int ch = 0) => (tick, new byte[] { (byte)(0xE0 | ch), (byte)(value & 0x7F), (byte)((value >> 7) & 0x7F) });
    private static IEnumerable<(int, byte[])> Note(int tick, int len, int pitch, int ch = 0)
    {
        yield return (tick, new byte[] { (byte)(0x90 | ch), (byte)pitch, 90 });
        yield return (tick + len, new byte[] { (byte)(0x80 | ch), (byte)pitch, 0 });
    }

    private static IEnumerable<(int, byte[])> Line(int ch, params int[] pitches) => pitches.SelectMany((p, i) => Note(i * Div, Div / 2, p, ch));

    /// <summary>Conductor (tempo, two sections), a guitar line with controllers, a piano line and a drum line.</summary>
    private string Mixed()
    {
        var conductor = Chunk(new[] { Tempo(0, 120), Marker(0, "Intro"), Marker(4 * Div, "Verse") });
        var guitar = Chunk(new[] { Name("Lead"), Prog(29, 0), Cc(0, 7, 100), Bend(Div, 9000) }.Concat(Line(0, 52, 55, 57, 59)));
        var piano = Chunk(new[] { Name("Keys"), Prog(0, 1) }.Concat(Line(1, 60, 62, 64)));
        var drums = Chunk(new[] { Name("Kit") }.Concat(Line(9, 36, 38, 42, 38, 36)));
        var path = Path.Combine(_folder, "mixed.mid");
        File.WriteAllBytes(path, File1(conductor, guitar, piano, drums));
        return path;
    }

    private string PianoOnly()
    {
        var path = Path.Combine(_folder, "piano.mid");
        File.WriteAllBytes(path, File1(Chunk(new[] { Tempo(0, 120) }), Chunk(new[] { Name("Piano"), Prog(0, 0) }.Concat(Line(0, 60, 64, 67)))));
        return path;
    }

    private static int WrittenNotes(TrackModel track) =>
        track.Measures.Sum(m => m.Cells.Concat(m.Voice2Cells).Where(c => !c.IsRest).Sum(c => c.Notes.Count(n => !n.Tied)));

    private static List<int> SourceCounts(string path) =>
        SmfReader.Read(File.ReadAllBytes(path)).Tracks.Where(t => t.Notes.Count > 0).Select(t => t.Notes.Count).ToList();

    private static SongProject Import(string path, MidiImportMode mode, out ImportContext context)
    {
        context = new ImportContext { MidiMode = mode };
        return GuitarProImporter.Import(path, context);
    }

    [Fact]
    public void TabModeMakesTabTracksOnlyWithEveryNoteOnceAndNoMidiLanes()
    {
        var path = Mixed();
        var song = Import(path, MidiImportMode.Tab, out _);
        Assert.Equal(new[] { TrackKind.Guitar, TrackKind.Keys, TrackKind.Drums }, song.Tracks.Select(t => t.Kind));
        Assert.Equal(SourceCounts(path), song.Tracks.Select(WrittenNotes));
        Assert.All(song.Tracks, t =>
        {
            Assert.Empty(t.AudioClips);
            Assert.Empty(t.Lanes);
            Assert.False(TabForge.Presenters.PianoRollViewRules.IsMidiTrack(t));
        });
        Assert.Equal(new[] { "Intro", "Verse" }, song.Markers.Select(m => m.Title));
    }

    [Fact]
    public void MidiModeMakesMidiTracksOnlyWithEveryNoteOnceAndTheControllers()
    {
        var path = Mixed();
        var song = Import(path, MidiImportMode.Midi, out _);
        Assert.Equal(new[] { TrackKind.Guitar, TrackKind.Keys, TrackKind.Drums }, song.Tracks.Select(t => t.Kind));   // the same instrument mapping in both modes
        Assert.Equal(SourceCounts(path), song.Tracks.Select(t => Assert.Single(t.AudioClips).Notes!.Count));
        Assert.All(song.Tracks, t =>
        {
            Assert.Equal(0, WrittenNotes(t));
            Assert.True(TabForge.Presenters.PianoRollViewRules.IsMidiTrack(t));
            Assert.Single(t.Lanes);
        });
        var controllers = song.Tracks[0].AudioClips[0].Controllers!;
        Assert.Contains(controllers, p => p.Controller == 7 && p.Value == 100);
        Assert.Contains(controllers, p => p.Controller == ClipController.PitchBend && p.Value == 808);
        Assert.Equal(new[] { "Intro", "Verse" }, song.Markers.Select(m => m.Title));
    }

    [Fact]
    public void TheReportNamesTracksSectionsAndTheDataATabTrackCannotKeep()
    {
        var path = Mixed();
        Import(path, MidiImportMode.Tab, out var tab);
        Assert.Equal("3 tracks as tab and notation tracks, 2 sections found. Controllers and pitch bends are kept only in MIDI mode.", tab.MidiNotice);
        var notices = new List<string>();
        tab.AddNoticesTo(notices);
        Assert.Contains(tab.MidiNotice, notices);

        Import(path, MidiImportMode.Midi, out var midi);
        Assert.Equal("3 tracks as MIDI tracks, 2 sections found.", midi.MidiNotice);

        Import(PianoOnly(), MidiImportMode.Tab, out var plain);
        Assert.Equal("1 track as tab and notation tracks.", plain.MidiNotice);   // no controllers dropped, no sections: nothing more to say
    }

    [Fact]
    public void TheDefaultPickIsTabWhenAGuitarBassOrDrumTrackExistsElseMidi()
    {
        var mixed = MidiImportPreview.Inspect(Mixed());
        Assert.Equal(3, mixed.TrackCount);
        Assert.Equal(MidiImportMode.Tab, mixed.DefaultMode);
        var piano = MidiImportPreview.Inspect(PianoOnly());
        Assert.Equal(1, piano.TrackCount);
        Assert.Equal(MidiImportMode.Midi, piano.DefaultMode);
        var drums = Path.Combine(_folder, "drums.mid");
        File.WriteAllBytes(drums, File1(Chunk(new[] { Tempo(0, 120) }), Chunk(Line(9, 36, 38))));
        Assert.Equal(MidiImportMode.Tab, MidiImportPreview.Inspect(drums).DefaultMode);
    }

    private sealed class Dialogs : IFileDialogs
    {
        public string? SaveFile(FileDialogOptions options, object? owner) => null;
        public string? OpenFile(FileDialogOptions options, object? owner) => null;
        public string[]? OpenFiles(FileDialogOptions options, object? owner) => null;
        public string? PickFolder(FileDialogOptions options, object? owner) => null;
        public string[]? PickFolders(FileDialogOptions options, object? owner) => null;
    }

    private sealed class Rig
    {
        public readonly TabSettings Tabs = new() { OpenInCurrentTab = false };
        public readonly List<string> Reports = new();
        public readonly List<MidiImportPreview> Asked = new();
        public MidiImportAnswer? Answer;
        public int Saved;
        public readonly MainWindowTabs Songs;
        public readonly MainWindowFile File;

        public Rig()
        {
            Songs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false), settings: new AppSettings { Tabs = Tabs });
            File = new MainWindowFile(new Dialogs(), null, Songs, Reports.Add, () => Tabs, () => Saved++,
                (_, preview) => { Asked.Add(preview); return Task.FromResult(Answer); });
        }
    }

    [AvaloniaFact]
    public async Task AskShowsTheQuestionAndCancelImportsNothingAndChangesNothing()
    {
        var rig = new Rig { Answer = null };
        Assert.Equal(MidiImportModes.Ask, rig.Tabs.MidiImportMode);
        Assert.False(await rig.File.OpenPath(Mixed()));
        Assert.Single(rig.Asked);
        Assert.Equal(3, rig.Asked[0].TrackCount);
        Assert.Single(rig.Songs.Songs);
        Assert.Empty(rig.Reports);
        Assert.Equal(MidiImportModes.Ask, rig.Tabs.MidiImportMode);
        Assert.Equal(0, rig.Saved);
    }

    [AvaloniaFact]
    public async Task RememberWritesTheSettingAndALaterOpenSkipsTheQuestion()
    {
        var rig = new Rig { Answer = new MidiImportAnswer(MidiImportMode.Midi, Remember: true) };
        await rig.File.OpenPath(Mixed());   // the import itself needs the worker program, which a test host lacks; the choice is what is checked here
        Assert.Single(rig.Asked);
        Assert.Equal(MidiImportModes.Midi, rig.Tabs.MidiImportMode);
        Assert.Equal(1, rig.Saved);
        await rig.File.OpenPath(Mixed());
        Assert.Single(rig.Asked);

        var once = new Rig { Answer = new MidiImportAnswer(MidiImportMode.Tab, Remember: false) };
        await once.File.OpenPath(Mixed());
        Assert.Equal(MidiImportModes.Ask, once.Tabs.MidiImportMode);
        Assert.Equal(0, once.Saved);
    }

    [AvaloniaFact]
    public async Task ARememberedTabOrMidiChoiceSkipsTheQuestion()
    {
        foreach (var remembered in new[] { MidiImportModes.Tab, MidiImportModes.Midi })
        {
            var rig = new Rig();
            rig.Tabs.MidiImportMode = remembered;
            await rig.File.OpenPath(Mixed());
            Assert.Empty(rig.Asked);
        }
    }

    [Fact]
    public void TheSettingsRowOffersTheThreeChoicesAndResetReturnsAsk()
    {
        var settings = new AppSettings();
        var row = SettingsCatalog.Build(settings).Single(r => r.Key == "tabs.midiimport");
        Assert.Equal(SettingsCatalog.Tabs, row.Category);
        Assert.Equal("MIDI import", row.Title);
        Assert.Equal(new[] { "Ask each time", "Always as tab tracks", "Always as MIDI tracks" }, row.Choices);
        Assert.Equal("Ask each time", row.Get());
        row.Set("Always as tab tracks");
        Assert.Equal(MidiImportMode.Tab, MidiImportModes.Remembered(settings.Tabs.MidiImportMode));
        row.Set("Always as MIDI tracks");
        Assert.Equal(MidiImportMode.Midi, MidiImportModes.Remembered(settings.Tabs.MidiImportMode));
        var defaults = SettingsCatalog.Build(new AppSettings()).Single(r => r.Key == "tabs.midiimport");   // what Reset all settings restores
        row.Set(defaults.Get());
        Assert.Null(MidiImportModes.Remembered(settings.Tabs.MidiImportMode));
        Assert.Equal("Ask each time", row.Get());
    }

    [AvaloniaFact]
    public void TheQuestionWindowIsThemedPicksTheDefaultAndAnswersWithEnterAndEsc()
    {
        var window = new MidiImportPromptWindow(Path.Combine(_folder, "song.mid"), new MidiImportPreview(3, true));
        window.Show();
        Assert.Equal("Import MIDI", window.Title);
        var tab = DialogTestKit.Named<RadioCard>(window, "TabCard");
        var midi = DialogTestKit.Named<RadioCard>(window, "MidiCard");
        Assert.True(tab.IsSelected);
        Assert.False(midi.IsSelected);
        Assert.Equal("song.mid", DialogTestKit.Named<TextBlock>(window, "FileText").Text);
        Assert.Equal("3 tracks", DialogTestKit.Named<TextBlock>(window, "TracksText").Text);
        Assert.Equal(MidiImportPromptPresenter.TabHint, DialogTestKit.Named<TextBlock>(window, "TabHint").Text);
        Assert.Equal(MidiImportPromptPresenter.MidiHint, DialogTestKit.Named<TextBlock>(window, "MidiHint").Text);

        Assert.True(window.TryFindResource("PanelBackground", window.ActualThemeVariant, out var panel));
        Assert.Equal(((ISolidColorBrush)panel!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        Assert.True(window.TryFindResource("AccentBrush", window.ActualThemeVariant, out var accent));
        Assert.True(window.TryFindResource("FocusRingBrush", window.ActualThemeVariant, out var ring));
        var picked = Assert.IsAssignableFrom<ISolidColorBrush>(tab.BorderBrush).Color;   // the picked card wears the accent, or the focus ring while it holds the focus
        Assert.Contains(picked, new[] { ((ISolidColorBrush)accent!).Color, ((ISolidColorBrush)ring!).Color });
        Assert.NotEqual(picked, Assert.IsAssignableFrom<ISolidColorBrush>(midi.BorderBrush).Color);
        foreach (Control control in new Control[] { tab, midi, DialogTestKit.Named<Button>(window, "ImportButton"), DialogTestKit.Named<Button>(window, "CancelButton"), DialogTestKit.Named<CheckBox>(window, "RememberBox") })
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, control.Name);
        Assert.Contains(AvaloniaDefaultLooks.AccentLook, DialogTestKit.Named<Button>(window, "ImportButton").Classes);

        midi.Select();
        DialogTestKit.Named<CheckBox>(window, "RememberBox").IsChecked = true;
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(new MidiImportAnswer(MidiImportMode.Midi, true), window.Answer);

        var cancelled = new MidiImportPromptWindow("a.mid", new MidiImportPreview(1, false));
        cancelled.Show();
        Assert.True(DialogTestKit.Named<RadioCard>(cancelled, "MidiCard").IsSelected);   // no tab part: MIDI is picked first
        cancelled.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Null(cancelled.Answer);
    }
}
