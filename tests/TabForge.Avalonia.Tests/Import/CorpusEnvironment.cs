using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TabForge.Avalonia.Tests;

// Owns: pointing TABFORGE_SAMPLES_DIR at the checkout's Samples\corpus folder when it is not set, so the corpus tests run instead of skipping.
//   An explicit value from the environment always wins.
internal static class CorpusEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TABFORGE_SAMPLES_DIR"))) return;
        string corpus;
        try { corpus = Path.Combine(TestRepo.CheckoutRoot(), "Samples", "corpus"); }
        catch (InvalidOperationException) { return; } // swallow: no checkout found; the corpus tests then report that themselves
        if (Directory.Exists(corpus)) Environment.SetEnvironmentVariable("TABFORGE_SAMPLES_DIR", corpus);
    }
}
