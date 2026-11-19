namespace TabForge.Avalonia.Views;

/// <summary>Runs the commands a right-click menu row names, by command id.</summary>
public interface IMenuCommandRunner
{
    /// <summary>True when the window has a handler for this command id; a row with no handler renders disabled.</summary>
    bool Handles(string commandId);

    /// <summary>Runs the command; an id with no handler throws, because the menu never enables one.</summary>
    void Run(string commandId, string? argument = null);
}
