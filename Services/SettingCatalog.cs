using KWin.Controls;
using KWin.Models;
using Microsoft.Win32;

namespace KWin.Services
{
    /// <summary>
    /// The on/off Windows settings K-Win offers, as data. These carry over exactly what K-Win 1.0
    /// changed, plus the matching "Off" values (the Windows default) so every setting can be switched
    /// both ways. Nothing here touches Windows Defender, Windows Update or other security components.
    /// </summary>
    public static class SettingCatalog
    {
        private const string Personalize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ExplorerAdvanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Desktop = @"HKCU\Control Panel\Desktop";
        private const string PoliciesSystem = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System";
        private const string ContextMenuClsid = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

        // ---------------- Performance ----------------

        public static readonly SettingDefinition VisualEffects = new()
        {
            Id = "visual_effects",
            Group = SettingGroup.Performance,
            Glyph = Glyphs.Pc,
            RecommendOn = null, // a trade-off between looks and responsiveness
            SignOutRequired = true,
            On = new[]
            {
                new RegistryTweak(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2),
                new RegistryTweak(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String),
                new RegistryTweak(ExplorerAdvanced, "ListviewShadow", 0),
            },
            Off = new[]
            {
                new RegistryTweak(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0, MatchesWhenMissing: true),
                new RegistryTweak(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "1", RegistryValueKind.String, MatchesWhenMissing: true),
                new RegistryTweak(ExplorerAdvanced, "ListviewShadow", 1, MatchesWhenMissing: true),
            }
        };

        public static readonly SettingDefinition GameMode = new()
        {
            Id = "game_mode",
            Group = SettingGroup.Performance,
            Glyph = "",
            RecommendOn = true, // Windows default
            On = new[]
            {
                new RegistryTweak(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1, MatchesWhenMissing: true),
                new RegistryTweak(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, MatchesWhenMissing: true),
            },
            Off = new[]
            {
                new RegistryTweak(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 0),
                new RegistryTweak(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 0),
            }
        };

        public static readonly SettingDefinition FastStartup = new()
        {
            Id = "fast_startup",
            Risk = RiskLevel.Medium, // has a service or power step that is undoable only until K-win closes
            Group = SettingGroup.Performance,
            Glyph = Glyphs.Bolt,
            RecommendOn = true, // Windows default; turning it off only helps specific dual-boot/driver cases
            RestartRequired = true,
            On = new[] { new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, MatchesWhenMissing: true) },
            Off = new[] { new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) }
        };

        // ---------------- Privacy (On = more private) ----------------

        public static readonly SettingDefinition Telemetry = new()
        {
            Id = "telemetry",
            Risk = RiskLevel.Medium, // has a service or power step that is undoable only until K-win closes
            Group = SettingGroup.Privacy,
            Glyph = "",
            RecommendOn = true,
            On = new[]
            {
                new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1),
                new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4),
            },
            Off = new[]
            {
                new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", null),
                new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 2, MatchesWhenMissing: true),
            }
        };

        public static readonly SettingDefinition AdvertisingId = new()
        {
            Id = "advertising_id",
            Group = SettingGroup.Privacy,
            Glyph = Glyphs.Privacy,
            RecommendOn = true,
            On = new[] { new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1) },
            Off = new[] { new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", null) }
        };

        public static readonly SettingDefinition ActivityHistory = new()
        {
            Id = "activity_history",
            Group = SettingGroup.Privacy,
            Glyph = Glyphs.History,
            RecommendOn = true,
            On = new[]
            {
                new RegistryTweak(PoliciesSystem, "EnableActivityFeed", 0),
                new RegistryTweak(PoliciesSystem, "PublishUserActivities", 0),
                new RegistryTweak(PoliciesSystem, "UploadUserActivities", 0),
                new RegistryTweak(ExplorerAdvanced, "Start_TrackDocs", 0),
            },
            Off = new[]
            {
                new RegistryTweak(PoliciesSystem, "EnableActivityFeed", null),
                new RegistryTweak(PoliciesSystem, "PublishUserActivities", null),
                new RegistryTweak(PoliciesSystem, "UploadUserActivities", null),
                new RegistryTweak(ExplorerAdvanced, "Start_TrackDocs", 1, MatchesWhenMissing: true),
            }
        };

        public static readonly SettingDefinition Recall = new()
        {
            Id = "recall",
            Group = SettingGroup.Privacy,
            Glyph = "",
            RecommendOn = true,
            MinBuild = 26100, // Windows 11 24H2
            On = new[] { new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1) },
            Off = new[] { new RegistryTweak(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", null) }
        };

        // ---------------- Tweaks (preferences; On = the change, Off = Windows default) ----------------

        public static readonly SettingDefinition DarkMode = new()
        {
            Id = "dark_mode", Group = SettingGroup.Tweaks, Glyph = Glyphs.Moon,
            On = new[]
            {
                new RegistryTweak(Personalize, "AppsUseLightTheme", 0),
                new RegistryTweak(Personalize, "SystemUsesLightTheme", 0),
            },
            Off = new[]
            {
                new RegistryTweak(Personalize, "AppsUseLightTheme", 1, MatchesWhenMissing: true),
                new RegistryTweak(Personalize, "SystemUsesLightTheme", 1, MatchesWhenMissing: true),
            }
        };

        public static readonly SettingDefinition Transparency = new()
        {
            Id = "transparency", Group = SettingGroup.Tweaks, Glyph = "",
            On = new[] { new RegistryTweak(Personalize, "EnableTransparency", 1, MatchesWhenMissing: true) },
            Off = new[] { new RegistryTweak(Personalize, "EnableTransparency", 0) }
        };

        public static readonly SettingDefinition FileExtensions = new()
        {
            Id = "file_extensions", Group = SettingGroup.Tweaks, Glyph = Glyphs.Document,
            RecommendOn = true, // seeing ".exe" helps spot disguised malicious files
            On = new[] { new RegistryTweak(ExplorerAdvanced, "HideFileExt", 0) },
            Off = new[] { new RegistryTweak(ExplorerAdvanced, "HideFileExt", 1, MatchesWhenMissing: true) }
        };

        public static readonly SettingDefinition Widgets = new()
        {
            Id = "widgets", Group = SettingGroup.Tweaks, Glyph = "",
            On = new[] { new RegistryTweak(ExplorerAdvanced, "TaskbarDa", 1, MatchesWhenMissing: true) },
            Off = new[] { new RegistryTweak(ExplorerAdvanced, "TaskbarDa", 0) }
        };

        public static readonly SettingDefinition ChatIcon = new()
        {
            Id = "chat_icon", Group = SettingGroup.Tweaks, Glyph = "",
            On = new[] { new RegistryTweak(ExplorerAdvanced, "TaskbarMn", 1, MatchesWhenMissing: true) },
            Off = new[] { new RegistryTweak(ExplorerAdvanced, "TaskbarMn", 0) }
        };

        public static readonly SettingDefinition MenuDelay = new()
        {
            Id = "menu_delay", Group = SettingGroup.Tweaks, Glyph = "",
            SignOutRequired = true,
            On = new[] { new RegistryTweak(Desktop, "MenuShowDelay", "0", RegistryValueKind.String) },
            Off = new[] { new RegistryTweak(Desktop, "MenuShowDelay", "400", RegistryValueKind.String, MatchesWhenMissing: true) }
        };

        public static readonly SettingDefinition ShutdownSpeed = new()
        {
            Id = "shutdown_speed", Group = SettingGroup.Tweaks, Glyph = "",
            RestartRequired = true,
            On = new[]
            {
                new RegistryTweak(Desktop, "WaitToKillAppTimeout", "2000", RegistryValueKind.String),
                new RegistryTweak(Desktop, "HungAppTimeout", "1000", RegistryValueKind.String),
                new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", RegistryValueKind.String),
            },
            Off = new[]
            {
                new RegistryTweak(Desktop, "WaitToKillAppTimeout", "20000", RegistryValueKind.String, MatchesWhenMissing: true),
                new RegistryTweak(Desktop, "HungAppTimeout", "5000", RegistryValueKind.String, MatchesWhenMissing: true),
                new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "5000", RegistryValueKind.String, MatchesWhenMissing: true),
            }
        };

        public static readonly SettingDefinition AutoEndTasks = new()
        {
            Id = "auto_end_tasks", Group = SettingGroup.Tweaks, Glyph = Glyphs.Error,
            SignOutRequired = true,
            On = new[] { new RegistryTweak(Desktop, "AutoEndTasks", "1", RegistryValueKind.String) },
            Off = new[] { new RegistryTweak(Desktop, "AutoEndTasks", "0", RegistryValueKind.String, MatchesWhenMissing: true) }
        };

        public static readonly SettingDefinition ClassicContextMenu = new()
        {
            Id = "classic_context_menu", Group = SettingGroup.Tweaks, Glyph = "",
            On = new[] { new RegistryTweak(ContextMenuClsid + @"\InprocServer32", "", "", RegistryValueKind.String) },
            Off = new[] { new RegistryTweak(ContextMenuClsid, "", null, DeleteKeyTree: true) }
        };

        // ---------------- Memory ----------------

        public static readonly SettingDefinition SysMain = new()
        {
            Id = "sysmain", Group = SettingGroup.Memory, Glyph = Glyphs.Memory,
            Risk = RiskLevel.Medium, // service step
            RecommendOn = true, // Windows default; disabling rarely helps on modern PCs
            On = new[] { new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 2, MatchesWhenMissing: true) },
            Off = new[] { new RegistryTweak(@"HKLM\SYSTEM\CurrentControlSet\Services\SysMain", "Start", 4) }
        };

        public static IReadOnlyList<SettingDefinition> All { get; } = new[]
        {
            VisualEffects, GameMode, FastStartup,
            Telemetry, AdvertisingId, ActivityHistory, Recall,
            DarkMode, Transparency, FileExtensions, Widgets, ChatIcon, MenuDelay, ShutdownSpeed, AutoEndTasks, ClassicContextMenu,
            SysMain
        };

        public static IEnumerable<SettingDefinition> InGroup(SettingGroup g) => All.Where(s => s.Group == g);
    }
}
