using System.Globalization;

namespace TabForge.Services;

// Owns: the plain wording of the output sample rate ("44.1 kHz", "Device default") and the list a Preferences row offers.
// Does not own: asking a device which rates it supports (CatalogPlatform.SupportedSampleRates) or storing the choice (PluginSettings).
// Tests: OutputSettingsTests.
public static class OutputRates
{
    public const string DeviceDefault = "Device default";

    /// <summary>"44.1 kHz"; 0 or less is <see cref="DeviceDefault"/>.</summary>
    public static string Label(int hz) => hz <= 0 ? DeviceDefault : (hz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";

    /// <summary>The rate a label stands for (0 for <see cref="DeviceDefault"/> or anything unreadable).</summary>
    public static int Parse(string label) =>
        label.EndsWith(" kHz", StringComparison.Ordinal) && double.TryParse(label[..^4], NumberStyles.Float, CultureInfo.InvariantCulture, out var khz) ? (int)Math.Round(khz * 1000) : 0;

    /// <summary>The rates to offer: "Device default", the supported ones (the usual list when the device could not be asked) and the stored one, lowest first.</summary>
    public static IReadOnlyList<string> Choices(IReadOnlyList<int> supported, int stored) =>
        new[] { DeviceDefault }.Concat((supported.Count > 0 ? supported : AudioDrivers.SampleRates).Append(stored).Where(r => r > 0).Distinct().Order().Select(Label)).ToArray();

    /// <summary>The rates in <see cref="AudioDrivers.SampleRates"/> an output of <paramref name="driver"/> supports. Shared WASAPI runs at the Windows mix rate only, DirectSound has Windows convert (the usual rates), ASIO and exclusive WASAPI accept what <paramref name="accepts"/> says.</summary>
    public static IReadOnlyList<int> Supported(string driver, Func<int, bool> accepts, Func<int> mixRate) => driver switch
    {
        AudioDrivers.WasapiShared => new[] { mixRate() },
        AudioDrivers.DirectSound => AudioDrivers.SampleRates.Where(r => r <= 96000).ToArray(),
        _ => AudioDrivers.SampleRates.Where(accepts).ToArray(),
    };
}
