using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Features.Help;

namespace TabForge.Avalonia.Features.Help;

// Owns: loading the Help chapters from the app's embedded files: one chapter parsed on demand and kept (the chapter being viewed, never all of them at
//   once for reading), and the search index built once on a background thread the first time the window opens, then shared by every later window of the same feature.
// Does not own: the chapter model and parser (Core HelpDocument), the chapter list (HelpChapterCatalog), the index itself (Core HelpSearchIndex), drawing (HelpWindow).
// Tests: TabForge.Avalonia.Tests.HelpContentTests.
internal sealed class HelpContent
{
    private const string ResourceMarker = ".Help.Content.";

    private readonly Dictionary<int, HelpChapter> _parsed = new();
    private Task<HelpSearchIndex>? _index;

    /// <summary>How long the last index build took, in milliseconds (0 until one finished).</summary>
    internal double LastIndexBuildMs { get; private set; }

    /// <summary>The raw text of chapter <paramref name="index"/> from its embedded file; empty when the file is missing.</summary>
    internal static string ReadText(int index)
    {
        var assembly = typeof(HelpContent).Assembly;
        var file = HelpChapterCatalog.All[index].Key + ".md";
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains(ResourceMarker, StringComparison.Ordinal) && n.EndsWith(file, StringComparison.Ordinal));
        if (name is null) return "";
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    /// <summary>The parsed chapter, read and parsed the first time it is asked for. UI thread.</summary>
    internal HelpChapter Chapter(int index)
    {
        if (!_parsed.TryGetValue(index, out var chapter)) _parsed[index] = chapter = HelpDocument.Parse(ReadText(index), HelpChapterCatalog.Titles);
        return chapter;
    }

    /// <summary>The search index: started on a worker thread by the first call, the same task for every later call. Parsing for the index is a pass of its own; nothing it parses is kept.</summary>
    internal Task<HelpSearchIndex> Index() => _index ??= Task.Run(() =>
    {
        var watch = Stopwatch.StartNew();
        var chapters = new List<HelpChapter>(HelpChapterCatalog.All.Count);
        for (var i = 0; i < HelpChapterCatalog.All.Count; i++) chapters.Add(HelpDocument.Parse(ReadText(i), HelpChapterCatalog.Titles));
        var index = HelpSearchIndex.Build(chapters, HelpIntentMap.LoadEmbedded());
        LastIndexBuildMs = watch.Elapsed.TotalMilliseconds;
        return index;
    });

    /// <summary>True once the index is built.</summary>
    internal bool IndexReady => _index is { IsCompletedSuccessfully: true };
}
