using System.IO;
using System.Text;

namespace TabForge.Services;

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

        var length = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
        if (length + bytes.LongLength > maximumBytes)
        {
            FilePathPolicy.WriteAtomically(fullPath, stream => stream.Write(bytes), createDirectory: true);
            return;
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        using var output = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4 * 1024, FileOptions.SequentialScan);
        output.Write(bytes);
    }
}
