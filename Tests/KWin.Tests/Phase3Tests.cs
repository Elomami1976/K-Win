using System.Text.RegularExpressions;
using KWin.Core;
using KWin.Utils;
using Microsoft.Win32;
using Xunit;

namespace KWin.Tests
{
    /// <summary>Tests that change global state (DevMode, registry) run one at a time.</summary>
    [CollectionDefinition("Serial", DisableParallelization = true)]
    public class SerialCollection { }

    [Collection("Serial")]
    public class DevPreviewTests
    {
        [Fact]
        public async Task Preview_IsReadOnly_ForRegistryCleanupAndTools()
        {
            string sub = $@"Software\K-win-Tests\{Guid.NewGuid():N}";
            using var dir = new TempDirectory();
            string file = dir.File("keep.tmp", "data", DateTime.UtcNow.AddDays(-5));
            var reg = new RegistryHelper(Path.Combine(dir.Path, "backups"));

            DevMode.Initialize(new[] { "K-win.exe", DevMode.Switch });
            try
            {
                Assert.True(DevMode.IsUiPreview);

                var write = reg.SetValueWithBackup($@"HKCU\{sub}", "x", 1, RegistryValueKind.DWord);
                Assert.False(write.Success);
                Assert.Null(Registry.CurrentUser.OpenSubKey(sub)); // nothing was written

                Assert.Throws<InvalidOperationException>(() =>
                    CleanupManager.CleanTargets(new[] { new CleanupTarget("t", dir.Path, null) }));
                Assert.True(File.Exists(file)); // nothing was deleted

                var tool = await new SystemTools().ExecuteToolAsync("powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
                Assert.False(tool.Success);

                Assert.False(ElevationHelper.TryRestartElevated());
            }
            finally
            {
                DevMode.Initialize(Array.Empty<string>());
                try { Registry.CurrentUser.DeleteSubKeyTree(sub, false); } catch { }
            }
            Assert.False(DevMode.IsUiPreview);
        }

        [Fact]
        public void Preview_IsOffWithoutTheSwitch()
        {
            DevMode.Initialize(new[] { "K-win.exe", "--tab", "2" });
            Assert.False(DevMode.IsUiPreview);
        }
    }

    public class LocalizationCompletenessTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "K-win.csproj")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        /// <summary>Every literal Get("key") in the app exists in English (otherwise the raw key would show).</summary>
        [Fact]
        public void AllKeysUsedInCode_ExistInEnglish()
        {
            string root = RepoRoot();
            var keyPattern = new Regex(@"\bGet\(\s*""([a-z0-9_]+\.[a-z0-9_.]+)""", RegexOptions.Compiled);
            var missing = new SortedSet<string>();

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}"))
                    continue;
                foreach (Match m in keyPattern.Matches(File.ReadAllText(file)))
                {
                    string key = m.Groups[1].Value;
                    if (!Loc.HasKey("en", key)) missing.Add($"{key} ({Path.GetFileName(file)})");
                }
            }

            Assert.True(missing.Count == 0, "Missing English strings:\n" + string.Join("\n", missing));
        }

        /// <summary>Every string added in 1.5 is translated into Arabic and French.</summary>
        [Theory]
        [InlineData("ar")]
        [InlineData("fr")]
        public void All15Strings_AreTranslated(string lang)
        {
            var missing = Loc.Keys15.Where(k => !Loc.HasKey(lang, k)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"Missing {lang} strings:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void NavigationKeys_AreTranslated()
        {
            foreach (var key in new[] { "dashboard", "performance", "cleanup", "privacy", "startup", "health", "tools", "settings" })
            foreach (var lang in new[] { "en", "ar", "fr" })
            {
                Assert.True(Loc.HasKey(lang, "nav." + key), $"nav.{key} missing in {lang}");
                Assert.True(Loc.HasKey(lang, $"page.{key}_sub"), $"page.{key}_sub missing in {lang}");
            }
        }
    }
}
