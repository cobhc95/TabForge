using System.IO;
using System.Text;
using System.Text.Json;
using TabForge.Platform;

namespace TabForge.Services.Reset;

// Owns: the pending-wipe marker written after the window closes and run at the very start of the next launch, before any store opens.
// Does not own: the plan (ResetAppPlan) or the wipe itself (ResetAppWipe); it rebuilds a fresh plan from the saved item names.
// Tests: ResetAppPlanTests.
/// <summary>The outcome of looking for and running a pending wipe.</summary>
public sealed record PendingAppWipeOutcome(bool Ran, string? Problem, ResetAppWipeResult? Result);

/// <summary>A marker holding only item names, never raw paths: the next start resolves the paths again and revalidates them.</summary>
public static class PendingAppWipe
{
    public const string FileName = "pending-wipe.json";
    private const int Version = 1;
    private const int MaxBytes = 64 * 1024;
    private static JsonSerializerOptions Json => new() { WriteIndented = true };

    public static string MarkerPath(IAppFolders folders) => Path.Combine(folders.Settings, FileName);

    /// <summary>Writes the confirmed ticks (and whether a backup was taken) for the next launch.</summary>
    public static void Write(IAppFolders folders, IEnumerable<ResetAppItemKind> items, bool backupTaken)
    {
        var request = new PendingWipeRequest(Version, items.Select(item => item.ToString()).Distinct(StringComparer.Ordinal).ToArray(), backupTaken);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json));
        Directory.CreateDirectory(folders.Settings);
        FilePathPolicy.WriteAtomically(MarkerPath(folders), stream => stream.Write(bytes));
    }

    /// <summary>Runs a marker left by the previous run; false when there is none. The marker is removed before the wipe starts.</summary>
    public static bool TryRun(IAppFolders folders)
    {
        var path = MarkerPath(folders);
        try
        {
            if (!File.Exists(path)) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }

        PendingWipeRequest? request = null;
        string? problem = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length <= MaxBytes)
                request = JsonSerializer.Deserialize<PendingWipeRequest>(File.ReadAllText(path), Json);
            else problem = "the pending wipe file is too large";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            problem = "the pending wipe file could not be read: " + ex.Message;
        }

        TryDelete(path);
        var items = request?.Items?.Select(item => Enum.TryParse<ResetAppItemKind>(item, out var kind) ? kind : (ResetAppItemKind?)null)
            .OfType<ResetAppItemKind>().Distinct().ToList() ?? new List<ResetAppItemKind>();
        if (request is null || request.Version != Version || items.Count == 0)
        {
            if (problem is not null) Trace.Error(Trace.Ui, "reset app: " + problem);
            return true;   // a marker was present but unusable: never retry it forever
        }

        try
        {
            var plan = ResetAppPlan.Create(folders, items);
            var result = ResetAppWipe.Run(plan);
            Trace.Write(Trace.Ui, $"reset app: wiped {result.Files} files, {result.Values} settings values; {result.Refused.Count} refused");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Trace.Error(Trace.Ui, "reset app: " + ex.Message);
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.Error(Trace.Ui, "reset app: remove marker: " + ex.Message); }
    }

    private sealed record PendingWipeRequest(int Version, string[] Items, bool BackupTaken);
}
