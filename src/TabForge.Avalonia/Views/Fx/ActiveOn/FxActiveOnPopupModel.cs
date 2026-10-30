using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Plugins;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the choices and the answer of the "Active on" popup without any control: the driver rows, the output device rows (connected ones, then
//   the ones the existing rule names that are not connected now), the picked values, the "Right now" line, and Clear / Cancel / Done.
// Does not own: the controls (FxActiveOnPopup), the device names (FxActiveOnDevices), the rule or its matcher (ActiveOnRules).
// Tests: FxActiveOnTests.
// Threads: UI thread; the device rows are read from cached lists (Refresh reads them again when a first listing settled).
internal sealed class FxActiveOnPopupModel
{
    /// <summary>A driver row: its words and the kind it means (null: any).</summary>
    internal readonly record struct DriverChoice(string Label, ActiveOnDriverKind? Kind);

    /// <summary>An output row: its words, the device name it stores (null: any) and whether it is connected now.</summary>
    internal readonly record struct DeviceChoice(string Label, string? Name, bool Connected);

    private readonly Func<ActiveOnDriverKind?, IReadOnlyList<FxActiveOnDevice>> _devices;
    private readonly Func<ActiveOnOutput> _output;
    private readonly Action<ActiveOnRule?> _applied;
    private readonly string? _initialDevice;
    private ActiveOnDriverKind? _driverKind;
    private string? _deviceName;

    /// <param name="initial">The existing rule (null: none); its choices are the starting values.</param>
    /// <param name="subject">The name of the plug-in or container (the subtitle).</param>
    /// <param name="devices">The output rows known for a driver kind.</param>
    /// <param name="output">The output the rule is matched against right now.</param>
    /// <param name="applied">Called once by Done / Clear with the normalised rule (null: no rule) - the one undo step.</param>
    public FxActiveOnPopupModel(ActiveOnRule? initial, string? subject, Func<ActiveOnDriverKind?, IReadOnlyList<FxActiveOnDevice>> devices,
        Func<ActiveOnOutput> output, Action<ActiveOnRule?> applied)
    {
        (_devices, _output, _applied, Subject) = (devices, output, applied, subject);
        _driverKind = initial?.DriverKind;
        _deviceName = string.IsNullOrWhiteSpace(initial?.DeviceName) ? null : initial!.DeviceName!.Trim();
        _initialDevice = _deviceName;
        DriverChoices = BuildDrivers();
        Refresh();
    }

    public string? Subject { get; }
    public string Subtitle => ActiveOnPresenter.SubtitleFor(Subject);
    public IReadOnlyList<DriverChoice> DriverChoices { get; }
    public IReadOnlyList<DeviceChoice> DeviceChoices { get; private set; } = Array.Empty<DeviceChoice>();
    public bool Finished { get; private set; }

    public ActiveOnDriverKind? DriverKind
    {
        get => _driverKind;
        set
        {
            if (_driverKind == value) return;
            _driverKind = value;
            Refresh();
        }
    }

    /// <summary>The picked output device name (null: any output device).</summary>
    public string? DeviceName
    {
        get => _deviceName;
        set => _deviceName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>The rule the pickers describe now (null: nothing restricted).</summary>
    public ActiveOnRule? Rule => ActiveOnRule.Normalize(new ActiveOnRule { DriverKind = _driverKind, DeviceName = _deviceName });

    /// <summary>"Right now: active (matches)" or "Right now: inactive on this device", from the current driver and device.</summary>
    public string Status => ActiveOnPresenter.Status(ActiveOnRules.Matches(Rule, _output()));

    public int DriverIndex => Math.Max(0, DriverChoices.ToList().FindIndex(c => c.Kind == _driverKind));

    public int DeviceIndex => Math.Max(0, DeviceChoices.ToList().FindIndex(c => string.Equals(c.Name, _deviceName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Reads the device rows again (after a driver pick, or when a first listing settled); the picked device stays picked.</summary>
    public void Refresh()
    {
        var rows = new List<DeviceChoice> { new(ActiveOnPresenter.DeviceAny, null, true) };
        var known = _devices(_driverKind);
        foreach (var device in known)
            rows.Add(Row(device.Name, device.Connected));
        // A device the rule (or the pick) names stays selectable when it is not connected now.
        foreach (var name in new[] { _deviceName, _initialDevice })
            if (!string.IsNullOrWhiteSpace(name) && rows.All(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                rows.Add(Row(name!, false));
        DeviceChoices = rows;
    }

    /// <summary>Done: applies the pickers as one change and finishes.</summary>
    public void Done()
    {
        if (Finished) return;
        Finished = true;
        _applied(Rule);
    }

    /// <summary>Clear rule: applies "no rule" as one change and finishes.</summary>
    public void Clear()
    {
        if (Finished) return;
        (_driverKind, _deviceName) = (null, null);
        Finished = true;
        _applied(null);
    }

    /// <summary>Cancel / Esc: changes nothing.</summary>
    public void Cancel() => Finished = true;

    private static DeviceChoice Row(string name, bool connected) =>
        new(connected ? name : $"{name} ({ActiveOnPresenter.NotConnected})", name, connected);

    private static List<DriverChoice> BuildDrivers()
    {
        var rows = new List<DriverChoice> { new(ActiveOnPresenter.DriverAny, null) };
        if (FxActiveOnDevices.HasKind(ActiveOnDriverKind.Asio)) rows.Add(new(ActiveOnPresenter.DriverAsio, ActiveOnDriverKind.Asio));
        if (FxActiveOnDevices.HasKind(ActiveOnDriverKind.Wasapi)) rows.Add(new(ActiveOnPresenter.DriverWasapi, ActiveOnDriverKind.Wasapi));
        return rows;
    }
}
