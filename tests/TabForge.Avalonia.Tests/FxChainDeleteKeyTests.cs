using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Fx;
using TabForge.Documents;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// The Delete key removes the selected plug-in of the FX chain as one undo step, as the Remove button and its tooltip say.
/// Owns: this check. Does not own: the removal (FxChainWindow.RemoveSelected), the list (FxChainList).
/// Tests: this class.
/// </summary>
public class FxChainDeleteKeyTests
{
    [AvaloniaFact]
    public void TheDeleteKeyRemovesTheSelectedPluginAsOneUndoStep()
    {
        var (document, host, track) = FxTestKit.Song();
        var window = new FxChainWindow(host, track);
        window.Show();
        var list = FxTestKit.List(window);
        list.Click(1);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
        Assert.Equal(2, track.Rig.Plugins.Count);
        Assert.True(document.Undo.CanUndo);
        window.Close();
    }
}
