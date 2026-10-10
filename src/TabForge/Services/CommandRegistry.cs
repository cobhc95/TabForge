namespace TabForge.Services;

// Owns: an id -> action table for plain commands (one handler each), with a duplicate check.
// Does not own: which ids exist (HotkeyCatalog), the handlers, or routing order (the window's RunHotkey asks the routers first).
// Tests: TestCommandRegistryRouting.
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Action> _actions = new(StringComparer.Ordinal);

    /// <summary>The registered ids.</summary>
    public IReadOnlyCollection<string> Ids => _actions.Keys;

    /// <summary>Registers a command. An id registered twice is a bug, so it throws.</summary>
    public void Add(string id, Action run)
    {
        if (!_actions.TryAdd(id, run)) throw new InvalidOperationException($"Command '{id}' is registered twice.");
    }

    /// <summary>Runs the command; false when the id is not registered.</summary>
    public bool TryRun(string id)
    {
        if (!_actions.TryGetValue(id, out var run)) return false;
        run();
        return true;
    }
}
