using System.IO;
using System.Security.Cryptography;
using System.Text;
using TabForge.Audio.Contracts;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>
/// Plug-in states of chains kept in the settings file (auto-load chains, startup tracks) live in their own files under
/// %APPDATA%\TabForge\ChainStates, named by content hash; the settings file keeps only "@state:&lt;hash&gt;". Plug-in states
/// are often tens of kilobytes to megabytes, far beyond the settings file's per-text and total size limits.
/// </summary>
public static class ChainStateStore
{
    public const string Prefix = "@state:";

    /// <summary>Tests only: a scratch folder instead of the user's.</summary>
    internal static string? FolderOverride;

    public static string Folder => FolderOverride ?? Path.Combine(PluginLibrary.Root, "ChainStates");

    public static bool IsReference(string? state) => state is not null && state.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Moves every inline state out to its file and leaves a reference. Returns true when anything moved.</summary>
    public static bool Externalise(IEnumerable<PluginSlot> plugins)
    {
        var moved = false;
        foreach (var p in plugins)
        {
            if (string.IsNullOrEmpty(p.State) || IsReference(p.State)) continue;
            if (p.State.Length > PluginStateLimits.MaxBase64Chars) { p.State = ""; moved = true; continue; }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(p.State))).ToLowerInvariant();
            var path = Path.Combine(Folder, hash + ".state");
            if (!File.Exists(path) || !StoredMatches(path, hash))   // a damaged copy is replaced, not reused
            {
                Directory.CreateDirectory(Folder);
                var bytes = Encoding.ASCII.GetBytes(p.State);
                FilePathPolicy.WriteAtomically(path, s => s.Write(bytes));
            }
            p.State = Prefix + hash;
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// Replaces references by the stored states (missing / invalid files: empty state, the plug-in's default). The file name is the
    /// SHA-256 of its content: a file whose content does not hash to its name (damaged, truncated or tampered) is treated as missing.
    /// </summary>
    public static void Resolve(IEnumerable<PluginSlot> plugins)
    {
        foreach (var p in plugins)
        {
            if (!IsReference(p.State)) continue;
            var hash = p.State![Prefix.Length..];
            p.State = "";
            if (!IsHashName(hash)) continue;
            try
            {
                var bytes = InputLimits.ReadBoundedBytes(Path.Combine(Folder, hash + ".state"), PluginStateLimits.MaxBase64Chars, "plug-in state");
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)) continue;
                p.State = Encoding.ASCII.GetString(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "plug-in state: read: " + ex.Message); }
        }
    }

    private static bool StoredMatches(string path, string hash)
    {
        try
        {
            var bytes = InputLimits.ReadBoundedBytes(path, PluginStateLimits.MaxBase64Chars, "plug-in state");
            return Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { Services.Trace.Error(Services.Trace.Engine, "plug-in state: verify: " + ex.Message); return false; }
    }

    private static bool IsHashName(string hash) => hash.Length == 64 && hash.All(char.IsAsciiHexDigit);

    /// <summary>Unreferenced files younger than this are kept: a concurrent writer may have just stored a state it references next.</summary>
    internal static readonly TimeSpan GarbageMinimumAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Deletes stored states that no chain in <paramref name="settings"/> references (auto-load chains, startup tracks) and that are
    /// older than <paramref name="minimumAge"/> (default <see cref="GarbageMinimumAge"/>). Call it after the settings file was written,
    /// with the settings just written. Returns the number of files deleted.
    /// </summary>
    public static int CollectGarbage(PluginSettings settings, TimeSpan? minimumAge = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chains = (settings.AutoChains ?? new()).Select(c => c?.Plugins).Concat((settings.StartupTracks ?? new()).Select(t => t?.Plugins));
        foreach (var chain in chains)
            foreach (var p in chain ?? new())
                if (p is not null && IsReference(p.State)) referenced.Add(p.State![Prefix.Length..]);
        var deleted = 0;
        try
        {
            if (!Directory.Exists(Folder)) return 0;
            var cutoff = DateTime.UtcNow - (minimumAge ?? GarbageMinimumAge);
            foreach (var file in Directory.EnumerateFiles(Folder, "*.state"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!IsHashName(name) || referenced.Contains(name)) continue;   // only this store's own hash-named files
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: garbage collection: a locked state file is kept for the next sweep.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "plug-in state: sweep folder: " + ex.Message); }
        return deleted;
    }
}
