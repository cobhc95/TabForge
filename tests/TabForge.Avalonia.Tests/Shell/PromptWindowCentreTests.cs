using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the centring of a prompt on its owner: the "+ Add track" question opens over the main window, centred on it (not in the screen's corner).
/// Does not own: the prompt's layout or answers (PromptWindowTests) or the wording (PromptTextsPresenter).
/// Tests: TabForge.Avalonia.Tests.PromptWindowCentreTests.
/// </summary>
public class PromptWindowCentreTests
{
    [AvaloniaFact]
    public async Task AddTrackPromptOpensCentredOnItsOwnerWindow()
    {
        var owner = new Window { Width = 800, Height = 600 };
        owner.Show();
        var asked = PromptWindow.AskAsync(owner, PromptTextsPresenter.AddTrack());
        Dispatcher.UIThread.RunJobs();

        var prompt = Assert.Single(owner.OwnedWindows.OfType<PromptWindow>());
        Assert.Equal(WindowStartupLocation.CenterOwner, prompt.WindowStartupLocation);
        Assert.Same(owner, prompt.Owner);

        prompt.Close();
        await asked;
        owner.Close();
    }
}
