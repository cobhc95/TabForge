// Owns: the key-name and time-signature denominator choices that the project settings dialog and the standard dialogs (GpDialogs) offer, as read-only lists.
// Does not own: the key and time-signature rules (ProjectSettingsPresenter, StandardDialogsPresenter) or the combo boxes that show the choices.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>The choices shared by the signature combo boxes.</summary>
public static class SignatureChoices
{
    /// <summary>Key names ordered from seven flats (index 0, Cb) to seven sharps (index 14, C#); index 7 is C.</summary>
    public static readonly IReadOnlyList<string> KeyNames = Array.AsReadOnly(new[]
    {
        "Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#",
    });

    /// <summary>The time-signature beat values the denominator combo box offers.</summary>
    public static readonly IReadOnlyList<int> Denominators = Array.AsReadOnly(new[] { 1, 2, 4, 8, 16, 32 });
}
