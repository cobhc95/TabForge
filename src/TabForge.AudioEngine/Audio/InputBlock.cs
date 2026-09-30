namespace TabForge.AudioEngine.Audio;

/// <summary>
/// One block of live input (inputs 1 and 2 at the engine rate), read from <see cref="InputCapture"/> once per mixer
/// block and shared read-only by every armed chain, so several monitored tracks hear the same samples.
/// </summary>
public sealed class InputBlock
{
    public readonly float[] L, R;
    /// <summary>Valid frames in <see cref="L"/> / <see cref="R"/> for the current block.</summary>
    public int Frames;

    public InputBlock(int maxBlock)
    {
        L = new float[maxBlock];
        R = new float[maxBlock];
    }
}
