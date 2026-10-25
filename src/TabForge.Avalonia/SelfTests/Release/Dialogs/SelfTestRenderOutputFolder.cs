using System;
using System.IO;
using TabForge.Services;

namespace TabForge;

// Owns: the check that a typed render output folder naming a device path is refused and creates nothing.
// Does not own: the render dialog or the render itself.
// Tests: TestRenderOutputFolderRefused.
public static partial class SelfTest
{
    /// <summary>The render window's typed output folder goes through FilePathPolicy.OutputDirectory: a device path is refused with a readable error and no folder is created.</summary>
    private static void TestRenderOutputFolderRefused()
    {
        var target = Path.Combine(Path.GetTempPath(), "tf-render-refused-" + Guid.NewGuid().ToString("N"));
        foreach (var prefix in new[] { @"\\?\", @"\\.\" })
        {
            string? error = null;
            try { FilePathPolicy.OutputDirectory(prefix + target, "render output folder"); }
            catch (InvalidDataException ex) { error = ex.Message; }
            Check($"render output folder: a {prefix} device path is refused with the folder named in the error and nothing is created",
                error == "The selected render output folder is a device path, which is not allowed." && !Directory.Exists(target), error ?? "accepted");
        }
        Check("render output folder: an ordinary typed folder is accepted as its full path",
            FilePathPolicy.OutputDirectory(target, "render output folder") == Path.GetFullPath(target));
    }
}
