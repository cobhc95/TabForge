using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Plugins;

namespace TabForge.Services;

// Owns: the data of the "Review plug-ins" window: the plug-ins of a song that are not approved, and approving the ticked ones (each approval covers that exact file).
// Does not own: the window (Views/ApprovalReviewWindows), saving and the mixer refresh after an approval (the caller), the trust rules (PluginTrust).
// Tests: TabForge.Avalonia.Tests.ApprovalReviewWindowsTests.
/// <summary>The plug-ins of a song that are not approved yet, each with the reason it is not.</summary>
public sealed class PluginApprovalReview
{
    private readonly PluginSettings _settings;
    private readonly PluginTrust _trust;
    private readonly IPluginFileTrust _files;

    public PluginApprovalReview(PluginSettings settings, PluginTrust trust, IPluginFileTrust files, IReadOnlyList<(string Path, string Reason)> untrusted)
    {
        _settings = settings;
        _trust = trust;
        _files = files;
        Untrusted = untrusted;
    }

    public IReadOnlyList<(string Path, string Reason)> Untrusted { get; }

    public void Approve(IEnumerable<string> paths)
    {
        foreach (var path in paths) _trust.Approve(_settings, path, _files);
    }
}
