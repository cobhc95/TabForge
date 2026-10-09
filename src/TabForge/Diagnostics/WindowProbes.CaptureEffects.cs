using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge.Diagnostics;

// Owns: the capture step "effect" (opens one note-effect editor on the cursor note and photographs its dialog off-screen, then answers Cancel).
// Does not own: the editors (Views/EffectEditors), step dispatch (WindowProbes.Capture.cs) or the shot writer (WindowProbes.CaptureShots.cs).
// Tests: none (diagnostics only; run through --capture).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        /// <summary>Writes effect-&lt;kind&gt;-&lt;theme&gt;.png for the editor of <paramref name="kind"/> (bend, tremolobar, trill, grace, harmonic).</summary>
        private void EffectShot(string kind)
        {
            if (!Enum.TryParse<EffectEditorKind>(kind, ignoreCase: true, out var editorKind))
                throw new InvalidOperationException($"unknown effect editor '{kind}' (bend, tremolobar, trill, grace, harmonic)");
            var file = $"effect-{editorKind.ToString().ToLowerInvariant()}-{_theme}.png";
            var flow = new EffectEditorFlow(_w.Window);
            var photographed = false;
            void Photograph(ThemedEditorDialog dialog)
            {
                // Laid out off-screen (never activated, never on a monitor), photographed, then closed: no modal loop.
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.ShowActivated = false;
                dialog.Left = CaptureOffscreen;
                dialog.Top = CaptureOffscreen;
                TryRootDpi(dialog);
                dialog.Show();
                dialog.UpdateLayout();
                var bitmap = Render((FrameworkElement)dialog.Content, dialog.ActualWidth, dialog.ActualHeight);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                FilePathPolicy.WriteAtomically(FilePathPolicy.OutputFile(Path.Combine(_out, file), "screenshot", ".png"), encoder.Save);
                dialog.Close();
                photographed = true;
                Log($"effect {editorKind} ({_theme}): {file} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
            }
            if (editorKind is EffectEditorKind.Bend or EffectEditorKind.TremoloBar)
                flow.Open(editorKind, dialog => { Photograph(dialog.Dialog); return EditorAnswer.Cancel; });
            else
                flow.OpenOrnament(editorKind, dialog => { Photograph(dialog.Dialog); return EditorAnswer.Cancel; });
            if (!photographed) throw new InvalidOperationException($"the {editorKind} editor did not open (no note at the cursor?)");
        }
    }
}
