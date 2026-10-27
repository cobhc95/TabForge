using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the message box answers for Enter, Esc and each button, the dialog host's capture seam and the file filter parsing.
/// Does not own: the presenter's rules (MessageBoxPresenter); the expected answers come from it.
/// Tests: this class.
/// </summary>
public class AvaloniaDialogTests
{
    private static AvaloniaMessageBox Shown(MessageBoxButtons buttons, MessageBoxAnswer requested = MessageBoxAnswer.None)
    {
        var box = new AvaloniaMessageBox("Text", "Caption", buttons, MessageBoxIcon.Question, requested);
        box.Show();
        return box;
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.OK, MessageBoxAnswer.None, MessageBoxAnswer.OK)]
    [InlineData(MessageBoxButtons.YesNo, MessageBoxAnswer.None, MessageBoxAnswer.Yes)]
    [InlineData(MessageBoxButtons.YesNoCancel, MessageBoxAnswer.No, MessageBoxAnswer.No)]
    public void EnterPressesTheDefault(MessageBoxButtons buttons, MessageBoxAnswer requested, MessageBoxAnswer expected)
    {
        var box = Shown(buttons, requested);
        box.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(expected, box.Answer);
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.OK, MessageBoxAnswer.OK)]
    [InlineData(MessageBoxButtons.OKCancel, MessageBoxAnswer.Cancel)]
    [InlineData(MessageBoxButtons.YesNoCancel, MessageBoxAnswer.Cancel)]
    public void EscPressesTheCancelButton(MessageBoxButtons buttons, MessageBoxAnswer expected)
    {
        var box = Shown(buttons);
        box.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Equal(expected, box.Answer);
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.OK)]
    [InlineData(MessageBoxButtons.OKCancel)]
    [InlineData(MessageBoxButtons.YesNo)]
    [InlineData(MessageBoxButtons.YesNoCancel)]
    public void EachButtonAnswersItsOwnAnswer(MessageBoxButtons buttons)
    {
        foreach (var (_, answer) in MessageBoxPresenter.Buttons(buttons))
        {
            var box = Shown(buttons);
            var button = box.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, answer));
            button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(answer, box.Answer);
        }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task CaptureSeesTheDialogAndSuppliesTheResult()
    {
        Window? seen = null;
        var host = new AvaloniaDialogHost { Capture = dialog => { seen = dialog; return MessageBoxAnswer.No; } };
        var answer = await AvaloniaMessageBox.Show(host, new Window(), "Save changes?", "TabForge", MessageBoxButtons.YesNoCancel);
        Assert.Equal(MessageBoxAnswer.No, answer);
        var box = Assert.IsType<AvaloniaMessageBox>(seen);
        Assert.Equal("TabForge", box.Title);
        Assert.Equal(3, box.GetLogicalDescendantsOfButtons());
    }

    [Fact]
    public void FilterTextBecomesFileTypes()
    {
        var types = AvaloniaFileDialogs.FileTypes("Scores|*.gp5;*.tf|All files|*.*")!;
        Assert.Equal(new[] { "Scores", "All files" }, types.Select(t => t.Name));
        Assert.Equal(new[] { "*.gp5", "*.tf" }, types[0].Patterns);
        Assert.Null(AvaloniaFileDialogs.FileTypes(null));
        Assert.Throws<System.ArgumentException>(() => AvaloniaFileDialogs.FileTypes("Scores"));
    }
}

internal static class DialogTestExtensions
{
    public static int GetLogicalDescendantsOfButtons(this Window window) =>
        global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(window).OfType<Button>().Count();
}
