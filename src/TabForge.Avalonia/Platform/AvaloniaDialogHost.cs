using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Every modal dialog of the Avalonia app opens through here, the counterpart of the WPF <c>Views/DialogHost</c>.
/// Esc and Enter come from the dialog's own buttons (<c>IsCancel</c> and <c>IsDefault</c>, which Avalonia binds to the window's keys).
/// Owns: showing a built dialog modally over its owner and the capture seam. Does not own: building dialogs or reading their answers.
/// Tests: AvaloniaDialogTests.
/// </summary>
public sealed class AvaloniaDialogHost
{
    /// <summary>When set, receives the fully built dialog instead of it being shown, and returns its result (headless tests and the capture tour).</summary>
    public Func<Window, object?>? Capture { get; set; }

    /// <summary>Shows <paramref name="dialog"/> modally over <paramref name="owner"/>; the result is what the dialog closed with.</summary>
    public Task<T?> ShowModal<T>(Window dialog, Window owner)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (Capture is { } capture) return Task.FromResult(capture(dialog) is T result ? result : default);
        ArgumentNullException.ThrowIfNull(owner);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return dialog.ShowDialog<T?>(owner);
    }
}
