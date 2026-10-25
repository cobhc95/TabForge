using System.IO;
using System.Text;

namespace TabForge.Services;

// Owns: the always-on, size-bounded errors.log of swallowed errors in the diagnostics folder.
// Does not own: the catch sites (they call Trace.Error) and the opt-in trace files.
// Tests: no dedicated test class.
/// <summary>
/// Appends one line per swallowed error to <c>errors.log</c> (cap 256 KB; at the cap the file moves to <c>errors.1.log</c> and a new one starts).
/// Thread-safe and never throws. An identical line within one second of the last is dropped and counted; the count is written as
/// "(repeated N more times)" before the next different line. Not for per-frame or pointer-move paths: those stay silent.
/// </summary>
internal sealed class ErrorLog
{
    public const long CapBytes = 256L * 1024;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly long _cap;
    private readonly Func<DateTime> _now;
    private string? _lastText;
    private DateTime _lastAt;
    private int _suppressed;

    /// <summary>The app-wide log in the diagnostics folder (resolved on first write, so --profile applies).</summary>
    public static ErrorLog Sink { get; } = new(() => FilePathPolicy.DefaultDiagnosticsPath("errors.log"), CapBytes, () => DateTime.Now);

    private readonly Func<string> _pathFactory;

    internal ErrorLog(Func<string> pathFactory, long cap, Func<DateTime> now)
    {
        _pathFactory = pathFactory;
        _cap = cap;
        _now = now;
    }

    /// <summary>Records one error line. Never throws.</summary>
    public void Write(string area, string text)
    {
        try
        {
            lock (_gate)
            {
                var now = _now();
                var key = area + "|" + text;
                if (key == _lastText && now - _lastAt < RepeatWindow) { _suppressed++; return; }
                var sb = new StringBuilder();
                if (_suppressed > 0) sb.Append($"{now:HH:mm:ss.fff} (repeated {_suppressed} more times){Environment.NewLine}");
                sb.Append($"{now:HH:mm:ss.fff} [{area}] {text.ReplaceLineEndings(" ")}{Environment.NewLine}");
                _lastText = key; _lastAt = now; _suppressed = 0;
                Append(sb.ToString());
            }
        }
        catch (Exception ex)   // never throws, whatever the path or disk does // Not logged: the log cannot log its own failure
        {
            System.Diagnostics.Debug.WriteLine($"errors.log write failed: {ex.Message}");
        }
    }

    private void Append(string block)
    {
        var path = FilePathPolicy.OutputFile(_pathFactory(), "error log");
        var bytes = new UTF8Encoding(false).GetBytes(block);
        if (bytes.LongLength > _cap) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Single-open size check (no Exists + FileInfo sweeps); rotation closes and reopens only when capped.
        using (var probe = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite,
                   bufferSize: 16 * 1024, FileOptions.SequentialScan))
        {
            if (probe.Length + bytes.LongLength <= _cap)
            {
                probe.Seek(0, SeekOrigin.End);
                probe.Write(bytes);
                return;
            }
        }
        try { File.Move(path, Path.ChangeExtension(path, ".1.log"), overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite,
            bufferSize: 16 * 1024, FileOptions.SequentialScan);
        output.Write(bytes);
    }
}
