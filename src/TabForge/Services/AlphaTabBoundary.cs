using System.IO;
using System.Reflection;
using AlphaTab;
using AlphaTab.Importer;

namespace TabForge.Services;

// Owns: the required version and assembly check of the bundled score library and the import settings it is created with.
// Does not own: the song model and the file conversion.
// Tests: TestGuitarProImportWorker, TestLongGuitarPro35Import.
/// <summary>
/// The one place where TabForge configures and checks the alphaTab dependency for importing Guitar Pro files (audit R4).
/// </summary>
/// <remarks>
/// TabForge ships alphaTab 1.8.4 built from source plus three patches (package and assembly <c>TabForge.AlphaTab</c>, see
/// vendor/alphatab/README.md): <see cref="ImporterSettings.MaxGp3To5BarCount"/> replaces upstream's hard-coded 1,000-bar
/// threshold for Guitar Pro 3-5 files. The limit is a property of the <see cref="Settings"/> object of one import, so
/// imports running at the same time cannot influence each other, and it never exceeds
/// <see cref="InputLimits.MaxMeasuresPerTrack"/>. The setting is reached through the typed API; an assembly that lacks it fails
/// <see cref="Inspect"/> and the import is refused with a clear message instead of reading a long song wrongly or not at all.
/// </remarks>
internal static class AlphaTabBoundary
{
    /// <summary>Assembly (and package) name of the patched alphaTab; upstream's is "AlphaTab".</summary>
    internal const string RequiredAssemblyName = "TabForge.AlphaTab";
    /// <summary>The patched build is assembly version 1.8.4.3 (the fourth number counts TabForge's patch revisions).</summary>
    internal static readonly Version RequiredVersion = new(1, 8, 4, 3);

    private static readonly Lazy<string?> InstalledProblem = new(() => Inspect(typeof(ScoreLoader).Assembly));

    /// <summary>Self-test seam: makes <see cref="Problem"/> report this text on the current thread (an unusable reader component). Null in production.</summary>
    [ThreadStatic] internal static string? ProblemOverride;

    /// <summary>Null when the alphaTab in use is the patched build this code needs; otherwise a message for the user.</summary>
    internal static string? Problem => ProblemOverride ?? InstalledProblem.Value;

    /// <summary>
    /// Checks <paramref name="alphaTab"/> against what the importer needs: the patched identity and version, the
    /// per-import bar limit setting, and the percussion table the drum import reads. Returns null when it matches.
    /// </summary>
    internal static string? Inspect(Assembly alphaTab)
    {
        var name = alphaTab.GetName();
        if (!string.Equals(name.Name, RequiredAssemblyName, StringComparison.Ordinal))
            return $"The score reader component is not the one TabForge needs ({name.Name} {name.Version}; expected {RequiredAssemblyName} {RequiredVersion}). Reinstall TabForge.";
        if (name.Version is null || name.Version.Major != RequiredVersion.Major || name.Version.Minor != RequiredVersion.Minor
            || name.Version.Build != RequiredVersion.Build || name.Version.Revision < RequiredVersion.Revision)
            return $"The score reader component has the wrong version ({name.Version}; expected {RequiredVersion}). Reinstall TabForge.";
        var limit = alphaTab.GetType("AlphaTab.ImporterSettings")?.GetProperty("MaxGp3To5BarCount", BindingFlags.Public | BindingFlags.Instance);
        if (limit is null || limit.PropertyType != typeof(double) || !limit.CanRead || !limit.CanWrite)
            return "The score reader component lacks the per-file bar limit TabForge needs for long songs. Reinstall TabForge.";
        if (alphaTab.GetType("AlphaTab.Model.PercussionMapper")?.GetMethod("GetArticulationById", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(double) }) is null)
            return "The score reader component lacks the percussion table TabForge needs for drum tracks. Reinstall TabForge.";
        var info = alphaTab.GetType("AlphaTab.Model.PlaybackInformation");
        foreach (var fractionName in new[] { "VolumeFraction", "BalanceFraction" })
            if (info?.GetProperty(fractionName, BindingFlags.Public | BindingFlags.Instance) is not { CanRead: true, CanWrite: true } fraction || fraction.PropertyType != typeof(double))
                return "The score reader component lacks the exact mixer volume and balance TabForge needs for .gp files. Reinstall TabForge.";
        return null;
    }

    /// <summary>Settings for reading one file: alphaTab's defaults plus this import's own bar limit (never above TabForge's per-track limit).</summary>
    internal static Settings CreateImportSettings(int maxBars)
    {
        var settings = new Settings();
        settings.Importer.MaxGp3To5BarCount = Math.Clamp(maxBars, 1, InputLimits.MaxMeasuresPerTrack);
        return settings;
    }

    /// <summary>True when <paramref name="error"/> is alphaTab's Guitar Pro 3-5 "bar count" safety threshold.</summary>
    internal static bool IsBarCountRefusal(Exception error)
    {
        var root = error.GetBaseException();
        return root is AlphaTab.Io.OverflowError && root.Message.Contains("'bar count'", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads <paramref name="data"/> with alphaTab's ScoreLoader, accepting at most <paramref name="maxBars"/> bars in a Guitar Pro 3-5 file.
    /// Throws <see cref="InvalidDataException"/> when the reader component does not match (<see cref="Problem"/>) or the file
    /// declares more bars than that; alphaTab's own errors otherwise.
    /// </summary>
    internal static AlphaTab.Model.Score LoadScore(byte[] data, int maxBars = InputLimits.MaxMeasuresPerTrack)
    {
        if (Problem is { } problem) throw new InvalidDataException(problem);
        try { return ScoreLoader.LoadScoreFromBytes(data, CreateImportSettings(maxBars)); }
        catch (MissingMemberException ex) { throw new InvalidDataException("The score reader component does not match this TabForge. Reinstall TabForge.", ex); }
        catch (Exception ex) when (IsBarCountRefusal(ex)) { throw new InvalidDataException("The score file contains too many measures.", ex); }
    }

    /// <summary>
    /// Diagnostics only (the wording of a damaged-file message): re-reads a Guitar Pro 3-5 file that failed and returns how far
    /// alphaTab got, as the partly built score, or null when that cannot be told. This is the one remaining use of private
    /// alphaTab members (<c>Gp3To5Importer</c> and its <c>_score</c> field); it is never on the path of a successful import
    /// and a changed alphaTab only makes it return null (no hint in the message).
    /// </summary>
    internal static AlphaTab.Model.Score? ReadPartial(byte[] data)
    {
        try
        {
            var type = typeof(ScoreLoader).Assembly.GetType("AlphaTab.Importer.Gp3To5Importer");
            if (type is null || Activator.CreateInstance(type, true) is not ScoreImporter importer) return null;
            importer.Init(AlphaTab.Io.ByteBuffer.FromBuffer(data), CreateImportSettings(InputLimits.MaxMeasuresPerTrack));
            try { importer.ReadScore(); } catch (Exception) { /* expected: the caller is locating this failure */ }
            return type.GetField("_score", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(importer) as AlphaTab.Model.Score;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
}
