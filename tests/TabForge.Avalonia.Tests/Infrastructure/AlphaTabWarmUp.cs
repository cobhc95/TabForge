using System;


namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the one single-threaded touch of alphaTab's static tables (importer registry) before a test imports a Guitar Pro file. Its own static initializer
/// can fail with "Collection was modified" when several tests import for the first time at once.
/// Does not own: the import itself (GuitarProImporter, AlphaTabBoundary).
/// Tests: LegatoSlideChainDropTests (the first caller; add it to any other test that imports a song).
/// </summary>
internal static class AlphaTabWarmUp
{
    private static readonly Lazy<bool> Done = new(Touch, true);

    /// <summary>Runs the touch once, on whichever thread asks first; the other callers wait for it.</summary>
    public static void EnsureReady() => _ = Done.Value;

    private static bool Touch()
    {
        try { AlphaTab.Importer.ScoreLoader.LoadScoreFromBytes(Array.Empty<byte>(), new AlphaTab.Settings()); }
        catch (AlphaTab.Io.EndOfReaderError) { /* expected: empty data ends the reader at once; only the importer registry is built here */ }
        return true;
    }
}
