using NAudio.Wave;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Output;

// Owns: reading what an ASIO driver offers (the rates it accepts, its preferred buffer) for TabForge's settings, in the engine process that owns every ASIO driver.
// Does not own: opening the output device (AudioOutputFactory) or the IPC (EngineHost / EngineSession).
// Tests: AsioProbeTests (with a fake driver).

/// <summary>What one ASIO driver offers: the rates it accepts, its preferred buffer (0: unknown) and its input and output channel names (none: unknown).</summary>
internal sealed record AsioOffer(IReadOnlyList<int> Rates, int Buffer, IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

/// <summary>One driver's answer; TabForge's own process never opens ASIO (a driver takes one client at a time and the engine may hold it).</summary>
internal static class AsioProbe
{
    /// <summary>What <paramref name="driver"/> offers: from the open driver (<paramref name="readHeld"/>) when <paramref name="heldName"/> (the open driver name, read here so a failure answers too) is that driver (<paramref name="resolveName"/> turns the asked name into an installed one), else by opening it briefly (<paramref name="openBriefly"/>); answers are remembered per driver name in <paramref name="known"/> (the engine's cache). Never throws: a driver that cannot be read answers nothing.</summary>
    public static AsioProbedMessage Answer(int requestId, string driver, Func<string?> heldName, Func<string, string> resolveName, Func<AsioOffer> readHeld, Func<string, AsioOffer> openBriefly, Dictionary<string, AsioOffer> known, EngineLogWriter log)
    {
        try
        {
            var name = resolveName(driver);
            if (!known.TryGetValue(name, out var offer))
            {
                var held = heldName() is { } open && string.Equals(open, name, StringComparison.OrdinalIgnoreCase);
                offer = held ? readHeld() : openBriefly(name);
                if (held ? offer.Rates.Count >= 2 : offer.Rates.Count > 0) known[name] = offer;   // a running driver may report only its current rate: that is asked again next time
            }
            return new AsioProbedMessage(requestId, offer.Buffer, offer.Rates, offer.Inputs, offer.Outputs);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // a driver error (COM, ASIO) or no driver installed
        {
            log.Write($"ASIO probe of '{driver}' failed: {ex.Message}");
            return new AsioProbedMessage(requestId, 0, Array.Empty<int>(), Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>Shows the control panel of <paramref name="driver"/>: through the open driver (<paramref name="showHeld"/>) when <paramref name="heldName"/> is that driver, else by opening it for the panel (<paramref name="showBriefly"/>). Never throws: the answer says why a panel could not be shown.</summary>
    public static AsioPanelShownMessage ShowPanel(int requestId, string driver, string? heldName, Func<string, string> resolveName, Action showHeld, Action<string> showBriefly, EngineLogWriter log)
    {
        try
        {
            var name = resolveName(driver);
            if (heldName is not null && string.Equals(heldName, name, StringComparison.OrdinalIgnoreCase)) showHeld(); else showBriefly(name);
            return new AsioPanelShownMessage(requestId, "");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // a driver error (COM, ASIO) or no driver installed
        {
            log.Write($"ASIO control panel of '{driver}' failed: {ex.Message}");
            return new AsioPanelShownMessage(requestId, ex.Message.Length > 0 ? ex.Message : "The driver reported an error.");
        }
    }

    /// <summary>Opens driver <paramref name="name"/> only to show its control panel, and closes it when the panel call returns.</summary>
    public static void ShowBriefly(string name)
    {
        using var asio = new AsioOut(name);
        asio.ShowControlPanel();
    }

    /// <summary>Opens driver <paramref name="name"/> for this one question and closes it again.</summary>
    public static AsioOffer OpenBriefly(string name, EngineLogWriter log)
    {
        using var asio = new AsioOut(name);
        return Read(asio, log);
    }

    /// <summary>The accepted rates (of <see cref="AudioDriverNames.SampleRates"/>), the preferred buffer and the channel names of an open driver.</summary>
    public static AsioOffer Read(AsioOut asio, EngineLogWriter log)
    {
        // NAudio has no public access to the driver's capabilities; a NAudio change that renames the field is logged, not silently a buffer of 0.
        var field = typeof(AsioOut).GetField("driver", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field is null) log.Write("ASIO preferred buffer unknown: AsioOut driver field not found");
        var buffer = (field?.GetValue(asio) as NAudio.Wave.Asio.AsioDriverExt)?.Capabilities.BufferPreferredSize ?? 0;
        return new AsioOffer(AcceptedRates(asio.IsSampleRateSupported), buffer,
            Enumerable.Range(0, asio.DriverInputChannelCount).Select(i => AudioOutputFactory.Safe(() => asio.AsioInputChannelName(i), $"Input {i + 1}")).ToArray(),
            Enumerable.Range(0, asio.DriverOutputChannelCount).Select(i => AudioOutputFactory.Safe(() => asio.AsioOutputChannelName(i), $"Output {i + 1}")).ToArray());
    }

    /// <summary>The standard rates <paramref name="canSampleRate"/> (the open driver's CanSampleRate) accepts; a rate it throws for counts as not accepted, so one odd answer does not hide the others.</summary>
    public static IReadOnlyList<int> AcceptedRates(Func<int, bool> canSampleRate) =>
        AudioDriverNames.SampleRates.Where(rate =>
        {
            try { return canSampleRate(rate); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }   // a driver error (ASIO, COM) for this rate
        }).ToArray();
}
