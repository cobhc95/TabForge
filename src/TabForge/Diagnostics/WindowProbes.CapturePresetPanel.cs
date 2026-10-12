using System.Windows;
using System.Windows.Controls;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Presets;

namespace TabForge.Diagnostics;

// Owns: the capture step "preset-panel": "seed" writes example presets of all four kinds into the scratch profile (before the window
//   opens); "plugin", "chains", "midi" or "processor" draws that drop-down's open list (its real rows, built by PresetRows) over the
//   last opened tool window, under the drop-down, without opening a popup ("midi-rename" shows a row being renamed with a refused name).
// Does not own: step dispatch (WindowProbes.CaptureExtras.cs), the rows (Views/Presets) or the shots.
// Tests: none (diagnostics only; run through --capture).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private static readonly string[] ExamplePresets = { "Warm pad", "Bright lead", "Tight rhythm", "Soft intro with a long name to trim" };

        /// <summary>The preset manager as the tool window "PresetManager" (after "preset-panel":"seed"): on a plug-in, on the Velocity processor, with a search, or with a built-in drum map selected.</summary>
        private void PresetManagerShot(string what)
        {
            _toolName = "PresetManager";
            if (_tools.TryGetValue(_toolName, out var old)) { CloseWindow(old); _tools.Remove(_toolName); }
            foreach (var store in new[] { PresetFolderStore.Containers(), new PresetFolderStore(DrumMapLibrary.UserFolder, ".json") })
                FilePathPolicy.WriteAtomically(store.PathOf("Drive stack"), s => s.Write("{\"Name\":\"Drive stack\",\"Notes\":{\"36\":\"Kick\"}}"u8), createDirectory: true);
            DrumMapLibrary.Reload();
            if (what == "selected")   // three presets of the containers, selected together
                foreach (var name in ExamplePresets) FilePathPolicy.WriteAtomically(PresetFolderStore.Containers().PathOf(name), s => s.Write("{\"Name\":\"x\",\"Notes\":{\"36\":\"Kick\"}}"u8), createDirectory: true);
            var window = what switch
            {
                "processor" => new PresetManagerWindow(null, PresetKind.MidiProcessor, "velocity"),
                "builtin" => new PresetManagerWindow(null, PresetKind.DrumMap, null),
                "selected" => new PresetManagerWindow(null, PresetKind.Container, null),
                "plugin" or "search" => new PresetManagerWindow(null, PresetKind.PluginPreset, PresetFolderStore.PluginKey(new PluginSlot { Name = "Example instrument" })),
                _ => throw new InvalidOperationException($"unknown preset manager view '{what}' (plugin, processor, search, builtin, selected)"),
            };
            Adopt(window);
            if (what == "search") window.SearchText = "lead";
            if (what == "selected") window.SelectNames(ExamplePresets[0], ExamplePresets[1], ExamplePresets[2]);
            window.UpdateLayout();
        }

        /// <summary>The delete question for three presets with its "Don't ask again" tick, unticked (the message-box step "delete").</summary>
        private Window PresetDeleteBox() => ThemedMessageBox.Build(_w.Window, PresetDeleteQuestion.TextFor(ExamplePresets.Take(3).ToList()), "Preset manager",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.None, _ => { }, PresetDeleteQuestion.RememberText);

        private void PresetPanel(string kind)
        {
            var slot = (_tools.GetValueOrDefault(_toolName ?? "") as Views.FxChainWindow)?.Track.Rig.Plugins.FirstOrDefault() ?? new PluginSlot { Name = "Example instrument" };
            var stores = new Dictionary<string, (string Target, Func<PresetFolderStore> Store, string Title)>
            {
                ["plugin"] = ("Fx.Presets", () => PresetFolderStore.Plugin(slot), "Saved presets of Example instrument"),
                ["chains"] = ("Fx.Chains", PresetFolderStore.FxChains, "Saved FX chains"),
                ["midi"] = ("Midi.Presets", PresetFolderStore.MidiChains, "Saved MIDI processing presets"),
                ["processor"] = ("Midi.ProcessorPresets", () => PresetFolderStore.MidiProcessor("velocity"), "Saved presets of this processor"),
            };
            if (kind == "seed")
            {
                foreach (var (key, entry) in stores)
                    foreach (var name in ExamplePresets) FilePathPolicy.WriteAtomically(entry.Store().PathOf(name), s => s.Write("[]"u8), createDirectory: true);
                return;
            }
            var rename = kind.EndsWith("-rename", StringComparison.Ordinal);
            if (!stores.TryGetValue(rename ? kind[..^7] : kind, out var place)) throw new InvalidOperationException($"unknown preset panel '{kind}' (seed, plugin, chains, midi, processor, midi-rename)");
            var window = _tools.GetValueOrDefault(_toolName ?? "") ?? throw new InvalidOperationException("open the FxChain-example or MidiProcessing window first");
            var target = FindVisuals<FrameworkElement>(window).FirstOrDefault(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) == place.Target)
                ?? throw new InvalidOperationException($"the window has no '{place.Target}'");
            var host = (ItemsControl)target;
            var rows = PresetRows.Of(host) ?? throw new InvalidOperationException($"'{place.Target}' has no organisable rows");
            var stack = new StackPanel();
            foreach (var item in host.Items)
            {
                FrameworkElement row = item switch
                {
                    Separator => new Separator(),
                    MenuItem { Header: Border } menu => rows.Row(System.Windows.Automation.AutomationProperties.GetName(menu), System.Windows.Automation.AutomationProperties.GetName(menu)),
                    MenuItem menu => PresetRows.Plain((menu.Header as string ?? "").Replace("_", "")),
                    ComboBoxItem box => rows.View(box, box.Content?.ToString() ?? ""),
                    _ => rows.View(item, item.ToString() ?? ""),
                };
                row.Margin = new Thickness(6, 2, 6, 2);
                stack.Children.Add(row);
            }
            if (rename) { rows.BeginRename(ExamplePresets[1]); rows.Rename(ExamplePresets[1], "a/b"); }
            var content = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(0, 2, 0, 2), Child = stack, MinWidth = target.ActualWidth };
            content.SetResourceReference(Border.BackgroundProperty, "Panel2Brush");
            content.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            content.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            // The list is drawn on a layer over the window's own content, where its popup opens.
            var canvas = new Canvas();
            if (window.Content is Grid { Tag: "preset-panel" } layered) { layered.Children.RemoveAt(1); layered.Children.Add(canvas); }
            else
            {
                var body = (UIElement)window.Content;
                window.Content = null;
                layered = new Grid { Tag = "preset-panel" };
                layered.Children.Add(body); layered.Children.Add(canvas);
                window.Content = layered;
            }
            window.UpdateLayout();
            var at = target.TranslatePoint(new Point(0, target.ActualHeight), layered);
            Canvas.SetLeft(content, at.X); Canvas.SetTop(content, at.Y);
            canvas.Children.Add(content);
            window.UpdateLayout();
        }
    }
}
