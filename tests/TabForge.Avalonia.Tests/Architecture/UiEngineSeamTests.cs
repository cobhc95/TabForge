using System.IO;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the built-in editors read their parameter ids and descriptors from the Contracts seam, not from the engine's implementation namespace.
/// Does not own: the editors' layout or the engine-side effects (the built-in effect tests).
/// Tests: this class.
/// </summary>
public sealed class UiEngineSeamTests
{
    [Theory]
    [InlineData("Limiter/LimiterEditor.cs")]
    [InlineData("Compressor/CompressorEditor.cs")]
    [InlineData("Eq/EqEditor.cs")]
    [InlineData("Gate/GateEditor.cs")]
    [InlineData("Delay/DelayEditor.cs")]
    public void BuiltinEditorUsesTheContractsParameterCatalog(string relative)
    {
        var repo = ArchitectureScan.RepoRoot();
        var path = Path.Combine(repo, "Source", "src", "TabForge.Avalonia", "Views", "Builtin", relative.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("TabForge.AudioEngine.Builtin", text);
        Assert.Contains("TabForge.Audio.Contracts.Builtin", text);
    }
}
