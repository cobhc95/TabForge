namespace TabForge.Features.Recording.Rules;

// Owns: which pass of each repeat region the timeline shows (view state only).
// Does not own: the regions (RepeatRegions), playback or the song; nothing here is saved or undoable.
// Tests: TabForge.Avalonia.Tests.RepeatMapTests.

/// <summary>
/// The shown pass per repeat region: the pill picker's choice, defaulting to pass 1. Keyed by region start bar;
/// call <see cref="Reset"/> when the song reloads. Playback follow records each region's last played pass, which is
/// kept after playback leaves the region. In-memory only: never saved, never undoable.
/// </summary>
public sealed class RepeatViewState
{
    private readonly Dictionary<int, int> shown = new();
    private readonly Dictionary<int, int> user = new();
    private readonly Dictionary<int, int> lastPlayed = new();

    /// <summary>The pass shown for <paramref name="region"/>: the stored choice, default 1, re-clamped to 1..N.</summary>
    public int ShownPass(RepeatRegion region) => Clamp(region, shown.TryGetValue(region.StartBar, out var pass) ? pass : 1);

    /// <summary>Show <paramref name="pass"/> of <paramref name="region"/>, clamped to 1..N (the pill picker).</summary>
    public void SetPass(RepeatRegion region, int pass)
    {
        var clamped = Clamp(region, pass);
        shown[region.StartBar] = clamped;
        user[region.StartBar] = clamped;
    }

    /// <summary>
    /// Follow playback: show the pass now playing and remember it as the region's last played pass
    /// (kept after leaving the region). The user's picked pass resumes being used when playback stops.
    /// </summary>
    public void FollowPlayback(RepeatRegion region, int playingPass)
    {
        var pass = Clamp(region, playingPass);
        shown[region.StartBar] = pass;
        lastPlayed[region.StartBar] = pass;
    }

    /// <summary>The last pass playback played in <paramref name="region"/>: 1 when it never played there.</summary>
    public int LastPlayed(RepeatRegion region) =>
        lastPlayed.TryGetValue(region.StartBar, out var pass) ? Clamp(region, pass) : 1;

    /// <summary>
    /// Show every region's own picked pass again (playback stopped): follow-playback overwrites only
    /// the shown pass, never the pick, so stopping restores the <see cref="SetPass"/> choice (1 by default).
    /// True when any shown pass changed.
    /// </summary>
    public bool ResumeUserChoices()
    {
        var changed = false;
        var keys = new HashSet<int>(shown.Keys);
        foreach (var key in user.Keys) keys.Add(key);
        foreach (var key in keys)
        {
            var want = user.TryGetValue(key, out var picked) ? picked : 1;
            if (shown.TryGetValue(key, out var now) ? now != want : want != 1)
            {
                if (want == 1) shown.Remove(key);
                else shown[key] = want;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Forget every shown, picked and last-played pass (song reload).</summary>
    public void Reset()
    {
        shown.Clear();
        user.Clear();
        lastPlayed.Clear();
    }

    /// <summary>
    /// Drops every region that no longer exists and re-clamps the kept shown, picked and last-played
    /// passes to the region's current N (in-place edits change N; zoom must keep its pick).
    /// </summary>
    public void Prune(IReadOnlyList<RepeatRegion> regions)
    {
        var counts = new Dictionary<int, int>();
        foreach (var region in regions) counts[region.StartBar] = region.PassCount;
        PruneMap(shown, counts);
        PruneMap(user, counts);
        PruneMap(lastPlayed, counts);
    }

    private static void PruneMap(Dictionary<int, int> map, Dictionary<int, int> counts)
    {
        var dead = new List<int>();
        foreach (var key in map.Keys)
        {
            if (!counts.TryGetValue(key, out var total)) dead.Add(key);
            else map[key] = total < 1 ? 1 : Math.Clamp(map[key], 1, total);
        }
        foreach (var key in dead) map.Remove(key);
    }

    private static int Clamp(RepeatRegion region, int pass) =>
        region.PassCount < 1 ? 1 : Math.Clamp(pass, 1, region.PassCount);
}
