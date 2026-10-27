using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using TabForge.Audio.Contracts;

namespace TabForge.AudioEngine.Plugins.Clap;

// Owns: the saved state of a CLAP plug-in (clap.state through memory streams) and its parameter list / setter (clap.params).
// Does not own: the process call (ClapPlugin.cs), where the state is stored (the song, like every other plug-in format).
// Tests: ClapHostTests (state round trip, parameters), ClapProtocolTests.
public sealed unsafe partial class ClapPlugin
{
    private const uint ParamHidden = 1u << 2, ParamReadOnly = 1u << 3, ParamStepped = 1u << 0;
    private const int MaxQueuedParameters = 64, MaxParameters = 4096;

    private readonly ConcurrentQueue<(uint Id, double Value, IntPtr Cookie)> _queuedParameters = new();
    private Dictionary<uint, (double Min, double Max, bool ReadOnly, IntPtr Cookie)>? _parameterIndex;

    private sealed class StateSink
    {
        public readonly MemoryStream Data = new();
        public bool TooBig;
    }

    private sealed class StateSource
    {
        public byte[] Data = Array.Empty<byte>();
        public int Position;
    }

    public byte[]? GetState()
    {
        if (_disposed || _plugin == null || _stateExt == null) return null;
        PumpMainThread();
        var sink = new StateSink();
        var handle = GCHandle.Alloc(sink);
        try
        {
            var stream = new ClapOStream { Ctx = (void*)GCHandle.ToIntPtr(handle), Write = &StateWrite };
            var ok = _stateExt->Save(_plugin, &stream);
            if (sink.TooBig) throw new InvalidDataException($"The plug-in state is larger than the {PluginStateLimits.MaxRawBytes / 1048576} MiB limit.");
            if (ok == 0) throw new InvalidOperationException("The plug-in did not write its state.");
            return sink.Data.Length == 0 ? null : sink.Data.ToArray();
        }
        finally { handle.Free(); }
    }

    /// <summary>Applies a saved state; throws <see cref="InvalidDataException"/> when the plug-in rejects it, so the load is reported instead of silently using the defaults.</summary>
    public void SetState(byte[] state)
    {
        if (state.Length == 0 || _disposed || _plugin == null || _stateExt == null) return;
        var source = new StateSource { Data = state };
        var handle = GCHandle.Alloc(source);
        try
        {
            var stream = new ClapIStream { Ctx = (void*)GCHandle.ToIntPtr(handle), Read = &StateRead };
            if (_stateExt->Load(_plugin, &stream) == 0) throw new InvalidDataException($"The CLAP plug-in rejected its saved state ({state.Length} bytes).");
        }
        finally { handle.Free(); }
        _parameterIndex = null;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long StateWrite(ClapOStream* stream, void* buffer, ulong size)
    {
        try
        {
            var sink = (StateSink)GCHandle.FromIntPtr((IntPtr)stream->Ctx).Target!;
            if (size > (ulong)PluginStateLimits.MaxRawBytes || sink.Data.Length + (long)size > PluginStateLimits.MaxRawBytes) { sink.TooBig = true; return -1; }
            sink.Data.Write(new ReadOnlySpan<byte>(buffer, (int)size));
            return (long)size;
        }
        catch (Exception) { return -1; }   // an exception must not cross into the plug-in
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long StateRead(ClapIStream* stream, void* buffer, ulong size)
    {
        try
        {
            var source = (StateSource)GCHandle.FromIntPtr((IntPtr)stream->Ctx).Target!;
            var count = (int)Math.Min(size, (ulong)(source.Data.Length - source.Position));
            if (count <= 0) return 0;
            source.Data.AsSpan(source.Position, count).CopyTo(new Span<byte>(buffer, count));
            source.Position += count;
            return count;
        }
        catch (Exception) { return -1; }
    }

    public IReadOnlyList<PluginParameterInfo> GetParameters()
    {
        if (_disposed || _plugin == null || _params == null) return Array.Empty<PluginParameterInfo>();
        PumpMainThread();
        var count = (int)Math.Min(_params->Count(_plugin), (uint)MaxParameters);
        var list = new List<PluginParameterInfo>(count);
        var index = new Dictionary<uint, (double, double, bool, IntPtr)>(count);
        for (var i = 0; i < count; i++)
        {
            ClapParamInfo info = default;
            if (_params->GetInfo(_plugin, (uint)i, &info) == 0) continue;
            var readOnly = (info.Flags & ParamReadOnly) != 0;
            index[info.Id] = (info.MinValue, info.MaxValue, readOnly, (IntPtr)info.Cookie);
            if ((info.Flags & ParamHidden) != 0) continue;
            double value = info.DefaultValue;
            _params->GetValue(_plugin, info.Id, &value);
            list.Add(new PluginParameterInfo(info.Id, Fixed(info.Name, 256), Fixed(info.Module, 1024), info.MinValue, info.MaxValue, info.DefaultValue, value, (info.Flags & ParamStepped) != 0, readOnly, ValueText(info.Id, value)));
        }
        _parameterIndex = index;
        return list;
    }

    public bool SetParameter(uint id, double value)
    {
        if (_disposed || _plugin == null || _params == null || !double.IsFinite(value)) return false;
        if (_parameterIndex is null) GetParameters();
        if (_parameterIndex is null || !_parameterIndex.TryGetValue(id, out var p) || p.ReadOnly) return false;
        if (_queuedParameters.Count >= MaxQueuedParameters) return false;
        _queuedParameters.Enqueue((id, Math.Clamp(value, p.Min, p.Max), p.Cookie));
        return true;
    }

    /// <summary>Audio thread: puts the parameter changes made since the last block at the start of the event list.</summary>
    private void WriteQueuedParameters()
    {
        while (_eventCount < MaxEvents && _queuedParameters.TryDequeue(out var q))
        {
            var e = (ClapEventParamValue*)(_events + _eventCount * EventStride);
            *e = default;
            e->Header = new ClapEventHeader { Size = (uint)sizeof(ClapEventParamValue), Time = 0, Type = ClapIds.ParamValue };
            e->ParamId = q.Id;
            e->Cookie = (void*)q.Cookie;
            e->NoteId = -1; e->PortIndex = -1; e->Channel = -1; e->Key = -1;
            e->Value = q.Value;
            _eventCount++;
        }
    }

    internal void NoteParametersChanged() => _parameterIndex = null;

    /// <summary>The plug-in's own text for a value ("-6.0 dB"), or empty when it gives none.</summary>
    private string ValueText(uint id, double value)
    {
        if (_params->ValueToText == null) return "";
        var buffer = stackalloc byte[ValueTextBytes];
        new Span<byte>(buffer, ValueTextBytes).Clear();
        return _params->ValueToText(_plugin, id, value, buffer, ValueTextBytes) != 0 ? Fixed(buffer, ValueTextBytes - 1) : "";
    }

    private const int ValueTextBytes = 64;

    private static string Fixed(byte* p, int max)
    {
        var n = 0;
        while (n < max && p[n] != 0) n++;
        return Encoding.UTF8.GetString(p, n).Trim();
    }
}
