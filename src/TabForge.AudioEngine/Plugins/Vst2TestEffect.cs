using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TabForge.AudioEngine.Plugins;

public sealed unsafe partial class Vst2Plugin
{
    /// <summary>
    /// Headless tests: a pass-through VST2 effect implemented in managed code behind the real VST2 ABI, so the host
    /// side (<see cref="Vst2Plugin"/>) runs unchanged without a DLL on disk. It records the rate and block size the host
    /// set, the opcode sequence (main thread) and the processed block lengths (audio thread, allocation-free).
    /// One instance at a time.
    /// </summary>
    public static class TestEffect
    {
        public const string PathName = "selftest-vst2-effect";
        private static AEffect* _next;
        private static readonly object Gate = new();
        private static readonly List<int> OpcodeLog = new();
        private static float _sampleRate;
        private static int _openDelayMs;
        private static int _blockSize, _blocks, _shortBlocks, _maxFrames, _minFrames = int.MaxValue;

        /// <summary>Blocks shorter than this count as short (the device's block size after a change).</summary>
        public static volatile int ExpectFrames;
        public static double SampleRate => Volatile.Read(ref _sampleRate);
        public static int BlockSize => Volatile.Read(ref _blockSize);
        public static int Blocks => Volatile.Read(ref _blocks);
        public static int ShortBlocks => Volatile.Read(ref _shortBlocks);
        public static int MaxFrames => Volatile.Read(ref _maxFrames);
        public static int MinFrames => Volatile.Read(ref _minFrames);

        /// <summary>The main-thread opcodes since the last <see cref="ResetLog"/> (effProcessEvents excluded).</summary>
        public static int[] Opcodes { get { lock (Gate) return OpcodeLog.ToArray(); } }
        /// <summary>stop (72), mains off (12/0 -> -12), sample rate (10), block size (11), mains on (12), start (71).</summary>
        public static readonly int[] ReconfigureSequence = { EffStopProcess, -EffMainsChanged, EffSetSampleRate, EffSetBlockSize, EffMainsChanged, EffStartProcess };

        public static void ResetLog(int expectFrames)
        {
            lock (Gate) OpcodeLog.Clear();
            ExpectFrames = expectFrames;
            Volatile.Write(ref _blocks, 0); Volatile.Write(ref _shortBlocks, 0); Volatile.Write(ref _maxFrames, 0); Volatile.Write(ref _minFrames, int.MaxValue);
        }

        /// <summary>A new instance (a fresh AEffect) hosted by the real <see cref="Vst2Plugin"/> code path.</summary>
        /// <param name="openDelayMs">effOpen sleeps this long (a slow-loading instrument).</param>
        /// <param name="inputs">Declared audio inputs (the host allocates exactly this many, at least two; more than 128 is refused).</param>
        public static Vst2Plugin Create(double sampleRate, int maxBlock, int openDelayMs = 0, int inputs = 2, int outputs = 2)
        {
            Volatile.Write(ref _openDelayMs, openDelayMs);
            var effect = (AEffect*)NativeMemory.AllocZeroed((nuint)sizeof(AEffect));   // lives as long as the process (tests only)
            effect->magic = Magic;
            effect->dispatcher = (IntPtr)(delegate* unmanaged[Cdecl]<AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)&Dispatcher;
            effect->processReplacing = (IntPtr)(delegate* unmanaged[Cdecl]<AEffect*, float**, float**, int, void>)&ProcessReplacing;
            effect->numInputs = inputs; effect->numOutputs = outputs;
            effect->flags = FlagCanReplacing;
            lock (Gate)
            {
                _next = effect;
                return new Vst2Plugin(PathName, IntPtr.Zero, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, AEffect*>)&Main, sampleRate, maxBlock);
            }
        }

        private static IntPtr _host;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static AEffect* Main(IntPtr host) { _host = host; return _next; }

        /// <summary>Each processed block calls audioMasterAutomate (opcode 0) back into the host, from the audio thread.</summary>
        public static volatile bool AutomateInProcess;
        /// <summary>The output is NaN (a misbehaving plug-in).</summary>
        public static volatile bool OutputNaN;
        /// <summary>The host's VstTimeInfo as seen by the last processed block (audioMasterGetTime from processReplacing).</summary>
        public static int TimeSigNumerator, TimeSigDenominator, TimeFlags;
        public static double BarStartPpq, PpqPosition, Tempo;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr Dispatcher(AEffect* effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
        {
            if (opcode == EffProcessEvents) return 1;
            if (opcode == EffOpen && Volatile.Read(ref _openDelayMs) > 0) Thread.Sleep(_openDelayMs);
            if (opcode == EffSetSampleRate) Volatile.Write(ref _sampleRate, opt);
            if (opcode == EffSetBlockSize) Volatile.Write(ref _blockSize, (int)value);
            lock (Gate) OpcodeLog.Add(opcode == EffMainsChanged && value == 0 ? -EffMainsChanged : opcode);
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void ProcessReplacing(AEffect* effect, float** inputs, float** outputs, int frames)
        {
            Interlocked.Increment(ref _blocks);
            if (frames < ExpectFrames) Interlocked.Increment(ref _shortBlocks);
            if (frames > _maxFrames) Volatile.Write(ref _maxFrames, frames);
            if (frames < _minFrames) Volatile.Write(ref _minFrames, frames);
            for (var c = 0; c < 2; c++) new ReadOnlySpan<float>(inputs[c], frames).CopyTo(new Span<float>(outputs[c], frames));
            if (OutputNaN) for (var c = 0; c < 2; c++) new Span<float>(outputs[c], frames).Fill(float.NaN);
            var host = (delegate* unmanaged[Cdecl]<AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)_host;
            if (host is null) return;
            if (AutomateInProcess) host(effect, 0, 0, IntPtr.Zero, IntPtr.Zero, 0.5f);   // audioMasterAutomate
            var time = (VstTimeInfo*)host(effect, 7, 0, IntPtr.Zero, IntPtr.Zero, 0);   // audioMasterGetTime
            if (time is null) return;
            TimeSigNumerator = time->timeSigNumerator; TimeSigDenominator = time->timeSigDenominator; TimeFlags = time->flags;
            BarStartPpq = time->barStartPos; PpqPosition = time->ppqPos; Tempo = time->tempo;
        }
    }
}
