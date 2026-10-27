using System.Diagnostics;

namespace TabForge.Platform;

// Owns: opening a folder, file or web address with the system's own program, and showing a file in the file manager, per operating system.
// Does not own: choosing what to open, or the Avalonia launcher window code (FolderLauncher decides which to use).
// Tests: ShellCommandsTests.
/// <summary>Opens things with the system's default program. Every method answers false (never throws) when nothing could be started.</summary>
public interface IShellLauncher
{
    bool OpenFolder(string folder);
    bool OpenFile(string file);

    /// <summary>Opens an http or https address in the default browser; any other scheme is refused.</summary>
    bool OpenUrl(string url);

    /// <summary>Shows the file in the file manager (selected where the system can; otherwise its folder).</summary>
    bool Reveal(string path);
}

/// <summary>What to do with a target.</summary>
public enum ShellVerb { Open, Reveal }

/// <summary>One program start: no shell, every argument its own entry.</summary>
public sealed record ShellCommand(string Program, IReadOnlyList<string> Arguments);

/// <summary>The commands of the non-Windows launchers, as plain data so they can be checked on any machine.</summary>
public static class ShellCommands
{
    /// <summary>macOS: <c>open</c> / <c>open -R</c>. Linux: <c>xdg-open</c> (reveal opens the parent folder). Null for Windows (its adapter uses the shell directly).</summary>
    public static ShellCommand? For(HostOs os, ShellVerb verb, string target) => os switch
    {
        HostOs.MacOS => verb == ShellVerb.Reveal ? new ShellCommand("open", new[] { "-R", target }) : new ShellCommand("open", new[] { target }),
        HostOs.Linux => new ShellCommand("xdg-open", new[] { verb == ShellVerb.Reveal ? Path.GetDirectoryName(Path.GetFullPath(target)) ?? target : target }),
        _ => null,
    };

    /// <summary>True for an absolute http or https address.</summary>
    public static bool IsWebAddress(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>The macOS and Linux launcher: starts <c>open</c> or <c>xdg-open</c> without a shell and without waiting for it.</summary>
public sealed class ProcessShellLauncher : IShellLauncher
{
    private readonly HostOs _os;

    public ProcessShellLauncher(HostOs os) => _os = os;

    public bool OpenFolder(string folder) => Directory.Exists(folder) && Run(ShellVerb.Open, folder);
    public bool OpenFile(string file) => File.Exists(file) && Run(ShellVerb.Open, file);
    public bool OpenUrl(string url) => ShellCommands.IsWebAddress(url) && Run(ShellVerb.Open, url);
    public bool Reveal(string path) => (File.Exists(path) || Directory.Exists(path)) && Run(ShellVerb.Reveal, path);

    private bool Run(ShellVerb verb, string target)
    {
        if (ShellCommands.For(_os, verb, target) is not { } command) return false;
        try
        {
            var start = new ProcessStartInfo(command.Program) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return false; }   // Not logged: the caller reports the refusal
    }
}

/// <summary>The shell launcher of this system, chosen once at start-up.</summary>
public static class ShellLauncher
{
    public static IShellLauncher Current { get; } =
#if WINDOWS
        new Windows.WindowsShellLauncher();
#else
        new ProcessShellLauncher(HostSystem.Current);
#endif
}
