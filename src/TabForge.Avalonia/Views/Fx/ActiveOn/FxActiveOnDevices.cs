using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Audio.Contracts;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the output device rows of the "Active on" popup: the names the app knows now (cached, listed on a worker by AudioDeviceNames)
//   plus remembered names from the output profiles and from the chain's own rules (an unplugged device stays selectable, greyed).
// Does not own: the popup (FxActiveOnPopup), the rules (ActiveOnRules) or the listing (AudioDeviceNames).
// Tests: FxActiveOnTests.
// Threads: the names are read from the cached lists on the UI thread; the first listing completes on a worker and Settled reports it.

/// <summary>One output device row: its name as the app stores it, and whether it is connected now (false: greyed, still selectable).</summary>
internal readonly record struct FxActiveOnDevice(string Name, bool Connected);

internal static class FxActiveOnDevices
{
    /// <summary>The rows for a driver kind: known output names first (alphabetical), then remembered/unplugged ones, one per name.</summary>
    public static IReadOnlyList<FxActiveOnDevice> Choices(ActiveOnDriverKind? kind, PluginSettings settings, IEnumerable<string> ruleNames)
    {
        var known = KnownNames(kind);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (!names.ContainsKey(name)) names[name] = name;
        }
        foreach (var name in known ?? Array.Empty<string>()) Add(name);
        foreach (var (driver, device) in Profiles(settings))
            if (Allows(kind, driver)) Add(device);
        if (Allows(kind, settings.Driver)) Add(settings.Device);
        foreach (var name in ruleNames) Add(name);
        return names.Values
            .Select(name => new FxActiveOnDevice(name, known is null || known.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Completes when the listing for the kind's drivers finished (immediately when the app has no cached lists).</summary>
    public static Task Settled(ActiveOnDriverKind? kind)
    {
        if (CurrentApp is not { } app) return Task.CompletedTask;
        return Task.WhenAll(Drivers(kind).Select(app.DeviceNames.Settled));
    }

    /// <summary>True when this OS's drivers include the kind (ASIO and WASAPI only exist on Windows; the portable build has miniaudio).</summary>
    public static bool HasKind(ActiveOnDriverKind kind) => AudioDrivers.All.Any(driver => ActiveOnRules.DriverKindOf(driver) == kind);

    /// <summary>The driver names of a kind (both WASAPI modes are one kind; null lists both kinds).</summary>
    public static IEnumerable<string> Drivers(ActiveOnDriverKind? kind) => kind switch
    {
        ActiveOnDriverKind.Asio => HasKind(ActiveOnDriverKind.Asio) ? new[] { AudioDriverNames.Asio } : Array.Empty<string>(),
        ActiveOnDriverKind.Wasapi => HasKind(ActiveOnDriverKind.Wasapi) ? new[] { AudioDriverNames.WasapiShared, AudioDriverNames.WasapiExclusive } : Array.Empty<string>(),
        _ => new[] { AudioDriverNames.WasapiShared, AudioDriverNames.WasapiExclusive, AudioDriverNames.Asio }.Where(driver => AudioDrivers.All.Contains(driver)).ToArray(),
    };

    private static TabForge.Avalonia.App? CurrentApp => global::Avalonia.Application.Current as TabForge.Avalonia.App;

    // Null until the first listing of any of the kind's drivers finished: then every remembered row is judged against what is known.
    private static IReadOnlyList<string>? KnownNames(ActiveOnDriverKind? kind)
    {
        if (CurrentApp is not { } app) return null;
        var names = new List<string>(); var listed = false;
        foreach (var driver in Drivers(kind)) if (app.DeviceNames.Names(driver) is { } list) { listed = true; names.AddRange(list); }
        return listed ? names : null;
    }

    private static IEnumerable<(string Driver, string Device)> Profiles(PluginSettings settings)
    {
        foreach (var key in settings.OutputProfiles.Keys)
        {
            var separator = key.IndexOf('|');
            if (separator < 0) continue;
            yield return (key[..separator], key[(separator + 1)..]);
        }
    }

    private static bool Allows(ActiveOnDriverKind? kind, string? driver) =>
        kind is null || ActiveOnRules.DriverKindOf(driver) == kind;
}
