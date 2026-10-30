namespace TabForge.Services.Reset;

// Owns: the tick state of the first dialog step (all ticked by default, Select all/none, the "Wipe N items" count) and the
//   second step's typed RESET word.
// Does not own: the plan's paths (ResetAppPlan) or the Avalonia controls (ResetAppWindow).
// Tests: ResetAppPromptTests.
/// <summary>The dialog's eight ticks; all ticked until the user changes them.</summary>
public sealed class ResetAppSelection
{
    private readonly HashSet<ResetAppItemKind> _checked;

    public ResetAppSelection(ResetAppPlan plan, bool allChecked = true)
    {
        Plan = plan;
        _checked = allChecked ? plan.Items.Select(item => item.Kind).ToHashSet() : new();
    }

    public ResetAppPlan Plan { get; }
    public IReadOnlyList<ResetAppItem> Items => Plan.Items;
    public int CheckedCount => _checked.Count;
    public bool CanWipe => CheckedCount > 0;
    public bool AllChecked => CheckedCount == Items.Count;
    public bool NoneChecked => CheckedCount == 0;
    /// <summary>Off by default, as the owner asked.</summary>
    public bool BackUpFirst { get; set; }

    public bool IsChecked(ResetAppItemKind kind) => _checked.Contains(kind);
    public void SetChecked(ResetAppItemKind kind, bool value)
    {
        if (value) _checked.Add(kind); else _checked.Remove(kind);
    }

    public void SelectAll()
    {
        _checked.Clear();
        foreach (var item in Items) _checked.Add(item.Kind);
    }

    public void SelectNone() => _checked.Clear();

    public IReadOnlyList<ResetAppItemKind> CheckedItems => Items.Select(item => item.Kind).Where(_checked.Contains).ToList();

    public IReadOnlyList<ResetAppItem> CheckedPlanItems => Items.Where(item => _checked.Contains(item.Kind)).ToList();
    public IReadOnlyList<ResetAppItem> KeptPlanItems => Items.Where(item => !_checked.Contains(item.Kind)).ToList();

    /// <summary>The primary button's text: it counts ticks, not files, and is disabled at zero.</summary>
    public string WipeText => CheckedCount == 1 ? "Wipe 1 item" : $"Wipe {CheckedCount} items";
}

/// <summary>The second step's gate: "RESET" must be typed exactly.</summary>
public static class ResetAppPrompt
{
    public const string ConfirmWord = "RESET";

    public static bool CanWipe(string? typed) => string.Equals(typed, ConfirmWord, StringComparison.Ordinal);

    public static string KeptText(IReadOnlyList<ResetAppItem> kept) =>
        kept.Count == 0 ? "Nothing is kept from the list above." : string.Join(Environment.NewLine, kept.Select(item => "• " + item.Title));

    public static string WipedText(IReadOnlyList<ResetAppItem> wiped) =>
        wiped.Count == 0 ? "Nothing is ticked." : string.Join(Environment.NewLine, wiped.Select(item => "• " + item.Title + " — " + item.CountText));
}
