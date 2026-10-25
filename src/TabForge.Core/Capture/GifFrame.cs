namespace TabForge.Capture;

// Owns: one animation frame handed to GifEncoder: its size, its pixels and how long it shows.
// Does not own: reading the pixels from a screen (the capture host), the encoding (GifEncoder).
// Tests: TabForge.Avalonia.Tests.GifEncoderTests.

/// <summary>One frame of an animation: <paramref name="Rgba"/> holds Width x Height pixels as R, G, B, A bytes (alpha below 128 counts as transparent, which GIF cannot blend), shown for <paramref name="DelayMs"/>.</summary>
public sealed record GifFrame(int Width, int Height, byte[] Rgba, int DelayMs)
{
    /// <summary>Throws when the pixel array does not match the size, so a wrong frame fails at the call and not inside the encoder.</summary>
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || Width > ushort.MaxValue || Height > ushort.MaxValue) throw new ArgumentException($"A frame of {Width} x {Height} cannot be a GIF frame.");
        if (Rgba.Length != Width * Height * 4) throw new ArgumentException($"A {Width} x {Height} frame needs {Width * Height * 4} bytes, not {Rgba.Length}.");
        if (DelayMs < 0) throw new ArgumentException("A frame delay cannot be negative.");
    }
}
