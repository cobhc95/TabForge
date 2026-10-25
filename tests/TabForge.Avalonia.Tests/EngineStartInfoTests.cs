using System.IO;
using System.Linq;
using TabForge.Audio;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine's start command line (EngineStartInfo): the mode, the session, the parent's id and this run's log file, each as its own argument.
/// Does not own: starting the process (AudioEngineClient) or the engine's own argument checks (EngineHost).
/// Tests: this class.
/// </summary>
public sealed class EngineStartInfoTests
{
    [Fact]
    public void EngineStartsWithItsRunLogFileAsAFourthArgument()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-engine-start");

        var start = EngineStartInfo.For("TabForge.exe", "0123456789abcdef", 4242, folder);

        Assert.Equal(new[] { "--audio-engine", "0123456789abcdef", "4242", Path.Combine(folder, "tabforge-audioengine-0123456789abcdef.log") },
            start.ArgumentList.ToArray());
    }
}
