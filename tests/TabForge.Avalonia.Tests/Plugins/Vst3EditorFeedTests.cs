using System;
using System.IO;
using System.Runtime.InteropServices;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the VST3 shim's feed from a plug-in's processor to its editor (output parameter ring, message copy / queueing), checked through the shim's own
//   tfv3_selftest export, and that the shim next to the tests exports the editor-idle entry point the engine calls.
// Does not own: hosting a real plug-in (no VST3 plug-in is installed on the build machines).
// Tests: itself.
public sealed class Vst3EditorFeedTests
{
    private static string Shim => Path.Combine(AppContext.BaseDirectory, "tfvst3.dll");

    [Fact]
    public void TheShimsOwnChecksPass()
    {
        if (!OperatingSystem.IsWindows()) return;   // tfvst3.dll is the Windows shim
        Assert.True(File.Exists(Shim), "tfvst3.dll is copied next to the tests");
        Assert.Equal(0, Vst3Plugin.NativeSelfTest());
    }

    [Fact]
    public void TheShimExportsTheEditorIdleFeedAndAcceptsANullHandle()
    {
        if (!OperatingSystem.IsWindows()) return;
        var lib = NativeLibrary.Load(Shim);
        try
        {
            Assert.True(NativeLibrary.TryGetExport(lib, "tfv3_selftest", out _));
            Assert.True(NativeLibrary.TryGetExport(lib, "tfv3_editor_idle", out var idle));
            var call = Marshal.GetDelegateForFunctionPointer<IdleDelegate>(idle);
            Assert.Equal(0, call(IntPtr.Zero));   // no plug-in: nothing to apply, nothing requested
        }
        finally { NativeLibrary.Free(lib); }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IdleDelegate(IntPtr plugin);
}
