using TabForge.Platform.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using TabForge.Playback;

namespace TabForge.Avalonia.Views;

// Owns: the audio device name lists the Settings pickers show (the driver's output devices, the recording devices): listed on a worker thread, never on the UI thread,
//   remembered per key and listed again in the background once they are older than RefreshSec (the app gets no Windows device-change notification), so a new device shows the next time a list is read.
// Does not own: the enumeration calls (IAudioDevices) or the pickers' "Checking…" entry (PendingChoices).
// Tests: TabForge.Avalonia.Tests.AudioDeviceNamesTests.
/// <summary>Device name lists for Settings: <see cref="Names"/> answers at once from memory (null while the first listing runs), <see cref="Settled"/> completes when a listing finishes.</summary>
public sealed class AudioDeviceNames
{
    /// <summary>The key of the recording devices (the output devices are keyed by driver name).</summary>
    public const string Capture = "";
    private const double RefreshSec = 10;

    // Key: driver name or Capture. Ceiling: the drivers plus one. Thread: all guarded by _gate; the worker only writes the finished list.
    private readonly Dictionary<string, (IReadOnlyList<string> Names, long At)> _known = new();
    private readonly Dictionary<string, Task> _listing = new();
    private readonly object _gate = new();
    private readonly IAudioDevices _devices;

    public AudioDeviceNames(IAudioDevices devices) => _devices = devices;

    /// <summary>The names for <paramref name="key"/>; null until the first listing finished (it is started here). A list older than the refresh interval is returned as it is while a new listing starts.</summary>
    public IReadOnlyList<string>? Names(string key)
    {
        lock (_gate)
        {
            var found = _known.TryGetValue(key, out var known);
            if (!found || Stopwatch.GetElapsedTime(known.At).TotalSeconds > RefreshSec) StartListing(key);
            return found ? known.Names : null;
        }
    }

    /// <summary>Completes when the listing for <paramref name="key"/> that is running (or the last one) has finished.</summary>
    public Task Settled(string key)
    {
        lock (_gate) return _listing.TryGetValue(key, out var running) ? running : Task.CompletedTask;
    }

    private void StartListing(string key)
    {
        if (_listing.ContainsKey(key)) return;
        _listing[key] = Task.Run(() =>
        {
            IReadOnlyList<string> names = Array.Empty<string>();
            try { names = key == Capture ? _devices.CaptureEndpointNames() : _devices.OutputNames(key); }
            catch (Exception ex) when (ex is not OutOfMemoryException)   // a driver or COM error of any kind: an empty list, so "Checking…" always ends
            {
                TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, $"audio device list not read: {ex.Message}");
            }
            finally
            {
                lock (_gate)
                {
                    _known[key] = (names, Stopwatch.GetTimestamp());
                    _listing.Remove(key);
                }
            }
        });
    }
}
