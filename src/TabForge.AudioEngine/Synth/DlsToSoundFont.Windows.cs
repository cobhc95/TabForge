namespace TabForge.AudioEngine.Synth;

// Owns: where the Windows GM/GS DLS bank lives (the one Windows folder lookup of the synth).
public static partial class DlsToSoundFont
{
    /// <summary>Path of the Windows GM/GS DLS bank.</summary>
    public static string WindowsBankPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "gm.dls");
}
