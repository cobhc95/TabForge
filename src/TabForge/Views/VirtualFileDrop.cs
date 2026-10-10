using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// Virtual files in a drag (FileGroupDescriptorW / FileGroupDescriptor + FileContents): some plug-ins and archive views drag
/// files that exist only in memory. Each supported entry is written to a TabForge staging folder so the timeline can measure it
/// and the drop can keep a copy beside the song.
/// </summary>
public static class VirtualFileDrop
{
    public const string DescriptorW = "FileGroupDescriptorW";
    public const string DescriptorA = "FileGroupDescriptor";
    public const string Contents = "FileContents";
    public const long MaxFileBytes = 512L * 1024 * 1024;
    public const int MaxFiles = 64;

    private const int FdFileSize = 0x40;
    private const int FdAttributes = 0x04;
    private const int FileAttributeDirectory = 0x10;
    private const int DescriptorWSize = 592;   // FILEDESCRIPTORW
    private const int DescriptorASize = 332;   // FILEDESCRIPTORA

    public sealed record Entry(string Name, long? Size, bool Folder);

    /// <summary>The entries a FILEGROUPDESCRIPTOR(W) lists (bounded; malformed data gives fewer entries, never an exception).</summary>
    public static List<Entry> Parse(byte[] bytes, bool unicode)
    {
        var list = new List<Entry>();
        if (bytes.Length < 4) return list;
        var count = BitConverter.ToUInt32(bytes, 0);
        var size = unicode ? DescriptorWSize : DescriptorASize;
        for (var i = 0; i < count && i < 4096; i++)
        {
            var at = 4 + i * size;
            if (at + size > bytes.Length) break;
            var flags = BitConverter.ToUInt32(bytes, at);
            var attributes = BitConverter.ToUInt32(bytes, at + 36);
            long? length = (flags & FdFileSize) != 0
                ? ((long)BitConverter.ToUInt32(bytes, at + 64) << 32) | BitConverter.ToUInt32(bytes, at + 68)
                : null;
            var nameBytes = bytes.AsSpan(at + 72, size - 72);
            var name = unicode ? Encoding.Unicode.GetString(nameBytes) : Encoding.Default.GetString(nameBytes);
            var nul = name.IndexOf('\0');
            if (nul >= 0) name = name[..nul];
            list.Add(new Entry(name, length, (flags & FdAttributes) != 0 && (attributes & FileAttributeDirectory) != 0));
        }
        return list;
    }

    /// <summary>Builds a FILEGROUPDESCRIPTORW (tests, and the off-screen render).</summary>
    public static byte[] Build(IReadOnlyList<(string Name, long Size)> files)
    {
        var bytes = new byte[4 + files.Count * DescriptorWSize];
        BitConverter.GetBytes((uint)files.Count).CopyTo(bytes, 0);
        for (var i = 0; i < files.Count; i++)
        {
            var at = 4 + i * DescriptorWSize;
            BitConverter.GetBytes((uint)FdFileSize).CopyTo(bytes, at);
            BitConverter.GetBytes((uint)(files[i].Size >> 32)).CopyTo(bytes, at + 64);
            BitConverter.GetBytes((uint)(files[i].Size & 0xFFFFFFFF)).CopyTo(bytes, at + 68);
            var name = Encoding.Unicode.GetBytes(files[i].Name.Length > 259 ? files[i].Name[..259] : files[i].Name);
            name.CopyTo(bytes, at + 72);
        }
        return bytes;
    }

    public static List<Entry>? Descriptors(System.Windows.IDataObject data)
    {
        foreach (var (format, unicode) in new[] { (DescriptorW, true), (DescriptorA, false) })
        {
            if (!data.GetDataPresent(format)) continue;
            var bytes = ReadAll(data.GetData(format), 4 + 4096L * DescriptorWSize);
            if (bytes is not null) return Parse(bytes, unicode);
        }
        return null;
    }

    /// <summary>
    /// Writes each audio / MIDI entry into <paramref name="folder"/> and returns the files written. Folders, other file types and
    /// entries over the size limit are skipped (<paramref name="unsupported"/> counts them). <paramref name="contentsAt"/> replaces the
    /// FileContents reader (tests); by default index 0 comes from the managed data object, the rest through COM with lindex.
    /// </summary>
    public static List<string> Materialise(System.Windows.IDataObject data, string folder, Func<int, Stream?>? contentsAt, out int unsupported)
        => Materialise(data, folder, contentsAt, out unsupported, out _);

    /// <summary>As above; <paramref name="unread"/> lists the supported entries whose contents could not be read (yet).</summary>
    public static List<string> Materialise(System.Windows.IDataObject data, string folder, Func<int, Stream?>? contentsAt, out int unsupported, out List<string> unread)
        => Materialise(data, folder, contentsAt, out unsupported, out unread, MaxFileBytes);

    /// <summary>
    /// While the pointer is only passing over the timeline the UI thread reads at most <paramref name="budgetBytes"/> in all (by the
    /// sizes the descriptors give): bigger entries become placeholders that the drop reads (with the full limit).
    /// </summary>
    public const long HoverBudgetBytes = 64L * 1024 * 1024;

    public static List<string> Materialise(System.Windows.IDataObject data, string folder, Func<int, Stream?>? contentsAt, out int unsupported, out List<string> unread, long budgetBytes)
    {
        long budgetUsed = 0;
        unsupported = 0;
        unread = new List<string>();
        var written = new List<string>();
        var entries = Descriptors(data);
        if (entries is null) return written;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count && written.Count < MaxFiles; i++)
        {
            var entry = entries[i];
            if (entry.Folder) continue;
            var name = MediaDrop.SafeFileName(entry.Name);
            var role = MediaDrop.RoleOf(name);
            if (role is not (MediaDrop.FileRole.Audio or MediaDrop.FileRole.Midi) || entry.Size > MaxFileBytes) { unsupported++; continue; }
            var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name);
            for (var n = 2; !used.Add(name); n++) name = $"{stem} ({n}){ext}";
            if (budgetBytes < MaxFileBytes && budgetUsed + (entry.Size ?? 0) > budgetBytes) { unread.Add(name); continue; }
            budgetUsed += entry.Size ?? 0;
            try
            {
                using var stream = (contentsAt ?? (index => ContentsAt(data, index)))(i);
                if (stream is null) { unread.Add(name); continue; }
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, name);
                using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > MaxFileBytes) throw new IOException("virtual file too large");
                        file.Write(buffer, 0, read);
                    }
                    // An HGLOBAL can be larger than the file it holds: the descriptor's size wins.
                    if (entry.Size is { } exact && exact < total) file.SetLength(exact);
                }
                written.Add(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException or ArgumentException) // Not logged: unreadable name: the file is skipped in the drop
            {
                unread.Add(name);
            }
        }
        return written;
    }

    /// <summary>The FileContents of entry <paramref name="index"/>: a stream the caller disposes, or null.</summary>
    public static Stream? ContentsAt(System.Windows.IDataObject data, int index)
    {
        if (index == 0)
        {
            try
            {
                if (data.GetData(Contents) is { } managed && ReadAll(managed, MaxFileBytes) is { } bytes) return new MemoryStream(bytes, writable: false);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or OutOfMemoryException) { Services.Trace.Error(Services.Trace.Ui, "virtual file drop: read contents: " + ex.Message); }
        }
        return data is ComTypes.IDataObject com ? ComContents(com, index) : null;
    }

    private static Stream? ComContents(ComTypes.IDataObject com, int index)
    {
        var format = new ComTypes.FORMATETC
        {
            cfFormat = unchecked((short)System.Windows.DataFormats.GetDataFormat(Contents).Id),
            dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = index, ptd = IntPtr.Zero,
            tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL,
        };
        ComTypes.STGMEDIUM medium;
        try { com.GetData(ref format, out medium); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotImplementedException) { return null; } // Not logged: probe: null means none
        try
        {
            if (medium.tymed == ComTypes.TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
            {
                var size = (long)GlobalSize(medium.unionmember);
                if (size <= 0 || size > MaxFileBytes) return null;
                var ptr = GlobalLock(medium.unionmember);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, (int)size);
                    return new MemoryStream(bytes, writable: false);
                }
                finally { GlobalUnlock(medium.unionmember); }
            }
            if (medium.tymed == ComTypes.TYMED.TYMED_ISTREAM && medium.unionmember != IntPtr.Zero)
            {
                var stream = (ComTypes.IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                var result = new MemoryStream();
                var buffer = new byte[81920];
                var readPtr = Marshal.AllocCoTaskMem(sizeof(int));
                try
                {
                    while (true)
                    {
                        stream.Read(buffer, buffer.Length, readPtr);
                        var read = Marshal.ReadInt32(readPtr);
                        if (read <= 0) break;
                        if (result.Length + read > MaxFileBytes) return null;
                        result.Write(buffer, 0, read);
                    }
                }
                finally { Marshal.FreeCoTaskMem(readPtr); Marshal.ReleaseComObject(stream); }
                result.Position = 0;
                return result;
            }
            return null;
        }
        finally { ReleaseStgMedium(ref medium); }
    }

    private static byte[]? ReadAll(object? value, long limit)
    {
        switch (value)
        {
            case byte[] bytes: return bytes.Length <= limit ? bytes : null;
            case MemoryStream memory: return memory.Length <= limit ? memory.ToArray() : null;
            case Stream stream:
            {
                using var copy = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (copy.Length + read > limit) return null;
                    copy.Write(buffer, 0, read);
                }
                return copy.ToArray();
            }
            default: return null;
        }
    }

    /// <summary>Removes staging folders left by drags that were cancelled more than an hour ago.</summary>
    public static void CleanOldStaging()
    {
        try
        {
            if (!Directory.Exists(MediaDrop.StagingRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(MediaDrop.StagingRoot).Take(256))
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddHours(-1)) MediaDropSession.TryDelete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Not logged: staging cleanup: a folder in use is left for the next pass.
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr hMem);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
}
