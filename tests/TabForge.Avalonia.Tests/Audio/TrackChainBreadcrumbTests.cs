using System;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Stage 13 B8 regression proof that each chain stage clears its crash breadcrumb when its plug-in call returns,
//   including the non-finite early return, so a later fault is not blamed on the last plug-in that ran.
public sealed class TrackChainBreadcrumbTests
{
    [Fact]
    public void ANonFiniteStageDoesNotLeaveItsBreadcrumbActive()
    {
        using var shared = SharedBlock.Create($"tf-crumb-{Guid.NewGuid():N}");
        var plugin = new ProbePlugin("crashy", shared) { NonFinite = true };
        using var chain = new TrackChain(0, -1, null, new[] { new TrackChain.Effect(plugin, 1f, 0) }, 256);
        Render(chain, shared);

        Assert.Null(shared.AudioPluginCall());
    }

    [Fact]
    public void EveryStageSeesItsOwnBreadcrumbAndTheChainEndsWithNone()
    {
        using var shared = SharedBlock.Create($"tf-crumb-{Guid.NewGuid():N}");
        var first = new ProbePlugin("first", shared);
        var second = new ProbePlugin("second", shared);
        using var chain = new TrackChain(0, -1, null, new[]
        {
            new TrackChain.Effect(first, 1f, 0),
            new TrackChain.Effect(second, 1f, 1),
        }, 256);
        Render(chain, shared);

        Assert.True(first.SawOwnBreadcrumb, "the first stage must be attributed to its own path");
        Assert.True(second.SawOwnBreadcrumb, "the second stage must be attributed to its own path");
        Assert.Null(shared.AudioPluginCall());
    }

    private static void Render(TrackChain chain, SharedBlock shared)
    {
        var frames = 256;
        var input = new float[frames];
        var output = new float[frames];
        for (var i = 0; i < frames; i++) input[i] = 0.1f;
        chain.Render(output, new float[frames], 0, frames, new TransportInfo { Tempo = 120, Playing = true, Speed = 1 }, shared);
    }

    private sealed class ProbePlugin : IPluginInstance
    {
        private readonly SharedBlock _shared;
        public ProbePlugin(string path, SharedBlock shared) { Path = path; _shared = shared; }
        public bool NonFinite { get; init; }
        public bool SawOwnBreadcrumb { get; private set; } = true;
        public string Path { get; }
        public bool IsInstrument => false;
        public bool HasEditor => false;
        public int LatencySamples => 0;
        public int TailSamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport)
        {
            SawOwnBreadcrumb &= _shared.AudioPluginCall()?.Path == Path;
            for (var c = 0; c < 2; c++)
                for (var i = 0; i < frames; i++)
                    output[c][i] = NonFinite ? float.NaN : input[c][i];
        }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) => null;
        public void CloseEditor() { }
        public void EditorIdle() { }
        public void Dispose() { }
    }
}
