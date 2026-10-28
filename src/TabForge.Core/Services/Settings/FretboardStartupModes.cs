namespace TabForge.Services;

/// <summary>The values of "Show fretboard at startup" (<see cref="AppearanceSettings.FretboardAtStartup"/>).</summary>
public static class FretboardStartupModes
{
    public const string Remember = "Remember last";
    public const string Always = "Always";
    public const string Never = "Never";
    public static string[] All => new[] { Remember, Always, Never };
}
