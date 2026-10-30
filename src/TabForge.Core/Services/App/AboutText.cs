namespace TabForge.Services;

// Owns: the text of the About TabForge dialog (version line, product description, disclaimer), shared by the app.
// Does not own: the dialog itself (ThemedConfirmDialog, the Avalonia message box), its buttons, or the version (AppInfo).
// Tests: TabForge.Avalonia.Tests.HelpMenuTests.
/// <summary>The About TabForge dialog text.</summary>
public static class AboutText
{
    /// <summary>The About text: the version line, the one-line product description, the disclaimer and the feature summary.</summary>
    public static string Body =>
        $"{AppInfo.VersionLine}\nTablature and notation workstation\n\nTabForge is an independent project, not affiliated with any other software maker.\n\nTabForge is under active development: keep backups of your files.\n\nMultitrack TAB + notation, durations/dots/triplets, rests/ties/fermata, repeats/endings/sections, chord/text/lyrics/markers, mixer with MIDI vol/pan/chorus/reverb, speed-trainer loop, metronome, fretboard, chord/scale finders, tuning reference, .gp3/.gp4/.gp5/.gpx/.gp import, .gp save, MIDI + ASCII export, VST2/VST3 plug-ins in a separate audio engine.\n\n{SoundBanksCredit}";

    /// <summary>The Sound banks credit line of About: each available bank's author and licence (downloads are optional; nothing is bundled).</summary>
    public static string SoundBanksCredit =>
        "Sound banks (optional downloads; nothing is bundled and TabForge never alters them):\n" +
        string.Join("\n", SoundBankCatalog.All.Select(bank => $"{bank.Name} — {bank.Author} — {bank.LicenceName}"));
}
