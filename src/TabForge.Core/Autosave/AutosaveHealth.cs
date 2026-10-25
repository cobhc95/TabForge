using System;
using System.IO;
using TabForge.Services;

namespace TabForge.Autosave;

// Owns: what the autosave has achieved so far (the last attempt and the last success, the failure and its reason) and the status line that says so.
// Does not own: the copies (AutosaveRunner), the notice bar that shows the line (the window's autosave controller).
// Tests: MainWindowAutosaveTests.

/// <summary>Why the last autosave failed, in the words the notice uses.</summary>
public enum AutosaveFailureKind { None, DiskFull, AccessDenied, Other }

/// <summary>
/// The autosave's health: the last attempt and the last SUCCESS are tracked separately, so a failing autosave is visible in the notice instead of
/// only reaching the debug output. UI thread only (the runner updates it on the caller's thread after its worker finishes).
/// </summary>
public sealed class AutosaveHealth
{
    public DateTime? LastAttemptUtc { get; private set; }
    public DateTime? LastSuccessUtc { get; private set; }
    public AutosaveFailureKind FailureKind { get; private set; }
    public string? FailureDetail { get; private set; }
    /// <summary>The Recovery folder the last failure belongs to (the probe cache: one entry per folder).</summary>
    public string? Folder { get; private set; }
    /// <summary>The last write probe's detail for <see cref="Folder"/> (set by the Retry probe; null: no probe ran).</summary>
    public string? ProbeDetail { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public bool Failing => ConsecutiveFailures > 0;

    public void Succeeded(DateTime now)
    {
        LastAttemptUtc = LastSuccessUtc = now;
        Clear();
    }

    public void Failed(DateTime now, Exception error, string? folder = null, string? probeDetail = null)
    {
        LastAttemptUtc = now;
        ConsecutiveFailures++;
        FailureKind = Classify(error);
        FailureDetail = error.Message;
        if (folder is not null) Folder = folder;
        if (probeDetail is not null) ProbeDetail = probeDetail;
    }

    /// <summary>Nothing needed saving (or everything was saved by hand): nothing is at risk, so any earlier failure notice goes.</summary>
    public void Clear()
    {
        ConsecutiveFailures = 0;
        FailureKind = AutosaveFailureKind.None;
        FailureDetail = null;
        Folder = null;
        ProbeDetail = null;
    }

    /// <summary>The failure kind of an error, looking through its inner errors (Win32 codes inside an HRESULT).</summary>
    public static AutosaveFailureKind Classify(Exception error)
    {
        for (Exception? e = error; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return AutosaveFailureKind.AccessDenied;
            if (e is not IOException io) continue;
            var code = io.HResult & 0xFFFF;
            if (code is ErrorHandleDiskFull or ErrorDiskFull) return AutosaveFailureKind.DiskFull;
            if (code == ErrorAccessDenied) return AutosaveFailureKind.AccessDenied;
        }
        return AutosaveFailureKind.Other;
    }

    private const int ErrorAccessDenied = 5;
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorDiskFull = 0x70;

    /// <summary>The line the notice shows while failing (null when all is well).</summary>
    public string? Describe(DateTime now)
    {
        if (!Failing) return null;
        var reason = FailureKind switch
        {
            AutosaveFailureKind.DiskFull => "the disk is full",
            AutosaveFailureKind.AccessDenied => "access to the Recovery folder was denied." + Services.FilePathPolicy.AccessDeniedHint,
            _ => string.IsNullOrWhiteSpace(FailureDetail) ? "unknown error" : FailureDetail!.Trim(),
        };
        var last = LastSuccessUtc is { } ok ? $"Last recovery copy: {Ago(now - ok)} ago" : "No recovery copy yet";
        var probe = string.IsNullOrWhiteSpace(ProbeDetail) ? "" : $" Folder check: {ProbeDetail!.Trim()}";
        return $"Autosave failed: {reason}. {last}; the previous copy is kept. Retrying every {AutosavePlanner.RetryMinutes} min.{probe}";
    }

    private static string Ago(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1) ? "under a minute" :
        span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes} min" : $"{(int)span.TotalHours} h";
}
