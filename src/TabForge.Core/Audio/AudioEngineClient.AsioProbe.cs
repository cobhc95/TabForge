using System.Diagnostics;
using System.IO;
using TabForge.Audio.Contracts;

namespace TabForge.Audio;

// Owns: asking the engine about an ASIO driver without blocking the UI: what it offers (rates, preferred buffer, channel names; answers remembered per driver) and showing its control panel.
// Does not own: opening the driver (only the engine process ever does) or the settings rows that show the answer.
// Tests: AsioProbeTests.

/// <summary>What an ASIO driver offers: the rates it accepts, the buffer size it prefers (0: unknown) and its input and output channel names (none: unknown).</summary>
public sealed record AsioCapabilities(IReadOnlyList<int> Rates, int PreferredBuffer, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

public sealed partial class AudioEngineClient
{
    // Key: driver name as asked. Ceiling: the installed ASIO drivers. Thread: all guarded by _gate.
    private readonly Dictionary<string, AsioCapabilities> _asioKnown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)> _asioNames = new(StringComparer.OrdinalIgnoreCase);   // kept when the rates are asked again: the names do not change with a restart
    private readonly HashSet<string> _asioGivenUp = new(StringComparer.OrdinalIgnoreCase);   // asked, never usefully answered: not asked again this session
    private readonly Dictionary<string, long> _asioRetryAt = new(StringComparer.OrdinalIgnoreCase);   // asked, not answered in time (or no engine): Stopwatch time before which it is not asked again
    private readonly Dictionary<string, Task<AsioCapabilities?>> _asioAsking = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TaskCompletionSource<AsioCapabilities>> _asioAsked = new();
    private readonly Dictionary<int, (TaskCompletionSource<string> Asked, long OpenedTicks)> _panelAsked = new();   // OpenedTicks: Stopwatch time the panel was asked for (its cap runs from there)
    private int _nextAsioRequest;

    /// <summary>How long an unanswered question about an ASIO driver (no engine, or no reply in time) is left alone before it is asked again; until then the driver counts as given up, so the settings do not ask in a loop.</summary>
    internal TimeSpan AsioRetryCooldown { get; set; } = TimeSpan.FromSeconds(30);
    private const int PanelTimeoutMs = (int)EngineWatchdog.PanelCapSec * 1000;   // the panel is modal in the engine: it answers when the user closes it; the liveness check waits for it up to the same cap

    /// <summary>True while an ASIO control panel is open in the engine: its modal call blocks the engine's main thread, which then cannot answer pings.</summary>
    internal bool PanelPending => PanelHeldAt(Stopwatch.GetTimestamp());

    /// <summary>Whether a panel still holds the engine at <paramref name="nowTicks"/> (a Stopwatch timestamp): one asked for longer than <see cref="EngineWatchdog.PanelCapSec"/> ago no longer does.</summary>
    internal bool PanelHeldAt(long nowTicks)
    {
        lock (_gate)
        {
            foreach (var panel in _panelAsked.Values)
                if (EngineWatchdog.PanelHolds(panel.OpenedTicks, nowTicks)) return true;
            return false;
        }
    }

    /// <summary>The driver the running engine has open as its ASIO output (empty: the first installed); null when the engine is not running or outputs through another driver.</summary>
    public string? RunningAsioDevice => IsRunning && _config is { } config && config.Driver == TabForge.Services.AudioDrivers.Asio ? config.Device : null;

    /// <summary>What ASIO driver <paramref name="driver"/> offered when it was last asked; null when not asked yet or unreadable.</summary>
    public AsioCapabilities? KnownAsio(string driver)
    {
        lock (_gate) return _asioKnown.GetValueOrDefault(driver);
    }

    /// <summary>Drops the remembered answers that list fewer than two rates (a driver that is running may report only its current rate); multi-rate answers stay, and so do the channel names. Called when the engine (re)starts or publishes a device, and when the driver or device changes: no other time, so asking cannot loop.</summary>
    internal void ForgetSingleRateAsio()
    {
        lock (_gate)
        {
            foreach (var driver in _asioKnown.Where(k => k.Value.Rates.Count < 2).Select(k => k.Key).ToList()) _asioKnown.Remove(driver);
            _asioRetryAt.Clear();
        }
    }

    /// <summary>The channel names of ASIO driver <paramref name="driver"/>: the open driver's when the running engine has that device open, else what the last question reported; null when not known.</summary>
    public (IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs)? AsioChannelNamesOf(string driver)
    {
        if (AsioChannels is { } open && string.Equals(open.Device, driver, StringComparison.OrdinalIgnoreCase)) return (open.Inputs, open.Outputs);
        lock (_gate) return _asioNames.TryGetValue(driver, out var names) ? names : null;
    }

    /// <summary>True when ASIO driver <paramref name="driver"/> was asked and answered unreadable (not installed or unreadable: not asked again this session), or its last question went unanswered less than <see cref="AsioRetryCooldown"/> ago.</summary>
    public bool AsioGivenUp(string driver)
    {
        lock (_gate) return _asioGivenUp.Contains(driver) || _asioRetryAt.TryGetValue(driver, out var until) && Stopwatch.GetTimestamp() < until;
    }

    /// <summary>
    /// What ASIO driver <paramref name="driver"/> offers (empty: the first installed), asked of the engine process, which answers from its open driver or opens it briefly
    /// (TabForge itself never opens ASIO: the driver takes one client). Starts the engine for the question when it is not running and ends it again when nothing else wanted it.
    /// Returns at once; the task completes with null when unanswered after <paramref name="timeoutMs"/> (asked again after the cooldown) or unreadable. Answers (and an unreadable one: <see cref="AsioGivenUp"/>) are remembered, and one question per driver is in flight.
    /// </summary>
    public Task<AsioCapabilities?> ProbeAsioAsync(string driver, int timeoutMs = 5000)
    {
        var result = new TaskCompletionSource<AsioCapabilities?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_asioKnown.TryGetValue(driver, out var known)) return Task.FromResult<AsioCapabilities?>(known);
            if (_asioGivenUp.Contains(driver) || _asioRetryAt.TryGetValue(driver, out var until) && Stopwatch.GetTimestamp() < until) return Task.FromResult<AsioCapabilities?>(null);
            if (_asioAsking.TryGetValue(driver, out var asking)) return asking;
            _asioAsking[driver] = result.Task;
        }
        _ = Answer(driver, timeoutMs, result);
        return result.Task;
    }

    private async Task Answer(string driver, int timeoutMs, TaskCompletionSource<AsioCapabilities?> result)
    {
        AsioCapabilities? answer = null;
        var unreadable = false;
        try { (answer, unreadable) = await AskAsio(driver, timeoutMs); }
        finally
        {
            lock (_gate)
            {
                _asioAsking.Remove(driver);
                if (answer is not null)
                {
                    _asioKnown[driver] = answer;
                    _asioRetryAt.Remove(driver);
                    if (answer.Inputs.Count + answer.Outputs.Count > 0) _asioNames[driver] = (answer.Inputs, answer.Outputs);
                }
                else if (unreadable) _asioGivenUp.Add(driver);
                else _asioRetryAt[driver] = Stopwatch.GetTimestamp() + (long)(AsioRetryCooldown.TotalSeconds * Stopwatch.Frequency);   // no answer in time or no engine: asked again after the cooldown
            }
            result.TrySetResult(answer);
        }
    }

    private async Task<(AsioCapabilities? Answer, bool Unreadable)> AskAsio(string driver, int timeoutMs)
    {
        var startedHere = !IsRunning;
        if (startedHere && !Start()) return (null, false);
        var id = Interlocked.Increment(ref _nextAsioRequest);
        var asked = new TaskCompletionSource<AsioCapabilities>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _asioAsked[id] = asked;
        Send(EngineCommand.ProbeAsio, new ProbeAsioMessage(id, driver).Write);
        var answered = await Task.WhenAny(asked.Task, Task.Delay(timeoutMs)) == asked.Task;
        lock (_gate) _asioAsked.Remove(id);
        StopIfStartedFor(startedHere);
        if (!answered) return (null, false);
        var answer = await asked.Task;
        return answer.Rates.Count == 0 && answer.PreferredBuffer <= 0 ? (null, true) : (answer, false);   // an explicit empty answer: unreadable, given up
    }

    /// <summary>
    /// Has the engine show the control panel of ASIO driver <paramref name="driver"/> (empty: the first installed): the engine's own open driver, or the driver opened briefly. Starts the engine
    /// for it when it is not running and ends it again afterwards. The task completes when the panel call returns, with why it could not be shown (empty: it was shown).
    /// </summary>
    public async Task<string> ShowAsioPanelAsync(string driver, int timeoutMs = PanelTimeoutMs)
    {
        var startedHere = !IsRunning;
        if (startedHere && !Start()) return "The audio engine could not be started.";
        var id = Interlocked.Increment(ref _nextAsioRequest);
        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _panelAsked[id] = (asked, Stopwatch.GetTimestamp());
        Send(EngineCommand.ShowAsioPanel, new ProbeAsioMessage(id, driver).Write);
        var error = await Task.WhenAny(asked.Task, Task.Delay(timeoutMs)) == asked.Task ? await asked.Task : "The driver's control panel did not report back in time.";
        lock (_gate) _panelAsked.Remove(id);
        StopIfStartedFor(startedHere);
        return error;
    }

    private void StopIfStartedFor(bool startedHere)
    {
        if (startedHere && _config is null && _slots.Count == 0) Stop();   // nobody else wanted the engine meanwhile
    }

    /// <summary>Reader thread: one <see cref="EngineEvent.AsioProbed"/> frame; an answer nobody waits for any more is dropped.</summary>
    internal void OnAsioProbed(BinaryReader r)
    {
        var message = AsioProbedMessage.Read(r);
        TaskCompletionSource<AsioCapabilities>? asked;
        lock (_gate) _asioAsked.TryGetValue(message.RequestId, out asked);
        var named = _engineProtocol is 0 or EngineNames.ProtocolVersion;   // an engine of another version: its names are not trusted
        asked?.TrySetResult(new AsioCapabilities(message.Rates, message.PreferredBuffer, named ? message.Inputs : Array.Empty<string>(), named ? message.Outputs : Array.Empty<string>()));
    }

    /// <summary>Reader thread: one <see cref="EngineEvent.AsioPanelShown"/> frame.</summary>
    internal void OnAsioPanelShown(BinaryReader r)
    {
        var message = AsioPanelShownMessage.Read(r);
        TaskCompletionSource<string>? asked = null;
        lock (_gate) { if (_panelAsked.Remove(message.RequestId, out var panel)) asked = panel.Asked; }
        asked?.TrySetResult(message.Error);
    }
}
