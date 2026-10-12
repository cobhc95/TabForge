using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

// Owns: the saved state of a VST2 plug-in that has no chunk of its own (no effFlagsProgramChunks): the list of its
//       parameter values, and reading single parameters for diagnostics.
// Does not own: chunk states (Vst2Plugin.GetState / SetState), anything on the audio thread.
// Tests: TestFxChainExchangeFormat (the layout, through the chain exchange format that carries the same block).
public sealed unsafe partial class Vst2Plugin
{
    private const int EffGetParamName = 8;
    /// <summary>More parameters than this are not saved or applied as a list.</summary>
    public const int MaxListedParameters = 65536;
    /// <summary>The first eight bytes of a parameter-list state; one little-endian float (0..1) per parameter follows.</summary>
    public static ReadOnlySpan<byte> ParameterListMark => new byte[] { 0xEF, 0xBE, 0xAD, 0xDE, 0x0D, 0xF0, 0xAD, 0xDE };

    public int UniqueId => _effect->uniqueID;
    public bool UsesChunks => (_effect->flags & FlagProgramChunks) != 0;
    public int DeclaredInputs => _effect->numInputs;
    public int DeclaredOutputs => _effect->numOutputs;
    public int ParameterCount => Math.Clamp(_effect->numParams, 0, MaxListedParameters);

    public float GetParameter(int index) =>
        index >= 0 && index < ParameterCount && _effect->getParameter != IntPtr.Zero
            ? ((delegate* unmanaged[Cdecl]<AEffect*, int, float>)_effect->getParameter)(_effect, index) : 0;

    public string ParameterName(int index)
    {
        if (index < 0 || index >= ParameterCount) return "";
        var buffer = stackalloc byte[256];   // the ABI says 8 characters; plug-ins write more
        Dispatch(EffGetParamName, index, IntPtr.Zero, (IntPtr)buffer, 0);
        buffer[255] = 0;
        return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "";
    }

    private byte[]? GetParameterListState()
    {
        var count = ParameterCount;
        if (count == 0 || _effect->getParameter == IntPtr.Zero) return null;
        var bytes = new byte[8 + count * 4];
        ParameterListMark.CopyTo(bytes);
        for (var i = 0; i < count; i++) BitConverter.TryWriteBytes(bytes.AsSpan(8 + i * 4, 4), GetParameter(i));
        return bytes;
    }

    /// <summary>Applies a parameter-list state; false when <paramref name="state"/> is not one.</summary>
    private bool TrySetParameterListState(byte[] state)
    {
        if (state.Length < 12 || (state.Length - 8) % 4 != 0 || !state.AsSpan(0, 8).SequenceEqual(ParameterListMark)) return false;
        if (_effect->setParameter == IntPtr.Zero) return true;
        var set = (delegate* unmanaged[Cdecl]<AEffect*, int, float, void>)_effect->setParameter;
        var count = Math.Min((state.Length - 8) / 4, ParameterCount);
        for (var i = 0; i < count; i++)
        {
            var value = BitConverter.ToSingle(state, 8 + i * 4);
            // As stored: some plug-ins keep values past 0..1 in their list, and clamping would change their sound. Absurd values are skipped.
            if (float.IsFinite(value) && MathF.Abs(value) <= 1e6f) set(_effect, i, value);
        }
        return true;
    }
}
