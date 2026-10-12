namespace TabForge.AudioEngine.Audio;

/// <summary>
/// One block of live input (every channel the input device delivers, at the engine rate), read from <see cref="InputCapture"/>
/// once per mixer block and shared read-only by every armed chain, so several monitored tracks hear the same samples.
/// </summary>
public sealed class InputBlock
{
    /// <summary>One buffer per input channel (sized once; <see cref="Channels"/> of them carry input).</summary>
    public readonly float[][] Ch;
    /// <summary>Inputs 1 and 2.</summary>
    public float[] L => Ch[0];
    public float[] R => Ch[1];
    /// <summary>Channels that carry input in the current block (never more than <see cref="Ch"/> holds).</summary>
    public int Channels = 2;
    /// <summary>Valid frames in each channel for the current block.</summary>
    public int Frames;

    public InputBlock(int maxBlock, int channels = 2)
    {
        Ch = new float[Math.Max(2, channels)][];
        for (var c = 0; c < Ch.Length; c++) Ch[c] = new float[maxBlock];
    }
}
