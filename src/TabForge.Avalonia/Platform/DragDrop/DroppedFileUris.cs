#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still delivers the dropped data.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the portable fallback for a file drag whose GetFiles() is empty (X11 file managers offer only text/uri-list or
//   x-special/gnome-copied-files): the file:// URI parsing (percent-decoding, UTF-8, comments, the copy/cut word line) and the drag trace line.
// Does not own: the drop decisions (MainWindowFileDrop, MainWindowAudioDrop).
// Tests: TabForge.Avalonia.Tests.DroppedFileUrisTests.
/// <summary>Reads local file paths out of uri-list text. Pure and thread-safe.</summary>
public static class DroppedFileUris
{
    private static string[] Formats => new[] { "text/uri-list", "x-special/gnome-copied-files", "text/plain", "UTF8_STRING", "text" };

    /// <summary>The local paths in uri-list text: one file:// URI per line; blank lines, # comments and non-file lines (the copy/cut word) are skipped.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return paths;
        foreach (var raw in text.Split('\n', '\r', '\0'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || !line.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
            if (PathOf(line) is { Length: > 0 } path) paths.Add(path);
        }
        return paths;
    }

    private static string? PathOf(string uri)
    {
        var rest = uri.Substring(5);
        if (!rest.StartsWith("//", StringComparison.Ordinal)) return null;   // file:/path (no authority) is rare; file:///path is the norm
        rest = rest.Substring(2);
        var slash = rest.IndexOf('/');
        if (slash < 0) return null;
        var host = rest.Substring(0, slash);
        if (host.Length > 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;   // a remote host is not a local file
        var path = rest.Substring(slash);
        var cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) path = path.Substring(0, cut);
        path = Uri.UnescapeDataString(path);
        if (path.Length > 2 && path[0] == '/' && path[2] == ':' && char.IsLetter(path[1])) path = path.Substring(1).Replace('/', System.IO.Path.DirectorySeparatorChar);   // file:///C:/x on Windows
        return path;
    }

    /// <summary>The local paths a drag carries as uri-list text (empty when it carries none); never throws.</summary>
    public static IReadOnlyList<string> FromData(IDataObject data)
    {
        foreach (var format in Formats)
        {
            try
            {
                var value = data.Get(format);
                var text = value switch { string s => s, byte[] b => Encoding.UTF8.GetString(b), _ => null };
                var paths = Parse(text);
                if (paths.Count > 0) return paths;
            }
            catch (Exception ex) { Trace.Write(Trace.Ui, $"drop: format {format} unreadable: {ex.GetType().Name}"); }
        }
        return Array.Empty<string>();
    }

    /// <summary>One opt-in trace line (TABFORGE_TRACE=ui) about what a drop did or why it did nothing. Never throws.</summary>
    public static void Note(string line)
    {
        Trace.Write(Trace.Ui, "drop " + line);   // never throws
    }

    /// <summary>What each path is, for the trace: song / midi / audio / other.</summary>
    public static string Describe(IEnumerable<string> files)
    {
        try
        {
            return string.Join("; ", files.Select(file =>
            {
                var ext = System.IO.Path.GetExtension(file);
                var kind = FileTypes.IsMidi(ext) ? "midi" : FileTypes.IsOpenable(ext) ? "song" : AudioDropPlanner.Accepts(file, FilePathBoundary.RefusalReason) ? "audio" : "other";
                return $"{System.IO.Path.GetFileName(file)}={kind}";
            }));
        }
        catch (Exception ex) { return "describe failed: " + ex.GetType().Name; }
    }

    /// <summary>Opt-in trace (TABFORGE_TRACE=ui) of one drag step: the offered formats, the effect, the paths and the decision. Never throws.</summary>
    public static void TraceDrag(string step, DragEventArgs e, IReadOnlyCollection<string> paths, string decision)
    {
        if (!Trace.IsOn(Trace.Ui)) return;
        try
        {
            var formats = string.Join(",", e.Data.GetDataFormats());
            Trace.Write(Trace.Ui, $"drop {step}: formats=[{formats}] effects={e.DragEffects} paths=[{string.Join(" | ", paths)}] -> {decision}");
        }
        catch (Exception ex) { Trace.Write(Trace.Ui, $"drop {step}: trace failed {ex.GetType().Name}"); }
    }
}
#pragma warning restore CS0618
