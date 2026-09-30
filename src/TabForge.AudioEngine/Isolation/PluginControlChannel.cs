using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Isolation;

/// <summary>
/// The isolated plug-in's control channel (engine side): every command carries a request id that the plug-in's process
/// echoes in its reply ([uint32 id][payload] after the frame type). Each <see cref="Ask"/> waits only for its own id, so a
/// reply that arrives after its request timed out (a slow GetState) is dropped as unknown and is never taken for the next
/// request's reply. Unsolicited messages (<see cref="RemotePlugin.MsgEdited"/>) carry no id and are not routed here.
/// </summary>
public sealed class PluginControlChannel
{
    private readonly Stream _pipe;
    private readonly object _gate = new();
    private readonly Dictionary<uint, TaskCompletionSource<(byte Type, byte[] Data)>> _pending = new();
    private uint _nextId;
    private int _dropped;

    public PluginControlChannel(Stream pipe) => _pipe = pipe;

    /// <summary>Replies dropped because their request was no longer waiting (timed out) or was never sent.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>
    /// Sends <paramref name="command"/> and waits up to <paramref name="timeoutMs"/> for the reply with the same id.
    /// Returns the reply payload when its type is <paramref name="expect"/>, otherwise null (a failure reply included).
    /// </summary>
    public byte[]? Ask(byte command, Action<BinaryWriter>? payload, byte expect, int timeoutMs, out bool timedOut)
    {
        timedOut = false;
        var waiter = new TaskCompletionSource<(byte Type, byte[] Data)>(TaskCreationOptions.RunContinuationsAsynchronously);
        uint id;
        lock (_gate)
        {
            id = unchecked(++_nextId);
            if (id == 0) id = _nextId = 1;
            _pending[id] = waiter;
        }
        try
        {
            Frames.Write(_pipe, command, w => { w.Write(id); payload?.Invoke(w); });
            if (!waiter.Task.Wait(timeoutMs)) { timedOut = true; return null; }
            var (type, data) = waiter.Task.Result;
            if (type == RemotePlugin.MsgFailed)
            {
                EngineLog.Write($"isolated plug-in command {command} failed: {FailureText(data)}");
                return null;
            }
            return type == expect ? data : null;
        }
        finally
        {
            lock (_gate) _pending.Remove(id);   // a late reply for this id is now unknown and dropped
        }
    }

    /// <summary>Reply reader thread: routes one reply frame (id first) to its waiting request. False when it was dropped.</summary>
    public bool Receive(byte type, BinaryReader reader)
    {
        var id = reader.ReadUInt32();
        var length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        var data = reader.ReadBytes(length);
        TaskCompletionSource<(byte, byte[])>? waiter;
        lock (_gate) _pending.Remove(id, out waiter);
        if (waiter is null) { Interlocked.Increment(ref _dropped); return false; }
        waiter.TrySetResult((type, data));
        return true;
    }

    /// <summary>
    /// Plug-in process side: runs one command's handler and always answers the request id, with
    /// <see cref="RemotePlugin.MsgFailed"/> and the error text when the handler throws.
    /// </summary>
    public static void Answer(Stream pipe, uint id, Func<(byte Type, Action<BinaryWriter>? Payload)> handler)
    {
        byte type;
        Action<BinaryWriter>? payload;
        try { (type, payload) = handler(); }
        catch (Exception ex)
        {
            EngineLog.Write($"plug-in command {id} failed: {ex.GetBaseException().Message}");
            type = RemotePlugin.MsgFailed;
            var message = ex.GetBaseException().Message;
            payload = w => w.WriteString(message);
        }
        try { Frames.Write(pipe, type, w => { w.Write(id); payload?.Invoke(w); }); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private static string FailureText(byte[] data)
    {
        try { using var r = new BinaryReader(new MemoryStream(data)); return r.ReadBoundedString(2048); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException) { return "(no detail)"; }
    }
}
