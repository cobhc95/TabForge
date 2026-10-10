using System.IO;
using System.Text;
using TabForge.Plugins;

namespace TabForge.Services;

// Owns: the size and count limits applied to every file and pasted input read.
// Does not own: the reading code and error messages.
// Tests: TestSecurityInputBoundaries, TestGuitarProImportWorker.
/// <summary>Small, shared resource bounds for files and model graphs accepted from disk.</summary>
public static class InputLimits
{
    // These caps are intentionally far above ordinary songs while bounding parser and object-graph growth.
    public const long MaxTforgeFileBytes = 128L * 1024 * 1024;
    public const long MaxGuitarProFileBytes = 128L * 1024 * 1024;
    /// <summary>The JSON size of a crash-recovery copy, written and read back from the app's own Recovery folder only (a song over <see cref="MaxTforgeFileBytes"/> must still be recoverable).</summary>
    public const long MaxRecoveryProjectBytes = 1024L * 1024 * 1024;
    public const long MaxSettingsJsonBytes = 2L * 1024 * 1024;
    /// <summary>Score clips read from the clipboard (untrusted, <see cref="ScoreClip"/>): size and count caps.</summary>
    public const int MaxClipboardBytes = 16 * 1024 * 1024;
    public const int MaxClipBars = 4_096;
    public const int MaxClipEvents = 65_536;
    public const double MaxClipSlots = MaxClipBars * (double)MaxCellsPerMeasure;
    public const int MaxJsonDepth = 64;

    public const int MaxTracks = 256;
    public const int MaxMeasuresPerTrack = 20_000;
    public const int MaxTotalMeasures = 100_000;
    public const int MaxCellsPerMeasure = 256;
    public const int MaxTotalCells = 2_000_000;
    public const int MaxVoicesPerMeasure = 8;
    public const int MaxBeatsPerMeasure = 1_024;
    public const int MaxNavigationEntriesPerMeasure = 64;
    public const int MaxNotesPerCell = 256;
    public const int MaxNotesPerMeasure = 4_096;
    public const int MaxTotalNotes = 1_000_000;
    public const int MaxMarkers = 10_000;
    public const int MaxStringsPerTrack = 16;
    public const int MaxFrets = 127;
    public const int MaxPluginsPerTrack = 64;
    /// <summary>Largest saved plug-in state kept in a song, in base64 characters: the one system-wide contract (16 MiB raw, see PluginStateLimits).</summary>
    public const int MaxPluginStateChars = TabForge.Audio.Contracts.PluginStateLimits.MaxBase64Chars;
    public const int MaxCurvePoints = 512;
    public const int MaxTotalCurvePoints = 2_000_000;
    public const int MaxTechniquesPerNote = 64;
    public const int MaxTotalTechniques = 1_000_000;
    public const int MaxTotalStringTuningValues = 4_096;
    public const int MaxTotalPluginSlots = 16_384;
    public const int MaxTotalArticulationBindings = 100_000;

    public const int MaxTitleLength = 256;
    public const int MaxUserTextLength = 4_096;
    public const int MaxLyricsLength = 65_536;
    /// <summary>The song's notice (Guitar Pro "notices": one line per entry, often a long tabber's note; real songs reach 6,000+ characters). Same bound as lyrics.</summary>
    public const int MaxNoticeLength = 65_536;
    public const int MaxLyricsLinesPerBeat = 64;
    public const int MaxSettingsTextLength = 4_096;
    public const int MaxRecentColourLength = 64;
    public const int MaxHotkeyActionIdLength = 128;
    public const int MaxHotkeyGestureLength = 64;
    // Windows extended-length paths are bounded by 32,767 UTF-16 code units.
    public const int MaxPathLength = 32_767;

    public const int MinTempo = 20;
    public const int MaxTempo = 400;
    public const int MaxTimeSignatureNumerator = 32;
    public const int MaxTimeSignatureDenominator = 64;

    public const int MaxRecentColours = 32;
    public const int MaxHotkeyBindings = 128;
    public const int MaxWorkspaceFloatingWindows = 32;
    public const int MaxWorkspaceClosedPanels = 64;
    public const int MaxWorkspacePanelsPerNode = 32;
    public const int MaxWorkspaceNodes = 512;
    public const int MaxWorkspaceDepth = 32;
    public const int MaxVstScanEntries = 50_000;
    public const int MaxVstPlugins = 4_096;
    public const int MaxVstScanDepth = 64;
    public const int MaxDiagnosticRecords = 100_000;
    public const long MaxDiagnosticLogBytes = 8L * 1024 * 1024;
    public const long MaxLayoutLogBytes = 256L * 1024;
    public const int MaxBundledSvgAssets = 1_024;
    public const int MaxBundledSvgIndexEntries = 5_000;
    public const int MaxBundledSvgDirectoryDepth = 32;
    public const long MaxBundledSvgBytes = 1L * 1024 * 1024;

    public static byte[] ReadBoundedBytes(string path, long maximumBytes, string fileDescription)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"The selected {fileDescription} no longer exists.", path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException($"The {fileDescription} exceeds the {FormatBytes(maximumBytes)} size limit.");
        if (stream.Length > int.MaxValue)
            throw new InvalidDataException($"The {fileDescription} is too large to read safely.");

        var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new InvalidDataException($"The {fileDescription} was truncated while being read.");
            offset += read;
        }
        if (stream.ReadByte() != -1)
            throw new InvalidDataException($"The {fileDescription} changed while being read.");
        return bytes;
    }

    public static string ReadBoundedText(string path, long maximumBytes, string fileDescription)
    {
        var bytes = ReadBoundedBytes(path, maximumBytes, fileDescription);
        try { return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException($"The {fileDescription} is not valid UTF-8 text.", ex);
        }
    }

    public static void RequireExtension(string path, string fileDescription, params string[] extensions)
    {
        var extension = Path.GetExtension(path);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Choose a {fileDescription} with one of these extensions: {string.Join(", ", extensions)}.");
    }

    public static bool IsValidTimeSignatureDenominator(int denominator) =>
        denominator is >= 1 and <= MaxTimeSignatureDenominator && (denominator & (denominator - 1)) == 0;

    public static bool IsSafeText(string? value, int maximumLength, bool allowLineBreaks = true)
    {
        if (value is null || value.Length > maximumLength) return false;
        foreach (var character in value)
        {
            if (character == '\0') return false;
            if (char.IsControl(character) && !(allowLineBreaks && character is '\r' or '\n' or '\t')) return false;
        }
        return true;
    }

    private static string FormatBytes(long bytes) => bytes % (1024 * 1024) == 0
        ? $"{bytes / (1024 * 1024)} MiB"
        : $"{bytes / 1024} KiB";
}
