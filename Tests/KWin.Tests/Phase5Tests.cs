using KWin.Core;
using KWin.Models;
using KWin.Services;
using KWin.Utils;
using Microsoft.Win32;
using Xunit;

namespace KWin.Tests
{
    /// <summary>
    /// A throwaway registry sandbox: real HKCU/HKLM paths are redirected into
    /// HKCU\Software\K-win-Tests\{guid}\HKCU|HKLM\..., so nothing outside the sandbox is touched.
    /// </summary>
    public sealed class RegistrySandbox : IDisposable
    {
        public string Sub { get; } = $@"Software\K-win-Tests\{Guid.NewGuid():N}";
        public TempDirectory Dir { get; } = new();
        public RegistryHelper Registry { get; }

        public RegistrySandbox()
        {
            Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\K-win-Tests")?.Dispose();
            Registry = new RegistryHelper(Path.Combine(Dir.Path, "backups"));
        }

        public string Map(string key)
        {
            int slash = key.IndexOf('\\');
            string root = key[..slash].ToUpperInvariant() switch
            {
                "HKEY_LOCAL_MACHINE" => "HKLM",
                "HKEY_CURRENT_USER" => "HKCU",
                var r => r
            };
            return $@"HKCU\{Sub}\{root}{key[slash..]}";
        }

        public bool IsEmpty()
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Sub);
            return k == null || (k.SubKeyCount == 0 && k.ValueCount == 0);
        }

        public void Dispose()
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Sub, false); } catch { }
            Dir.Dispose();
        }
    }

    [Collection("Serial")]
    public class SettingEngineTests
    {
        public static IEnumerable<object[]> Catalog() => SettingCatalog.All.Select(d => new object[] { d.Id });

        [Fact]
        public void Catalog_DefinitionsAreValid()
        {
            var problems = SettingCatalog.All.SelectMany(TweakEngine.Validate).ToList();
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.Equal(SettingCatalog.All.Count, SettingCatalog.All.Select(d => d.Id).Distinct().Count());
        }

        [Fact]
        public void Catalog_NeverTouchesSecurityComponents()
        {
            string[] forbidden = { "WinDefend", "Windows Defender", "WindowsUpdate", "wuauserv", @"\Lsa", "SecurityHealth", "mpssvc", "BFE" };
            foreach (var d in SettingCatalog.All)
                foreach (var t in d.On.Concat(d.Off))
                    Assert.DoesNotContain(forbidden, f => t.Key.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Catalog_AdminAndPolicyFlags_AreConsistent()
        {
            Assert.True(SettingCatalog.FastStartup.RequiresAdmin);
            Assert.True(SettingCatalog.Telemetry.RequiresAdmin);
            Assert.True(SettingCatalog.Telemetry.IsPolicy);
            Assert.False(SettingCatalog.GameMode.RequiresAdmin);
            Assert.False(SettingCatalog.DarkMode.RequiresAdmin);
            Assert.True(SettingCatalog.ShutdownSpeed.RequiresAdmin); // one HKLM value
            Assert.False(SettingCatalog.DarkMode.IsPolicy);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("fr")]
        [InlineData("ar")]
        public void Catalog_AllTextsExist(string lang)
        {
            foreach (var d in SettingCatalog.All)
                foreach (var part in new[] { "title", "desc", "benefit", "side", "on", "off" })
                    Assert.True(Loc.HasKey(lang, d.TextKey(part)), $"{lang}: {d.TextKey(part)}");
        }

        /// <summary>
        /// Every setting in the catalog: On -> verified On, Off -> verified Off, and Undo of all
        /// changes returns the (empty) sandbox to exactly its original state.
        /// </summary>
        [Theory]
        [MemberData(nameof(Catalog))]
        public async Task EverySetting_AppliesVerifiesAndRollsBack(string id)
        {
            var def = SettingCatalog.All.Single(d => d.Id == id);
            using var box = new RegistrySandbox();
            var engine = new TweakEngine(box.Registry, box.Map);
            var safety = new SafetyEngine(box.Registry, Path.Combine(box.Dir.Path, "data"));

            safety.BeginOperation();
            var on = engine.Apply(def, true);
            Assert.True(on.Success, on.ErrorMessage);
            Assert.Equal(SettingState.On, engine.ReadState(def));

            var off = engine.Apply(def, false);
            Assert.True(off.Success, off.ErrorMessage);
            Assert.Equal(SettingState.Off, engine.ReadState(def));
            safety.EndOperation(id);

            // Undo restores the original state: nothing was set before, so nothing remains.
            var undo = await safety.UndoAllActions();
            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.True(box.IsEmpty(), $"{id}: sandbox not empty after undo");
        }

        [Fact]
        public async Task PrivacySetting_UndoRestoresPreviousPolicyValue()
        {
            using var box = new RegistrySandbox();
            var engine = new TweakEngine(box.Registry, box.Map);
            var safety = new SafetyEngine(box.Registry, Path.Combine(box.Dir.Path, "data"));
            string key = box.Map(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection");

            // Something else had set AllowTelemetry = 3 before K-Win.
            using (var k = Registry.CurrentUser.CreateSubKey(key[5..]))
                k.SetValue("AllowTelemetry", 3, RegistryValueKind.DWord);
            Assert.Equal(SettingState.Custom, engine.ReadState(SettingCatalog.Telemetry));

            safety.BeginOperation();
            Assert.True(engine.Apply(SettingCatalog.Telemetry, true).Success);
            safety.EndOperation("telemetry");
            Assert.Equal(SettingState.On, engine.ReadState(SettingCatalog.Telemetry));

            var undo = await safety.UndoLastAction();
            Assert.True(undo.Success, undo.ErrorMessage);
            using var after = Registry.CurrentUser.OpenSubKey(key[5..]);
            Assert.Equal(3, after!.GetValue("AllowTelemetry"));
            Assert.Equal(SettingState.Custom, engine.ReadState(SettingCatalog.Telemetry));
        }

        [Fact]
        public void MissingValue_CountsAsWindowsDefault_WhereDocumented()
        {
            using var box = new RegistrySandbox();
            var engine = new TweakEngine(box.Registry, box.Map);
            Assert.Equal(SettingState.Off, engine.ReadState(SettingCatalog.FileExtensions));   // hidden by default
            Assert.Equal(SettingState.On, engine.ReadState(SettingCatalog.GameMode));          // on by default
            Assert.Equal(SettingState.Off, engine.ReadState(SettingCatalog.AdvertisingId));    // no policy set
        }

        [Fact]
        public void FailedWrite_IsReported_NotSilentlyIgnored()
        {
            using var box = new RegistrySandbox();
            // Redirect into a root RegistryHelper refuses (HKCR), so every write fails.
            var engine = new TweakEngine(box.Registry, k => @"HKCR\K-win-Tests\" + Guid.NewGuid().ToString("N"));
            var r = engine.Apply(SettingCatalog.DarkMode, true);
            Assert.False(r.Success);
            Assert.False(string.IsNullOrEmpty(r.ErrorMessage));
        }

        [Fact]
        public void DescribeChanges_ListsEveryValue()
        {
            var lines = TweakEngine.DescribeChanges(SettingCatalog.ActivityHistory, false);
            Assert.Equal(4, lines.Count);
            Assert.Contains(lines, l => l.Contains("remove") && l.Contains("EnableActivityFeed"));
        }
    }

    [Collection("Serial")]
    public class StartupServiceTests
    {
        private static (RegistrySandbox Box, StartupLocations Loc, string UserFolder) Setup()
        {
            var box = new RegistrySandbox();
            string userFolder = Path.Combine(box.Dir.Path, "Startup");
            string commonFolder = Path.Combine(box.Dir.Path, "CommonStartup");
            Directory.CreateDirectory(userFolder);
            Directory.CreateDirectory(commonFolder);
            var loc = new StartupLocations
            {
                UserRun = box.Map(StartupLocations.Default.UserRun),
                MachineRun = box.Map(StartupLocations.Default.MachineRun),
                MachineRun32 = box.Map(StartupLocations.Default.MachineRun32),
                UserApproved = box.Map(StartupLocations.Default.UserApproved),
                MachineApproved = box.Map(StartupLocations.Default.MachineApproved),
                UserFolder = userFolder,
                CommonFolder = commonFolder
            };
            return (box, loc, userFolder);
        }

        private static void SetValue(string fullKey, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
        {
            using var k = Registry.CurrentUser.CreateSubKey(fullKey[5..]);
            k.SetValue(name, value, kind);
        }

        [Fact]
        public void Discover_FindsAllLocations_AndReadsApprovedFlags()
        {
            var (box, loc, userFolder) = Setup();
            using (box)
            {
                SetValue(loc.UserRun, "UserApp", "\"C:\\Apps\\user.exe\" --tray");
                SetValue(loc.MachineRun, "MachineApp", @"C:\Apps\machine.exe");
                SetValue(loc.MachineRun32, "OldApp", @"C:\Apps (x86)\old.exe /min");
                File.WriteAllText(Path.Combine(userFolder, "Notes.lnk"), "x");
                File.WriteAllText(Path.Combine(userFolder, "desktop.ini"), "x");
                // MachineApp was disabled in Task Manager before
                SetValue(loc.MachineApproved + @"\Run", "MachineApp", StartupService.BuildApproved(false, DateTime.UtcNow), RegistryValueKind.Binary);

                var entries = new StartupService(box.Registry, loc).Discover();

                Assert.Equal(4, entries.Count); // desktop.ini is not an entry
                Assert.Contains(entries, e => e.Name == "UserApp" && e.Source == StartupSource.RegistryUser && e.Enabled && e.ExecutablePath == @"C:\Apps\user.exe");
                Assert.Contains(entries, e => e.Name == "MachineApp" && e.Source == StartupSource.RegistryMachine && !e.Enabled && e.IsMachineWide);
                Assert.Contains(entries, e => e.Name == "OldApp" && e.Source == StartupSource.RegistryMachine32 && e.ApprovedKey.EndsWith(@"\Run32"));
                Assert.Contains(entries, e => e.Name == "Notes" && e.Source == StartupSource.FolderUser && e.ApprovedValueName == "Notes.lnk");
            }
        }

        [Fact]
        public async Task Disable_KeepsTheEntry_AndUndoRestoresOriginalConfiguration()
        {
            var (box, loc, _) = Setup();
            using (box)
            {
                SetValue(loc.UserRun, "UserApp", @"C:\Apps\user.exe");
                var service = new StartupService(box.Registry, loc);
                var safety = new SafetyEngine(box.Registry, Path.Combine(box.Dir.Path, "data"));
                var entry = service.Discover().Single();

                safety.BeginOperation();
                var r = service.SetEnabled(entry, false);
                safety.EndOperation("disable");
                Assert.True(r.Success, r.ErrorMessage);

                var after = service.Discover().Single();
                Assert.False(after.Enabled);
                Assert.Equal(@"C:\Apps\user.exe", after.Command); // Run value untouched

                using (var approved = Registry.CurrentUser.OpenSubKey((loc.UserApproved + @"\Run")[5..]))
                    Assert.Equal(0x03, ((byte[])approved!.GetValue("UserApp")!)[0]);

                Assert.True(service.SetEnabled(after, true).Success);
                Assert.True(service.Discover().Single().Enabled);

                // Undo of the disable restores the original state: no StartupApproved value at all.
                var undo = await safety.UndoLastAction();
                Assert.True(undo.Success, undo.ErrorMessage);
                using var check = Registry.CurrentUser.OpenSubKey((loc.UserApproved + @"\Run")[5..]);
                Assert.True(check == null || check.GetValue("UserApp") == null || ((byte[])check.GetValue("UserApp")!)[0] == 0x02);
            }
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(new byte[] { 0x02, 0, 0, 0 }, true)]
        [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
        [InlineData(new byte[] { 0x03, 0, 0, 0 }, false)]
        [InlineData(new byte[] { 0x07, 0, 0, 0 }, false)]
        public void ApprovedFlag_IsParsedLikeTaskManager(byte[]? data, bool enabled)
        {
            Assert.Equal(enabled, StartupService.ParseApproved(data));
        }

        [Fact]
        public void BuildApproved_WritesFlagAndTimestamp()
        {
            var t = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
            var off = StartupService.BuildApproved(false, t);
            Assert.Equal(12, off.Length);
            Assert.Equal(0x03, off[0]);
            Assert.Equal(t.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
            Assert.Equal(0x02, StartupService.BuildApproved(true, t)[0]);
        }

        [Theory]
        [InlineData("\"C:\\Program Files\\App\\app.exe\" -silent", @"C:\Program Files\App\app.exe")]
        [InlineData(@"C:\Program Files\App\app.exe -silent", @"C:\Program Files\App\app.exe")]
        [InlineData("rundll32.exe shell32.dll,Something", "rundll32.exe")]
        [InlineData("", null)]
        public void ExtractExecutable_ForDisplay(string command, string? expected)
        {
            Assert.Equal(expected, StartupService.ExtractExecutable(command));
        }

        [Fact]
        public void MissingProgram_IsFlagged()
        {
            var e = new StartupEntry
            {
                Name = "Gone", Command = @"C:\does-not-exist\gone.exe", Source = StartupSource.RegistryUser,
                Location = "x", ApprovedKey = "x", ApprovedValueName = "Gone", ExecutablePath = @"C:\does-not-exist\gone.exe", Enabled = true
            };
            StartupService.FillDetails(e);
            Assert.False(e.TargetExists);
            Assert.Null(e.Publisher);
        }
    }

    [Collection("Serial")]
    public class CleanupPlannerTests
    {
        private static CleanupCategory Cat(string id, string path, bool selected) => new()
        {
            Id = id, Name = id, Description = id, Selected = selected,
            Targets = new[] { new CleanupTarget(id, path, null) }
        };

        [Fact]
        public async Task OnlySelectedCategories_AreCleaned()
        {
            using var t = new TempDirectory();
            string a = t.File(@"a\one.tmp", "11");
            string b = t.File(@"b\two.tmp", "22");
            int recycleCalls = 0;
            var planner = new CleanupPlanner(() => Result<(long, long)>.Ok((5, 1)), () => { recycleCalls++; return Result<bool>.Ok(true); });
            var cats = new List<CleanupCategory>
            {
                Cat("a", Path.Combine(t.Path, "a"), true),
                Cat("b", Path.Combine(t.Path, "b"), false),
                new() { Id = "recycle_bin", Name = "rb", Description = "rb", Kind = CleanupKind.RecycleBin, Selected = false }
            };

            await planner.ScanAsync(cats, null, CancellationToken.None);
            Assert.Equal(2, cats[0].MeasuredBytes);
            Assert.Equal(5, cats[2].MeasuredBytes);

            var results = await planner.ExecuteAsync(cats, null, CancellationToken.None);

            Assert.Single(results);
            Assert.False(File.Exists(a));
            Assert.True(File.Exists(b));          // not selected
            Assert.Equal(0, recycleCalls);         // Recycle Bin never emptied unless selected
        }

        [Fact]
        public async Task SelectedRecycleBin_UsesTheRecycleBinAction()
        {
            int calls = 0;
            var planner = new CleanupPlanner(() => Result<(long, long)>.Ok((100, 2)), () => { calls++; return Result<bool>.Ok(true); });
            var cats = new List<CleanupCategory> { new() { Id = "rb", Name = "rb", Description = "rb", Kind = CleanupKind.RecycleBin, Selected = true } };
            await planner.ScanAsync(cats, null, CancellationToken.None);
            var r = await planner.ExecuteAsync(cats, null, CancellationToken.None);
            Assert.Equal(1, calls);
            Assert.Equal(CategoryOutcome.Completed, r[0].Outcome);
        }

        [Fact]
        public async Task Cancellation_StopsAndKeepsPartialResults()
        {
            using var t = new TempDirectory();
            for (int i = 0; i < 3; i++) t.File($@"first\f{i}.tmp", "x");
            t.File(@"second\s.tmp", "x");
            using var cts = new CancellationTokenSource();
            var planner = new CleanupPlanner(() => Result<(long, long)>.Ok((0, 0)), () => Result<bool>.Ok(true));
            var cats = new List<CleanupCategory>
            {
                Cat("first", Path.Combine(t.Path, "first"), true),
                Cat("second", Path.Combine(t.Path, "second"), true)
            };

            // Cancel as soon as the first category has started.
            var progress = new SyncProgress(msg => cts.Cancel());
            var results = await planner.ExecuteAsync(cats, progress, cts.Token);

            Assert.Equal(CategoryOutcome.Cancelled, results[0].Outcome);
            Assert.Equal(CategoryOutcome.NotRun, results[1].Outcome);
            Assert.True(File.Exists(Path.Combine(t.Path, "second", "s.tmp")));
        }

        [Fact]
        public async Task FailingCategory_IsReported_OthersStillRun()
        {
            using var t = new TempDirectory();
            string keep = t.File(@"ok\x.tmp", "x");
            var planner = new CleanupPlanner(() => Result<(long, long)>.Ok((0, 0)), () => Result<bool>.Fail("boom"));
            var cats = new List<CleanupCategory>
            {
                new() { Id = "rb", Name = "rb", Description = "rb", Kind = CleanupKind.RecycleBin, Selected = true },
                Cat("ok", Path.Combine(t.Path, "ok"), true)
            };
            var r = await planner.ExecuteAsync(cats, null, CancellationToken.None);
            Assert.Equal(CategoryOutcome.Failed, r[0].Outcome);
            Assert.Equal("boom", r[0].Error);
            Assert.Equal(CategoryOutcome.Completed, r[1].Outcome);
            Assert.False(File.Exists(keep));
        }

        [Fact]
        public void DefaultCategories_AreSafe()
        {
            var cats = CleanupPlanner.DefaultCategories(k => k);
            Assert.False(cats.Single(c => c.Id == "recycle_bin").Selected); // never selected by default
            var protectedDirs = Validator.GetProtectedDirectories();
            foreach (var target in cats.SelectMany(c => c.Targets))
                Assert.DoesNotContain(protectedDirs, p => Validator.IsUnderDirectory(target.Path, p) ||
                    string.Equals(Path.GetFullPath(p).TrimEnd('\\'), target.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>IProgress that runs synchronously (Progress&lt;T&gt; posts asynchronously).</summary>
        private sealed class SyncProgress : IProgress<string>
        {
            private readonly Action<string> _a;
            public SyncProgress(Action<string> a) => _a = a;
            public void Report(string value) => _a(value);
        }
    }

    public class MemoryCompressionTests
    {
        [Theory]
        [InlineData("True\r\n", true)]
        [InlineData("False", false)]
        [InlineData("", null)]
        [InlineData("Access is denied", null)]
        public void PowerShellOutput_IsParsedStrictly(string output, bool? expected)
        {
            Assert.Equal(expected, MemoryManager.ParsePowerShellBool(output));
        }

        [Fact]
        public async Task State_CanBeReadWithoutAdministrator()
        {
            var r = await MemoryManager.GetMemoryCompressionAsync();
            Assert.True(r.Success, r.ErrorMessage);
            if (!ElevationHelper.IsAdministrator)
                Assert.Equal(MemoryManager.CompressionSource.CompressionProcess, r.Data.Source);
        }

        [Fact]
        public void OrgManagementDetection_DoesNotThrow()
        {
            _ = OrgManagement.IsManaged;
            _ = OrgManagement.IsDomainJoined;
            _ = OrgManagement.IsMdmEnrolled;
        }
    }
}
