using TabForge.Platform.Contracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Avalonia.Views;
using TabForge.Playback;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

public sealed class AudioDeviceNamesTests
{
    private sealed class FakeDevices : IAudioDevices
    {
        public readonly ManualResetEventSlim Gate = new(false);
        public int Calls;
        public int CallerThread;
        public IReadOnlyList<string> CaptureEndpointNames() => new[] { "Mic" };
        public IReadOnlyList<string> OutputNames(string driver)
        {
            Interlocked.Increment(ref Calls);
            CallerThread = Environment.CurrentManagedThreadId;
            Gate.Wait();
            return driver == AudioDrivers.Asio ? new[] { "Driver A", "Driver B" } : new[] { "Speakers", "Headphones" };
        }
        public IReadOnlyList<string> AsioDriverNames() => new[] { "Driver A", "Driver B" };
        public IReadOnlyList<int> SupportedSampleRates(string driver, string device) => new[] { 48000 };
    }

    private sealed class ThrowingDevices : IAudioDevices
    {
        public IReadOnlyList<string> CaptureEndpointNames() => throw new ArgumentException("odd");
        public IReadOnlyList<string> OutputNames(string driver) => throw new ArgumentException("odd");
        public IReadOnlyList<string> AsioDriverNames() => throw new ArgumentException("odd");
        public IReadOnlyList<int> SupportedSampleRates(string driver, string device) => throw new ArgumentException("odd");
    }

    [Fact]
    public async Task AnyListingErrorEndsInAnEmptyListNotCheckingForever()
    {
        var names = new AudioDeviceNames(new ThrowingDevices());
        Assert.Null(names.Names(AudioDrivers.Asio));
        await names.Settled(AudioDrivers.Asio);
        Assert.Empty(names.Names(AudioDrivers.Asio)!);
    }

    [Fact]
    public async Task ListsOnAWorkerShowPendingUntilAnsweredThenKeepEveryDevicePerDriver()
    {
        var fake = new FakeDevices();
        var names = new AudioDeviceNames(fake);
        Assert.Null(names.Names(AudioDrivers.Asio));   // returns at once while the listing is blocked: the caller never waited
        Assert.Null(names.Names(AudioDrivers.Asio));
        fake.Gate.Set();
        await names.Settled(AudioDrivers.Asio);
        Assert.NotEqual(Environment.CurrentManagedThreadId, fake.CallerThread);
        Assert.Equal(new[] { "Driver A", "Driver B" }, names.Names(AudioDrivers.Asio));
        Assert.Null(names.Names(AudioDrivers.WasapiShared));
        await names.Settled(AudioDrivers.WasapiShared);
        Assert.Equal(new[] { "Speakers", "Headphones" }, names.Names(AudioDrivers.WasapiShared));   // switching driver lists that driver's devices
        names.Names(AudioDeviceNames.Capture);
        await names.Settled(AudioDeviceNames.Capture);
        Assert.Equal(new[] { "Mic" }, names.Names(AudioDeviceNames.Capture));
        Assert.Equal(2, fake.Calls);
    }
}
