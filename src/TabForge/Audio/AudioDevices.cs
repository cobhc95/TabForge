using NAudio.CoreAudioApi;
using NAudio.Wave;
using TabForge.Audio.Contracts;

namespace TabForge.Audio;

/// <summary>
/// Output device names for Settings > Audio &amp; VST (names only: no driver is opened or loaded). The audio engine
/// process opens the device itself; this only lists what Windows (or the ASIO registry) reports.
/// </summary>
public static class AudioDevices
{
    /// <summary>Recording devices (Windows capture endpoints); ASIO inputs come from the ASIO driver instead.</summary>
    public static IReadOnlyList<string> InputNames()
    {
        try { return new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => d.FriendlyName).ToList(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// The driver's channel names for Settings: what the running engine reported, else asked from the driver (only while the
    /// engine is not running, since an ASIO driver serves one program), else plain numbers.
    /// </summary>
    public static (string[] Inputs, string[] Outputs) AsioChannelNames(string driverName)
    {
        var engine = AudioEngineClient.Instance;
        if (engine.AsioChannels is { } known && known.Inputs.Length + known.Outputs.Length > 0) return known;
        if (_channelCache.TryGetValue(driverName, out var cached)) return cached;
        if (!engine.IsRunning)
        {
            try
            {
                var names = AsioOut.GetDriverNames();
                var name = names.FirstOrDefault(n => string.Equals(n, driverName, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
                if (name is not null)
                {
                    using var asio = new AsioOut(name);
                    var found = ReadChannels(asio);
                    _channelCache[driverName] = found;
                    return found;
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NAudio.MmException or DllNotFoundException) { }
        }
        return (Enumerable.Range(1, 8).Select(i => $"Input {i}").ToArray(), Enumerable.Range(1, 8).Select(i => $"Output {i}").ToArray());
    }

    private static readonly Dictionary<string, (string[] Inputs, string[] Outputs)> _channelCache = new(StringComparer.OrdinalIgnoreCase);

    private static (string[] Inputs, string[] Outputs) ReadChannels(AsioOut asio)
    {
        static string Name(Func<string> f, string fallback)
        {
            try { return f(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return fallback; }
        }
        return (Enumerable.Range(0, asio.DriverInputChannelCount).Select(i => Name(() => asio.AsioInputChannelName(i), $"Input {i + 1}")).ToArray(),
                Enumerable.Range(0, asio.DriverOutputChannelCount).Select(i => Name(() => asio.AsioOutputChannelName(i), $"Output {i + 1}")).ToArray());
    }

    /// <summary>Opens the ASIO driver's own control panel (buffer size, sample rate, routing). False with a reason when it cannot.</summary>
    public static bool ShowAsioControlPanel(string driverName, out string message)
    {
        message = "";
        try
        {
            var names = AsioOut.GetDriverNames();
            var name = names.FirstOrDefault(n => string.Equals(n, driverName, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
            if (name is null) { message = "No ASIO driver is installed."; return false; }
            using var asio = new AsioOut(name);
            asio.ShowControlPanel();
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or NAudio.MmException or DllNotFoundException)
        {
            message = "The driver did not open its control panel (it may be in use by the audio engine): " + ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<string> Names(string driver)
    {
        try
        {
            return driver switch
            {
                AudioDriverNames.Asio => AsioOut.GetDriverNames(),
                AudioDriverNames.DirectSound => DirectSoundOut.Devices.Select(d => d.Description).ToList(),
                _ => new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).ToList(),
            };
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }
}
