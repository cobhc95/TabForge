namespace TabForge.Controllers;

/// <summary>One notice row above a window's status bar: shown with its text, or hidden again. Hiding never removes the row.</summary>
internal interface IStatusNotice
{
    void Show(string text, string? toolTip = null);
    void Hide();
}

/// <summary>Creates the notice rows of one window. A row joins the window the first time it is shown; each new row sits directly above the status bar.</summary>
internal interface IStatusNotices
{
    /// <summary>A row with one button. <paramref name="automationName"/> names the row for screen readers (null: unnamed).</summary>
    IStatusNotice Create(string? automationName, string buttonText, Action onButton);
}
