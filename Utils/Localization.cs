using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.Json;

namespace KWin.Utils
{
    /// <summary>
    /// Localization for K-win. Strings live in .resx resources:
    ///   Resources/Strings.resx     English (neutral, also the fallback)
    ///   Resources/Strings.fr.resx  French
    ///   Resources/Strings.ar.resx  Arabic (right-to-left)
    /// A missing translation falls back to English; a missing key returns the key itself
    /// (the tests make sure that never happens in shipped code).
    /// The chosen language is saved to %AppData%\K-win\language.json.
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceManager Resources =
            new("KWin.Resources.Strings", typeof(Loc).Assembly);

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-win", "language.json");

        private static string _currentLang = "en";
        private static CultureInfo _culture = CultureInfo.GetCultureInfo("en");

        public static string CurrentLanguage => _currentLang;
        public static CultureInfo Culture => _culture;
        public static bool IsRTL => _currentLang == "ar";

        /// <summary>Languages with complete translations: code, native name, English name.</summary>
        public static readonly (string Code, string Native, string English)[] AvailableLanguages =
        {
            ("en", "English", "English"),
            ("fr", "Français", "French"),
            ("ar", "العربية", "Arabic"),
        };

        public static bool IsSupported(string? code) => AvailableLanguages.Any(l => l.Code == code);

        /// <summary>Localized string for the current language.</summary>
        public static string Get(string key)
        {
            try
            {
                return Resources.GetString(key, _culture) ?? key;
            }
            catch (MissingManifestResourceException)
            {
                return key;
            }
        }

        /// <summary>Localized string with format arguments.</summary>
        public static string Get(string key, params object[] args)
        {
            var template = Get(key);
            try { return string.Format(_culture, template, args); }
            catch (FormatException) { return template; }
        }

        /// <summary>Sets the active language (en/fr/ar) and saves the choice.</summary>
        public static void SetLanguage(string langCode)
        {
            if (!IsSupported(langCode)) langCode = "en";
            _currentLang = langCode;
            _culture = CultureInfo.GetCultureInfo(langCode);
            SavePreference();
        }

        /// <summary>Loads the saved language. Returns true if a supported preference was found.</summary>
        public static bool LoadPreference()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                if (data != null && data.TryGetValue("language", out var lang))
                {
                    // 1.0 offered languages that were never translated; those fall back to English.
                    _currentLang = IsSupported(lang) ? lang : "en";
                    _culture = CultureInfo.GetCultureInfo(_currentLang);
                    return true;
                }
            }
            catch
            {
                // unreadable preference: ask again
            }
            return false;
        }

        public static void SavePreference()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["language"] = _currentLang },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // not critical
            }
        }

        /// <summary>
        /// Keeps numbers, paths and technical identifiers (e.g. "358.5 GB / 673.6 GB", a command line)
        /// in left-to-right order inside Arabic text, using a Unicode left-to-right embedding.
        /// </summary>
        public static string Ltr(string text) => IsRTL ? "‪" + text + "‬" : text;

        #region Resource inspection (tests)

        private static CultureInfo CultureFor(string lang) =>
            lang == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(lang);

        /// <summary>True if the language's own resource file defines the key (no fallback).</summary>
        internal static bool HasKey(string lang, string key)
        {
            var set = Resources.GetResourceSet(CultureFor(lang), createIfNotExists: true, tryParents: false);
            return set?.GetString(key) != null;
        }

        /// <summary>All keys of one language's resource file.</summary>
        internal static IReadOnlyCollection<string> KeysOf(string lang)
        {
            var set = Resources.GetResourceSet(CultureFor(lang), createIfNotExists: true, tryParents: false);
            var keys = new List<string>();
            if (set == null) return keys;
            foreach (DictionaryEntry e in set)
                if (e.Key is string k) keys.Add(k);
            return keys;
        }

        /// <summary>Raw value without fallback (null if missing), for placeholder checks in tests.</summary>
        internal static string? RawValue(string lang, string key) =>
            Resources.GetResourceSet(CultureFor(lang), createIfNotExists: true, tryParents: false)?.GetString(key);

        #endregion
    }
}
