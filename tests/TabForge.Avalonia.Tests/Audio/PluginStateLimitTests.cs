using System;
using System.IO;
using System.Text;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 13 D3's one state-size contract: every format uses PluginStateLimits.Check, so a blob at the limit is
// accepted and one byte over it is refused with the same readable message.
public sealed class PluginStateLimitTests
{
    [Fact]
    public void AStateAtTheLimitIsAcceptedAndOneByteOverIsRefused()
    {
        PluginStateLimits.Check(0);
        PluginStateLimits.Check(PluginStateLimits.MaxRawBytes);

        var error = Assert.Throws<InvalidDataException>(() => PluginStateLimits.Check(PluginStateLimits.MaxRawBytes + 1L));
        Assert.Contains("16 MiB limit", error.Message);
    }

    /// <summary>Regression for Stage 13 A11: the default text bound is the app's maximum path length, not one character more.</summary>
    [Fact]
    public void TheDefaultBoundedStringMatchesTheMaximumPathLength()
    {
        using var atLimit = new MemoryStream();
        using (var writer = new BinaryWriter(atLimit, Encoding.UTF8, leaveOpen: true)) writer.Write(new string('a', Frames.MaxPathChars));
        atLimit.Position = 0;
        using (var reader = new BinaryReader(atLimit, Encoding.UTF8, leaveOpen: true))
            Assert.Equal(Frames.MaxPathChars, reader.ReadBoundedString().Length);

        using var over = new MemoryStream();
        using (var writer = new BinaryWriter(over, Encoding.UTF8, leaveOpen: true)) writer.Write(new string('a', Frames.MaxPathChars + 1));
        over.Position = 0;
        using var bounded = new BinaryReader(over, Encoding.UTF8, leaveOpen: true);
        Assert.Throws<InvalidDataException>(() => bounded.ReadBoundedString());
    }
}
