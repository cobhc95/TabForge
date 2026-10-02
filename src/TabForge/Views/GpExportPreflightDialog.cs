using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Shown before a clean Guitar Pro file is written, only when the song uses something that file cannot hold (see <see cref="GpExportPreflight"/>).
/// Three choices: keep a full TabForge copy first, write only the compatible file, or cancel. Esc is Cancel; the default button (Enter)
/// keeps the full copy, so the safe choice loses nothing.
/// </summary>
public static class GpExportPreflightDialog
{
    public const string KeepId = "GpPreflight.KeepCopy", CompatibleId = "GpPreflight.Compatible", CancelId = "GpPreflight.Cancel";

    /// <summary>The dialog and a way to read what was chosen (Cancel until a button is pressed).</summary>
    public sealed class Handle
    {
        public required Window Window { get; init; }
        public GpExportChoice Choice { get; internal set; } = GpExportChoice.Cancel;
    }

    /// <summary>The person's choice; Cancel when the window is closed any other way.</summary>
    public static GpExportChoice Ask(Window owner, GpPreflightReport report, GpExportKind kind, string fileName)
    {
        var handle = Build(report, kind, fileName);
        handle.Window.Owner = owner;
        handle.Window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DialogHost.ShowModal(handle.Window);
        return handle.Choice;
    }

    public static Handle Build(GpPreflightReport report, GpExportKind kind, string fileName)
    {
        var w = new Window
        {
            Title = kind == GpExportKind.Save ? "Save as .gp file" : "Export .gp file", Width = 560, MaxHeight = 640,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var handle = new Handle { Window = w };
        void Finish(GpExportChoice choice)
        {
            handle.Choice = choice;
            try { w.DialogResult = choice != GpExportChoice.Cancel; } catch (InvalidOperationException) { w.Close(); }   // not shown (tests, captures)
        }

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = $"{fileName} cannot hold everything in this song.", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = "A compatible .gp file keeps the notes and the common marks, but these things are changed or left out:",
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(intro);

        var list = new TextBox
        {
            Text = report.Summary(12), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(8, 6, 8, 6),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1),
        };
        list.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
        list.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetName(list, "What a compatible .gp file leaves out");
        root.Children.Add(list);

        var advice = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            Text = kind == GpExportKind.Save
                ? "Keep a full TabForge copy writes a .tforge project with everything first, and your song then follows that copy. "
                  + "Save compatible .gp writes the .gp plus its audio-settings file (plug-ins, FX chains, mixer groups) and marks the song saved; the notation details listed above are not kept."
                : "Keep a full TabForge copy also writes a .tforge project with everything beside the file. Your song and its unsaved state are not changed either way.",
        };
        advice.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(advice);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Button Make(string text, string id, bool isDefault, bool isCancel, GpExportChoice choice)
        {
            var b = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
            AutomationProperties.SetAutomationId(b, id);
            AutomationProperties.SetName(b, text);
            if (isDefault) { b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush"); b.BorderThickness = new Thickness(2); }
            b.Click += (_, _) => Finish(choice);
            buttons.Children.Add(b);
            return b;
        }
        var keep = Make("Keep a full TabForge copy", KeepId, true, false, GpExportChoice.KeepNativeCopy);
        Make(kind == GpExportKind.Save ? "Save compatible .gp" : "Export compatible copy", CompatibleId, false, false, GpExportChoice.ExportCompatible);
        Make("Cancel", CancelId, false, true, GpExportChoice.Cancel);
        root.Children.Add(buttons);
        w.Content = root;
        w.Loaded += (_, _) => keep.Focus();
        return handle;
    }
}
