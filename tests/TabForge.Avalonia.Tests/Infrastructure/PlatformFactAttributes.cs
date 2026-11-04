using System;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>A fact that only runs on Windows; on other systems xUnit reports it as skipped instead of silently passing.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only test.";
    }
}

/// <summary>A fact that only runs on Unix (Linux/macOS); elsewhere xUnit reports it as skipped.</summary>
internal sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Unix-only test.";
    }
}


