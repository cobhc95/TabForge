using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the piano roll key strip redesign: left auditions only, right selects (Ctrl toggles, Shift adds the
//   anchor range), no context menu on the keys, the Piano / Names corner pair (radio, persisted, migrated),
//   T opening the Transpose By dialog with the count and preview as one undo step, and the hover / selection look.
// Does not own: the edits (PianoRollPresenter) or the toolbar geometry (PianoRollToolbarLayoutTests).
// Tests: PianoRollSurface, PianoRollView.
public sealed class PianoRollKeyboardTests
{
    private static (PianoRollView View, Window Window) Show(SongDocument document, PianoRollHost? host = null, ThemeVariant? variant = null)
    {
        var view = new PianoRollView(host ?? new PianoRollHost { Document = () => document });
        var window = new Window { Width = 900, Height = 600, Content = view };
        if (variant is not null) window.RequestedThemeVariant = variant;
        window.Show();
        view.Show(document, 0, newVisit: true);
        window.UpdateLayout();
        return (view, window);
    }

    private static Point KeyPoint(PianoRollSurface surface, int pitch) =>
        new(surface.GridLeft / 2, surface.YOf(pitch) + surface.RowPx / 2);

    [AvaloniaFact]
    public void LeftClickAuditionsOnlyAndHoldSoundsWhileHeld()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var sounded = new System.Collections.Generic.List<int>();
        view.Surface.Audition = sounded.Add;
        try
        {
            view.Presenter.Tool = PianoRollTool.Select;
            Assert.True(view.Surface.Press(KeyPoint(view.Surface, 60), KeyModifiers.None));
            view.Surface.Release(KeyModifiers.None);
            Assert.Equal(new[] { 60 }, sounded.ToArray());
            Assert.Empty(view.Presenter.Selected);   // a left click never selects
            Assert.True(view.Surface.Press(KeyPoint(view.Surface, 60), KeyModifiers.None));
            view.Surface.DragTo(KeyPoint(view.Surface, 64));
            view.Surface.Release(KeyModifiers.None);
            Assert.Equal(new[] { 60, 60, 64 }, sounded.ToArray());
            Assert.Empty(view.Presenter.Selected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RightClickSelectsThePitchAndCtrlTogglesIt()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        var sounded = new System.Collections.Generic.List<int>();
        view.Surface.Audition = sounded.Add;
        try
        {
            Assert.True(view.Surface.PressRight(KeyPoint(view.Surface, 60), KeyModifiers.None));
            Assert.True(view.Surface.SuppressMenu);   // no context menu on the keys
            view.Surface.Release(KeyModifiers.None);
            Assert.Equal(new[] { 0 }, view.Presenter.Selected.ToArray());
            Assert.Empty(sounded);   // selecting never auditions
            Assert.True(view.Surface.PressRight(KeyPoint(view.Surface, 60), KeyModifiers.Control));
            view.Surface.Release(KeyModifiers.Control);
            Assert.Empty(view.Presenter.Selected);   // Ctrl toggles the pitch back out
            Assert.True(view.Surface.PressRight(KeyPoint(view.Surface, 64), KeyModifiers.Control));
            view.Surface.Release(KeyModifiers.Control);
            Assert.Equal(new[] { 1 }, view.Presenter.Selected.ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ShiftRightClickAddsTheAnchorRange()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            view.Surface.PressRight(KeyPoint(view.Surface, 60), KeyModifiers.None);
            view.Surface.Release(KeyModifiers.None);
            Assert.Equal(60, view.Surface.KeyAnchor);
            view.Surface.PressRight(KeyPoint(view.Surface, 64), KeyModifiers.Shift);
            view.Surface.Release(KeyModifiers.Shift);
            Assert.Equal(new[] { 0, 1 }, view.Presenter.Selected.OrderBy(i => i).ToArray());
            Assert.Equal(64, view.Surface.KeyAnchor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ShiftRightClickWithNoAnchorBehavesAsAPlainRightClick()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            Assert.Equal(-1, view.Surface.KeyAnchor);
            view.Surface.PressRight(KeyPoint(view.Surface, 64), KeyModifiers.Shift);
            view.Surface.Release(KeyModifiers.Shift);
            Assert.Equal(new[] { 1 }, view.Presenter.Selected.ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RightClickOnTheKeysNeverOpensAMenu()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            view.Surface.PrepareMenu(KeyPoint(view.Surface, 60));
            Assert.True(view.Surface.MenuInKeys);
            Assert.Empty(view.MenuRows());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HoverStateFollowsModifierAndSelection()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            Assert.Empty(view.Surface.SelectedPitches);
            view.Surface.HoverAt(KeyPoint(view.Surface, 60), KeyModifiers.None);
            Assert.Empty(view.Surface.PreviewPitches);   // no modifier: no preview
            view.Surface.HoverAt(KeyPoint(view.Surface, 60), KeyModifiers.Control);
            Assert.Equal(new[] { 60 }, view.Surface.PreviewPitches.ToArray());   // Ctrl: this click would toggle this pitch
            view.Surface.HoverAt(KeyPoint(view.Surface, 64), KeyModifiers.Shift);
            Assert.Equal(new[] { 64 }, view.Surface.PreviewPitches.ToArray());   // Shift with no anchor: this key alone
            view.Surface.PressRight(KeyPoint(view.Surface, 60), KeyModifiers.None);
            view.Surface.Release(KeyModifiers.None);
            Assert.Equal(new[] { 60 }, view.Surface.SelectedPitches.ToArray());   // lit keys equal the selected pitches
            view.Surface.HoverAt(KeyPoint(view.Surface, 64), KeyModifiers.Shift);
            Assert.Equal(new[] { 60, 61, 62, 63, 64 }, view.Surface.PreviewPitches.OrderBy(p => p).ToArray());   // the anchor..hover range
            view.Presenter.ClearSelection();
            view.RefreshInfo();
            Assert.Empty(view.Surface.SelectedPitches);   // nothing selected: nothing lit
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PianoAndNamesAreARadioPairAboveTheKeysPersistedPerTrack()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var piano = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PianoRollKeyPiano");
            var names = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "PianoRollKeyNames");
            Assert.True(piano.IsChecked == true ^ names.IsChecked == true);   // exactly one on
            Assert.True(piano.IsChecked == true);   // the track never chose: piano keys
            var steps = document.Undo.UndoCount;
            view.SetNoteNames(true);
            Assert.True(names.IsChecked == true && piano.IsChecked != true);
            Assert.True(document.Project.Tracks[0].PianoRollNoteNames);   // the saved value migrates with the track
            Assert.Equal(steps + 1, document.Undo.UndoCount);
            Assert.True(view.Surface.NoteNames);
            view.SetNoteNames(false);
            Assert.True(piano.IsChecked == true && names.IsChecked != true);
            Assert.False(document.Project.Tracks[0].PianoRollNoteNames);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePairSitsInTheRulerCornerOnTheSharedToolbarGeometry()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            var header = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PianoRollKeyHeader");
            Assert.Equal(view.Surface.GridLeft, header.Width);
            Assert.Equal(PianoRollSurface.RulerSize, header.Height);
            var toggles = header.GetVisualDescendants().OfType<ToggleButton>().ToList();
            Assert.Equal(2, toggles.Count);
            foreach (var toggle in toggles)
            {
                Assert.Equal(PianoRollToolbarLayout.ControlHeight, toggle.Bounds.Height, 0.5);
                Assert.True(toggle.Focusable);   // keyboard-accessible
                Assert.NotNull(ToolTip.GetTip(toggle));
            }
            var centres = toggles.Select(t => t.TranslatePoint(new Point(t.Bounds.Width / 2, t.Bounds.Height / 2), window)!.Value.Y).ToList();
            Assert.True(centres.Max() - centres.Min() < 1.0, "the pair shares one vertical centre");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TOpensTheTransposePromptWithTheCountAndPreviewAsOneStep()
    {
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document);
        try
        {
            Assert.False(view.Surface.HandleKey(Key.T, KeyModifiers.None));   // nothing selected: T goes on to the window
            view.Presenter.Select(new[] { 0, 1 });
            PianoRollTransposeDialog? shown = null;
            view.TransposePrompt = model => { shown = model; return Task.FromResult<MoveNotesAnswer?>(new MoveNotesAnswer(1, false, false)); };
            var steps = document.Undo.UndoCount;
            Assert.True(view.Surface.HandleKey(Key.T, KeyModifiers.None));
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(shown);
            Assert.Equal("2 notes selected", shown!.CountText);
            Assert.Equal(new[] { 60, 64 }.Select(PianoRollPositionText.PitchName), shown.BeforeChips);
            Assert.Equal(steps + 1, document.Undo.UndoCount);   // one gesture: one undo step
            Assert.Equal(new[] { 61, 65 }, document.Project.Tracks[0].AudioClips[0].Notes!.Select(n => n.Pitch).OrderBy(p => p).ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LitKeysUseTheAccentTokenInBothThemes(bool light)
    {
        var appearance = new AppearanceSettings { ThemeMode = light ? "Light" : "Dark" };
        ThemeColours.ApplyPreset(appearance, light);
        appearance.Accent = "#DC2626";
        AvaloniaPalette.Publish(appearance);
        var document = PianoRollSongs.Midi();
        var (view, window) = Show(document, null, light ? ThemeVariant.Light : ThemeVariant.Dark);
        try
        {
            var lit = view.Surface.KeyLit();
            Assert.Equal(AvaloniaPalette.Get("Accent", light), lit);   // the Accent token path, never a fixed palette blue
            Assert.True(lit.R > lit.B, "the lit key follows the crimson accent");
            var fill = AvaloniaPalette.Get("PianoRollKeyWhite", light);
            var tint = PianoRollSurface.KeySelectedTint(lit, fill);   // the same token blend helper
            Assert.NotEqual(lit, tint);
            Assert.NotEqual(fill, tint);
        }
        finally { window.Close(); AvaloniaPalette.Publish(new AppearanceSettings()); }
    }

    [AvaloniaFact]
    public void SKeyTogglesSnapLikeTheMagnetAndOtherModifiersDoNothing()
    {
        var document = PianoRollSongs.Midi();
        var snap = new SnapSettings { Enabled = true };
        var saves = 0;
        var (view, window) = Show(document, new PianoRollHost { Document = () => document, Snap = () => snap, ToggleSnap = () => { snap.Enabled = !snap.Enabled; saves++; } });
        try
        {
            var magnet = view.GetVisualDescendants().OfType<ToggleButton>().First(b => TabForge.Avalonia.Platform.Controls.HotkeyTip.GetCommandId(b) == "PianoRoll.Snap");
            Assert.EndsWith("(Alt+S)", (string)ToolTip.GetTip(magnet)!);
            Assert.True(view.Surface.HandleKey(Key.S, KeyModifiers.Alt));
            Assert.False(snap.Enabled);
            Assert.False(magnet.IsChecked);
            Assert.True(view.Surface.HandleKey(Key.S, KeyModifiers.Alt));
            Assert.True(snap.Enabled);
            Assert.True(magnet.IsChecked);
            Assert.Equal(2, saves);
            Assert.False(view.Surface.HandleKey(Key.S, KeyModifiers.Control));
            Assert.False(view.Surface.HandleKey(Key.S, KeyModifiers.Shift));
            Assert.False(view.Surface.HandleKey(Key.S, KeyModifiers.Alt | KeyModifiers.Shift));
            Assert.Equal(2, saves);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ThePianoRollSnapKeyIsRegisteredOnceForTheShortcutsList()
    {
        Assert.Equal("Alt+S", HotkeyCatalog.ById("PianoRoll.Snap")!.DefaultGesture);
        Assert.Equal(1, HotkeyCatalog.All.Count(a => a.Id == "PianoRoll.Snap"));
    }

    [Fact]
    public void TheTransposeDialogIsRegisteredForTheShortcutsList()
    {
        Assert.NotNull(HotkeyCatalog.ById("PianoRoll.TransposeDialog"));
    }
}
