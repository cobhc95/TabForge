using Avalonia.Headless.XUnit;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// An Allow or a lifted quarantine brings the engine in line without an undo step or a dirty mark: the song's own history is not touched.
/// Owns: this check. Does not own: the host (SongFxChainHost) or the window that asks (FxChainWindow.Editor).
/// Tests: this class.
/// </summary>
public class FxSyncWithoutUndoTests
{
    [AvaloniaFact]
    public void SyncingTheEngineMakesNoUndoStepAndNoChange()
    {
        var (document, host, track) = FxTestKit.Song();
        host.SyncEngine(track);
        Assert.False(document.Undo.CanUndo);
        Assert.False(document.Project.IsDirty);
    }
}
