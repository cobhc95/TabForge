namespace TabForge.Models;

/// <summary>
/// The one dynamics table (ppp..fff to MIDI velocity) shared by Guitar Pro import, playback and export.
/// Forte is 95, the value every note had before dynamics were imported, so songs written entirely
/// in forte are unchanged. The editor's dynamics tool should use <see cref="VelocityFor"/> too.
/// </summary>
public static class Dynamics
{
    public const int Forte = 95;

    public static readonly string[] Names = { "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff" };

    public static readonly int[] Velocities = { 16, 33, 49, 64, 80, Forte, 112, 127 };

    /// <summary>Velocity for a dynamic name (ppp..fff, case-insensitive); forte when unknown.</summary>
    public static int VelocityFor(string? name)
    {
        if (name is null) return Forte;
        var index = Array.FindIndex(Names, n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return index < 0 ? Forte : Velocities[index];
    }

    /// <summary>Velocity for an alphaTab DynamicValue name (PPP..FFF, plus the extended values).</summary>
    public static int VelocityForAlphaTab(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Forte;
        return name.ToUpperInvariant() switch
        {
            "PPPP" or "PPPPP" or "PPPPPP" => Velocities[0],
            "FFFF" or "FFFFF" or "FFFFFF" => Velocities[7],
            "SF" or "SFZ" or "SFFZ" or "FZ" or "RF" or "RFZ" => Velocities[6],
            "FP" or "SFP" or "SFPP" or "SFZP" => Velocities[5],
            "PF" => Velocities[5],
            "N" => Velocities[4],
            _ => VelocityFor(name)
        };
    }

    /// <summary>Index into <see cref="Names"/> of the dynamic nearest a velocity.</summary>
    public static int NearestIndex(int velocity)
    {
        var best = 0;
        for (var i = 1; i < Velocities.Length; i++)
            if (Math.Abs(Velocities[i] - velocity) < Math.Abs(Velocities[best] - velocity)) best = i;
        return best;
    }

    /// <summary>Nearest dynamic name (ppp..fff) for a velocity, used by export.</summary>
    public static string NearestName(int velocity) => Names[NearestIndex(velocity)];

    /// <summary>The velocity playback and export use for a note (always 1..127).</summary>
    public static int Clamp(int velocity) => Math.Clamp(velocity, 1, 127);
}
