using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Configure button of an ASIO driver (the audio engine shows the driver's panel; the button is off while the engine plays through another driver),
//       and redrawing the page when a list the engine was still being asked for ("Checking…") is answered.
// Does not own: asking the engine (AudioEngineClient.AsioProbe) or the rows (SettingsCatalog.Audio).
// Tests: TabForge.Avalonia.Tests.PreferencesDeadInstructionTests, TabForge.Avalonia.Tests.AsioProbeTests.
public sealed partial class PreferencesWindow
{
    private const string OtherDriverTip = "The audio engine is playing through another ASIO driver. Choose this driver as the output and apply it, then configure it.";

    /// <summary>The Configure… button: off without an engine to ask, and off (with the reason as tooltip) while the engine plays through another ASIO driver.</summary>
    private Button ConfigureAsioButton(SettingDescriptor row)
    {
        var button = RowButton(row, "Configure…", _actions?.ConfigureAsio is not null, () => _ = ConfigureAsioAsync());
        if (!EngineHoldsOtherDriver()) return button;
        button.IsEnabled = false;
        ToolTip.SetTip(button, OtherDriverTip);
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private bool EngineHoldsOtherDriver() =>
        _actions?.RunningAsioDevice?.Invoke() is { Length: > 0 } running && Working.Plugins.Device.Length > 0
        && !string.Equals(running, Working.Plugins.Device, StringComparison.OrdinalIgnoreCase);

    /// <summary>Has the engine show the control panel of the ASIO driver now chosen as the output device; why it could not is shown in the status line.</summary>
    private async Task ConfigureAsioAsync()
    {
        if (_actions?.ConfigureAsio is not { } show) return;
        try
        {
            var error = EngineHoldsOtherDriver() ? OtherDriverTip : await show(Working.Plugins.Device);
            if (error.Length > 0) _status.Text = "The ASIO driver's control panel could not be opened: " + error;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _status.Text = "The ASIO driver's control panel could not be opened: " + ex.Message;
        }
    }

    /// <summary>A row whose choices are still "Checking…" is drawn again when the engine has answered.</summary>
    private void RedrawWhenAnswered(System.Collections.Generic.IReadOnlyList<string> choices)
    {
        if (choices is PendingChoices { IsPending: true } pending) _ = RedrawAsync(pending);
    }

    private async Task RedrawAsync(PendingChoices pending)
    {
        try { await pending.Settled; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, $"settings list not answered: {ex.Message}"); }
        Rebuild();
    }
}
