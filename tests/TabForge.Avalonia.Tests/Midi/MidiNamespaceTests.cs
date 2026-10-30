using TabForge.Core.Midi;
using TabForge.Core.Midi.Processors;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the namespace of the MIDI types that live in the Core assembly (they must not claim the engine's namespace).
/// Does not own: the processor behaviour (the MIDI tests).
/// Tests: this class.
/// </summary>
public sealed class MidiNamespaceTests
{
    [Fact]
    public void CoreMidiTypesUseTheCoreNamespace()
    {
        Assert.Equal("TabForge.Core.Midi", typeof(MidiBuffer).Namespace);
        Assert.Equal("TabForge.Core.Midi", typeof(MidiProcessorChain).Namespace);
        Assert.Equal("TabForge.Core.Midi", typeof(IMidiProcessor).Namespace);
        Assert.Equal("TabForge.Core.Midi.Processors", typeof(ScaleSnapProcessor).Namespace);
    }
}
