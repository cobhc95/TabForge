namespace TabForge.Capture;

// Owns: reading the capture command line: --capture <script.json> --out <folder>.
// Does not own: starting the app or running the script (the capture host).
// Tests: TabForge.Avalonia.Tests.CaptureScriptTests.

/// <summary>The capture mode's command line. <see cref="TryParse"/> returns null when the arguments do not ask for capture mode.</summary>
public sealed record CaptureArguments(string ScriptPath, string OutputFolder)
{
    public const string Switch = "--capture";
    public const string OutSwitch = "--out";

    public static CaptureArguments? TryParse(string[] args)
    {
        var at = Array.IndexOf(args, Switch);
        if (at < 0) return null;
        if (at + 1 >= args.Length) throw new FormatException("--capture needs a script file.");
        var outAt = Array.IndexOf(args, OutSwitch);
        if (outAt < 0 || outAt + 1 >= args.Length) throw new FormatException("--capture needs --out <folder>.");
        return new CaptureArguments(Path.GetFullPath(args[at + 1]), Path.GetFullPath(args[outAt + 1]));
    }
}
