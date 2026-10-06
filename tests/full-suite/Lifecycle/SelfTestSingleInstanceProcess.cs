using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using TabForge.Services;

namespace TabForge;

/// <summary>Runs the real Explorer open handover between two app processes with an isolated profile and bounded cleanup.</summary>
public static partial class SelfTest
{
    private const uint CloseWindowMessage = 0x0010;

    private sealed record HandoverStatus(bool Visible, long Handle, string[] Documents);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    private static void TestSingleInstanceProcessHandover()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            Check("single-instance process: executable is available", false, $"exe '{exe}'");
            return;
        }

        var work = Path.Combine(Path.GetTempPath(), "tf-single-instance-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(work, "profile");
        var fixture = Path.Combine(work, "Explorer opened song.tforge");
        Process? first = null;
        Process? second = null;
        try
        {
            Directory.CreateDirectory(work);
            ProjectService.Save(fixture, TwoBarSong());
            first = StartApp(exe, profile);
            var ready = WaitForStatus(profile, status => status is { Visible: true, Handle: not 0, Documents.Length: 0 }, 30_000);
            Check("single-instance process: first process publishes a visible window after pipe listen and dialog prewarm", ready is not null);
            if (ready is null) return;

            second = StartApp(exe, profile, fixture);
            var secondExited = second.WaitForExit(5_000);
            Check("single-instance process: Explorer-style second launch exits within five seconds", secondExited, secondExited ? null : "second launch stayed running");
            if (secondExited) Check("single-instance process: second launch exits successfully", second.ExitCode == 0, $"exit code {second.ExitCode}");

            var opened = WaitForStatus(profile, status => status is { Visible: true } && status.Documents.Any(path => SamePath(path, fixture)), 30_000);
            Check("single-instance process: running window is visible and has the handed-over file as a document tab", opened is not null,
                opened is null ? "the profile status did not report the target document" : string.Join("; ", opened.Documents));

            var closeSent = PostMessage(new nint(ready.Handle), CloseWindowMessage, 0, 0);
            Check("single-instance process: last-window close message reaches the actual main window", closeSent);
            var firstExited = first.WaitForExit(7_000);
            Check("single-instance process: process exits after its last visible window closes", firstExited,
                firstExited ? null : "process remained alive after closing its last window (possible hidden prewarm window)");
            if (firstExited) Check("single-instance process: final-window shutdown exits successfully", first.ExitCode == 0, $"exit code {first.ExitCode}");
        }
        finally
        {
            StopChild(second);
            StopChild(first);
            if (Directory.Exists(work) && IsGeneratedWorkFolder(work))
            {
                try { Directory.Delete(work, recursive: true); }
                catch (IOException ex) { Log.Add($"  WARN  single-instance process: temporary profile cleanup failed: {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { Log.Add($"  WARN  single-instance process: temporary profile cleanup failed: {ex.Message}"); }
            }
        }
    }

    private static Process StartApp(string executable, string profile, string? file = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--profile");
        start.ArgumentList.Add(profile);
        if (file is not null) start.ArgumentList.Add(file);
        start.Environment["TABFORGE_TEST_SINGLE_INSTANCE"] = "1";
        return Process.Start(start) ?? throw new InvalidOperationException("TabForge test child did not start.");
    }

    private static HandoverStatus? WaitForStatus(string profile, Func<HandoverStatus?, bool> complete, int timeoutMs)
    {
        var path = Path.Combine(profile, "test-single-instance.json");
        var timer = Stopwatch.StartNew();
        do
        {
            HandoverStatus? status = null;
            try
            {
                if (File.Exists(path)) status = JsonSerializer.Deserialize<HandoverStatus>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (IOException) { }
            catch (JsonException) { }
            if (complete(status)) return status;
            Thread.Sleep(40);
        } while (timer.ElapsedMilliseconds < timeoutMs);
        return null;
    }

    private static bool SamePath(string? left, string right) => left is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedWorkFolder(string path) =>
        string.Equals(Path.GetDirectoryName(path), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path).StartsWith("tf-single-instance-", StringComparison.Ordinal);

    private static void StopChild(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
}
