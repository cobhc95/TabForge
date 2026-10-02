using System.IO;
using TabForge.Audio;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Song files in a drag from Windows (they open in new tabs wherever they are dropped).</summary>
public static class DroppedSongs
{
    /// <summary>The song files (scores and projects) in a file drop: they open in new tabs wherever they are dropped.</summary>
    public static string[] In(System.Windows.IDataObject? data)
    {
        try
        {
            return data?.GetDataPresent(System.Windows.DataFormats.FileDrop) == true && data.GetData(System.Windows.DataFormats.FileDrop) is string[] files
                ? files.Where(f => MediaDrop.RoleOf(f) == MediaDrop.FileRole.Song).ToArray()
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or OutOfMemoryException) { return Array.Empty<string>(); }
    }

}

/// <summary>
/// What one drag carries, measured once when it enters the timeline: the audio and MIDI files of a file drop (Explorer or a
/// plug-in's temp file), or the virtual files of a FileGroupDescriptorW drop written to a TabForge staging folder.
/// </summary>
public sealed class MediaDropSession : IDisposable
{
    private MediaDropSession(string key, List<DropItem> items, int unsupported, string? staging)
    {
        Key = key; Items = items; Unsupported = unsupported; StagingFolder = staging;
    }

    /// <summary>A drag of items already measured (tests and the off-screen render).</summary>
    public static MediaDropSession FromItems(IEnumerable<DropItem> items) => new("items|" + Guid.NewGuid().ToString("N"), items.ToList(), 0, null);

    public string Key { get; }
    public IReadOnlyList<DropItem> Items { get; }
    /// <summary>Files in the drop that are neither audio, MIDI nor songs.</summary>
    public int Unsupported { get; }
    public string? StagingFolder { get; private set; }
    /// <summary>Some virtual files could not be read while dragging: the drop reads the drag again.</summary>
    public bool HasDeferred => Items.Any(i => i.Deferred);

    /// <summary>A cheap identity for a drag (the same drag re-entering the timeline is not measured again).</summary>
    public static string? KeyOf(System.Windows.IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(System.Windows.DataFormats.FileDrop) && data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
                return "files|" + string.Join("|", files);
            if (VirtualFileDrop.Descriptors(data) is { Count: > 0 } virtuals)
                return "virtual|" + string.Join("|", virtuals.Select(v => $"{v.Name}:{v.Size}"));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or OutOfMemoryException or IOException) { }
        return null;
    }

    /// <summary>
    /// The drag's media, or null when it carries none (only song files, or data that is not files: those are someone else's).
    /// <paramref name="contentsAt"/> replaces the virtual-file reader (tests).
    /// </summary>
    /// <paramref name="onDrop"/> true reads every virtual file now (the drop); while dragging only <see cref="VirtualFileDrop.HoverBudgetBytes"/> are read.
    public static MediaDropSession? From(System.Windows.IDataObject data, Func<int, Stream?>? contentsAt = null, bool onDrop = false, MediaContext? media = null)
    {
        var key = KeyOf(data);
        if (key is null) return null;
        var items = new List<DropItem>();
        var unsupported = 0;
        string? staging = null;
        if (key.StartsWith("files|", StringComparison.Ordinal))
        {
            var files = (string[])data.GetData(System.Windows.DataFormats.FileDrop)!;
            var measured = 0;
            foreach (var file in files.Take(64))
                switch (MediaDrop.RoleOf(file))
                {
                    // The first 16 audio files are measured while the drag enters (a header read each); any more are measured on drop.
                    case MediaDrop.FileRole.Audio when measured++ >= 16:
                        items.Add(new DropItem { Path = file, Name = Path.GetFileNameWithoutExtension(file), Kind = DropItemKind.Audio, Transient = MediaDrop.IsTransient(file) });
                        break;
                    case MediaDrop.FileRole.Audio: items.Add(Measure(file, DropItemKind.Audio, MediaDrop.IsTransient(file), media)); break;
                    case MediaDrop.FileRole.Midi: items.Add(Measure(file, DropItemKind.Midi, MediaDrop.IsTransient(file), media)); break;
                    case MediaDrop.FileRole.Unsupported: unsupported++; break;
                }
            // only songs (or songs beside other files): the window opens them
            if (items.Count == 0 && (unsupported == 0 || files.Any(f => MediaDrop.RoleOf(f) == MediaDrop.FileRole.Song))) return null;
        }
        else
        {
            staging = Path.Combine(MediaDrop.StagingRoot, Guid.NewGuid().ToString("N"));
            VirtualFileDrop.CleanOldStaging();
            var written = VirtualFileDrop.Materialise(data, staging, contentsAt, out unsupported, out var unread,
                onDrop ? VirtualFileDrop.MaxFileBytes : VirtualFileDrop.HoverBudgetBytes);
            foreach (var file in written)
                items.Add(Measure(file, MidiFileImport.IsMidiFile(file) ? DropItemKind.Midi : DropItemKind.Audio, transient: true, media));
            // A source that hands its contents over only on drop: placeholders now (one bar each), read again when dropped.
            foreach (var name in unread)
                items.Add(new DropItem { Path = "", Name = Path.GetFileNameWithoutExtension(name), Kind = MidiFileImport.IsMidiFile(name) ? DropItemKind.Midi : DropItemKind.Audio, Transient = true, Deferred = true });
            if (items.Count == 0 && unsupported == 0) { TryDelete(staging); return null; }
        }
        return new MediaDropSession(key, items, unsupported, staging);
    }

    /// <summary>Measures a file once: audio length (local files only; a network file is measured on drop), or the MIDI file read.</summary>
    public static DropItem Measure(string file, DropItemKind kind, bool transient, MediaContext? media = null)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (kind == DropItemKind.Midi)
        {
            // The same path rules as audio: device paths refused; a file on a network or removable drive is read only when dropped
            // (never while the pointer merely passes over the timeline).
            var where = MediaPathPolicy.Classify(file, (media ?? MediaContext.Anonymous).BaseDirectory, requireAudio: false);
            if (where.Refused) return new DropItem { Path = file, Name = name, Kind = kind, Transient = transient, Problem = $"{name}: {where.Problem}" };
            if (where.Location != MediaLocation.Local) return new DropItem { Path = file, Name = name, Kind = kind, Transient = transient };
            return ReadMidi(file, transient);
        }
        var verdict = MediaPathPolicy.Classify(file, (media ?? MediaContext.Anonymous).BaseDirectory);
        if (verdict.Refused) return new DropItem { Path = file, Name = name, Kind = kind, Transient = transient, Problem = $"{name}: {verdict.Problem}" };
        if (verdict.Location != MediaLocation.Local) return new DropItem { Path = file, Name = name, Kind = kind, Transient = transient };   // measured on drop
        var seconds = WaveformCache.LengthOf(file, media ?? MediaContext.Anonymous, userPicked: true);
        return seconds > 0
            ? new DropItem { Path = file, Name = name, Kind = kind, Seconds = seconds, Transient = transient }
            : new DropItem { Path = file, Name = name, Kind = kind, Transient = transient, Problem = $"{name} could not be read as audio" };
    }

    /// <summary>Reads a MIDI file (on drop for one that was not read while dragging); a file that cannot be read gets a problem.</summary>
    public static DropItem ReadMidi(string file, bool transient)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        const DropItemKind kind = DropItemKind.Midi;
        try { return new DropItem { Path = file, Name = name, Kind = kind, Midi = MidiFileImport.Read(file), Transient = transient }; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new DropItem { Path = file, Name = name, Kind = kind, Transient = transient, Problem = $"{name}: {ex.Message}" }; }
    }

    public void Dispose()
    {
        if (StagingFolder is { } folder) TryDelete(folder);
        StagingFolder = null;
    }

    internal static void TryDelete(string folder)
    {
        try
        {
            // Only TabForge's own staging folders are ever removed.
            if (MediaPathPolicy.IsInside(MediaPathPolicy.Normalize(folder), MediaPathPolicy.Normalize(MediaDrop.StagingRoot)) && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
