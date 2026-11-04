// Owns: the readable refusal text the Linux probe reports per category (Stage 9).
// Does not own: the probe itself (PluginCatalog) or the Wine texts (WineBridgeSupport).
// Tests: ProbeRefusalTests.
namespace TabForge.Plugins;

/// <summary>Readable refusal categories for a probe that could not identify a plug-in.</summary>
public static class ProbeRefusal
{
    public const string CannotIdentifyText = "Could not identify this plug-in (the helper did not report its type).";
    public const string CannotReadText = "Could not read this plug-in file to check it.";
    public const string HelperCrashText = "The plug-in helper ended while identifying this plug-in (helper crash).";
    public const string TimeoutPrefix = "The plug-in helper did not answer within";

    public const string CopyProtectionText =
        "This plug-in needs copy protection (iLok/PACE) or a kernel driver that does not run under Wine, so it cannot be loaded on Linux.";

    public const string MissingDependencyText =
        "The plug-in could not be loaded under Wine (a DLL or driver it needs is missing). Copy-protected plug-ins (iLok/PACE) and driver-based ones do not run under Wine.";

    public const string UnsupportedBitnessText =
        "This plug-in's bitness or format is not supported by the Wine bridge (for example a 32-bit helper is missing, or the VST3 bundle has no matching Windows binary).";

    public const string BrokenPrefixText =
        "TabForge's Wine prefix could not be prepared (see the status line). Delete the prefix folder to retry, or check disk space and permissions.";

    /// <summary>Timeout refusal for <paramref name="timeoutMs"/> (first Wine setup needs minutes, not seconds).</summary>
    public static string ForTimeout(int timeoutMs) =>
        $"{TimeoutPrefix} {timeoutMs / 1000} s. If this is the first Windows plug-in after installing, Wine may still be preparing; otherwise the plug-in hung and was stopped.";

    /// <summary>Maps helper stderr + exit code to a readable category (pure, tested on any OS).</summary>
    public static string Classify(string? stderr, int exitCode, string? path = null)
    {
        var err = (stderr ?? "").Trim();
        if (err.Length == 0) return exitCode == 5
            ? "The file changed since approval or could not be read; approve it again."
            : $"{HelperCrashText} (exit {exitCode}).";
        if (ContainsAny(err, "iLok", "PACE", "Sentinel", "eLicenser", "e-licenser", "Wibu", "CodeMeter", "codemeter", "kernel driver", "driver"))
        {
            // A "could not be loaded" with a protection/driver hint is copy protection, not a plain missing DLL.
            if (ContainsAny(err, "could not be loaded", "missing", "DLL", "error"))
                return CopyProtectionText;
            return CopyProtectionText;
        }
        if (ContainsAny(err, "wineboot", "WINEPREFIX", "prefix", "ShowCrashDialog", "bundled Wine", "Wine"))
            return err.Contains("could not be prepared", StringComparison.OrdinalIgnoreCase) ? $"{BrokenPrefixText} ({FirstLine(err)})" : MissingWineText(err);
        if (ContainsAny(err, "could not be loaded", "missing", "DLL that is missing", "could not be started", "VST3 host shim"))
            return MissingDependencyText;
        if (ContainsAny(err, "no 32-bit", "no 64-bit", "no VST3 audio processor", "not a VST2", "Unsupported plug-in format",
                "does not support 32-bit float", "no matching", "has no", "bitness", "not a 32-bit", "not a 64-bit"))
            return UnsupportedBitnessText;
        if (ContainsAny(err, "faulted", "did not start", "bad VST2 entry", "crash", "fault"))
            return HelperCrashText;
        if (exitCode == 5) return $"The plug-in was refused before loading: {FirstLine(err)}";
        return $"{CannotIdentifyText} {FirstLine(err)}";
    }

    private static string MissingWineText(string err) =>
        $"TabForge's Windows plug-in bridge (Wine) is missing or broken on this build: {FirstLine(err)}";

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var n in needles)
            if (text.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return line.Length > 220 ? line[..220] + "…" : line;
    }
}

internal static class ProbeTupleExtensions
{
    public static (string Role, string Vendor, string Name) ToTriple(this (string Role, string Vendor, string Name, string? Refusal) detailed)
        => (detailed.Role, detailed.Vendor, detailed.Name);
}
