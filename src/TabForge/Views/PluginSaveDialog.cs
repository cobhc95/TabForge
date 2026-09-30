using System.Windows;
using System.Windows.Controls;

namespace TabForge.Views;

/// <summary>
/// Asked once per song when a song with TabForge audio data (plug-ins, FX, mixer groups) is saved as .gp.
/// Songs without audio data never see it and save as .gp as usual.
/// </summary>
public static class PluginSaveDialog
{
    /// <summary>.gp with the TabForge data inside (Guitar Pro skips it). Default.</summary>
    public const string GpWithData = "gp+data";
    /// <summary>A clean .gp plus "song.tfaudio" beside it: the most compatible with Guitar Pro.</summary>
    public const string GpPlusDataFile = "gp+file";
    /// <summary>TabForge's own format.</summary>
    public const string TForge = "tforge";

    /// <summary>The choice, or null when cancelled.</summary>
    public static string? Ask(Window owner, string fileName)
    {
        string? result = null;
        var w = new Window
        {
            Title = "Save song with audio settings", Owner = owner, Width = 540, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        w.SetResourceReference(Window.ForegroundProperty, "TextBrush");
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "This song uses TabForge audio settings (VST plug-ins, FX chains or mixer groups).", FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
        });
        var detail = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            Text = $"Guitar Pro does not know these settings: it plays {fileName} with its own sounds, and if Guitar Pro saves the file, "
                 + "the TabForge settings are removed. How should TabForge save it?"
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        root.Children.Add(detail);

        void Option(string title, string text, string value, bool recommended)
        {
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 6) };
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            content.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            content.Children.Add(sub);
            b.Content = content;
            if (recommended) { b.IsDefault = true; b.SetResourceReference(Control.BorderBrushProperty, "AccentBrush"); b.BorderThickness = new Thickness(2); }
            b.Click += (_, _) => { result = value; w.DialogResult = true; };
            root.Children.Add(b);
        }
        Option("Guitar Pro file with the audio settings inside (recommended)",
            "One .gp file for both programs: TabForge keeps everything, Guitar Pro skips the TabForge part. "
            + "For the best compatibility with Guitar Pro, choose the next option.", GpWithData, true);
        Option("Clean Guitar Pro file + audio data file",
            "The .gp contains nothing TabForge-specific (exactly what Guitar Pro expects); the audio settings go in a "
            + ".tfaudio file beside it, which TabForge loads automatically when you open the .gp.", GpPlusDataFile, false);
        Option("TabForge project (.tforge)", "TabForge's own format: keeps everything. Guitar Pro cannot open it.", TForge, false);
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 3, 12, 3) };
        root.Children.Add(cancel);
        w.Content = root;
        return DialogHost.ShowModal(w) == true ? result : null;
    }
}
