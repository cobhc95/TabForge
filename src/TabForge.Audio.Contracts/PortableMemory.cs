using System.IO.MemoryMappedFiles;

namespace TabForge.Audio.Contracts;

// Owns: the public door to the portable file-backed shared memory for the engine's plug-in host block (the engine assembly cannot see PortableSharedMemory, which is internal).
// Does not own: the backend (PortableSharedMemory) or the layout of a block.
// Tests: CrossPlatformStage5Tests.
/// <summary>File-backed named shared memory that works on every OS; used for the plug-in host block where the Windows named mapping does not exist.</summary>
public static class PortableMemory
{
    /// <summary>Creates a new mapping; fails when the name exists. The file goes away when the returned map is disposed.</summary>
    public static MemoryMappedFile Create(string name, long capacity) => PortableSharedMemory.Create(name, capacity);

    /// <summary>Opens the mapping the other process created.</summary>
    public static MemoryMappedFile Open(string name) => PortableSharedMemory.Open(name);
}
