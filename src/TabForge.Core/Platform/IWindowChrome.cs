namespace TabForge.Platform;

// Owns: the window-chrome actions a custom title bar asks of its window (move, resize, caption buttons) and the window state.
// Does not own: hit testing the chrome (Core/Shell/ChromeHitRules) or any toolkit window (adapters in src/TabForge.Avalonia/Platform/).
// Tests: no dedicated test class.

/// <summary>The window edge or corner a resize drag starts from.</summary>
public enum ResizeEdge { Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>The state a window is in.</summary>
public enum WindowChromeState { Normal, Maximised, Minimised, FullScreen }

/// <summary>What a custom title bar can ask of its window.</summary>
public interface IWindowChrome
{
    WindowChromeState State { get; }

    /// <summary>Raised after <see cref="State"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Starts the system move drag (call from a pointer press on the caption).</summary>
    void BeginMove();

    /// <summary>Starts the system resize drag from <paramref name="edge"/>.</summary>
    void BeginResize(ResizeEdge edge);

    void Minimize();

    /// <summary>Maximises a normal window and restores a maximised one.</summary>
    void ToggleMaximise();

    void Close();
}
