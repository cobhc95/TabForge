using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace ParityRecorder;

/// <summary>
/// One TabForge process started with <c>--capture</c>, so the app parks its windows off-screen itself.
/// Owns: the process, its UI Automation session and the window lookup by process id. Kills only the process it started.
/// Does not own: the capture script (Recorder) or what is read from the windows (TreeDump, HitMap, Scenarios).
/// </summary>
internal sealed class TabForgeProcess : IDisposable
{
    private readonly Process _process;

    public UIA3Automation Automation { get; } = new();

    public int Pid => _process.Id;

    /// <summary>Starts TabForge with <paramref name="arguments"/> (a scratch <c>--profile</c> is always the first pair).</summary>
    public TabForgeProcess(string exe, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        _process = Process.Start(info) ?? throw new InvalidOperationException("TabForge did not start.");
    }

    /// <summary>An off-screen capture session: the app parks its windows at -20000 and runs <paramref name="script"/>.</summary>
    public static TabForgeProcess Capture(string exe, string profile, string script, string outDir) =>
        new(exe, ["--profile", profile, "--capture", script, outDir]);

    /// <summary>A live session on the desktop with a scratch profile; the caller drives it with real input (inside the test VM only).</summary>
    public static TabForgeProcess Live(string exe, string profile) => new(exe, ["--profile", profile]);


    /// <summary>The main window's handle as the process reports it (the first top-level window it created).</summary>
    public IntPtr MainHandle()
    {
        _process.Refresh();
        return _process.MainWindowHandle;
    }

    public List<AutomationElement> Windows() =>
        Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(_process.Id)).ToList();

    /// <summary>Waits until <paramref name="ready"/> holds and the window list has not changed for three polls.</summary>
    public List<AutomationElement> WaitForWindows(Func<IReadOnlyList<AutomationElement>, bool> ready, int limitSeconds = 90)
    {
        var deadline = DateTime.UtcNow.AddSeconds(limitSeconds);
        var last = "";
        var stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited) throw new InvalidOperationException($"TabForge exited early with code {_process.ExitCode}.");
            var windows = Windows();
            var fingerprint = string.Join("|", windows.Select(w => $"{ElementText.NameOf(w)}{w.BoundingRectangle}"));
            stable = ready(windows) && fingerprint == last ? stable + 1 : 0;
            if (stable >= 3) return windows;
            last = fingerprint;
            Thread.Sleep(500);
        }
        throw new TimeoutException($"TabForge windows were not ready within {limitSeconds} s.");
    }

    /// <summary>The focused element when it belongs to this process, otherwise null.</summary>
    public AutomationElement? FocusedInside()
    {
        try
        {
            var focused = Automation.FocusedElement();
            return focused is not null && focused.Properties.ProcessId.ValueOrDefault == _process.Id ? focused : null;
        }
        catch (Exception)
        {
            return null; // Not logged: a focus that cannot be read is recorded as outside the app.
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10000);
        }
        catch (InvalidOperationException)
        {
            // Not logged: the process already ended.
        }
        Automation.Dispose();
        _process.Dispose();
    }
}
