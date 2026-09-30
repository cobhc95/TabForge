namespace TabForge.Documents;

/// <summary>
/// Shared default limits for document undo states. UndoController owns the per-document stacks.
/// </summary>
public static class UndoHistory
{
    /// <summary>Most states to keep, newest first.</summary>
    public const int MaxLevels = 200;

    /// <summary>Most bytes the history may hold (the count cap is usually the binding one).</summary>
    public const long MaxBytes = 48L * 1024 * 1024;

}
