namespace TabForge.Services;

// Owns: one folder a user approved a project to read linked audio from (network or removable media).
// Does not own: the approval decisions (MediaContext) or the saved list (AudioSettings.ApprovedMedia).
// Tests: no dedicated test class.
/// <summary>A folder the user allowed a project to read linked audio from (network or removable).</summary>
public sealed class MediaApproval
{
    /// <summary>The project's file path (empty for a song that was never saved).</summary>
    public string Project { get; set; } = "";
    public string Folder { get; set; } = "";
}
