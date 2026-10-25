namespace TabForge.Documents;

// Owns: the default limits for document undo states (the level count and the byte cap).
// Does not own: the per-document stacks (UndoController) or what a state holds.
// Tests: no dedicated test class.

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
