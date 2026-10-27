using System.Security.Cryptography;
using System.Text;

namespace TabForge.Audio.Contracts;

// Owns: the pipe name a process uses on this system. Windows keeps the name as written. Elsewhere a .NET named pipe is a Unix domain socket whose path
// (temp folder + "CoreFxPipe_" + name) must stay under 104 bytes on macOS, so the name becomes a short fixed-length one.
// Does not own: creating the pipe or the token check (PipeAuth).
// Tests: PortableIpcTests.
public static class PipeNames
{
    /// <summary>The name for this system: unchanged on Windows, shortened elsewhere.</summary>
    public static string ForPlatform(string name) => OperatingSystem.IsWindows() ? name : Short(name);

    /// <summary>The pipe between the engine and one isolated plug-in host process: the name on Windows as it always was, shortened elsewhere.</summary>
    public static string PluginHost(string id) => ForPlatform($"TabForge.PluginHost.{id}");

    /// <summary>The pipe between TabForge and its import worker (the caller passes the prefix and a fresh id).</summary>
    public static string ImportWorker(string prefix, string id) => ForPlatform(prefix + id);

    /// <summary>"tf." plus 20 hex characters of the name's hash: the same name always gives the same short name.</summary>
    public static string Short(string name) => "tf." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..20].ToLowerInvariant();
}
