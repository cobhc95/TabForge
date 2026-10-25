using TabForge.Models;

namespace TabForge.Ports;

// Owns: the narrow contract Services edit rules need of one open document: its song and the one
//   "run this mutation as one edit" entry point, with the outcome as a Toolkit-free value.
// Does not own: the edit implementation (Documents.DocumentEdits), undo stacks, tabs, files or windows.
// Tests: no dedicated test class.

/// <summary>The outcome of one logical edit: whether anything changed.</summary>
public readonly record struct EditOutcome(bool Changed);

/// <summary>The outcome of one logical edit that produced a value; <c>Value</c> is null when nothing changed.</summary>
public readonly record struct EditOutcome<T>(bool Changed, T? Value) where T : class;

/// <summary>
/// What the Services edit rules need of an explicit document: its song and the one-undo-step edit entry point. The
/// Documents layer implements it (an <c>IEditableDocument</c>) without the Services layer referencing Documents.
/// </summary>
public interface IEditTarget
{
    SongProject Project { get; }

    /// <summary>Runs <paramref name="mutate"/> as one undo step; false means nothing changed and no step is kept.</summary>
    EditOutcome Run(Func<SongProject, bool> mutate, bool invalidatesTimeline = true, string label = "Edit");

    /// <summary>As <c>Run</c>; <paramref name="mutate"/> returns the edit's value, or null when nothing changed.</summary>
    EditOutcome<T> Run<T>(Func<SongProject, T?> mutate, bool invalidatesTimeline = true, string label = "Edit") where T : class;
}
