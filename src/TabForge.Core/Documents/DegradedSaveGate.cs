using System;
using System.Collections.Generic;

namespace TabForge.Documents;

// Owns: the degraded mode after an unexpected error (the model may be inconsistent): the flag, and the files the person has already agreed to
//     overwrite while degraded, so the question is asked once per file.
// Does not own: the question (the window's save prompt), the save itself (DocumentSaveFlow), the crash record (the crash log).
// Tests: TabForge.Avalonia.Tests.MainWindowSaveTests.
// Thread: UI thread only (the windows of one process share one gate; the crash handler's mark is a plain flag write read on the UI thread).

/// <summary>Whether a save over an existing file must ask first, and which files the person has agreed to replace.</summary>
public sealed class DegradedSaveGate
{
    private readonly HashSet<string> _confirmedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set after an unhandled error: overwriting a file is opt-in until the app restarts.</summary>
    public bool Degraded { get; private set; }

    public void MarkDegraded() => Degraded = true;

    /// <summary>True when a save over <paramref name="path"/> still has to ask before it overwrites.</summary>
    public bool MustConfirmOverwrite(string path) => Degraded && !_confirmedPaths.Contains(path);

    /// <summary>The person agreed to write <paramref name="path"/> while degraded (overwrite, or the new file it was saved as): no second question for it.</summary>
    public void ConfirmOverwrite(string path)
    {
        if (Degraded) _confirmedPaths.Add(path);
    }
}
