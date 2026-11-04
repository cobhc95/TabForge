using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the batch rules of Approve all / Approve selected: scope, texts, the same hash record as approving one plug-in, failures skipped, cancelling between plug-ins.
/// Does not own: the dialog and the buttons (PluginApprovalWindowTests).
/// Tests: this class. Shares PluginCatalog.ProbeOverride, so it runs in the "PluginProbeSeam" collection.
/// </summary>
[Collection("PluginProbeSeam")]
public class PluginApprovalPresenterTests
{
    internal sealed class PathTrust : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => fullPath;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    internal static (PluginSettings Settings, PluginTrust Trust, IPluginFileTrust Files, List<VstPluginInfo> Plugins) Setup(int count)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-approve-batch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var plugins = new List<VstPluginInfo>();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(folder, $"P{i}.dll");
            File.WriteAllBytes(file, [1, 2, 3, (byte)i]);
            plugins.Add(new VstPluginInfo($"P{i}", file, "VST2", "", ""));
        }
        return (new PluginSettings { LastScan = plugins }, new PluginTrust(), new PathTrust(), plugins);
    }

    private static List<PluginBrowserRow> Rows(IEnumerable<VstPluginInfo> plugins) =>
        plugins.Select(p => new PluginBrowserRow(p.Name, p.Vendor, p.Format, p.Role, p)).ToList();

    private static Task<int> Run(IReadOnlyList<VstPluginInfo> plugins, PluginSettings settings, PluginTrust trust, IPluginFileTrust files, Action<PluginApprovalStep>? step = null, CancellationToken cancel = default) =>
        PluginApprovalPresenter.RunAsync(plugins, settings, trust, files, step ?? (_ => { }), cancel);

    [Fact]
    public void TheTextsCountThePlugins()
    {
        Assert.Equal("Approve plug-ins?", PluginApprovalPresenter.Title);
        Assert.Equal("You are about to allow 3 plug-ins to run on this computer. TabForge will run each once in a separate helper to read its type and vendor. Only approve files you trust.", PluginApprovalPresenter.Body(3));
        Assert.Equal("Approve 3", PluginApprovalPresenter.ApproveLabel(3));
    }

    [Fact]
    public void TheScopeIsOnlyUnapprovedRowsOncePerFileAndAnApprovedOneIsLeftOut()
    {
        var (settings, trust, files, plugins) = Setup(3);
        trust.Approve(settings, plugins[0].Path, files);
        var rows = Rows(plugins.Append(plugins[1]));
        var scope = PluginApprovalPresenter.Unapproved(rows, p => trust.Check(p, settings, files));
        Assert.Equal(new[] { plugins[1].Path, plugins[2].Path }, scope.Select(p => p.Path));
    }

    [Fact]
    public void AnUnapprovedVst3ThatNamesItsRoleIsStillInScope()
    {
        var (settings, trust, files, plugins) = Setup(1);
        var withRole = plugins[0] with { Role = "Effect" };
        Assert.Single(PluginApprovalPresenter.Unapproved(Rows([withRole]), p => trust.Check(p, settings, files)));
    }

    [Fact]
    public void AFileChangedSinceItsApprovalIsNotInABatch()
    {
        var (settings, trust, files, plugins) = Setup(1);
        trust.Approve(settings, plugins[0].Path, files);
        File.WriteAllBytes(plugins[0].Path, [9, 9, 9, 9, 9]);
        Assert.Empty(PluginApprovalPresenter.Unapproved(Rows(plugins), p => trust.Check(p, settings, files)));
        Assert.False(trust.IsTrusted(plugins[0].Path, settings, files));
    }

    [Fact]
    public async Task EachPluginGetsTheSameHashRecordAsApprovingOneAndIsProbed()
    {
        var (settings, trust, files, plugins) = Setup(2);
        var (single, singleTrust, _, _) = (new PluginSettings(), new PluginTrust(), 0, 0);
        singleTrust.Approve(single, plugins[0].Path, files);
        var hashes = new List<string>();
        PluginCatalog.ProbeOverride = (_, hash, _) => { lock (hashes) hashes.Add(hash); return ("Effect", "Vendor", "Name"); };
        try
        {
            var steps = new List<PluginApprovalStep>();
            Assert.Equal(2, await Run(plugins, settings, trust, files, steps.Add));
            Assert.All(steps, s => { Assert.True(s.Identified); Assert.Equal("Vendor", s.Vendor); });
            Assert.Equal(new[] { 1, 2 }, steps.Select(s => s.Done));
        }
        finally { PluginCatalog.ProbeOverride = null; }
        Assert.Equal(2, hashes.Count);
        Assert.All(plugins, p => Assert.True(trust.IsTrusted(p.Path, settings, files)));
        var record = settings.TrustRecords.Single(r => string.Equals(r.Path, plugins[0].Path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(single.TrustRecords.Single().Sha256, record.Sha256);
        Assert.Equal(64, record.Sha256.Length);
    }

    [Fact]
    public async Task AFailedProbeIsSkippedAndTheBatchGoesOn()
    {
        var (settings, trust, files, plugins) = Setup(3);
        PluginCatalog.ProbeOverride = (path, _, _) => path == plugins[1].Path ? null : ("Instrument", "V", "N");
        try
        {
            var steps = new List<PluginApprovalStep>();
            Assert.Equal(3, await Run(plugins, settings, trust, files, steps.Add));
            Assert.Equal(new[] { true, false, true }, steps.Select(s => s.Identified));
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [Fact]
    public async Task CancellingStopsBeforeTheNextPluginAndLeavesTheRestUnapproved()
    {
        var (settings, trust, files, plugins) = Setup(3);
        using var cancel = new CancellationTokenSource();
        PluginCatalog.ProbeOverride = (_, _, _) => ("Effect", "V", "N");
        try
        {
            Assert.Equal(1, await Run(plugins, settings, trust, files, _ => cancel.Cancel(), cancel.Token));
        }
        finally { PluginCatalog.ProbeOverride = null; }
        Assert.True(trust.IsTrusted(plugins[0].Path, settings, files));
        Assert.False(trust.IsTrusted(plugins[1].Path, settings, files));
        Assert.False(trust.IsTrusted(plugins[2].Path, settings, files));
    }

    [Fact]
    public async Task AFileChangedAfterItsApprovalIsNeverProbedAgain()
    {
        var (settings, trust, files, plugins) = Setup(1);
        var probes = 0;
        PluginCatalog.ProbeOverride = (_, _, _) => { Interlocked.Increment(ref probes); return null; };   // not identified, so Describe would probe again if trusted
        try
        {
            await Run(plugins, settings, trust, files);
            Assert.Equal(1, probes);
            File.WriteAllBytes(plugins[0].Path, [7, 7, 7, 7, 7]);
            Assert.False(trust.IsTrusted(plugins[0].Path, settings, files));
            Assert.Equal("", PluginCatalog.Describe(plugins[0].Path, settings, trust, files).Role);
            Assert.Equal(1, probes);
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }
}
