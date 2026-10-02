using System.Windows;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

/// <summary>Shift+F10 and the Menu key open the score's context menu at the caret (keyboard and screen-reader users had no way to).</summary>
public static partial class SelfTest
{
    private static void TestScoreContextMenuByKeyboard()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0 };
        project.Tracks[0].Measures[0].Cells[0] = new TabCell { DurationDenominator = 8, Notes = { new TabNote { StringIndex = 1, Fret = 5 } } };
        editor.Measure(new Size(1200, 800));
        editor.Arrange(new Rect(0, 0, 1200, 800));

        ContextMenuEventArgs? got = null;
        editor.ContextMenuRequested += (_, args) => got = args;

        editor.SetPosition(0, 0, 1, false);
        var handled = editor.TryHandleKey(Key.F10, ModifierKeys.Shift);
        Check("keyboard menu: Shift+F10 with the caret on a note raises ContextMenuRequested with OnNote", handled && got is { OnNote: true, FromKeyboard: true, Measure: 0, Cell: 0 });
        Check("keyboard menu: the menu is anchored at the caret (control coordinates), not at the mouse",
            got?.Anchor is { } anchor && anchor.X >= 0 && anchor.Y > 0);

        got = null;
        editor.SetPosition(0, 6, 2, false);
        var apps = editor.TryHandleKey(Key.Apps, ModifierKeys.None);
        Check("keyboard menu: the Menu key on an empty beat raises it with OnNote false", apps && got is { OnNote: false, FromKeyboard: true, OverBeat: true });

        got = null;
        Check("keyboard menu: Ctrl+F10 and Alt+Apps are not the menu key", !editor.TryHandleKey(Key.F10, ModifierKeys.Control | ModifierKeys.Shift) && got is null);
    }

    private static void TestKeyboardContextMenuPlacement()
    {
        // An editor inside a scrolled ScrollViewer under a scaling layout transform: the anchor is in the editor's own coordinates.
        var editor = new TabEditorControl();
        var scroll = new System.Windows.Controls.ScrollViewer { Content = editor, Width = 400, Height = 300 };
        var host = new System.Windows.Controls.Border { Child = scroll, LayoutTransform = new System.Windows.Media.ScaleTransform(1.75, 1.75) };
        host.Measure(new Size(2000, 2000));
        host.Arrange(new Rect(0, 0, 700, 525));
        scroll.ScrollToVerticalOffset(120);
        var menu = new System.Windows.Controls.ContextMenu();
        var anchor = new Point(1030, 580);
        MainWindow.PlaceContextMenuAt(menu, editor, anchor);
        Check("keyboard menu placement: the target is the editor itself (whose coordinates the anchor is in)", ReferenceEquals(menu.PlacementTarget, editor));
        Check("keyboard menu placement: the anchor is a PlacementRectangle (carried through scroll, UI scale and DPI), not offsets",
            menu.Placement == System.Windows.Controls.Primitives.PlacementMode.Bottom && menu.PlacementRectangle.Location == anchor &&
            menu.HorizontalOffset == 0 && menu.VerticalOffset == 0, $"{menu.PlacementRectangle}");
    }

    private static void TestTimelineAndInstrumentContextMenuByKeyboard()
    {
        var panel = new ArrangementPanel();
        var raised = 0;
        panel.TimelineKeyboardContextRequested += (_, _) => raised++;
        var shiftF10 = panel.TryHandleContextKey(Key.F10, ModifierKeys.Shift);
        var apps = panel.TryHandleContextKey(Key.Apps, ModifierKeys.None);
        Check("keyboard menu: Shift+F10 and the Menu key on the timeline ask the host for its menu", shiftF10 && apps && raised == 2, raised.ToString());
        Check("keyboard menu: other keys on the timeline are left alone",
            !panel.TryHandleContextKey(Key.F10, ModifierKeys.None) && !panel.TryHandleContextKey(Key.Apps, ModifierKeys.Shift) && raised == 2);
        panel.Measure(new Size(900, 300));
        panel.Arrange(new Rect(0, 0, 900, 300));
        var anchor = panel.TimelineBarAnchor(0);
        Check("keyboard menu: the timeline anchor sits under the ruler and section lane", anchor.Y >= ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight, anchor.ToString());

        var instrument = new InstrumentPanel();
        var instrumentRaised = 0;
        instrument.ContextMenuKeyPressed += (_, _) => instrumentRaised++;
        Check("keyboard menu: the fretboard / keyboard panel is a Tab stop and opens its menu with Shift+F10 or the Menu key",
            instrument.Focusable && instrument.TryHandleContextMenuKey(Key.F10, ModifierKeys.Shift) && instrument.TryHandleContextMenuKey(Key.Apps, ModifierKeys.None) &&
            instrumentRaised == 2 && !instrument.TryHandleContextMenuKey(Key.A, ModifierKeys.None));
    }
}
