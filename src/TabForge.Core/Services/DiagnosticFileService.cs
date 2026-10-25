using System.IO;
using System.Text;

namespace TabForge.Services;

// Owns: size-capped writes and appends of diagnostic files.
// Does not own: what is diagnosed and where the call sites are.
// Tests: TestSecurityInputBoundaries.
/// <summary>Small bounded log helpers. Runtime diagnostics are opt-in and never include score bodies.</summary>
public static class DiagnosticFileService
{
    public static void WriteText(string path, string text, long maximumBytes = InputLimits.MaxDiagnosticLogBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumBytes < 0 || text.Length > maximumBytes)
            throw new InvalidDataException("The diagnostic output exceeds its size limit.");
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        if (bytes.LongLength > maximumBytes)
            throw new InvalidDataException("The diagnostic output exceeds its size limit.");
        FilePathPolicy.WriteAtomically(path, stream => stream.Write(bytes), createDirectory: true);
    }

    public static void AppendCappedLine(string path, string line, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(line);
        var fullPath = FilePathPolicy.OutputFile(path, "diagnostic log");
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(line + Environment.NewLine);
        if (bytes.LongLength > maximumBytes) return;

        // Single open (no Exists + FileInfo sweeps): the stream length is the size check, so bursty
        // trace writes cost one open + one write with a 16 KB buffer instead of two stats + open.
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        using var output = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
            bufferSize: 16 * 1024, FileOptions.SequentialScan);
        if (output.Length + bytes.LongLength > maximumBytes) output.SetLength(0);
        else output.Seek(0, SeekOrigin.End);
        output.Write(bytes);
    }

    /// <summary>
    /// Buffered batch append (additive): appends <paramref name="lines"/> under one open for bursty callers.
    /// Existing single-line callers are unchanged.
    /// </summary>
    public static void AppendCappedLines(string path, IEnumerable<string> lines, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var blocks = lines.Where(l => l is not null).Select(l => l + Environment.NewLine).ToList();
        if (blocks.Count == 0) return;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var fullPath = FilePathPolicy.OutputFile(path, "diagnostic log");
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        using var output = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
            bufferSize: 16 * 1024, FileOptions.SequentialScan);
        foreach (var block in blocks)
        {
            var bytes = encoding.GetBytes(block);
            if (bytes.LongLength > maximumBytes) continue;
            if (output.Length + bytes.LongLength > maximumBytes) output.SetLength(0);
            else output.Seek(0, SeekOrigin.End);
            output.Write(bytes);
        }
    }
}
