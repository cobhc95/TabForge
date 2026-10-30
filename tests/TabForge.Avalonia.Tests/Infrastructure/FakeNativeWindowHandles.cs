using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Platform;
using TabForge.Avalonia.Platform;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: a fake INativeWindowHandles that returns one handle and records every full repaint, so a test never needs a real HWND.</summary>
internal sealed class FakeNativeWindowHandles : INativeWindowHandles
{
    /// <summary>The handle <see cref="Of"/> returns (zero by default: no native window).</summary>
    public IntPtr Handle { get; set; }

    /// <summary>The handles <see cref="Redraw"/> was asked to repaint, in order.</summary>
    public List<IntPtr> Redraws { get; } = new();

    public IntPtr Of(TopLevel? window) => Handle;

    public IPlatformHandle Wrap(IntPtr handle) => throw new NotSupportedException("The fake has no native child window.");

    public void Redraw(IntPtr handle) => Redraws.Add(handle);
}
