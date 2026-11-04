using System;
using System.Runtime.InteropServices;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 13 B4/B5: the VST2 host callback's canDo answer and string writer do not build managed strings.
public sealed class Vst2HostCallbackTests
{
    [Theory]
    [InlineData("sendVstEvents", true)]
    [InlineData("receiveVstMidiEvent", true)]
    [InlineData("sendVstMidiEventX", false)]
    [InlineData("", false)]
    public void CanDoComparesTheNativeStringWithoutAllocating(string name, bool expected)
    {
        var ptr = Marshal.StringToHGlobalAnsi(name);
        try { Assert.Equal(expected, Vst2Plugin.CanDo(ptr)); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    [Fact]
    public void WriteStringCopiesAsciiAndTerminatesWithinTheBound()
    {
        var buffer = Marshal.AllocHGlobal(16);
        try
        {
            for (var i = 0; i < 16; i++) Marshal.WriteByte(buffer, i, 0x7F);
            Assert.Equal((IntPtr)1, Vst2Plugin.WriteString(buffer, "TabForge", 8));
            Assert.Equal("TabForg", Marshal.PtrToStringAnsi(buffer));
            Assert.Equal(0, Marshal.ReadByte(buffer, 7));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
