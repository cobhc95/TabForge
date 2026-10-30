using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Audio;
using TabForge.AudioEngine;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Plugins;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the checks that the app-wide services (meter tick, plug-in trust, editor windows) are instances handed in, and that each listener a window or
/// view adds is gone when it closes or detaches (the meter tick, the row events of the timeline, the document watch of the mixer, the subscription bag),
/// plus the mixer rebuilding only the group whose track changed.
/// Does not own: the rules of those services (PluginTrust, the editor window code).
/// Tests: this class.
/// </summary>
public class EntanglementSeamTests
{
    private static SongDocument DocumentOf(TabForge.Models.SongProject project) => new(() => null) { Project = project };

    private static int Listeners(object owner, string eventName) =>
        (owner.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;

    [AvaloniaFact]
    public void MeterTickKeepsOneEntryPerSubscriberAndDisposeDropsThemAll()
    {
        using var meter = new SharedMeterTick();
        Action a = () => { }, b = () => { };
        meter.Subscribe(a); meter.Subscribe(a); meter.Subscribe(b);
        Assert.Equal(2, meter.ListenerCount);
        meter.Unsubscribe(a);
        Assert.Equal(1, meter.ListenerCount);
        meter.Dispose();
        Assert.Equal(0, meter.ListenerCount);
    }

    [AvaloniaFact]
    public void TwoMeterTicksAreIndependent()
    {
        using var one = new SharedMeterTick();
        using var two = new SharedMeterTick();
        one.Subscribe(() => { });
        Assert.Equal(0, two.ListenerCount);
    }

    [AvaloniaFact]
    public void ClosingTheTunerReleasesItsMeterListener()
    {
        using var meter = new SharedMeterTick();
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        var window = new TunerWindow(() => DemoSongFactory.Create().Tracks[0], engine, meter);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, meter.ListenerCount);
        window.Close();
        Assert.Equal(0, meter.ListenerCount);
    }

    [AvaloniaFact]
    public void DetachingAnArrangementReleasesTheArmedRowsMeterAndTheTimelineEvents()
    {
        using var meter = new SharedMeterTick();
        var doc = DocumentOf(DemoSongFactory.Create());
        doc.Project.Tracks[0].RecordArm = true;
        var view = new ArrangementView(doc.Project, doc, meterTick: meter);
        var window = new Window { Width = 1300, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Assert.Equal(2, meter.ListenerCount);   // the armed rows and the track meters share the app tick
        var rows = Listeners(view.Timeline, nameof(TimelineView.RowsChanged));
        var scrolls = Listeners(view.Timeline, nameof(TimelineView.RowScrollChanged));
        Assert.True(rows > 0 && scrolls > 0);

        window.Close();
        Assert.Equal(0, meter.ListenerCount);
        Assert.Equal(rows - 2, Listeners(view.Timeline, nameof(TimelineView.RowsChanged)));   // the armed layer and the meter column are no longer called
        Assert.Equal(scrolls - 2, Listeners(view.Timeline, nameof(TimelineView.RowScrollChanged)));
    }

    [AvaloniaFact]
    public void ReattachingAnArrangementWatchesItsRowsAgainOnce()
    {
        using var meter = new SharedMeterTick();
        var doc = DocumentOf(DemoSongFactory.Create());
        var view = new ArrangementView(doc.Project, doc, meterTick: meter);
        var first = new Window { Content = view };
        first.Show();
        var attached = Listeners(view.Timeline, nameof(TimelineView.RowsChanged));
        first.Close();
        first.Content = null;
        var second = new Window { Content = view };
        second.Show();
        Assert.Equal(attached, Listeners(view.Timeline, nameof(TimelineView.RowsChanged)));
        second.Close();
    }

    [AvaloniaFact]
    public void ClosingTheMixerStopsItWatchingTheDocument()
    {
        var doc = DocumentOf(DemoSongFactory.Create());
        var before = Listeners(doc, nameof(SongDocument.TimelineMarked));
        var window = new MixerWindow(doc);
        window.Show();
        window.EnsureStripsBuilt();
        Assert.True(Listeners(doc, nameof(SongDocument.TimelineMarked)) > before);
        window.Close();
        Assert.Equal(before, Listeners(doc, nameof(SongDocument.TimelineMarked)));
    }

    [AvaloniaFact]
    public void AnEditElsewhereRebuildsOnlyTheGroupOfTheChangedTrack()
    {
        var doc = DocumentOf(DemoSongFactory.Create());
        var window = new MixerWindow(doc);
        window.Show();
        window.EnsureStripsBuilt();
        List<Border> Panels() => window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("Group: ") == true).ToList();
        var panels = Panels();
        Assert.True(panels.Count >= 2, "the demo song must have two groups for this check");
        var changed = doc.Project.Tracks[0];
        var changedGroup = TrackOrdering.Layout(doc.Project).First(g => g.Tracks.Contains(changed)).Group;

        changed.Name += " renamed";
        doc.Project.MarkTimelineChanged();

        var after = Panels();
        Assert.Equal(panels.Count, after.Count);
        for (var i = 0; i < panels.Count; i++)
            Assert.Equal(panels[i].Name == $"Group: {changedGroup}", !ReferenceEquals(panels[i], after[i]));
        window.Close();
    }

    [Fact]
    public void ASubscriptionBagUndoesLatestFirstOnceAndRunsLateAddsAtOnce()
    {
        var bag = new SubscriptionBag();
        var order = new List<int>();
        bag.Add(() => order.Add(1));
        bag.Add(() => order.Add(2));
        bag.Dispose();
        bag.Dispose();
        Assert.Equal(new[] { 2, 1 }, order);
        bag.Add(() => order.Add(3));
        Assert.Equal(new[] { 2, 1, 3 }, order);
    }

    [Fact]
    public void ASubscriptionBagRunsTheRestWhenOneUndoThrows()
    {
        var bag = new SubscriptionBag();
        var order = new List<int>();
        bag.Add(() => order.Add(1));
        bag.Add(() => throw new InvalidOperationException("boom"));
        bag.Add(() => order.Add(3));
        bag.Dispose();
        Assert.Equal(new[] { 3, 1 }, order);
    }

    [AvaloniaFact]
    public void WindowsOfOneRegistryAskOneTrust()
    {
        var trust = new PluginTrust();
        var registry = new TabForge.Avalonia.Shell.TabWindowRegistry(new TabForge.Avalonia.Shell.AppServices(trust));
        var one = new TabForge.Avalonia.MainWindow(null, registry);
        var two = new TabForge.Avalonia.MainWindow(null, registry);   // a tear-off is made the same way: over the registry it came from
        Assert.Same(trust, one.Windows.Services.Trust);
        Assert.Same(trust, two.Windows.Services.Trust);
        Assert.Same(trust, TabForge.Avalonia.Views.Fx.FxChainWindows.For(one)!.Trust);
        Assert.Same(trust, TabForge.Avalonia.Views.Fx.FxChainWindows.For(two)!.Trust);
        one.Close(); two.Close();
    }

    [Fact]
    public void PluginTrustStateBelongsToTheInstance()
    {
        var one = new PluginTrust();
        var two = new PluginTrust();
        var path = @"C:\Plugins\changed-at-load.dll";
        one.MarkChanged(path);
        var files = new NoFiles();
        Assert.Equal(PluginTrust.ReasonChanged, one.Check(path, new TabForge.Services.PluginSettings(), files).Reason);
        Assert.NotEqual(PluginTrust.ReasonChanged, two.Check(path, new TabForge.Services.PluginSettings(), files).Reason);
    }

    [Fact]
    public void TheEngineGateAndTheWindowsShareOneTrustWhenHandedTheSame()
    {
        var trust = new PluginTrust();
        var gate = new PluginTrustLoadGate(new NoFiles(), trust);
        gate.MarkChanged(@"C:\Plugins\shared.dll");
        Assert.Equal(PluginTrust.ReasonChanged, trust.Check(@"C:\Plugins\shared.dll", new TabForge.Services.PluginSettings(), new NoFiles()).Reason);
        Assert.Same(trust, gate.Trust);
    }

    private sealed class NoFiles : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    // ---- plug-in editor windows (real Win32 windows, one registry per instance) ----

    [DllImport("user32")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    private sealed class FakePlugin : IPluginInstance
    {
        public IntPtr Window;
        public bool EditorOpen;
        public string Path => "fake.dll";
        public bool IsInstrument => false;
        public bool HasEditor => true;
        public int LatencySamples => 0;
        public void Process(float[][] input, float[][] output, int frames, ReadOnlySpan<BlockMidi> midi, in TransportInfo transport) { }
        public byte[]? GetState() => null;
        public void SetState(byte[] state) { }
        public (int Width, int Height)? OpenEditor(IntPtr parent) { Window = parent; EditorOpen = true; return (300, 200); }
        public void CloseEditor() => EditorOpen = false;
        public void EditorIdle() { }
        public void Dispose() { }
    }

    [AvaloniaFact]
    public void EditorWindowsOpenFocusAndCloseThroughTheirOwnInstance()
    {
        var editors = new EditorWindows(new EngineLogWriter());
        var other = new EditorWindows(new EngineLogWriter());
        var plugin = new FakePlugin();
        Assert.True(editors.Show(plugin, "Fake", IntPtr.Zero, dark: true));
        Assert.Equal(1, editors.OpenCount);
        Assert.True(editors.Show(plugin, "Fake", IntPtr.Zero, dark: true));   // shown again: brought forward, not opened twice
        Assert.Equal(1, editors.OpenCount);
        Assert.Equal(0, other.OpenCount);
        editors.CloseFor(plugin);
        Assert.Equal(0, editors.OpenCount);
        Assert.False(plugin.EditorOpen);
    }

    [AvaloniaFact]
    public void TheUserClosingAnEditorWindowReachesItsInstanceAndOnlyIt()
    {
        var editors = new EditorWindows(new EngineLogWriter());
        var other = new EditorWindows(new EngineLogWriter());
        var closed = 0;
        var otherClosed = 0;
        editors.UserClosed = () => closed++;
        other.UserClosed = () => otherClosed++;
        var plugin = new FakePlugin();
        Assert.True(editors.Show(plugin, "Fake", IntPtr.Zero, dark: true));
        SendMessage(plugin.Window, 0x10 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(1, closed);
        Assert.Equal(0, otherClosed);
        Assert.Equal(0, editors.OpenCount);
        Assert.False(plugin.EditorOpen);
    }

    [AvaloniaFact]
    public void CloseAllClearsTheWindowsAndTheUserClosedHook()
    {
        var editors = new EditorWindows(new EngineLogWriter()) { UserClosed = () => { } };
        Assert.True(editors.Show(new FakePlugin(), "A", IntPtr.Zero, dark: false));
        Assert.True(editors.Show(new FakePlugin(), "B", IntPtr.Zero, dark: false));
        Assert.Equal(2, editors.OpenCount);
        editors.CloseAll();
        Assert.Equal(0, editors.OpenCount);
        Assert.Null(editors.UserClosed);
    }
}
