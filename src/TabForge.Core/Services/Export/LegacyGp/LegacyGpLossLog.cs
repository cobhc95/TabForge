namespace TabForge.Services;

// Owns: collecting what the older-format writer changes or leaves out, as the preflight's loss lines (one line per kind, with the tracks and bars).
// Does not own: deciding what is lost (LegacyGpWriter) or the dialog (GpExportPreflightPresenter).
// Tests: TestLegacyGpExport.
/// <summary>The writer reports each change here as it writes, so the list shown before saving is exactly what the file lacks.</summary>
internal sealed class LegacyGpLossLog
{
    private readonly Dictionary<string, (string Result, List<(string Track, int Bar)> At)> _found = new();
    private readonly List<string> _order = new();

    /// <summary>Notes that <paramref name="feature"/> is changed or left out (<paramref name="result"/>) in a bar of a track; a bar of -1 means the whole track or song.</summary>
    public void Hit(string feature, string result, string track, int bar)
    {
        if (!_found.TryGetValue(feature, out var entry)) { _found[feature] = entry = (result, new()); _order.Add(feature); }
        entry.At.Add((track, bar));
    }

    public IEnumerable<GpLoss> ToLosses() =>
        _order.Select(feature => new GpLoss(feature, _found[feature].Result, _found[feature].At.Count, GpExportPreflight.Describe(_found[feature].At)));
}
