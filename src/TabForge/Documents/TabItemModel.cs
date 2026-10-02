using System.ComponentModel;

namespace TabForge.Documents;

/// <summary>
/// View model for one tab in the title-bar strip. Kept out of the window so the tab bar control and
/// the window can both bind to it.
/// </summary>
public sealed class TabItemModel : INotifyPropertyChanged
{
    public DocumentSession Session { get; init; } = null!;
    public int Index { get; set; }
    public bool IsActive { get; set; }
    public bool IsPlaying { get; set; }
    public bool ShowPlayingIndicator { get; set; } = true;

    // What the tab last showed (the bar compares these with the document to know what to announce).
    internal string? ShownTitle { get; set; }
    internal string? ShownTooltip { get; set; }
    internal bool ShownDirty { get; set; }

    public string Title => Session.DisplayName;
    public string Tooltip => Session.Tooltip;
    public System.Windows.Visibility DirtyVisibility =>
        Session.Project.IsDirty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility PlayingVisibility =>
        IsPlaying && ShowPlayingIndicator ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    // Close-button policy and shape, resolved from the tab settings when the strip is rebuilt.
    public bool ShowCloseOnHover { get; set; } = true;
    public bool CloseAlways { get; set; }
    public bool CloseNever { get; set; }
    public System.Windows.CornerRadius TabCornerRadius { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaiseAll()
    {
        ShownTitle = Session.DisplayName; ShownTooltip = Session.Tooltip; ShownDirty = Session.Project.IsDirty;
        Raise(nameof(Title));
        Raise(nameof(Tooltip));
        Raise(nameof(DirtyVisibility));
        Raise(nameof(PlayingVisibility));
        Raise(nameof(IsActive));
        Raise(nameof(IsPlaying));
        Raise(nameof(ShowPlayingIndicator));
        Raise(nameof(ShowCloseOnHover));
        Raise(nameof(CloseAlways));
        Raise(nameof(CloseNever));
        Raise(nameof(TabCornerRadius));
    }

    public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
