namespace TabForge.KeyboardMode;

// Owns: the running totals of a Keyboard mode run: hits, perfects, misses, extras, streak and accuracy, read from a judge in onset order.
// Does not own: judging (KeyboardModeJudge) or any display.
// Tests: TestKeyboardModeScore.
public sealed class KeyboardModeScore
{
    private int _consumed;
    private int _groupsDone;

    public int Hits { get; private set; }
    public int Perfects { get; private set; }
    public int Misses { get; private set; }
    /// <summary>Presses that matched no note (they do not change the accuracy).</summary>
    public int Extras { get; private set; }
    /// <summary>Chords with some notes hit and some missed.</summary>
    public int PartialChords { get; private set; }
    /// <summary>Hit notes whose key was released too soon.</summary>
    public int ShortHolds { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }
    public int Judged => Hits + Misses;
    /// <summary>Hits as a percentage of judged notes (0 when none is judged yet).</summary>
    public double AccuracyPercent => Judged == 0 ? 0 : 100.0 * Hits / Judged;

    /// <summary>Takes in every note the judge has resolved in order since the last call (a later note waits for an earlier pending one, so the streak is in score order).</summary>
    public void Absorb(KeyboardModeJudge judge)
    {
        while (_consumed < judge.ResolvedPrefix)
        {
            var r = judge.Results[_consumed++];
            if (r.IsHit)
            {
                Hits++;
                if (r.Grade == KeyboardModeGrade.Perfect) Perfects++;
                if (r.Sustained == false) ShortHolds++;
                BestStreak = Math.Max(BestStreak, ++Streak);
            }
            else { Misses++; Streak = 0; }
        }
        while (_groupsDone < judge.GroupStarts.Count && (_groupsDone + 1 < judge.GroupStarts.Count ? judge.GroupStarts[_groupsDone + 1] : judge.Results.Count) <= _consumed)
            if (judge.IsPartial(_groupsDone++)) PartialChords++;
        Extras = judge.Extras.Count;
    }

    public void Reset() => _consumed = _groupsDone = Hits = Perfects = Misses = Extras = PartialChords = ShortHolds = Streak = BestStreak = 0;
}
