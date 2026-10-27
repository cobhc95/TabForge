using System;

namespace TabForge.Avalonia.Platform;

// Owns: the start-up check that this process runs below Medium integrity, and the notice that explains it.
// Does not own: the message box (AvaloniaMessageBox) or when it is shown (App).
// Tests: TabForge.Avalonia.Tests.ProcessIntegrityTests.
/// <summary>
/// Detects a Low-integrity start. Windows starts an exe at the lower of the parent's integrity and the exe file's label, so a
/// build inside a folder an agent sandbox labelled Low (or a launch from that sandbox) runs at Low integrity: every write to
/// Videos, Music, Documents and %LocalAppData% is then refused with "Access is denied" and Windows treats the exe as a download.
/// </summary>
internal static class ProcessIntegrity
{
    /// <summary>SECURITY_MANDATORY_MEDIUM_RID: a normal (non-elevated) user process.</summary>
    internal const int MediumRid = 0x2000;

    internal const string NoticeCaption = "TabForge is running restricted";

    internal const string Notice =
        "Windows started TabForge at Low integrity, so it cannot record audio or video, autosave, or save settings in your user folders "
        + "(you will see \"Access is denied\").\n\n"
        + "This happens when TabForge.exe was built in, or started from, a folder or terminal that an agent sandbox restricted. "
        + "Close TabForge and start it from Explorer, or clear the folder's label in a normal terminal:\n"
        + "icacls \"<TabForge folder>\" /setintegritylevel (OI)(CI)M /T /C /Q";

    /// <summary>Untrusted (0) and Low (0x1000) are below Medium; Medium, High and System are not.</summary>
    internal static bool IsBelowMedium(int rid) => rid < MediumRid;

    /// <summary>The integrity RID of this process's token, or null when it cannot be read (or not Windows).</summary>
    internal static int? CurrentRid() => PlatformAdapters.ProcessIntegrity.CurrentRid();
}

/// <summary>Reads the integrity level of this process (Windows only; other systems have no such level).</summary>
internal interface IProcessIntegrity
{
    /// <summary>The integrity RID of this process's token, or null when it cannot be read (or there is no such level).</summary>
    int? CurrentRid();
}

/// <summary>The non-Windows stub: no integrity levels, so no notice.</summary>
internal sealed class NullProcessIntegrity : IProcessIntegrity
{
    public int? CurrentRid() => null;
}
