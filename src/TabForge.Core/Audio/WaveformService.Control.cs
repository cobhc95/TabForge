using System.Diagnostics;
using System.IO;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: retry, cancel and wait controls.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class WaveformService
{
    /// <summary>Forgets what was learned about <paramref name="file"/> in this document (a failed or refused read), so the next draw reads it again.</summary>
    public void Retry(string file, MediaContext context)
    {
        lock (Gate)
            if (Slots.TryGetValue(KeyOf(file, context), out var slot)) RemoveLocked(slot);
        RaiseReady(file);
    }

    /// <summary>Stops reading (and forgets) every file that is still loading and is not one of <paramref name="wanted"/>: a clip was removed or a timeline closed.</summary>
    public void CancelUnused(IEnumerable<string> wanted, MediaContext context)
    {
        var keep = new HashSet<string>(wanted.Select(f => KeyOf(f, context)), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            // Only this document's own requests: another window showing the same file keeps reading it.
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context) && s.State == WaveState.Loading && !keep.Contains(s.Key)).ToList())
                RemoveLocked(s);
        }
    }

    /// <summary>Stops reading (and forgets) these files if they are still loading: their timeline closed.</summary>
    public void Cancel(IEnumerable<string> files, MediaContext context)
    {
        var drop = new HashSet<string>(files.Select(f => KeyOf(f, context)), StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context) && s.State == WaveState.Loading && drop.Contains(s.Key)).ToList())
                RemoveLocked(s);
        }
    }

    /// <summary>A song closed: every slot it asked for goes (reading stops, the outlines leave the memory budget). Other songs' slots are untouched, whatever they share.</summary>
    /// <remarks>Tab-close must call this with the closing document's <see cref="MediaContext"/> (follow-up outside Audio scope: verify the tab-close path calls both <c>Media.Close()</c> and this; no caller was found in scope).</remarks>
    public void Forget(MediaContext context)
    {
        lock (Gate)
            foreach (var s in Slots.Values.Where(s => ReferenceEquals(s.Context, context)).ToList())
                RemoveLocked(s);
    }

    /// <summary>Forgets everything (tests).</summary>
    internal void ClearAll()
    {
        lock (Gate)
        {
            foreach (var s in Slots.Values.ToList()) RemoveLocked(s);
            Slots.Clear(); Queue.Clear();
        }
    }

    /// <summary>Waits until nothing is queued or being read (tests).</summary>
    internal bool WaitIdle(int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            lock (Gate) { if (Queue.Count == 0 && _active == 0 && Slots.Values.All(s => s.State != WaveState.Loading)) return true; }
            Thread.Sleep(10);
        }
        return false;
    }

    /// <summary>
    /// Subscribes <paramref name="handler"/> to <see cref="Ready"/> without keeping <paramref name="owner"/> alive (a closed timeline
    /// is collected; the subscription removes itself when it finds the owner gone). The handler must not capture the owner.
    /// </summary>
    public IDisposable SubscribeWeak<T>(T owner, Action<T, string> handler) where T : class
    {
        var weak = new WeakReference<T>(owner);
        Action<string>? h = null;
        h = file =>
        {
            if (weak.TryGetTarget(out var o)) handler(o, file);
            else Ready -= h;
        };
        Ready += h;
        return new Unsubscriber(() => Ready -= h);
    }

}
