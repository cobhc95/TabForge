using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

/// <summary>Clamps imported and persisted settings to practical UI, audio, and collection bounds.</summary>
public static class SettingsValidator
{
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new AppSettings();
        SettingsMigration.EnsureSections(settings);
        settings.Plugins ??= new PluginSettings();
        settings.Appearance.TrackTintPercent = Math.Clamp(settings.Appearance.TrackTintPercent, 0, 60);
        var plugins = settings.Plugins;
        plugins.Driver = AudioDrivers.All.FirstOrDefault(d => string.Equals(d, plugins.Driver, StringComparison.OrdinalIgnoreCase)) ?? AudioDrivers.WasapiShared;
        plugins.Device = plugins.Device is { Length: <= 256 } device ? device : "";
        plugins.InputDevice = plugins.InputDevice is { Length: <= 256 } input ? input : "";
        plugins.AsioInputChannel = Math.Clamp(plugins.AsioInputChannel, 0, 62);
        plugins.AsioInputLastChannel = Math.Clamp(plugins.AsioInputLastChannel, 0, 63);   // last < first is read as one channel
        if (settings.Timeline is { } timeline)
        {
            timeline.Snap ??= new SnapSettings();
            if (!SnapSettings.Grids.Contains(timeline.Snap.Grid)) timeline.Snap.Grid = "1/4";
            timeline.Snap.DistancePx = Math.Clamp(timeline.Snap.DistancePx, 1, 40);
            timeline.PlayheadStyle = PlayheadStyles.Normalize(timeline.PlayheadStyle);
        }
        plugins.AsioOutputChannel = Math.Clamp(plugins.AsioOutputChannel, 0, 62);
        plugins.AsioOutputLastChannel = Math.Clamp(plugins.AsioOutputLastChannel, 0, 63);
        plugins.SampleRate = AudioDrivers.SampleRates.Contains(plugins.SampleRate) ? plugins.SampleRate : 48000;
        plugins.BufferSize = Math.Clamp(plugins.BufferSize, AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer);
        plugins.Folders = (plugins.Folders ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength })
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        static List<KnownPlugin> CleanList(List<KnownPlugin>? list) => (list ?? new())
            .Where(k => k is not null && k.Path is { Length: > 0 and <= InputLimits.MaxPathLength } && (k.Name ?? "").Length <= 256 && (k.Vendor ?? "").Length <= 256)
            .Select(k => { k.Name ??= ""; k.Vendor ??= ""; k.Format = k.Format is "VST2" or "VST3" ? k.Format : ""; k.Role = k.Role is "Instrument" or "Effect" ? k.Role : ""; return k; })
            .Take(InputLimits.MaxVstPlugins).ToList();
        plugins.ScanCache = CleanList(plugins.ScanCache);
        plugins.Probed = CleanList(plugins.Probed);
        plugins.ApprovedPluginPaths = (plugins.ApprovedPluginPaths ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength })
            .Select(TabForge.Plugins.PluginTrust.Normalize).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(1024).ToList();
        settings.Audio.ApprovedMedia = (settings.Audio.ApprovedMedia ?? new()).Where(a => a is not null && a.Folder is { Length: > 0 and <= InputLimits.MaxPathLength } && (a.Project ?? "").Length <= InputLimits.MaxPathLength)
            .Select(a => new MediaApproval { Project = a.Project ?? "", Folder = a.Folder }).Take(512).ToList();
        plugins.Quarantined = (plugins.Quarantined ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength })
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(1024).ToList();

        settings.InstrumentHeight = Clamp(settings.InstrumentHeight, 80, 2_048, defaults.InstrumentHeight);
        settings.ArrangementHeight = Clamp(settings.ArrangementHeight, 80, 2_048, defaults.ArrangementHeight);
        settings.BottomTabsWidth = Clamp(settings.BottomTabsWidth, 120, 4_096, defaults.BottomTabsWidth);
        settings.SectionsPanelHeight = Clamp(settings.SectionsPanelHeight, 80, 2_048, defaults.SectionsPanelHeight);
        settings.PreviewHorizon = Math.Clamp(settings.PreviewHorizon, 0, 10);
        settings.FretboardFrets = settings.FretboardFrets is 12 or 24 ? settings.FretboardFrets : defaults.FretboardFrets;
        settings.ZoomFactor = Clamp(settings.ZoomFactor, 0, 4, defaults.ZoomFactor);
        settings.Speed = Clamp(settings.Speed, 0.25, 2, defaults.Speed);
        settings.WindowWidth = Clamp(settings.WindowWidth, 640, 7_680, defaults.WindowWidth);
        settings.WindowHeight = Clamp(settings.WindowHeight, 480, 4_320, defaults.WindowHeight);
        settings.ScaleHighlight = SafeText(settings.ScaleHighlight, 64, defaults.ScaleHighlight);
        settings.Notation = SafeText(settings.Notation, 64, defaults.Notation);

        NormalizeGeneral(settings.General, defaults.General);
        NormalizeAppearance(settings.Appearance, defaults.Appearance);
        NormalizeAudio(settings.Audio);
        NormalizeEditing(settings.Editing);
        NormalizeFollow(settings.Follow, defaults.Follow);
        NormalizeTabs(settings.Tabs, defaults.Tabs);
        NormalizeHotkeys(settings.Hotkeys);
        settings.Timeline ??= new TimelineSettings();

        if (settings.Workspace is not null && !SettingsFileService.NormalizeWorkspace(settings.Workspace))
            settings.Workspace = null;
        if (settings.WorkspaceBeforeSideHide is not null && !SettingsFileService.NormalizeWorkspace(settings.WorkspaceBeforeSideHide))
            settings.WorkspaceBeforeSideHide = null;

        return settings;
    }

    private static void NormalizeGeneral(GeneralSettings value, GeneralSettings defaults)
    {
        value.OpenFromExplorer = string.Equals(value.OpenFromExplorer, "A new window", StringComparison.OrdinalIgnoreCase) ? "A new window" : "A new tab";
        value.AutosaveMinutes = AutosaveService.NormalizeMinutes(value.AutosaveMinutes);
        value.PlaybackControllerX = Clamp(value.PlaybackControllerX, -32_768, 32_768, defaults.PlaybackControllerX);
        value.PlaybackControllerY = Clamp(value.PlaybackControllerY, -32_768, 32_768, defaults.PlaybackControllerY);
        value.PlaybackControllerHeight = Clamp(value.PlaybackControllerHeight, 80, 2_048, defaults.PlaybackControllerHeight);
        value.TutorialLastGuide = string.Equals(value.TutorialLastGuide, "detailed", StringComparison.OrdinalIgnoreCase) ? "detailed" : "basic";
        value.TutorialLastChapter = SafeText(value.TutorialLastChapter, 80, "") ?? "";
        value.TutorialWindowWidth = value.TutorialWindowWidth <= 0 ? 0 : Clamp(value.TutorialWindowWidth, 520, 7_680, 0);
        value.TutorialWindowHeight = value.TutorialWindowHeight <= 0 ? 0 : Clamp(value.TutorialWindowHeight, 380, 4_320, 0);
    }

    private static void NormalizeAppearance(AppearanceSettings value, AppearanceSettings defaults)
    {
        value.RecentColours ??= new List<string>();
        value.RecentColours = value.RecentColours.Where(colour => SettingsColor.IsValid(colour))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(InputLimits.MaxRecentColours).ToList();

        value.ThemeMode = Choice(value.ThemeMode, defaults.ThemeMode, "System", "Dark", "Light", "Custom");
        value.Density = Choice(value.Density, defaults.Density, "Compact", "Comfortable", "Spacious");
        value.LedgerLines = Choice(value.LedgerLines, defaults.LedgerLines, "Standard", "Minimal", "Hidden");
        value.ScorePaper = Choice(value.ScorePaper, defaults.ScorePaper, "Dark", "Light");
        value.FretboardPosition = Choice(value.FretboardPosition, defaults.FretboardPosition, "Left", "Centre", "Right");
        if (!double.IsFinite(value.InstrumentPaneHeight) || value.InstrumentPaneHeight < 0 || value.InstrumentPaneHeight > 4000)
            value.InstrumentPaneHeight = 0;

        value.UiScale = Clamp(value.UiScale, 0.8, 1.5, defaults.UiScale);
        value.AnimationSpeed = Clamp(value.AnimationSpeed, 0.25, 2, defaults.AnimationSpeed);
        value.FontSize = Clamp(value.FontSize, 8, 24, defaults.FontSize);
        value.ScoreTextSize = Clamp(value.ScoreTextSize, 8, 24, defaults.ScoreTextSize);
        value.IconSize = Clamp(value.IconSize, 10, 22, defaults.IconSize);
        value.SectionBracketThickness = Clamp(value.SectionBracketThickness, 1, 24, defaults.SectionBracketThickness);
        value.ScoreSpacing = Clamp(value.ScoreSpacing, 0.85, 1.6, defaults.ScoreSpacing);
        value.SystemVerticalSpacing = Clamp(value.SystemVerticalSpacing, 0.7, 1.6, defaults.SystemVerticalSpacing);
        value.MeasureHorizontalSpacing = Clamp(value.MeasureHorizontalSpacing, 0.8, 1.6, defaults.MeasureHorizontalSpacing);
        value.StaffLineOpacity = Clamp(value.StaffLineOpacity, 0, 1, defaults.StaffLineOpacity);
        value.HoverHighlightIntensity = Clamp(value.HoverHighlightIntensity, 0, 1, defaults.HoverHighlightIntensity);
        value.SelectionHighlightIntensity = Clamp(value.SelectionHighlightIntensity, 0, 1, defaults.SelectionHighlightIntensity);
        value.ScoreBarNumberFrequency = Math.Clamp(value.ScoreBarNumberFrequency, 1, 16);

        value.FontFamily = SafeText(value.FontFamily, 128, defaults.FontFamily)!;
        value.ScoreFontFamily = SafeText(value.ScoreFontFamily, 128, defaults.ScoreFontFamily)!;
        value.SelectionColour = Colour(value.SelectionColour, defaults.SelectionColour);
        value.HoverColour = Colour(value.HoverColour, defaults.HoverColour);
        value.Accent = Colour(value.Accent, defaults.Accent);
        value.Background = Colour(value.Background, defaults.Background);
        value.Panel = Colour(value.Panel, defaults.Panel);
        value.TitleBarColour = Colour(value.TitleBarColour, defaults.TitleBarColour);
        value.ActiveTabColour = Colour(value.ActiveTabColour, defaults.ActiveTabColour);
        value.TabHoverColour = Colour(value.TabHoverColour, defaults.TabHoverColour);
        value.Text = Colour(value.Text, defaults.Text);
        value.Muted = Colour(value.Muted, defaults.Muted);
        value.DarkScorePaperColour = Colour(value.DarkScorePaperColour, defaults.DarkScorePaperColour);
        value.LightScorePaperColour = Colour(value.LightScorePaperColour, defaults.LightScorePaperColour);
        value.DarkScoreInkColour = Colour(value.DarkScoreInkColour, defaults.DarkScoreInkColour);
        value.LightScoreInkColour = Colour(value.LightScoreInkColour, defaults.LightScoreInkColour);
        value.DarkScoreLinesColour = Colour(value.DarkScoreLinesColour, defaults.DarkScoreLinesColour);
        value.LightScoreLinesColour = Colour(value.LightScoreLinesColour, defaults.LightScoreLinesColour);
        value.PlayheadColour = Colour(value.PlayheadColour, defaults.PlayheadColour);
        value.CursorColour = Colour(value.CursorColour, defaults.CursorColour);
        value.TimelineScrollBarThumbColour = Colour(value.TimelineScrollBarThumbColour, defaults.TimelineScrollBarThumbColour);
    }

    private static void NormalizeAudio(AudioSettings value)
    {
        value.Speed = Clamp(value.Speed, 0.25, 2, 1);
        value.CountInBars = Math.Clamp(value.CountInBars, 1, 4);
        value.PreviewLengthMs = Math.Clamp(value.PreviewLengthMs, 60, 1_200);
        value.LetRingCapMs = Math.Clamp(value.LetRingCapMs, 400, 6_000);
        value.MetronomeClick = Math.Clamp(value.MetronomeClick, 0, 127);
        value.MetronomeAccent = Math.Clamp(value.MetronomeAccent, 0, 127);
        value.MetronomeVolume = Math.Clamp(value.MetronomeVolume, 0, 100);
        value.MetronomeAccentVolume = Math.Clamp(value.MetronomeAccentVolume, 0, 100);
        value.MetronomeClickVolume = Math.Clamp(value.MetronomeClickVolume, 0, 100);
        value.MetronomeSubdivision = value.MetronomeSubdivision is 1 or 2 or 3 or 4 ? value.MetronomeSubdivision : 1;
    }

    private static void NormalizeEditing(EditingSettings value)
    {
        value.DefaultDuration = value.DefaultDuration is 1 or 2 or 4 or 8 or 16 or 32 or 64 ? value.DefaultDuration : 4;
        value.ScoreWheelScrollPixels = Math.Clamp(value.ScoreWheelScrollPixels, 12, 96);
        value.FretboardFrets = value.FretboardFrets is 12 or 24 ? value.FretboardFrets : 24;
        value.InstrumentView = InstrumentViews.All.FirstOrDefault(v => string.Equals(v, value.InstrumentView, StringComparison.OrdinalIgnoreCase)) ?? InstrumentViews.MatchInstrument;
        if (!InstrumentViews.KeyboardSizes.Contains(value.KeyboardKeys)) value.KeyboardKeys = 88;
        value.ScaleHighlightStyle = ScaleHighlightStyles.All.FirstOrDefault(v => string.Equals(v, value.ScaleHighlightStyle, StringComparison.OrdinalIgnoreCase)) ?? ScaleHighlightStyles.Shaded;
        value.ScaleHighlightColour = ScaleHighlightStyles.Colours.FirstOrDefault(v => string.Equals(v, value.ScaleHighlightColour, StringComparison.OrdinalIgnoreCase)) ?? "Blue";
        value.ScaleHighlightStrength = Math.Clamp(value.ScaleHighlightStrength, ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength);
        value.FretMarkerColour = FretMarkerLevels.Colours.FirstOrDefault(v => string.Equals(v, value.FretMarkerColour, StringComparison.OrdinalIgnoreCase)) ?? "White";
        value.FretNumberSize = FretNumberSizes.All.FirstOrDefault(v => string.Equals(v, value.FretNumberSize, StringComparison.OrdinalIgnoreCase)) ?? FretNumberSizes.Large;
        value.FretStringSpacing = FretStringSpacings.All.FirstOrDefault(v => string.Equals(v, value.FretStringSpacing, StringComparison.OrdinalIgnoreCase)) ?? FretStringSpacings.Natural;
        value.FretMarkerBrightness =FretMarkerLevels.All.FirstOrDefault(v => string.Equals(v, value.FretMarkerBrightness, StringComparison.OrdinalIgnoreCase)) ?? FretMarkerLevels.Original;
        value.KeyboardKeyColours = KeyboardKeyStyles.All.FirstOrDefault(v => string.Equals(v, value.KeyboardKeyColours, StringComparison.OrdinalIgnoreCase)) ?? KeyboardKeyStyles.MatchTheme;
        value.PreviewHorizon = Math.Clamp(value.PreviewHorizon, 1, 10);
        value.ScaleHighlight = SafeText(value.ScaleHighlight, 64, null);
        foreach (var q in PasteQuestionInfo.All) PasteQuestionInfo.Set(value, q, PasteQuestionInfo.Get(value, q));
    }

    private static void NormalizeFollow(FollowSettings value, FollowSettings defaults)
    {
        value.Mode = Choice(value.Mode, defaults.Mode, FollowModes.Off, FollowModes.Jump, FollowModes.Smooth);
        value.MarginPercent = Math.Clamp(value.MarginPercent, 0, 60);
        value.VerticalTriggerPercent = Math.Clamp(value.VerticalTriggerPercent, 40, 95);
        value.AnticipationBars = Math.Clamp(value.AnticipationBars, 0, 4);
        value.MaxFps = Math.Clamp(value.MaxFps, 10, 240);
        value.DurationGlowOpacity = Clamp(value.DurationGlowOpacity, 0, 1, defaults.DurationGlowOpacity);
        value.SectionGlowIntensity = Clamp(value.SectionGlowIntensity, 0, 1, defaults.SectionGlowIntensity);
        value.PlayheadThickness = Clamp(value.PlayheadThickness, 0.5, 5, defaults.PlayheadThickness);
        value.HighlightColour = Colour(value.HighlightColour, defaults.HighlightColour);
        value.HighlightBackground = Colour(value.HighlightBackground, defaults.HighlightBackground);
        value.PlayheadColour = Colour(value.PlayheadColour, defaults.PlayheadColour);
        value.DurationGlowColour = Colour(value.DurationGlowColour, defaults.DurationGlowColour);
    }

    private static void NormalizeTabs(TabSettings value, TabSettings defaults)
    {
        // The catalog performs the remaining string-choice validation in SettingsMigration.Normalize.
        value.MaxTabWidth = Clamp(value.MaxTabWidth, 80, 600, defaults.MaxTabWidth);
        value.MinTabWidth = Clamp(value.MinTabWidth, 46, 400, defaults.MinTabWidth);
        value.TabHeight = Clamp(value.TabHeight, 26, 44, defaults.TabHeight);
        value.TabFontSize = Clamp(value.TabFontSize, 10, 16, defaults.TabFontSize);
    }

    private static void NormalizeHotkeys(HotkeySettings value)
    {
        value.Bindings ??= new Dictionary<string, string>();
        value.DisabledActions ??= new List<string>();
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in value.Bindings.Take(InputLimits.MaxHotkeyBindings))
        {
            var action = HotkeyCatalog.ById(pair.Key);
            if (action is null || !InputLimits.IsSafeText(pair.Value, 64, allowLineBreaks: false) ||
                !HotkeyCatalog.TryParse(pair.Value, out var key, out var modifiers)) continue;
            var gesture = HotkeyCatalog.Format(key, modifiers);
            if (!usedGestures.Add(gesture)) continue;
            bindings[action.Id] = gesture;
        }
        value.Bindings = bindings;
        value.DisabledActions = value.DisabledActions.Where(id => HotkeyCatalog.ById(id) is not null)
            .Select(id => HotkeyCatalog.ById(id)!.Id).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(InputLimits.MaxHotkeyBindings).ToList();
        foreach (var disabled in value.DisabledActions) value.Bindings.Remove(disabled);
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static string Choice(string? value, string fallback, params string[] choices) =>
        choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private static string Colour(string? value, string fallback) => SettingsColor.IsValid(value) ? value! : fallback;

    private static string? SafeText(string? value, int maximumLength, string? fallback) =>
        InputLimits.IsSafeText(value, maximumLength, allowLineBreaks: false) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
}
