using System.Collections.Generic;

namespace TabForge.Avalonia.Diagnostics;

// Owns: the JSON shapes of one Stage 10 matrix run (entry read from the runner, per-check result written back).
// Does not own: the checks (PluginMatrix), downloading or screenshots (tools/plugin-matrix/run.py).
// Tests: exercised by the wine-matrix workflow and the local Windows run; no unit test loads a real plug-in.

/// <summary>One manifest entry as handed to the app by the runner, with the local extracted plug-in path.</summary>
internal sealed class MatrixEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Format { get; set; } = "VST2";
    public int Bitness { get; set; }
    public string Role { get; set; } = "Effect";
    public string Path { get; set; } = "";
    public string? ExpectedSha256 { get; set; }
    /// <summary>Set by the manifest for a plug-in whose audio needs data the runner does not supply (for example a drum kit).</summary>
    public string? SkipAudioReason { get; set; }
}

/// <summary>One of the plan's checks 1-10, with a machine-readable status and free-form metrics.</summary>
internal sealed class MatrixCheck
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "skip";
    public string Detail { get; set; } = "";
    public Dictionary<string, object?> Metrics { get; set; } = new();
}

/// <summary>The per-plug-in result JSON. The runner adds screenshot paths/metrics after the process exits.</summary>
internal sealed class MatrixResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Format { get; set; } = "";
    public int Bitness { get; set; }
    public string Role { get; set; } = "";
    public string Path { get; set; } = "";
    public string Route { get; set; } = "";
    public string Overall { get; set; } = "error";
    public string? Error { get; set; }
    public string? FailureClass { get; set; }
    public string StartedUtc { get; set; } = "";
    public long DurationMs { get; set; }
    public string Host { get; set; } = "";
    public string? WineVersion { get; set; }
    public string? Helper { get; set; }
    public int? HelperPid { get; set; }
    public List<MatrixCheck> Checks { get; set; } = new();
    public List<string> Screenshots { get; set; } = new();
    public string? Log { get; set; }
    public string? EngineLog { get; set; }
}
