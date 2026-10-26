using System.Collections.Generic;

namespace TabForge.Platform;

/// <summary>
/// The running application as a port: shared resources, open windows, shutdown. The adapter is the Avalonia app services (<c>Ui.App</c>).
/// Owns: the application-access contract. Does not own: the windows themselves.
/// Tests: UiThreadTests.
/// </summary>
public interface IAppServices
{
    /// <summary>False when no application runs (some tool and test paths).</summary>
    bool IsRunning { get; }
    /// <summary>The application's UI thread, or null when no application runs.</summary>
    IUiThread? Thread { get; }
    /// <summary>A shared resource; throws when it is missing.</summary>
    object FindResource(object key);
    /// <summary>A shared resource, or null.</summary>
    object? TryFindResource(object key);
    /// <summary>Sets a shared resource.</summary>
    void SetResource(object key, object value);
    /// <summary>An application property (the application's property bag), or null.</summary>
    object? GetProperty(object key);
    /// <summary>Sets an application property.</summary>
    void SetProperty(object key, object? value);
    /// <summary>The open windows of type <typeparamref name="T"/>, in the application's order.</summary>
    IEnumerable<T> Windows<T>() where T : class;
    /// <summary>The main window, or null.</summary>
    object? MainWindow { get; }
    void Shutdown(int exitCode = 0);
}
