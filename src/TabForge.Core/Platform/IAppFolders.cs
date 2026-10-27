namespace TabForge.Platform;

// Owns: where the app keeps its own files per operating system: settings, data, cache, logs, temp, plug-in cache and the SoundFont folder.
// Does not own: the --profile redirect or the files inside the folders (UserPaths and its callers), the user's Music and Videos folders.
// Tests: AppFoldersTests.
/// <summary>The app's own folders on this system. Windows keeps the paths the app always used.</summary>
public interface IAppFolders
{
    /// <summary>Settings and approvals (Windows: %APPDATA%\TabForge; macOS: Application Support; Linux: XDG config).</summary>
    string Settings { get; }

    /// <summary>Local data: autosave, recovery, downloaded SoundFont (Windows: %LOCALAPPDATA%\TabForge; macOS: Application Support; Linux: XDG data).</summary>
    string Data { get; }

    /// <summary>Files that can be rebuilt (Windows: the data folder; macOS: Library/Caches; Linux: XDG cache).</summary>
    string Cache { get; }

    /// <summary>Crash and diagnostic logs.</summary>
    string Logs { get; }

    /// <summary>Scratch files.</summary>
    string Temp { get; }

    /// <summary>Per-user, short-lived runtime files such as the shared-memory files (Linux: XDG_RUNTIME_DIR when set; elsewhere the temp folder).</summary>
    string Runtime { get; }

    /// <summary>The plug-in library: approvals and the scan cache (next to the settings, as before).</summary>
    string PluginCache { get; }

    /// <summary>Where a downloaded SoundFont is kept.</summary>
    string SoundFonts { get; }

    /// <summary>The user's Music folder (default for rendered audio and recordings).</summary>
    string Music { get; }

    /// <summary>The user's Videos folder (Movies on macOS).</summary>
    string Videos { get; }

    /// <summary>REAPER's resource folder (its reaper.ini and FXChains).</summary>
    string ReaperResources { get; }

    /// <summary>The per-user "Common" plug-in root a plug-in installer may use (Windows: %LOCALAPPDATA%\Programs\Common); empty where there is none.</summary>
    string UserPluginRoot { get; }
}

/// <summary>The app folders of one operating system, from explicit inputs so every family can be checked on any machine.</summary>
public sealed class AppFolderLayout : IAppFolders
{
    private const string AppName = "TabForge";

    /// <param name="os">The family.</param>
    /// <param name="home">The user's home folder (macOS, Linux).</param>
    /// <param name="roaming">Windows %APPDATA%.</param>
    /// <param name="local">Windows %LOCALAPPDATA%.</param>
    /// <param name="temp">The system temp folder.</param>
    /// <param name="env">Environment lookup (XDG variables).</param>
    /// <param name="music">The Music folder on Windows.</param>
    /// <param name="videos">The Videos folder on Windows.</param>
    public AppFolderLayout(HostOs os, string home, string roaming, string local, string temp, Func<string, string?> env, string music = "", string videos = "")
    {
        Temp = temp;
        Runtime = temp;
        if (os == HostOs.Windows)
        {
            Settings = Path.Combine(roaming, AppName);
            Data = Path.Combine(local, AppName);
            Cache = Data;
            Music = music;
            Videos = videos;
            ReaperResources = Path.Combine(roaming, "REAPER");
            UserPluginRoot = Path.Combine(local, "Programs", "Common");
        }
        else if (os == HostOs.MacOS)
        {
            Settings = Data = Path.Combine(home, "Library", "Application Support", AppName);
            Cache = Path.Combine(home, "Library", "Caches", AppName);
            Music = Path.Combine(home, "Music");
            Videos = Path.Combine(home, "Movies");
            ReaperResources = Path.Combine(Path.GetDirectoryName(Settings)!, "REAPER");
        }
        else
        {
            Settings = Path.Combine(XdgBase(env, "XDG_CONFIG_HOME", home, ".config"), AppName);
            Data = Path.Combine(XdgBase(env, "XDG_DATA_HOME", home, ".local", "share"), AppName);
            Cache = Path.Combine(XdgBase(env, "XDG_CACHE_HOME", home, ".cache"), AppName);
            Music = Path.Combine(home, "Music");
            Videos = Path.Combine(home, "Videos");
            ReaperResources = Path.Combine(XdgBase(env, "XDG_CONFIG_HOME", home, ".config"), "REAPER");
            if (env("XDG_RUNTIME_DIR") is { Length: > 0 } run && Path.IsPathRooted(run)) Runtime = run;
        }
        Logs = Path.Combine(Data, "Diagnostics");
        PluginCache = Settings;
        SoundFonts = Path.Combine(Data, "SoundFonts");
    }

    public string Settings { get; }
    public string Data { get; }
    public string Cache { get; }
    public string Logs { get; }
    public string Temp { get; }
    public string Runtime { get; }
    public string PluginCache { get; }
    public string SoundFonts { get; }
    public string Music { get; } = "";
    public string Videos { get; } = "";
    public string ReaperResources { get; } = "";
    public string UserPluginRoot { get; } = "";

    // The XDG rule: a variable that is unset, empty or not an absolute path is ignored.
    private static string XdgBase(Func<string, string?> env, string variable, string home, params string[] fallback) =>
        env(variable) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : Path.Combine(new[] { home }.Concat(fallback).ToArray());
}
