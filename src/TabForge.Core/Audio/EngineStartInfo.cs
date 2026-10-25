using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace TabForge.Audio;

// Owns: the command line that starts the audio engine process: its mode, its session, TabForge's process id and this run's log file (named here, once per run).
// Does not own: starting the process or its pipes (AudioEngineClient), and the engine's own argument checks (EngineHost).
// Tests: TabForge.Avalonia.Tests.EngineStartInfoTests.
/// <summary>Builds the start information of one audio engine process; each argument is its own list entry, so no value can split into more arguments.</summary>
public static class EngineStartInfo
{
    /// <summary>The engine's log file for a run: a new file per session, in the given Diagnostics folder.</summary>
    public static string LogPathFor(string diagnosticsFolder, string session) => Path.Combine(diagnosticsFolder, $"tabforge-audioengine-{session}.log");

    public static ProcessStartInfo For(string exe, string session, int parentProcessId, string diagnosticsFolder, string? pipeToken = null)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--audio-engine");
        start.ArgumentList.Add(session);
        start.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(LogPathFor(diagnosticsFolder, session));
        if (pipeToken is not null) start.ArgumentList.Add(pipeToken);   // the pipe token: the engine sends it first (PipeAuth)
        return start;
    }
}
