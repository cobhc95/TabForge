using System.IO.MemoryMappedFiles;

namespace TabForge.Audio.Contracts;

/// <summary>
/// Named shared memory of the net8.0 build: no mapping exists on this target yet, so creating or opening one throws
/// PlatformNotSupportedException. Owns: that refusal. Does not own: the layout inside the block (<see cref="SharedBlock"/>).
/// Tests: none on this target; the net8.0 build is compile-checked only.
/// Compiled into net8.0 only; the Windows build uses NamedSharedMemory.Windows.cs.
/// </summary>
internal static class NamedSharedMemory
{
    private const string NotSupportedMessage =
        "Named shared memory is not implemented on this platform yet; only the net8.0-windows build of TabForge.Audio.Contracts can create or open it.";

    /// <summary>Always throws on this target.</summary>
    public static MemoryMappedFile Create(string name, long capacity) =>
        throw new PlatformNotSupportedException(NotSupportedMessage);

    /// <summary>Always throws on this target.</summary>
    public static MemoryMappedFile Open(string name) =>
        throw new PlatformNotSupportedException(NotSupportedMessage);
}
