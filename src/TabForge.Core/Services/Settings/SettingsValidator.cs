using TabForge.Docking;

namespace TabForge.Services;

// Owns: bounding every settings value to its valid range.
// Does not own: file access and migration.
// Tests: TestSecurityInputBoundaries, TestSettingsBadWorkspaceKeepsOtherSettings.
/// <summary>Clamps imported and persisted settings to practical UI, audio, and collection bounds.</summary>
public static class SettingsValidator
{
    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new AppSettings();
        SettingsMigration.EnsureSections(settings);
        settings.Appearance.TrackTintPercent = Math.Clamp(settings.Appearance.TrackTintPercent, 0, 60);
        settings.Appearance.MutedTrackDimPercent = Math.Clamp(settings.Appearance.MutedTrackDimPercent, 0, 100);
        NormalizePlugins(settings);
        NormalizeTimeline(settings);
        NormalizeWindow(settings, defaults);

        NormalizeGeneral(settings.General, defaults.General);
        NormalizeAppearance(settings.Appearance, defaults.Appearance);
        NormalizeAudio(settings.Audio);
        NormalizeEditing(settings.Editing);
        // The duration-key polarity lives with the editing settings and drives the catalogue defaults (not stored with the hotkeys).
        settings.Hotkeys.PlusKeyLengthensNotes = settings.Editing.PlusKeyLengthensNotes;
        NormalizeFollow(settings.Follow, defaults.Follow);
        NormalizeTabs(settings.Tabs, defaults.Tabs);
        NormalizeHotkeys(settings.Hotkeys);
        settings.Timeline ??= new TimelineSettings();
        Features.CatalogComposition.NormalizeSettings(settings);

        if (settings.Workspace is not null && !WorkspaceNormalizer.Normalize(settings.Workspace))
            settings.Workspace = null;
        if (settings.WorkspaceBeforeSideHide is not null && !WorkspaceNormalizer.Normalize(settings.WorkspaceBeforeSideHide))
            settings.WorkspaceBeforeSideHide = null;

        return settings;
    }

    private static void NormalizePlugins(AppSettings settings)
    {
        settings.Plugins ??= new PluginSettings();
        var plugins = settings.Plugins;
        plugins.Driver = AudioDrivers.All.FirstOrDefault(d => string.Equals(d, plugins.Driver, StringComparison.OrdinalIgnoreCase)) ?? AudioDrivers.Default;
        plugins.Device = plugins.Device is { Length: <= 256 } device ? device : "";
        plugins.InputDevice = plugins.InputDevice is { Length: <= 256 } input ? input : "";
        plugins.AsioInputChannel = Math.Clamp(plugins.AsioInputChannel, 0, 62);
        plugins.AsioInputLastChannel = Math.Clamp(plugins.AsioInputLastChannel, 0, 63);   // last <= first is read as one channel
        plugins.HiddenInputs ??= new();
        plugins.FxListWidth = InputLimits.ClampFinite(plugins.FxListWidth, 180, 2_000, 290);
        plugins.AsioOutputChannel = Math.Clamp(plugins.AsioOutputChannel, 0, 62);
        plugins.AsioOutputLastChannel = Math.Clamp(plugins.AsioOutputLastChannel, 0, 63);
        plugins.SampleRate = plugins.SampleRate == 0 || AudioDrivers.SampleRates.Contains(plugins.SampleRate) ? plugins.SampleRate : 48000;
        plugins.BufferSize = Math.Clamp(plugins.BufferSize, AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer);
        plugins.OutputProfiles = (plugins.OutputProfiles ?? new()).Where(p => p.Key is { Length: <= 600 } && p.Value is not null)
            .Take(64).ToDictionary(p => p.Key, p => new OutputProfile
            {
                BufferSize = Math.Clamp(p.Value.BufferSize, AudioDrivers.MinBuffer, AudioDrivers.MaxBuffer),
                SampleRate = p.Value.SampleRate == 0 || AudioDrivers.SampleRates.Contains(p.Value.SampleRate) ? p.Value.SampleRate : 0,
            });
        // A scan folder on a network share or a device path is dropped: a scan must not stall on a share or read a device.
        plugins.Folders = (plugins.Folders ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength } && TabForge.Plugins.VstScannerService.IsLocalScanFolder(f))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        plugins.CommonFolders = (plugins.CommonFolders ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength } && TabForge.Plugins.VstScannerService.IsLocalScanFolder(f))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        static List<KnownPlugin> CleanList(List<KnownPlugin>? list) => (list ?? new())
            .Where(k => k is not null && k.Path is { Length: > 0 and <= InputLimits.MaxPathLength } && (k.Name ?? "").Length <= 256 && (k.Vendor ?? "").Length <= 256)
            .Select(k => { k.Name ??= ""; k.Vendor ??= ""; k.Format = TabForge.Plugins.PluginFormats.IsKnown(k.Format) ? k.Format : ""; k.Role = k.Role is "Instrument" or "Effect" ? k.Role : ""; k.ClassId = TabForge.Plugins.Vst3ClassId.Normalize(k.ClassId); return k; })
            .Take(InputLimits.MaxVstPlugins).ToList();
        plugins.ScanCache = CleanList(plugins.ScanCache);
        plugins.Probed = CleanList(plugins.Probed);
        // Trust grants fail closed: an approval that is not a full path, or whose path cannot be normalised, is dropped. Approvals are uncapped;
        // one without a fingerprint record is not trusted (PluginTrust asks for approval again).
        plugins.ApprovedPluginPaths = (plugins.ApprovedPluginPaths ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength } && IsFullPath(f))
            .Select(TabForge.Plugins.PluginPath.Normalize).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.Audio.ApprovedMedia = (settings.Audio.ApprovedMedia ?? new()).Where(a => a is not null && a.Folder is { Length: > 0 and <= InputLimits.MaxPathLength } && IsFullPath(a.Folder)
                && TabForge.Plugins.PluginPath.Normalize(a.Folder).Length > 0 && (a.Project ?? "").Length <= InputLimits.MaxPathLength)
            .Select(a => new MediaApproval { Project = a.Project ?? "", Folder = a.Folder }).Take(512).ToList();
        plugins.Quarantined = (plugins.Quarantined ?? new()).Where(f => f is { Length: > 0 and <= InputLimits.MaxPathLength })
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(1024).ToList();
        plugins.TunerOffTracks = (plugins.TunerOffTracks ?? new()).Distinct().Take(1024).ToList();
        plugins.AutoChains = (plugins.AutoChains ?? new()).Where(chain => chain is not null).ToList();
        foreach (var chain in plugins.AutoChains) chain.Plugins = CleanChainSlots(chain.Plugins);
        plugins.StartupTracks = (plugins.StartupTracks ?? new()).Where(template => template is not null).ToList();
        foreach (var template in plugins.StartupTracks) template.Plugins = CleanChainSlots(template.Plugins);

        // Saved chains: empty entries are dropped and a plug-in format is "", VST2 or VST3 (an unknown one reads as "", as in the known-plug-in list).
        static List<TabForge.Plugins.PluginSlot> CleanChainSlots(List<TabForge.Plugins.PluginSlot>? slots) => (slots ?? new()).Where(slot => slot is not null)
            .Select(slot => { slot.Format = TabForge.Plugins.PluginFormats.IsKnown(slot.Format) ? slot.Format : ""; return slot; }).ToList();
    }

    private static void NormalizeTimeline(AppSettings settings)
    {
        if (settings.Timeline is { } timeline)
        {
            timeline.Snap ??= new SnapSettings();
            if (!SnapSettings.Grids.Contains(timeline.Snap.Grid)) timeline.Snap.Grid = "1/4";
            timeline.Snap.DistancePx = Math.Clamp(timeline.Snap.DistancePx, 1, 40);
            timeline.PlayheadStyle = PlayheadStyles.Normalize(timeline.PlayheadStyle);
            timeline.MasterTrackColour = Colour(timeline.MasterTrackColour, "#8B939F"); timeline.MasterTrackOpacityPercent = Math.Clamp(timeline.MasterTrackOpacityPercent, 5, 50);
            timeline.Band ??= new BandSettings();
            timeline.Band.InstrumentSize = BandChoices.NormalizeSize(timeline.Band.InstrumentSize);
            timeline.Band.LaneContent = BandChoices.NormalizeContent(timeline.Band.LaneContent);
            timeline.Band.LaneLayout = BandChoices.NormalizeLayout(timeline.Band.LaneLayout);
            timeline.Band.RowsPerScreen = Math.Clamp(timeline.Band.RowsPerScreen, 1, 5);
            timeline.Band.PlayheadLine = BandChoices.NormalizePlayhead(timeline.Band.PlayheadLine);
            timeline.Band.LaneZoom = BandChoices.ClampZoom(timeline.Band.LaneZoom);
            timeline.Band.RowMinHeight = BandChoices.NormalizeRowMin(timeline.Band.RowMinHeight);
            if (timeline.Band.InstrumentWidth != 0) timeline.Band.InstrumentWidth = BandChoices.ClampWidth(timeline.Band.InstrumentWidth);
        }
    }

    private static void NormalizeWindow(AppSettings settings, AppSettings defaults)
    {
        settings.InstrumentHeight = InputLimits.ClampFinite(settings.InstrumentHeight, 80, 2_048, defaults.InstrumentHeight);
        settings.ArrangementHeight = InputLimits.ClampFinite(settings.ArrangementHeight, 80, 2_048, defaults.ArrangementHeight);
        settings.BottomTabsWidth = InputLimits.ClampFinite(settings.BottomTabsWidth, 120, 4_096, defaults.BottomTabsWidth);
        settings.SectionsPanelHeight = InputLimits.ClampFinite(settings.SectionsPanelHeight, 80, 2_048, defaults.SectionsPanelHeight);
        settings.PreviewHorizon = Math.Clamp(settings.PreviewHorizon, 0, 10);
        settings.FretboardFrets = settings.FretboardFrets is 12 or 24 ? settings.FretboardFrets : defaults.FretboardFrets;
        settings.ZoomFactor = InputLimits.ClampFinite(settings.ZoomFactor, 0, 4, defaults.ZoomFactor);
        settings.Speed = InputLimits.ClampFinite(settings.Speed, 0.25, 2, defaults.Speed);
        settings.WindowWidth = InputLimits.ClampFinite(settings.WindowWidth, 640, 7_680, defaults.WindowWidth);
        settings.WindowHeight = InputLimits.ClampFinite(settings.WindowHeight, 480, 4_320, defaults.WindowHeight);
        // The remembered Settings window size, same bounds pattern as the main window: non-finite values fall back to the defaults.
        settings.SettingsWindowWidth = InputLimits.ClampFinite(settings.SettingsWindowWidth, 640, 7_680, defaults.SettingsWindowWidth);
        settings.SettingsWindowHeight = InputLimits.ClampFinite(settings.SettingsWindowHeight, 480, 4_320, defaults.SettingsWindowHeight);
        // The remembered Mixer window size: a floor small enough for small screens, the same upper bounds as the Settings window's.
        settings.MixerWindowWidth = InputLimits.ClampFinite(settings.MixerWindowWidth, 320, 7_680, defaults.MixerWindowWidth);
        settings.MixerWindowHeight = InputLimits.ClampFinite(settings.MixerWindowHeight, 240, 4_320, defaults.MixerWindowHeight);
        // A position outside the range (or not a number) is dropped, so the OS places the window as on a first start.
        settings.WindowLeft = IsSanePixelCoordinate(settings.WindowLeft) ? settings.WindowLeft : null;
        settings.WindowTop = IsSanePixelCoordinate(settings.WindowTop) ? settings.WindowTop : null;
        settings.ScaleHighlight = SafeText(settings.ScaleHighlight, 64, defaults.ScaleHighlight);
        settings.Notation = SafeText(settings.Notation, 64, defaults.Notation);
    }

    /// <summary>Screen coordinates in pixels, including the negative ones of a monitor left of or above the primary one.</summary>
    private const double ScreenCoordinateLimit = 16_384;

    private static bool IsSanePixelCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate) && Math.Abs(coordinate) <= ScreenCoordinateLimit;

    private static void NormalizeGeneral(GeneralSettings value, GeneralSettings defaults)
    {
        value.OpenFromExplorer = string.Equals(value.OpenFromExplorer, "A new window", StringComparison.OrdinalIgnoreCase) ? "A new window" : "A new tab";
        value.AutosaveMinutes = AutosaveService.NormalizeMinutes(value.AutosaveMinutes);
        value.TutorialLastGuide = string.Equals(value.TutorialLastGuide, "detailed", StringComparison.OrdinalIgnoreCase) ? "detailed" : "basic";
        value.TutorialLastChapter = SafeText(value.TutorialLastChapter, 80, "") ?? "";
        value.TutorialWindowWidth = value.TutorialWindowWidth <= 0 ? 0 : InputLimits.ClampFinite(value.TutorialWindowWidth, 520, 7_680, 0);
        value.TutorialWindowHeight = value.TutorialWindowHeight <= 0 ? 0 : InputLimits.ClampFinite(value.TutorialWindowHeight, 380, 4_320, 0);
    }

    private static void NormalizeAppearance(AppearanceSettings value, AppearanceSettings defaults)
    {
        value.RecentColours ??= new List<string>();
        value.RecentColours = value.RecentColours.Where(colour => SettingsColor.IsValid(colour))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(InputLimits.MaxRecentColours).ToList();

        value.ThemeMode = Choice(value.ThemeMode, defaults.ThemeMode, "System", "Dark", "Light", "Custom");
        value.Density = Choice(value.Density, defaults.Density, "ExtraCompact", "Compact", "Comfortable", "Spacious");
        value.LedgerLines = Choice(value.LedgerLines, defaults.LedgerLines, "Standard", "Minimal", "Hidden");
        value.ScorePaper = Choice(value.ScorePaper, defaults.ScorePaper, "Dark", "Light");
        value.FretboardPosition = Choice(value.FretboardPosition, defaults.FretboardPosition, "Left", "Centre", "Right");
        if (!double.IsFinite(value.InstrumentPaneHeight) || value.InstrumentPaneHeight < 0 || value.InstrumentPaneHeight > 4000)
            value.InstrumentPaneHeight = 0;
        if (!double.IsFinite(value.KeyboardPaneHeight) || value.KeyboardPaneHeight < 0 || value.KeyboardPaneHeight > 4000)
            value.KeyboardPaneHeight = 0;

        value.UiScale = InputLimits.ClampFinite(value.UiScale, 0.8, 1.5, defaults.UiScale);
        value.AnimationSpeed = InputLimits.ClampFinite(value.AnimationSpeed, 0.25, 2, defaults.AnimationSpeed);
        value.FontSize = InputLimits.ClampFinite(value.FontSize, 8, 24, defaults.FontSize);
        value.ScoreTextSize = InputLimits.ClampFinite(value.ScoreTextSize, 8, 24, defaults.ScoreTextSize);
        value.IconSize = InputLimits.ClampFinite(value.IconSize, 10, 22, defaults.IconSize);
        value.SectionBracketThickness = InputLimits.ClampFinite(value.SectionBracketThickness, 1, 24, defaults.SectionBracketThickness);
        value.ScoreSpacing = InputLimits.ClampFinite(value.ScoreSpacing, 0.85, 1.6, defaults.ScoreSpacing);
        value.SystemVerticalSpacing = InputLimits.ClampFinite(value.SystemVerticalSpacing, 0.7, 1.6, defaults.SystemVerticalSpacing);
        value.MeasureHorizontalSpacing = InputLimits.ClampFinite(value.MeasureHorizontalSpacing, 0.8, 1.6, defaults.MeasureHorizontalSpacing);
        value.StaffLineOpacity = InputLimits.ClampFinite(value.StaffLineOpacity, 0, 1, defaults.StaffLineOpacity);
        value.HoverHighlightIntensity = InputLimits.ClampFinite(value.HoverHighlightIntensity, 0, 1, defaults.HoverHighlightIntensity);
        value.SelectionHighlightIntensity = InputLimits.ClampFinite(value.SelectionHighlightIntensity, 0, 1, defaults.SelectionHighlightIntensity);
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
        value.Speed = InputLimits.ClampFinite(value.Speed, 0.25, 2, 1);
        value.CountInBars = Math.Clamp(value.CountInBars, 1, 4);
        value.PreviewLengthMs = Math.Clamp(value.PreviewLengthMs, 60, 1_200);
        value.LetRingCapMs = Math.Clamp(value.LetRingCapMs, 400, 6_000);
        value.MetronomeClick = Math.Clamp(value.MetronomeClick, 0, MidiLimits.Max);
        value.MetronomeAccent = Math.Clamp(value.MetronomeAccent, 0, MidiLimits.Max);
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
        value.ScaleHighlightColour = ScaleHighlightStyles.Colours.FirstOrDefault(v => string.Equals(v, value.ScaleHighlightColour, StringComparison.OrdinalIgnoreCase)) ?? "Accent";
        value.FretMarkerSizePercent = Math.Clamp(value.FretMarkerSizePercent, 60, 160);
        value.ScaleHighlightStrength =Math.Clamp(value.ScaleHighlightStrength, ScaleHighlightStyles.MinStrength, ScaleHighlightStyles.MaxStrength);
        value.FretMarkerColour = FretMarkerLevels.Colours.FirstOrDefault(v => string.Equals(v, value.FretMarkerColour, StringComparison.OrdinalIgnoreCase)) ?? "White";
        value.FretNumberSize = FretNumberSizes.All.FirstOrDefault(v => string.Equals(v, value.FretNumberSize, StringComparison.OrdinalIgnoreCase)) ?? FretNumberSizes.Large;
        value.FretStringSpacing = FretStringSpacings.All.FirstOrDefault(v => string.Equals(v, value.FretStringSpacing, StringComparison.OrdinalIgnoreCase)) ?? FretStringSpacings.Natural;
        value.FretMarkerBrightness =FretMarkerLevels.All.FirstOrDefault(v => string.Equals(v, value.FretMarkerBrightness, StringComparison.OrdinalIgnoreCase)) ?? FretMarkerLevels.Original;
        value.KeyboardKeyColours = KeyboardKeyStyles.All.FirstOrDefault(v => string.Equals(v, value.KeyboardKeyColours, StringComparison.OrdinalIgnoreCase)) ?? KeyboardKeyStyles.MatchTheme;
        value.PreviewHorizon = Math.Clamp(value.PreviewHorizon, 1, 10);
        value.ScaleHighlight = SafeText(value.ScaleHighlight, 64, null);
        foreach (var q in PasteQuestionInfo.All) PasteQuestionInfo.Set(value, q, PasteQuestionInfo.Get(value, q));
        value.PasteSpecialRange = (value.PasteSpecialRange ?? new RangePasteOptions()).Normalized();
    }

    private static void NormalizeFollow(FollowSettings value, FollowSettings defaults)
    {
        value.Mode = Choice(value.Mode, defaults.Mode, FollowModes.Jump, FollowModes.Smooth);
        value.MarginPercent = Math.Clamp(value.MarginPercent, 0, 60);
        value.VerticalTriggerPercent = Math.Clamp(value.VerticalTriggerPercent, 40, 95);
        value.AnticipationBars = Math.Clamp(value.AnticipationBars, 0, 4);
        value.MaxFps = Math.Clamp(value.MaxFps, 10, 240);
        value.ResumeAfterSeconds = Math.Clamp(value.ResumeAfterSeconds, 1, 10);
        value.DurationGlowOpacity = InputLimits.ClampFinite(value.DurationGlowOpacity, 0, 1, defaults.DurationGlowOpacity);
        value.SectionGlowIntensity = InputLimits.ClampFinite(value.SectionGlowIntensity, 0, 1, defaults.SectionGlowIntensity);
        value.PlayheadThickness = InputLimits.ClampFinite(value.PlayheadThickness, 0.5, 5, defaults.PlayheadThickness);
        value.HighlightColour = Colour(value.HighlightColour, defaults.HighlightColour);
        value.HighlightBackground = Colour(value.HighlightBackground, defaults.HighlightBackground);
        value.PlayheadColour = Colour(value.PlayheadColour, defaults.PlayheadColour);
        value.DurationGlowColour = Colour(value.DurationGlowColour, defaults.DurationGlowColour);
        value.PlayingBarColour = Colour(value.PlayingBarColour, defaults.PlayingBarColour);
        value.PlayingBarOpacity = InputLimits.ClampFinite(value.PlayingBarOpacity, 0, 1, defaults.PlayingBarOpacity);
    }

    private static void NormalizeTabs(TabSettings value, TabSettings defaults)
    {
        // The catalog performs the remaining string-choice validation in SettingsMigration.Normalize.
        value.MaxTabWidth = InputLimits.ClampFinite(value.MaxTabWidth, 80, 600, defaults.MaxTabWidth);
        value.MinTabWidth = InputLimits.ClampFinite(value.MinTabWidth, 46, 400, defaults.MinTabWidth);
        value.TabHeight = InputLimits.ClampFinite(value.TabHeight, 26, 44, defaults.TabHeight);
        value.TabFontSize = InputLimits.ClampFinite(value.TabFontSize, 10, 16, defaults.TabFontSize);
    }

    private static void NormalizeHotkeys(HotkeySettings value)
    {
        value.Bindings ??= new Dictionary<string, string>();
        value.DisabledActions ??= new List<string>();
        value.Bindings2 ??= new Dictionary<string, string>();
        value.DisabledActions2 ??= new List<string>();
        // One gesture is stored once across both slots; slot 1 is read first so it wins.
        var usedGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        value.Bindings = NormalizeBindings(value.Bindings, usedGestures);
        value.Bindings2 = NormalizeBindings(value.Bindings2, usedGestures);
        value.DisabledActions = NormalizeDisabled(value.DisabledActions);
        value.DisabledActions2 = NormalizeDisabled(value.DisabledActions2);
        foreach (var disabled in value.DisabledActions) value.Bindings.Remove(disabled);
        foreach (var disabled in value.DisabledActions2) value.Bindings2.Remove(disabled);
    }

    private static Dictionary<string, string> NormalizeBindings(Dictionary<string, string> source, HashSet<string> usedGestures)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Take(InputLimits.MaxHotkeyBindings))
        {
            var action = HotkeyCatalog.ById(pair.Key);
            if (action is null || !InputLimits.IsSafeText(pair.Value, 64, allowLineBreaks: false) ||
                !HotkeyCatalog.TryParse(pair.Value, out var key, out var modifiers)) continue;
            var gesture = HotkeyCatalog.Format(key, modifiers);
            if (!usedGestures.Add(gesture)) continue;
            bindings[action.Id] = gesture;
        }
        return bindings;
    }

    private static List<string> NormalizeDisabled(List<string> source) =>
        source.Where(id => HotkeyCatalog.ById(id) is not null)
            .Select(id => HotkeyCatalog.ById(id)!.Id).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(InputLimits.MaxHotkeyBindings).ToList();

    private static bool IsFullPath(string path) => System.IO.Path.IsPathFullyQualified(path.Trim());

    private static string Choice(string? value, string fallback, params string[] choices) =>
        choices.FirstOrDefault(choice => string.Equals(choice, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private static string Colour(string? value, string fallback) => SettingsColor.IsValid(value) ? value! : fallback;

    private static string? SafeText(string? value, int maximumLength, string? fallback) =>
        InputLimits.IsSafeText(value, maximumLength, allowLineBreaks: false) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
}
