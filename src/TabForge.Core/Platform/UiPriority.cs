namespace TabForge.Platform;

/// <summary>
/// The order in which queued UI work runs, lowest first. Values keep the old dispatcher priority numbers so an adapter maps them one to one.
/// Owns: the priority names. Does not own: queueing (see <see cref="IUiThread"/>).
/// </summary>
public enum UiPriority
{
    SystemIdle = 1,
    ApplicationIdle = 2,
    ContextIdle = 3,
    Background = 4,
    Input = 5,
    Loaded = 6,
    Render = 7,
    DataBind = 8,
    Normal = 9,
    Send = 10,
}
