using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the portable shared-memory backend: a file-backed memory-mapped file in a private per-user folder, opened by the other process by the same name.
// The creating side removes the file when it closes (FileOptions.DeleteOnClose); on Unix the folder is 0700 and the file 0600.
// Does not own: the layout inside the block (SharedBlock), or the Windows page-file mapping (NamedSharedMemory.Windows), which stays the default on Windows.
// Tests: PortableIpcTests (headless round trip, run on Windows too).
internal static class PortableSharedMemory
{
    private const string FolderName = "tabforge-shm";

    /// <summary>The private folder holding the files: under the runtime folder (Linux XDG_RUNTIME_DIR) or the temp folder.</summary>
    public static string Folder(string? runtimeDir = null)
    {
        var baseDir = runtimeDir ?? (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Path.IsPathRooted(run) && Directory.Exists(run) ? run : Path.GetTempPath());
        return Path.Combine(baseDir, FolderName);
    }

    /// <summary>A short, safe file name for a mapping name (no separators, no dots from the caller's text).</summary>
    public static string FileNameFor(string name) =>
        "tf-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..24].ToLowerInvariant();

    /// <summary>Creates a new mapping of <paramref name="capacity"/> bytes; fails when the name exists. The file goes away when the returned map is disposed.</summary>
    public static MemoryMappedFile Create(string name, long capacity, string? folder = null)
    {
        var path = Path.Combine(PrivateFolder(folder ?? Folder()), FileNameFor(name));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.DeleteOnClose, BufferSize = 1,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        try
        {
            stream.SetLength(capacity);
            return MemoryMappedFile.CreateFromFile(stream, null, capacity, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>Opens the mapping the other process created.</summary>
    public static MemoryMappedFile Open(string name, string? folder = null)
    {
        var path = Path.Combine(PrivateFolder(folder ?? Folder(), create: false), FileNameFor(name));
        var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite | FileShare.Delete, BufferSize = 1 });
        try { return MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false); }
        catch { stream.Dispose(); throw; }
    }

    // The folder is made owner-only on Unix; an existing folder that others can enter is refused (the files hold live engine state).
    private static string PrivateFolder(string folder, bool create = true)
    {
        if (OperatingSystem.IsWindows())
        {
            if (create) Directory.CreateDirectory(folder);
            return folder;
        }
        if (create) Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if ((File.GetUnixFileMode(folder) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("The shared memory folder is open to other users.");
        return folder;
    }
}
