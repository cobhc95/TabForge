using System.IO;
using TabForge.Models;

namespace TabForge.Services;

// Owns: how a MIDI file becomes tracks: the two import modes (tab and notation tracks, or MIDI tracks), the stored setting values, the look at a file
//   before the question (track count, which mode fits), and the import's report line.
// Does not own: the conversion (MidiToSong), the question window (MidiImportPromptWindow) or the setting row (SettingsCatalog.Tabs).
// Tests: MidiImportModeTests.
/// <summary>Whether a MIDI file's tracks become tab and notation tracks or MIDI tracks (piano roll). Never both for the same notes.</summary>
public enum MidiImportMode { Tab, Midi }

/// <summary>The stored "MIDI import" setting: ask each time, or always one of the two modes.</summary>
public static class MidiImportModes
{
    public const string Ask = "Ask each time";
    public const string Tab = "Always as tab tracks";
    public const string Midi = "Always as MIDI tracks";
    public static readonly IReadOnlyList<string> Labels = new[] { Ask, Tab, Midi };

    /// <summary>The remembered mode, or null for Ask (and for anything unknown).</summary>
    public static MidiImportMode? Remembered(string? stored) =>
        string.Equals(stored, Tab, StringComparison.OrdinalIgnoreCase) ? MidiImportMode.Tab :
        string.Equals(stored, Midi, StringComparison.OrdinalIgnoreCase) ? MidiImportMode.Midi : null;

    /// <summary>The setting value that remembers <paramref name="mode"/>.</summary>
    public static string Store(MidiImportMode mode) => mode == MidiImportMode.Tab ? Tab : Midi;
}

/// <summary>What the question needs to know about a MIDI file: how many tracks it makes and which mode suits it.</summary>
public sealed record MidiImportPreview(int TrackCount, bool HasTabInstrument)
{
    /// <summary>Tab when at least one track is a guitar, bass or drum part (by its program or channel 10), else MIDI.</summary>
    public MidiImportMode DefaultMode => HasTabInstrument ? MidiImportMode.Tab : MidiImportMode.Midi;

    /// <summary>Reads the file (bounded) and counts its tracks the way the import will. Throws like the import for a file that is not a readable MIDI score.</summary>
    public static MidiImportPreview Inspect(string path)
    {
        path = FilePathPolicy.ExistingFile(path, "score file", FileTypes.Midi);
        return Inspect(SmfReader.Read(InputLimits.ReadBoundedBytes(path, InputLimits.MaxGuitarProFileBytes, "score file")));
    }

    internal static MidiImportPreview Inspect(SmfFile smf)
    {
        var groups = MidiToSong.Groups(smf);
        var tab = false;
        foreach (var (name, drums, notes, source, partChannel) in groups)
        {
            if (drums) { tab = true; continue; }
            var (program, hasProgram) = MidiToSong.FirstProgram(source, partChannel, notes);
            var probe = new TrackModel();
            MidiToSong.SetInstrument(probe, program, hasProgram, name, notes.Min(n => n.Pitch), notes.Max(n => n.Pitch));
            if (probe.Kind is TrackKind.Guitar or TrackKind.Bass) tab = true;
        }
        return new MidiImportPreview(groups.Count, tab);
    }
}

/// <summary>The line the import reports once it is done.</summary>
internal static class MidiImportNotice
{
    public static string Text(MidiImportMode mode, int tracks, int sections, int droppedControllers)
    {
        var text = $"{tracks} track{(tracks == 1 ? "" : "s")} as {(mode == MidiImportMode.Tab ? "tab and notation tracks" : "MIDI tracks")}";
        if (sections > 0) text += $", {sections} section{(sections == 1 ? "" : "s")} found";
        text += ".";
        if (mode == MidiImportMode.Tab && droppedControllers > 0) text += " Controllers and pitch bends are kept only in MIDI mode.";
        return text;
    }
}
