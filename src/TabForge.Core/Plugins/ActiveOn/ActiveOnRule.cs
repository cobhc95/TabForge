using System.Text.Json.Serialization;

namespace TabForge.Plugins;

// Owns: the "Active on" rule of one effect or container (driver kind, output device name), the output snapshot it is matched
//   against, and the matcher that ANDs a container's rule with the rules of everything inside it.
// Does not own: where the current output comes from (AudioEngineClient, the settings), the bypass switch the engine already has
//   (AudioEngineClient.SyncBypass), the popup and the rows (Views/Fx/ActiveOn).
// Tests: FxActiveOnTests.

/// <summary>The output a rule can name: a driver kind, a device by name, or neither (every output).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ActiveOnDriverKind>))]
public enum ActiveOnDriverKind
{
    Asio,
    Wasapi,
}

/// <summary>
/// When an effect or container is active: a driver kind, an output device by name, or both (both must match). Null fields mean
/// "any", so a fresh rule is always active. Property names are the file format.
/// </summary>
public sealed class ActiveOnRule
{
    /// <summary>The longest device name kept (a rule loaded from a file is cut here); names are matched case-insensitively.</summary>
    public const int MaxDeviceNameChars = 256;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ActiveOnDriverKind? DriverKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceName { get; set; }

    /// <summary>True when neither field restricts anything: the rule means "every output".</summary>
    [JsonIgnore]
    public bool IsAny => DriverKind is null && string.IsNullOrWhiteSpace(DeviceName);

    /// <summary>A copy of the rule, or null when it restricts nothing (the file and the model then carry no field).</summary>
    public static ActiveOnRule? Normalize(ActiveOnRule? rule)
    {
        if (rule is null) return null;
        var name = rule.DeviceName?.Trim();
        if (name is { Length: > MaxDeviceNameChars }) name = name[..MaxDeviceNameChars];
        var copy = new ActiveOnRule { DriverKind = rule.DriverKind, DeviceName = string.IsNullOrEmpty(name) ? null : name };
        return copy.IsAny ? null : copy;
    }

    /// <summary>Deep copy without a JSON roundtrip.</summary>
    public ActiveOnRule Clone() => new() { DriverKind = DriverKind, DeviceName = DeviceName };
}

/// <summary>The output a rule is matched against: driver name as the app knows it ("ASIO", "WASAPI (shared)" ...) and device name (null/empty: unknown).</summary>
public readonly record struct ActiveOnOutput(string? Driver, string? Device)
{
    /// <summary>True when the app knows at least the driver; false before the first sync or settings load.</summary>
    public bool IsKnown => !string.IsNullOrWhiteSpace(Driver) || !string.IsNullOrWhiteSpace(Device);

    /// <summary>The opened device from the engine's Ready description ("WASAPI (shared): Speakers"): the part after the first ": ".</summary>
    public static string? DeviceFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var separator = description.IndexOf(": ", StringComparison.Ordinal);
        return (separator >= 0 ? description[(separator + 2)..] : description).Trim();
    }
}

/// <summary>Pure matching of <see cref="ActiveOnRule"/> against an <see cref="ActiveOnOutput"/>.</summary>
public static class ActiveOnRules
{
    /// <summary>The driver kinds the app really has: both WASAPI modes are one kind, ASIO is the other.</summary>
    public static ActiveOnDriverKind? DriverKindOf(string? driver) => driver switch
    {
        TabForge.Audio.Contracts.AudioDriverNames.Asio => ActiveOnDriverKind.Asio,
        TabForge.Audio.Contracts.AudioDriverNames.WasapiShared or TabForge.Audio.Contracts.AudioDriverNames.WasapiExclusive => ActiveOnDriverKind.Wasapi,
        _ => null,
    };

    /// <summary>True when the rule allows the output; null and an empty rule allow everything.</summary>
    public static bool Matches(ActiveOnRule? rule, ActiveOnOutput output)
    {
        if (rule is null || rule.IsAny) return true;
        if (rule.DriverKind is { } kind && DriverKindOf(output.Driver) != kind) return false;
        return string.IsNullOrWhiteSpace(rule.DeviceName) || SameName(rule.DeviceName, output.Device);
    }

    /// <summary>True when the rule does not allow the output (the effect is auto-bypassed and shows the hint). Device names are compared without case or surrounding spaces.</summary>
    public static bool Inactive(ActiveOnRule? rule, ActiveOnOutput output) => !Matches(rule, output);

    /// <summary>
    /// The rule of plug-in <paramref name="index"/> in effect: its own rule AND the rule of every container that covers it
    /// (so a container's rule applies to everything inside, and a child may be stricter).
    /// </summary>
    public static bool Matches(RigPreset rig, int index, ActiveOnOutput output)
    {
        if (index < 0 || index >= rig.Plugins.Count) return true;
        if (rig.Containers is { } containers)
            foreach (var container in containers)
                if (index >= container.Start && index < container.Start + container.Count && !Matches(container.ActiveOn, output))
                    return false;
        return Matches(rig.Plugins[index].ActiveOn, output);
    }

    /// <summary>True when plug-in <paramref name="index"/> is inactive on this output (its own rule or a container's rule).</summary>
    public static bool Inactive(RigPreset rig, int index, ActiveOnOutput output) => !Matches(rig, index, output);

    /// <summary>
    /// The engine's enabled flag per plug-in: the chain switch AND the plug-in's own switch (and container bypass)
    /// AND its Active on rules. This is the one bypass decision the engine receives.
    /// </summary>
    public static List<bool> Enabled(RigPreset rig, bool chainOn, ActiveOnOutput output)
    {
        var result = new List<bool>(rig.Plugins.Count);
        for (var i = 0; i < rig.Plugins.Count; i++)
            result.Add(chainOn && rig.Plugins[i].Plays && Matches(rig, i, output));
        return result;
    }

    /// <summary>True when a rule names the output device, so the popup keeps the name of an unplugged device (shown greyed) instead of forgetting it.</summary>
    public static bool NamesDevice(ActiveOnRule? rule) => rule is not null && !string.IsNullOrWhiteSpace(rule.DeviceName);

    private static bool SameName(string expected, string? actual) =>
        !string.IsNullOrWhiteSpace(actual) && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
}
