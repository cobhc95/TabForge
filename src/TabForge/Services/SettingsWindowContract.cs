namespace TabForge.Services;

public enum SettingsShowResult { Applied, Cancelled, Failed }

/// <summary>Framework-neutral boundary for previewing and committing application settings.</summary>
public interface ISettingsWindowHost
{
    SettingsShowResult Show(AppSettings current, Action<AppSettings> apply, Action<AppSettings> preview);
}
