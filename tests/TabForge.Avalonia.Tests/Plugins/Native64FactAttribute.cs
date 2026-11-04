using System;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the opt-in attribute for tests that need the built deterministic 64-bit test DLL.
// Kept out of Native64EngineCrashTests.cs because that whole file is excluded from portable builds; the dependency
// tests below still use the attribute and must compile (and skip) on Linux/macOS.
internal sealed class Native64FactAttribute : FactAttribute
{
    public Native64FactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "The native 64-bit VST2 route is Windows-only";
        else if (Vst32TestAssets.TestPluginPath64 is null) Skip = "passthrough64.dll is not built";
    }
}
