using System.Windows.Controls;

namespace TabForge.Views;

// Owns: the status bar's master readout text (tempo, key, note value) and the check that replaces it only when it changes.
// Does not own: the tempo map (MusicTime.TempoAt decides the tempo), the key, or the editor's note value; the caller passes them in.
// Tests: TestLiveTempoStatus.
/// <summary>The status bar's master readout (tempo, key and note value).</summary>
public static class MasterInfoLabel
{
    /// <summary>Sets the readout to the given values; nothing is formatted or replaced while they are the same as last time, so a refresh on every playback tick costs no allocation.</summary>
    public static void Update(TextBlock target, int tempo, string key, string duration, int dots, bool triplet)
    {
        var last = target.Tag as Last;
        if (last is not null && last.Tempo == tempo && last.Dots == dots && last.Triplet == triplet && last.Key == key && last.Duration == duration) return;
        target.Tag = last = new Last(tempo, key, duration, dots, triplet);
        var text = $"♩={tempo}  ·  {key}  ·  {duration}{(dots == 1 ? " dotted" : dots >= 2 ? " double-dotted" : "")}{(triplet ? " triplet" : "")}";
        if (target.Text != text) target.Text = text;
    }

    private sealed record Last(int Tempo, string Key, string Duration, int Dots, bool Triplet);
}
