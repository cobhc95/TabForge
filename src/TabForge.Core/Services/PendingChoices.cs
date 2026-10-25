namespace TabForge.Services;

// Owns: a choice list whose content is still being asked of the audio engine: it reads "Checking…" until the answer is known, then keeps the answer.
// Does not own: asking (CatalogPlatform) or redrawing the row when the answer arrives (the Preferences window waits on Settled).
// Tests: OutputSettingsTests.
/// <summary>A Settings choice list that shows <see cref="CheckingText"/> while its content is unknown. UI thread only.</summary>
public sealed class PendingChoices : IReadOnlyList<string>
{
    public const string CheckingText = "Checking…";
    private readonly Func<IReadOnlyList<string>?> _make;
    private readonly Func<Task> _settled;
    private readonly string[] _checking = { CheckingText };
    private IReadOnlyList<string>? _answered;

    /// <param name="make">The list, or null while it is still being asked (cheap: it runs on every read until the answer is known).</param>
    /// <param name="settled">A task that completes when the question is answered (or given up).</param>
    public PendingChoices(Func<IReadOnlyList<string>?> make, Func<Task> settled) { _make = make; _settled = settled; }

    /// <summary>True while the list is still "Checking…".</summary>
    public bool IsPending => Items == _checking;

    /// <summary>Completes when the question behind the list is answered or given up; read the list again then.</summary>
    public Task Settled => _settled();

    private IReadOnlyList<string> Items
    {
        get
        {
            if (_answered is null && _make() is { } made) _answered = made;
            return _answered ?? _checking;
        }
    }

    public int Count => Items.Count;
    public string this[int index] => Items[index];
    public IEnumerator<string> GetEnumerator() => Items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
