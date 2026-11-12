using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the portable shared-memory backend: a file-backed memory-mapped file in a private per-user folder, opened by the other process by the same name.
// The creating side removes the file when it closes (FileOptions.DeleteOnClose); on Unix the folder is 0700 and the file 0600.
// Does not own: the layout inside the block (SharedBlock), or the Windows page-file mapping (NamedSharedMemory.Windows), which stays the default on Windows.
// Tests: CrossPlatformStage2Tests.
internal static class PortableSharedMemory
{
    private const string FolderName = "tabforge-shm";

    /// <summary>
    /// The private folder holding the files: under the runtime folder (Linux XDG_RUNTIME_DIR) or the temp folder. On Unix the
    /// name carries the effective uid, so one user's 0700 folder in a shared /tmp is never reused by another user.
    /// </summary>
    public static string Folder(string? runtimeDir = null)
    {
        var baseDir = runtimeDir ?? (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Path.IsPathRooted(run) && Directory.Exists(run) ? run : Path.GetTempPath());
        var name = OperatingSystem.IsWindows() ? FolderName : FolderName + "-" + EffectiveUid();
        return Path.Combine(baseDir, name);
    }

    private static string EffectiveUid()
    {
        try { return LibcUid.GetEuid().ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return "uid"; }
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
            Reserve(stream, capacity);
            return MemoryMappedFile.CreateFromFile(stream, null, capacity, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        }
        catch { stream.Dispose(); throw; }
    }

    // SetLength alone only makes a sparse file on some file systems (tmpfs, NTFS): a full disk then accepts the block
    // and the first mapped write faults with SIGBUS/AccessViolation instead of a readable out-of-space error. Touch every
    // page so the space is really allocated while the caller can still turn a failure into a message.
    private static void Reserve(FileStream stream, long capacity)
    {
        stream.SetLength(capacity);
        var zero = new byte[(int)Math.Min(64 * 1024, Math.Max(1, capacity))];
        for (var remaining = capacity; remaining > 0;)
        {
            var count = (int)Math.Min(zero.Length, remaining);
            stream.Write(zero, 0, count);
            remaining -= count;
        }
        stream.Flush();
        stream.Seek(0, SeekOrigin.Begin);
    }

    /// <summary>Opens the mapping the other process created.</summary>
    public static MemoryMappedFile Open(string name, string? folder = null)
    {
        var path = Path.Combine(PrivateFolder(folder ?? Folder(), create: false), FileNameFor(name));
        var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite | FileShare.Delete, BufferSize = 1 });
        try { return MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false); }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>
    /// Creates a mapping at an explicit path and keeps the file after the map is disposed: the Wine bridge passes this path to
    /// the helper, so it must be a regular file that is never unlinked while mapped. The caller deletes it on dispose.
    /// </summary>
    public static MemoryMappedFile CreateAt(string path, long capacity)
    {
        var full = Path.GetFullPath(path);
        PrivateFolder(Path.GetDirectoryName(full) ?? throw new ArgumentException("The shared memory path has no folder.", nameof(path)));
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.None, BufferSize = 1,
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(full, options);
        try
        {
            Reserve(stream, capacity);
            return MemoryMappedFile.CreateFromFile(stream, null, capacity, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false);
        }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>Opens an explicit-path mapping the other process created (the helper side or a test).</summary>
    public static MemoryMappedFile OpenAt(string path)
    {
        var full = Path.GetFullPath(path);
        PrivateFolder(Path.GetDirectoryName(full) ?? throw new ArgumentException("The shared memory path has no folder.", nameof(path)), create: false);
        var stream = new FileStream(full, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite | FileShare.Delete, BufferSize = 1 });
        try { return MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, false); }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>The runtime folder shared-memory files live in (the same UID-keyed folder as <see cref="PortableSharedMemory"/>).</summary>
    public static string RuntimeFolder(string? runtimeDir = null) => Folder(runtimeDir);

    // The folder is made owner-only on Unix; an existing folder that others can enter, that is a link, or that this user
    // cannot create a file in (a 0700 folder pre-created by another user) is refused: the files hold live engine state.
    private static string PrivateFolder(string folder, bool create = true)
    {
        if (create)
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(folder);
            else Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        if (!Directory.Exists(folder)) throw new IOException("The shared memory folder does not exist.");
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || new DirectoryInfo(folder).LinkTarget is not null)
            throw new IOException("The shared memory folder is a link.");
        if (OperatingSystem.IsWindows()) return folder;
        if ((File.GetUnixFileMode(folder) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new IOException("The shared memory folder is open to other users.");
        // Ownership probe: a folder that exists with 0700 but belongs to another user lets this process create nothing.
        var probe = Path.Combine(folder, ".tf-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (new FileStream(probe, options)) { }
        }
        catch (UnauthorizedAccessException)
        {
            throw new IOException("The shared memory folder is not owned by this user.");
        }
        finally { try { File.Delete(probe); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
        return folder;
    }
}

/// <summary>The one runtime folder the shared-memory files (engine block, Wine bridge block) live in.</summary>
public static class SharedMemoryPaths
{
    /// <summary>Under the runtime folder (XDG_RUNTIME_DIR) or the temp folder; UID-keyed on Unix.</summary>
    public static string Folder(string? runtimeDir = null) => PortableSharedMemory.RuntimeFolder(runtimeDir);
}
