using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Output;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the ASIO question going through the engine process (never opened by TabForge itself): the engine's answer choice (open driver, brief open, failure),
// the wire pair and the client waiting for the reply.
// Does not own: the real driver (needs hardware) or the settings rows that show the answer (OutputSettingsTests).
// Tests: this file.
public class AsioProbeTests
{
    private static readonly AsioOffer Held = new(new[] { 48000 }, 256, new[] { "Mic 1" }, new[] { "Main L", "Main R" });
    private static readonly AsioOffer Fresh = new(new[] { 44100, 96000 }, 128, Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void TheDriverTheEngineHoldsIsReadWithoutOpeningItAgain()
    {
        var answer = AsioProbe.Answer(7, "", () => "Focusrite", _ => "Focusrite", () => Held, _ => throw new InvalidOperationException("opened a held driver"), new(StringComparer.OrdinalIgnoreCase), new EngineLogWriter());
        Assert.Equal((7, 256), (answer.RequestId, answer.PreferredBuffer));
        Assert.Equal(new[] { 48000 }, answer.Rates);
    }

    [Fact]
    public void AnotherDriverIsOpenedBrieflyAndAFailureAnswersNothing()
    {
        var other = AsioProbe.Answer(1, "Other", () => "Focusrite", n => n, () => throw new InvalidOperationException("read the wrong driver"), name => { Assert.Equal("Other", name); return Fresh; }, new(), new EngineLogWriter());
        Assert.Equal(new[] { 44100, 96000 }, other.Rates);
        var none = AsioProbe.Answer(2, "Gone", () => null, _ => throw new InvalidOperationException("No ASIO driver is installed."), () => Held, _ => Fresh, new(), new EngineLogWriter());
        Assert.Equal((2, 0, 0, 0, 0), (none.RequestId, none.PreferredBuffer, none.Rates.Count, none.Inputs.Count, none.Outputs.Count));
    }

    [Fact]
    public void AHeldDriverIsAskedForEveryStandardRateAndAThrowingRateOnlyDropsItself()
    {
        var asked = new List<int>();
        var rates = AsioProbe.AcceptedRates(rate => { asked.Add(rate); return rate == 192000 ? throw new InvalidOperationException("driver error") : rate != 176400; });
        Assert.Equal(AudioDriverNames.SampleRates, asked);
        Assert.Equal(new[] { 44100, 48000, 88200, 96000 }, rates);
    }

    [Fact]
    public void ADriversOfferIsAskedOnceAndRememberedPerName()
    {
        var known = new Dictionary<string, AsioOffer>(StringComparer.OrdinalIgnoreCase);
        var reads = 0;
        AsioOffer Many() { reads++; return new(AudioDriverNames.SampleRates, 128, Array.Empty<string>(), Array.Empty<string>()); }
        var first = AsioProbe.Answer(1, "", () => "Focusrite", _ => "Focusrite", Many, _ => throw new InvalidOperationException("opened"), known, new EngineLogWriter());
        var again = AsioProbe.Answer(2, "focusrite", () => "Focusrite", n => n, Many, _ => throw new InvalidOperationException("opened"), known, new EngineLogWriter());
        Assert.Equal(AudioDriverNames.SampleRates, first.Rates);
        Assert.Equal(AudioDriverNames.SampleRates, again.Rates);
        Assert.Equal(1, reads);
        var silent = AsioProbe.Answer(3, "Gone", () => null, n => n, () => Held, _ => new(Array.Empty<int>(), 0, Array.Empty<string>(), Array.Empty<string>()), known, new EngineLogWriter());
        Assert.False(known.ContainsKey("Gone"));   // nothing usable is not remembered
        Assert.Empty(silent.Rates);
    }

    [Fact]
    public void AOneRateAnswerFromAHeldDriverIsAskedAgainButABrieflyOpenedOneIsRemembered()
    {
        var known = new Dictionary<string, AsioOffer>(StringComparer.OrdinalIgnoreCase);
        AsioProbe.Answer(1, "Focusrite", () => "Focusrite", n => n, () => Held, _ => throw new InvalidOperationException("opened"), known, new EngineLogWriter());
        Assert.Empty(known);
        AsioProbe.Answer(2, "Other", () => "Focusrite", n => n, () => Held, _ => Held, known, new EngineLogWriter());
        Assert.True(known.ContainsKey("Other"));
    }

    [Fact]
    public async Task TheDriversChannelNamesComeBackWithoutALimitOfEightAndUnknownOnesAreNumbered()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var names = Enumerable.Range(1, 18).Select(i => $"Analogue {i}").ToArray();
        var asked = client.ProbeAsioAsync("Focusrite", 10000);
        Answer(client, 1, 64, new[] { 48000 }, names, new[] { "Main L", "Main R" });
        var caps = (await asked)!;
        Assert.Equal(18, caps.Inputs.Count);
        var picker = SettingsCatalog.Numbered(caps.Inputs, "Input");
        Assert.Equal(18, picker.Count);
        Assert.Equal(("1: Analogue 1", "18: Analogue 18"), (picker[0], picker[17]));
        Assert.Equal(new[] { "1: Main L", "2: Main R" }, SettingsCatalog.Numbered(caps.Outputs, "Output"));
        Assert.Equal(new[] { "1: Input 1", "2: Input 2", "3: Input 3", "4: Input 4", "5: Input 5", "6: Input 6", "7: Input 7", "8: Input 8" }, SettingsCatalog.Numbered(Array.Empty<string>(), "Input"));
        Assert.Equal("1: Output 1", SettingsCatalog.Numbered(null, "Output")[0]);
    }

    [Fact]
    public void TheEnginesOwnProbeReadsTheNamesOfTheHeldDriver()
    {
        var answer = AsioProbe.Answer(7, "", () => "Focusrite", _ => "Focusrite", () => Held, _ => throw new InvalidOperationException("opened a held driver"), new(StringComparer.OrdinalIgnoreCase), new EngineLogWriter());
        Assert.Equal(new[] { "Mic 1" }, answer.Inputs);
        Assert.Equal(new[] { "Main L", "Main R" }, answer.Outputs);
    }

    [Fact]
    public void TheAnswerSurvivesTheWire()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) new AsioProbedMessage(5, 192, new[] { 44100, 48000 }, new[] { "In 1", "In 2" }, new[] { "Out 1" }).Write(w);
        stream.Position = 0;
        var back = AsioProbedMessage.Read(new BinaryReader(stream));
        Assert.Equal((5, 192), (back.RequestId, back.PreferredBuffer));
        Assert.Equal(new[] { 44100, 48000 }, back.Rates);
        Assert.Equal(new[] { "In 1", "In 2" }, back.Inputs);
        Assert.Equal(new[] { "Out 1" }, back.Outputs);
    }

    private static void Answer(AudioEngineClient client, int id, int buffer, params int[] rates) => Answer(client, id, buffer, rates, Array.Empty<string>(), Array.Empty<string>());

    private static void Answer(AudioEngineClient client, int id, int buffer, int[] rates, string[] inputs, string[] outputs)
    {
        using var frame = new MemoryStream();
        using (var w = new BinaryWriter(frame, System.Text.Encoding.UTF8, leaveOpen: true)) new AsioProbedMessage(id, buffer, rates, inputs, outputs).Write(w);
        frame.Position = 0;
        client.OnAsioProbed(new BinaryReader(frame));
    }

    [Fact]
    public async Task TheClientAsksTheEngineWithoutBlockingAndRemembersTheAnswer()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = 0;
        client.SentForTest = command => { if (command == EngineCommand.ProbeAsio) asked++; };
        var first = client.ProbeAsioAsync("Focusrite", 10000);
        Assert.False(first.IsCompleted);   // returned at once: nothing waited for the engine
        Assert.Null(client.KnownAsio("Focusrite"));
        Assert.Same(first, client.ProbeAsioAsync("Focusrite", 10000));   // one question in flight
        Answer(client, 1, 64, 48000);
        var caps = await first;
        Assert.Equal(64, caps!.PreferredBuffer);
        Assert.Equal(new[] { 48000 }, caps.Rates);
        Assert.Same(caps, client.KnownAsio("Focusrite"));
        Assert.Same(caps, await client.ProbeAsioAsync("Focusrite", 10));   // remembered: no second question
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task AOneRateAnswerIsAskedAgainAfterAnEngineRestartButAMultiRateOneStays()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = 0;
        client.SentForTest = command => { if (command == EngineCommand.ProbeAsio) asked++; };
        var one = client.ProbeAsioAsync("Held", 10000);
        Answer(client, 1, 64, 48000);
        await one;
        var many = client.ProbeAsioAsync("Idle", 10000);
        Answer(client, 2, 64, 44100, 48000, 96000);
        await many;
        client.ForgetSingleRateAsio();   // what the engine's (re)start, a device change and a driver or device choice do
        Assert.Null(client.KnownAsio("Held"));
        Assert.NotNull(client.KnownAsio("Idle"));
        var again = client.ProbeAsioAsync("Held", 10000);
        Assert.False(again.IsCompleted);
        Assert.Equal(3, asked);
        Answer(client, 3, 64, 48000);
        await again;
    }

    [Fact]
    public async Task AnUnansweredQuestionIsAskedAgainNextTime()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = 0;
        client.SentForTest = command => { if (command == EngineCommand.ProbeAsio) asked++; };
        client.AsioRetryCooldown = TimeSpan.FromMilliseconds(150);
        Assert.Null(await client.ProbeAsioAsync("Silent", 30));   // a timeout says nothing about the driver
        Assert.True(client.AsioGivenUp("Silent"));   // but it is left alone for the cooldown: the settings page does not ask in a loop
        Assert.Null(await client.ProbeAsioAsync("Silent", 30));
        Assert.Equal(1, asked);
        await Task.Delay(300);
        Assert.False(client.AsioGivenUp("Silent"));
        Assert.Null(await client.ProbeAsioAsync("Silent", 30));
        Assert.Equal(2, asked);
    }

    private static void Ready(AudioEngineClient client, int? version)
    {
        var ready = new MemoryStream();
        using (var w = new BinaryWriter(ready, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(48000); w.Write(5); w.Write("desc"); w.Write(64);
            AsioChannelNames.Write(w, Enumerable.Range(1, 200).Select(i => $"In {i}").ToArray());   // more than the cap: the writer keeps to it
            AsioChannelNames.Write(w, new[] { "Out 1" });
            w.Write(0);   // recordable
            if (version is { } v) w.Write(v);
        }
        ready.Position = 0;
        client.HandleEvent((byte)EngineEvent.Ready, new BinaryReader(ready));
        Assert.Equal(ready.Length, ready.Position);   // the whole Ready was read, however many names it listed
    }

    [Fact]
    public async Task NamesFromAnEngineOfAnotherProtocolVersionAreIgnored()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        Ready(client, null);   // no version tail: an engine from before the protocol version
        var old = client.ProbeAsioAsync("Focusrite", 10000);
        Answer(client, 1, 64, new[] { 48000 }, new[] { "A", "B" }, new[] { "C" });
        Assert.Empty((await old)!.Inputs);
        Assert.Null(client.AsioChannelNamesOf("Focusrite"));
        Ready(client, EngineNames.ProtocolVersion);
        var current = client.ProbeAsioAsync("Audient", 10000);
        Answer(client, 2, 64, new[] { 48000 }, new[] { "A", "B" }, new[] { "C" });
        Assert.Equal(2, (await current)!.Inputs.Count);
    }

    [Fact]
    public void TheChannelNameListsAgreeOnTheirLengthEvenPastTheCap()
    {
        using var stream = new MemoryStream();
        var inputs = Enumerable.Range(1, 200).Select(i => $"In {i}").ToArray();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) new AsioProbedMessage(1, 64, new[] { 48000 }, inputs, new[] { "Main L", new string('x', 500) }).Write(w);
        stream.Position = 0;
        var back = AsioProbedMessage.Read(new BinaryReader(stream));
        Assert.Equal(AsioChannelNames.Max, back.Inputs.Count);
        Assert.Equal("In 128", back.Inputs[^1]);
        Assert.Equal(2, back.Outputs.Count);
        Assert.Equal("Main L", back.Outputs[0]);
        Assert.Equal(AsioChannelNames.MaxNameChars, back.Outputs[1].Length);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void AnAnswerFromAnEngineBeforeTheNamesReadsAsNoNames()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) { w.Write(3); w.Write(64); w.Write(1); w.Write(48000); }
        stream.Position = 0;
        var back = AsioProbedMessage.Read(new BinaryReader(stream));
        Assert.Equal(new[] { 48000 }, back.Rates);
        Assert.Empty(back.Inputs);
    }

    [Fact]
    public void AFailureReadingTheOpenDriversNameStillAnswers()
    {
        var answer = AsioProbe.Answer(9, "Gone", () => throw new InvalidOperationException("No ASIO driver is installed."), n => n, () => Held, _ => Fresh, new(), new EngineLogWriter());
        Assert.Equal((9, 0), (answer.RequestId, answer.Rates.Count));
    }

    [Fact]
    public async Task TheNamesStayWhenAOneRateAnswerIsForgottenAndDifferOnlyForTheirOwnDevice()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = client.ProbeAsioAsync("Focusrite", 10000);
        Answer(client, 1, 64, new[] { 48000 }, new[] { "Mic" }, new[] { "Main" });
        await asked;
        client.ForgetSingleRateAsio();
        Assert.Null(client.KnownAsio("Focusrite"));
        Assert.Equal(new[] { "Mic" }, client.AsioChannelNamesOf("Focusrite")!.Value.Inputs);
        Assert.Null(client.AsioChannelNamesOf("Audient"));
    }

    [Fact]
    public async Task AnExplicitlyUnreadableAnswerIsGivenUpAndNotAskedAgain()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = 0;
        client.SentForTest = command => { if (command == EngineCommand.ProbeAsio) asked++; };
        var first = client.ProbeAsioAsync("Broken", 10000);
        Answer(client, 1, 0);
        Assert.Null(await first);
        Assert.True(client.AsioGivenUp("Broken"));
        Assert.Null(await client.ProbeAsioAsync("Broken", 30));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task TheControlPanelIsShownByTheEngineAndItsErrorIsReported()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var asked = 0;
        client.SentForTest = command => { if (command == EngineCommand.ShowAsioPanel) asked++; };
        var shown = client.ShowAsioPanelAsync("Focusrite");
        Assert.False(shown.IsCompleted);
        using var frame = new MemoryStream();
        using (var w = new BinaryWriter(frame, System.Text.Encoding.UTF8, leaveOpen: true)) new AsioPanelShownMessage(1, "Driver busy").Write(w);
        frame.Position = 0;
        client.OnAsioPanelShown(new BinaryReader(frame));
        Assert.Equal("Driver busy", await shown);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task ASlowControlPanelKeepsTheEngineAliveThenTimesOut()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var shown = client.ShowAsioPanelAsync("Focusrite", 200);   // a fake panel nobody closes
        Assert.True(client.PanelPending);
        Assert.False(string.IsNullOrEmpty(await shown.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.False(client.PanelPending);
    }

    [Fact]
    public void AControlPanelHoldsTheEngineOnlyUpToTheSharedCapOnAFakeClock()
    {
        long Sec(double seconds) => (long)(seconds * System.Diagnostics.Stopwatch.Frequency);
        var opened = Sec(5000);
        Assert.False(EngineWatchdog.PanelHolds(0, opened));   // no panel open
        Assert.True(EngineWatchdog.PanelHolds(opened, opened + Sec(599)));
        Assert.False(EngineWatchdog.PanelHolds(opened, opened + Sec(EngineWatchdog.PanelCapSec)));
        Assert.Equal(600, EngineWatchdog.PanelCapSec);
    }

    [Fact]
    public async Task TheClientKeepsItsLivenessForAPanelOnlyUpToTheCap()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var before = System.Diagnostics.Stopwatch.GetTimestamp();
        var shown = client.ShowAsioPanelAsync("Focusrite", 60_000);
        var perSecond = System.Diagnostics.Stopwatch.Frequency;
        Assert.True(client.PanelHeldAt(before + 599 * perSecond));
        Assert.False(client.PanelHeldAt(before + 601 * perSecond));   // past the cap the engine is judged as usual
        using var frame = new MemoryStream();
        using (var w = new BinaryWriter(frame, System.Text.Encoding.UTF8, leaveOpen: true)) new AsioPanelShownMessage(1, "Closed").Write(w);
        frame.Position = 0;
        client.OnAsioPanelShown(new BinaryReader(frame));
        Assert.Equal("Closed", await shown);
        Assert.False(client.PanelPending);
    }

    [Fact]
    public async Task APendingControlPanelCompletesWhenTheEngineStopsBeforeAnswering()
    {
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var shown = client.ShowAsioPanelAsync("Focusrite");
        Assert.False(shown.IsCompleted);
        client.Stop();   // the engine ends (exit, crash or restart) with the panel call still unanswered
        var error = await shown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AChoiceListShowsCheckingUntilTheAnswerIsKnownThenKeepsIt()
    {
        IReadOnlyList<string>? answer = null;
        var choices = new TabForge.Services.PendingChoices(() => answer, () => Task.CompletedTask);
        Assert.True(choices.IsPending);
        Assert.Equal(new[] { "Checking…" }, choices);
        answer = new[] { "Device default", "48 kHz" };
        Assert.False(choices.IsPending);
        answer = null;
        Assert.Equal(new[] { "Device default", "48 kHz" }, choices);
    }
}
