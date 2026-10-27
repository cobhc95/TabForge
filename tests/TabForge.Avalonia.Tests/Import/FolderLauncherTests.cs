using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the folder launcher's guard (a folder that does not exist is ignored before any launcher is asked).
// Does not own: the launch itself (the OS file manager) or the windows that call it.
// Tests: this class.
public sealed class FolderLauncherTests
{
    [AvaloniaFact]
    public void MissingFolderIsIgnoredWithoutTouchingTheLauncher()
    {
        var window = new Window();
        FolderLauncher.Open(window, Path.Combine(Path.GetTempPath(), "tf-no-such-folder-" + Guid.NewGuid().ToString("N")));
    }
}
