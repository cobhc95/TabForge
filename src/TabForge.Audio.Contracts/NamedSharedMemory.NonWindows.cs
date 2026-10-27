using System.IO.MemoryMappedFiles;

namespace TabForge.Audio.Contracts;

/// <summary>
/// Named shared memory of the net10.0 build (macOS, Linux): the file-backed mapping of <see cref="PortableSharedMemory"/>, found by name.
/// Owns: choosing that backend for this target. Does not own: the layout inside the block (<see cref="SharedBlock"/>).
/// Tests: PortableIpcTests (the backend itself, also run on Windows).
/// Compiled into net10.0 only; the Windows build uses NamedSharedMemory.Windows.cs.
/// </summary>
internal static class NamedSharedMemory
{
    /// <summary>Creates a new mapping; fails if the name already exists. The file is removed when the map is disposed.</summary>
    public static MemoryMappedFile Create(string name, long capacity) => PortableSharedMemory.Create(name, capacity);

    /// <summary>Opens the existing mapping created by the other process.</summary>
    public static MemoryMappedFile Open(string name) => PortableSharedMemory.Open(name);
}
