using TabForge.Avalonia.Views.Fx;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the file-type lists of the FX chain dialogs: the Open dialog starts on "All FX chains" (both chain types), the Save dialog offers .tfchain only.
/// Does not own: the dialogs themselves (FxChainFiles.Load, FxChainFiles.Save).
/// Tests: this class.
/// </summary>
public sealed class FxChainFilterTests
{
    [Fact]
    public void OpenFilterStartsWithAllChainTypes()
    {
        Assert.StartsWith("All FX chains (*.tfchain, *.RfxChain)|*.tfchain;*.RfxChain|", FxChainFiles.LoadFilter);
    }

    [Fact]
    public void SaveFilterHasNoRfxChain()
    {
        Assert.DoesNotContain("RfxChain", FxChainFiles.ChainFilter);
    }
}
