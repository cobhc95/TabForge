using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: a track's FX chain window in a real main window: a plug-in's On switch (power and bypass) reaches the song, and closing the window
//     leaves no subscription behind on the engine or the pitch matcher, and stops its own timer.
// Does not own: the open, reuse and close of the windows and the stale sweep (TestMixerHost), the chain list rules and the engine.
// Tests: TestFxChainWindow.
public static partial class SelfTest
{
    /// <summary>How many handlers an event field of the target holds (the delegate's invocation list).</summary>
    private static int FxSubscribers(object target, string eventField) =>
        (target.GetType().GetField(eventField, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) as Delegate)?.GetInvocationList().Length ?? 0;

    private static void TestFxChainWindow()
    {
        var w = SmNewWindow();
        var engine = TabForge.Audio.AudioEngineClient.Instance;
        var previousMatcher = engine.AutoPitch;
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var track = doc.Project.Tracks[0];
            var slot = new PluginSlot { Name = "Delay", Path = "", Type = PluginSlotType.Effect, Format = "VST3" };
            track.Rig.Plugins.Add(slot);
            engine.AutoPitch ??= new AutoPitchMatcher(engine, static () => AppSettingsStore.Shared.Settings.Plugins);
            var matcher = engine.AutoPitch!;
            var changedBefore = FxSubscribers(matcher, "Changed");
            var editorClosedBefore = FxSubscribers(engine, "EditorClosed");

            w.MixerHost.OpenFxChain(track);
            var fx = w.MixerHost.Windows.FxWindowOf(track) ?? throw new InvalidOperationException("the FX window did not open");
            SmSettle();
            Check("fx chain window: while open it listens to the pitch matcher and to the engine's editor-closed event",
                FxSubscribers(matcher, "Changed") > changedBefore && FxSubscribers(engine, "EditorClosed") > editorClosedBefore,
                $"changed {changedBefore}->{FxSubscribers(matcher, "Changed")}, editor {editorClosedBefore}->{FxSubscribers(engine, "EditorClosed")}");

            // The On switch of the selected plug-in: unticking bypasses it in the song, ticking again turns it back on; both mark the song changed.
            var list = LtField<ListBox>(fx, "_list")!;
            var bypass = LtField<CheckBox>(fx, "_bypass")!;
            list.SelectedIndex = 0;
            SmSettle();
            doc.MarkClean();
            bypass.IsChecked = false;
            bypass.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SmSettle();
            var bypassed = !slot.Enabled;
            var bypassDirty = doc.Project.IsDirty;
            Check("fx chain window: unticking a plug-in's On switch bypasses it in the song and marks the song changed",
                bypassed && bypassDirty && list.Items.Count == 1, $"enabled {slot.Enabled}, dirty {bypassDirty}, items {list.Items.Count}");

            doc.MarkClean();
            bypass.IsChecked = true;
            bypass.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            SmSettle();
            Check("fx chain window: ticking the On switch again re-enables the plug-in and marks the song changed",
                slot.Enabled && doc.Project.IsDirty, $"enabled {slot.Enabled}, dirty {doc.Project.IsDirty}");

            // Closing the window: no handler of it stays on the engine or the matcher, its CPU timer stops, and the host forgets it.
            var cpuTimer = LtField<DispatcherTimer>(fx, "_cpuTimer");
            fx.Close();
            SmSettle();
            Check("fx chain window: closing it removes its handlers from the pitch matcher and the engine and stops its timer",
                FxSubscribers(matcher, "Changed") == changedBefore && FxSubscribers(engine, "EditorClosed") == editorClosedBefore
                && cpuTimer is { IsEnabled: false } && w.MixerHost.Windows.FxWindowOf(track) is null,
                $"changed {FxSubscribers(matcher, "Changed")} (was {changedBefore}), editor {FxSubscribers(engine, "EditorClosed")} (was {editorClosedBefore})");
        }
        finally
        {
            engine.AutoPitch = previousMatcher;
            SmCloseWindow(w);
        }
    }
}
