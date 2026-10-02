using TabForge.Audio.Contracts;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>D2, D4, D6 (Audit 2): one file-type list, one file-name sanitiser, EnsureSections never changes present values.</summary>
    private static void TestTrimMerges()
    {
        // D2: the lists agree; the installer comparison (TestInstallerAssociationParity) reads FileAssociations.Extensions, which is FileTypes.AllOpenable.
        var expected = FileTypes.GuitarPro.Append(FileTypes.Project).OrderBy(e => e).ToArray();
        Check("D2: FileTypes.AllOpenable is Guitar Pro types plus .tforge", FileTypes.AllOpenable.OrderBy(e => e).SequenceEqual(expected));
        Check("D2: FileAssociations and the importer use the FileTypes lists",
            ReferenceEquals(FileAssociations.Extensions, FileTypes.AllOpenable) && ReferenceEquals(GuitarProImporter.SupportedExtensions, FileTypes.GuitarPro));
        Check("D2: IsOpenable / IsGuitarPro are case-insensitive", FileTypes.IsOpenable(".GP5") && FileTypes.IsGuitarPro(".Gpx") && !FileTypes.IsGuitarPro(".tforge") && !FileTypes.IsOpenable(".mid"));

        // D4: shared sanitiser rules.
        Eq("D4: invalid characters become '_'", "a_b_c", SafeFileNames.SafeFileName("a:b*c", "x"));
        Eq("D4: empty falls back", "Track", SafeFileNames.SafeFileName("  ..  ", "Track"));
        Eq("D4: null falls back", "Untitled", SafeFileNames.SafeFileName(null, "Untitled"));
        Eq("D4: max length applied", new string('a', 64), SafeFileNames.SafeFileName(new string('a', 100), "x", 64));
        Eq("D4: reserved device name is prefixed", "_NUL", SafeFileNames.SafeFileName("NUL", "x"));
        Eq("D4: reserved name with extension is prefixed", "_com1.wav", SafeFileNames.AvoidReserved("com1.wav"));
        Eq("D4: RenderNaming keeps the same reserved rule", SafeFileNames.AvoidReserved("LPT9"), RenderNaming.AvoidReserved("LPT9"));
        Eq("D4: ordinary name is unchanged", "Lead Guitar", SafeFileNames.SafeFileName("Lead Guitar", "x"));

        // D6: EnsureSections fills only missing sections.
        var s = new AppSettings();
        s.Appearance.TrackTintPercent = 33;
        s.Hotkeys.Bindings["k"] = "Ctrl+K";
        s.Appearance.RecentColours = null!;
        s.Timeline = null!;
        SettingsMigration.EnsureSections(s);
        Check("D6: EnsureSections fills missing sections", s.Timeline is not null && s.Appearance.RecentColours is not null);
        Check("D6: EnsureSections keeps existing values", s.Appearance.TrackTintPercent == 33 && s.Hotkeys.Bindings.TryGetValue("k", out var k) && k == "Ctrl+K");
    }
}
