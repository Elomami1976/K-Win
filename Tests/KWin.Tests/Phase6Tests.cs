using System.Net;
using System.Text;
using KWin.Core;
using KWin.Models;
using KWin.Services;
using Microsoft.Win32;
using Xunit;

namespace KWin.Tests
{
    public class RecommendationTests
    {
        private static SettingDefinition Def(string id, bool? recommendOn, RiskLevel risk = RiskLevel.Low, int minBuild = 0, bool policy = false) => new()
        {
            Id = id,
            Group = SettingGroup.Performance,
            RecommendOn = recommendOn,
            Risk = risk,
            MinBuild = minBuild,
            On = new[] { new RegistryTweak(policy ? @"HKLM\SOFTWARE\Policies\Test" : @"HKCU\Software\Test", id, 1) },
            Off = new[] { new RegistryTweak(policy ? @"HKLM\SOFTWARE\Policies\Test" : @"HKCU\Software\Test", id, 0) }
        };

        [Fact]
        public void Analyze_AppliesEligibilityRules()
        {
            var pref = Def("pref", null);
            var done = Def("done", true);
            var low = Def("low", true);
            var medium = Def("medium", true, RiskLevel.Medium);
            var high = Def("high", false, RiskLevel.High);
            var custom = Def("custom", true);
            var future = Def("future", true, minBuild: 99999);
            var locked = Def("locked", true, policy: true);
            var unknown = Def("unknown", true);

            var state = new Dictionary<string, SettingState>
            {
                ["pref"] = SettingState.Off, ["done"] = SettingState.On, ["low"] = SettingState.Off,
                ["medium"] = SettingState.Off, ["high"] = SettingState.On, ["custom"] = SettingState.Custom,
                ["future"] = SettingState.Off, ["locked"] = SettingState.Off, ["unknown"] = SettingState.Unknown
            };

            var r = RecommendationEngine.Analyze(
                new[] { pref, done, low, medium, high, custom, future, locked, unknown },
                d => state[d.Id],
                d => d.Id == "locked" ? LockReason.Organization : LockReason.None,
                windowsBuild: 22631);

            Assert.Equal(new[] { "low", "custom", "medium", "high" }, r.Recommendations.Select(x => x.Setting.Id)); // sorted by risk
            Assert.Contains(pref, r.Preferences);               // preferences are never "optimizations"
            Assert.Contains(done, r.AlreadyRecommended);
            Assert.Contains(r.Skipped, s => s.Setting == future && s.Reason == SkipReason.NotApplicable);
            Assert.Contains(r.Skipped, s => s.Setting == locked && s.Reason == SkipReason.LockedByOrganization);
            Assert.Contains(r.Skipped, s => s.Setting == unknown && s.Reason == SkipReason.StateUnknown);

            Assert.True(r.Recommendations.Single(x => x.Setting.Id == "low").Selected);
            Assert.False(r.Recommendations.Single(x => x.Setting.Id == "medium").Selected);   // not pre-selected
            Assert.False(r.Recommendations.Single(x => x.Setting.Id == "high").Selected);     // never pre-selected
            var c = r.Recommendations.Single(x => x.Setting.Id == "custom");
            Assert.False(c.Selected);                                                        // changed elsewhere
            Assert.True(c.ChangedOutsideKWin);
            Assert.False(r.Recommendations.Single(x => x.Setting.Id == "high").TargetOn);
        }

        [Theory]
        [InlineData(RiskLevel.Low, SettingState.Off, true)]
        [InlineData(RiskLevel.Low, SettingState.Custom, false)]
        [InlineData(RiskLevel.Medium, SettingState.Off, false)]
        [InlineData(RiskLevel.High, SettingState.Off, false)]
        public void DefaultSelection(RiskLevel risk, SettingState state, bool selected) =>
            Assert.Equal(selected, RecommendationEngine.DefaultSelected(risk, state));

        [Fact]
        public void Catalog_RiskClassification()
        {
            Assert.Equal(RiskLevel.Medium, SettingCatalog.Telemetry.Risk);
            Assert.Equal(RiskLevel.Medium, SettingCatalog.FastStartup.Risk);
            Assert.Equal(RiskLevel.Medium, SettingCatalog.SysMain.Risk);
            Assert.Equal(RiskLevel.Low, SettingCatalog.AdvertisingId.Risk);
            // Every setting with a non-registry step is at least Medium.
            foreach (var id in new[] { "telemetry", "fast_startup", "sysmain" })
                Assert.NotEqual(RiskLevel.Low, SettingCatalog.All.Single(d => d.Id == id).Risk);
            // Preferences carry no recommendation.
            Assert.Null(SettingCatalog.DarkMode.RecommendOn);
            Assert.Null(SettingCatalog.VisualEffects.RecommendOn);
        }

        [Fact]
        public async Task Apply_OnlySelected_PartialFailureDoesNotStopOthers()
        {
            Recommendation Rec(string id, bool selected) => new() { Setting = Def(id, true), Current = SettingState.Off, TargetOn = true, Selected = selected };
            var recs = new[] { Rec("a", true), Rec("b", false), Rec("c", true), Rec("d", true) };
            var applied = new List<string>();

            var results = await RecommendationEngine.ApplyAsync(recs, rec =>
            {
                applied.Add(rec.Setting.Id);
                return rec.Setting.Id switch
                {
                    "c" => Task.FromResult<(bool, string?, string?)>((false, "write refused", null)),
                    "d" => throw new InvalidOperationException("boom"),
                    _ => Task.FromResult<(bool, string?, string?)>((true, null, "service warning"))
                };
            });

            Assert.Equal(new[] { "a", "c", "d" }, applied); // b not selected: never touched
            Assert.Equal(ItemOutcome.Applied, results[0].Outcome);
            Assert.Equal("service warning", results[0].Warning);
            Assert.Equal(ItemOutcome.Failed, results[1].Outcome);
            Assert.Equal("write refused", results[1].Error);
            Assert.Equal(ItemOutcome.Failed, results[2].Outcome);
            Assert.Equal("boom", results[2].Error);
        }

        [Fact]
        public async Task Apply_Cancelled_LeavesRemainingNotRun()
        {
            using var cts = new CancellationTokenSource();
            Recommendation Rec(string id) => new() { Setting = Def(id, true), Current = SettingState.Off, TargetOn = true, Selected = true };
            var results = await RecommendationEngine.ApplyAsync(new[] { Rec("a"), Rec("b") }, rec =>
            {
                cts.Cancel();
                return Task.FromResult<(bool, string?, string?)>((true, null, null));
            }, cts.Token);
            Assert.Equal(ItemOutcome.Applied, results[0].Outcome);
            Assert.Equal(ItemOutcome.NotRun, results[1].Outcome);
        }
    }

    public class PolicyLockTests
    {
        private static byte[] Pol(params (string Key, string Value)[] entries)
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("PReg"));
            ms.Write(BitConverter.GetBytes(1));
            void W(string s) => ms.Write(Encoding.Unicode.GetBytes(s));
            foreach (var (key, value) in entries)
            {
                byte[] data = BitConverter.GetBytes(1);
                W("["); W(key); W("\0"); W(";"); W(value); W("\0"); W(";");
                ms.Write(BitConverter.GetBytes(4)); W(";");               // REG_DWORD
                ms.Write(BitConverter.GetBytes(data.Length)); W(";");
                ms.Write(data); W("]");
            }
            return ms.ToArray();
        }

        [Fact]
        public void RegistryPol_IsParsed()
        {
            var entries = PolicyLocks.ParseRegistryPol(Pol(
                (@"Software\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry"),
                (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt")));
            Assert.Equal(new[]
            {
                @"Software\Policies\Microsoft\Windows\DataCollection\AllowTelemetry",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt"
            }, entries);
        }

        [Fact]
        public void RegistryPol_BrokenData_IsIgnoredSafely()
        {
            Assert.Empty(PolicyLocks.ParseRegistryPol(Array.Empty<byte>()));
            Assert.Empty(PolicyLocks.ParseRegistryPol(Encoding.ASCII.GetBytes("NOPE1234")));
            var full = Pol((@"Software\Policies\X", "Y"));
            PolicyLocks.ParseRegistryPol(full[..(full.Length - 5)]); // truncated: must not throw
        }

        [Fact]
        public void LocalPolicy_LocksMatchingSettings_ForTheRightHive()
        {
            var locks = new PolicyLocks(
                machinePolicyValues: new[] { @"SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry" },
                userPolicyValues: new[] { @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideFileExt" },
                orgManaged: false);

            Assert.Equal(LockReason.LocalGroupPolicy, locks.Check(SettingCatalog.Telemetry));
            Assert.Equal(LockReason.LocalGroupPolicy, locks.Check(SettingCatalog.FileExtensions));
            Assert.Equal(LockReason.None, locks.Check(SettingCatalog.GameMode));
        }

        [Fact]
        public void OrganizationManagement_LocksPolicySettingsOnly()
        {
            var locks = new PolicyLocks(Array.Empty<string>(), Array.Empty<string>(), orgManaged: true);
            Assert.Equal(LockReason.Organization, locks.Check(SettingCatalog.AdvertisingId)); // Group Policy value
            Assert.Equal(LockReason.None, locks.Check(SettingCatalog.DarkMode));              // user preference
        }

        [Fact]
        public void LockedSetting_IsNeverRecommended()
        {
            var locks = new PolicyLocks(new[] { @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo\DisabledByGroupPolicy" }, Array.Empty<string>(), false);
            var r = RecommendationEngine.Analyze(new[] { SettingCatalog.AdvertisingId }, _ => SettingState.Off, locks.Check, 22631);
            Assert.Empty(r.Recommendations);
            Assert.Equal(SkipReason.LockedByLocalPolicy, r.Skipped.Single().Reason);
        }
    }

    [Collection("Serial")]
    public class UndoEligibilityTests
    {
        [Fact]
        public async Task OutOfOrderUndo_IsAllowedOnlyWithoutConflicts()
        {
            using var box = new RegistrySandbox();
            var safety = new SafetyEngine(box.Registry, Path.Combine(box.Dir.Path, "data"));
            string key = box.Map(@"HKCU\Software\Test");

            safety.BeginOperation();
            box.Registry.SetValueWithBackup(key, "Shared", 1, RegistryValueKind.DWord);
            var a = safety.EndOperation("A");
            safety.BeginOperation();
            box.Registry.SetValueWithBackup(key, "Other", 1, RegistryValueKind.DWord);
            var b = safety.EndOperation("B");
            safety.BeginOperation();
            box.Registry.SetValueWithBackup(key, "Shared", 2, RegistryValueKind.DWord);
            var c = safety.EndOperation("C");

            Assert.False(safety.CanUndo(a.UndoId, out var reason)); // C changed the same value later
            Assert.Equal("C", reason);
            Assert.True(safety.CanUndo(b.UndoId, out _));           // no overlap: allowed out of order
            Assert.False((await safety.UndoByIdAsync(a.UndoId!)).Success);

            Assert.True((await safety.UndoByIdAsync(b.UndoId!)).Success);
            using (var k = Registry.CurrentUser.OpenSubKey(key[5..]))
            {
                Assert.Null(k!.GetValue("Other"));
                Assert.Equal(2, k.GetValue("Shared"));                // C untouched
            }

            Assert.True((await safety.UndoByIdAsync(c.UndoId!)).Success);
            Assert.True(safety.CanUndo(a.UndoId, out _));            // conflict gone
            Assert.True((await safety.UndoByIdAsync(a.UndoId!)).Success);
            Assert.True(box.IsEmpty());

            Assert.False(safety.CanUndo(a.UndoId, out reason));      // already undone
            Assert.Equal("not_available", reason);
        }

        [Fact]
        public void DeletedKey_ConflictsWithValuesInsideIt()
        {
            using var box = new RegistrySandbox();
            var safety = new SafetyEngine(box.Registry, Path.Combine(box.Dir.Path, "data"));
            string key = box.Map(@"HKCU\Software\Ctx");
            using (var k = Registry.CurrentUser.CreateSubKey((key + @"\Sub")[5..])) k.SetValue("", "");

            safety.BeginOperation();
            box.Registry.SetValueWithBackup(key + @"\Sub", "V", 1, RegistryValueKind.DWord);
            var a = safety.EndOperation("A");
            safety.BeginOperation();
            box.Registry.DeleteKeyTreeWithBackup(key);
            safety.EndOperation("B");

            Assert.False(safety.CanUndo(a.UndoId, out var reason));
            Assert.Equal("B", reason);
        }
    }

    public class HistoryPersistenceTests
    {
        [Fact]
        public void UndoneMark_AndWarnings_ArePersisted()
        {
            using var dir = new TempDirectory();
            var h = new OperationHistoryService(dir.Path);
            h.Add(new OperationRecord { Name = "op", UndoId = "u1", Status = OperationStatus.Completed, Warnings = { "service did not restart" } });
            h.MarkUndone("u1");
            h.MarkUndone("missing"); // ignored

            var reloaded = new OperationHistoryService(dir.Path).GetRecent(1).Single();
            Assert.NotNull(reloaded.UndoneUtc);
            Assert.Equal("service did not restart", reloaded.Warnings.Single());
        }
    }

    public class UpdateCheckerTests
    {
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public HttpRequestMessage? Last;
            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Last = request;
                return Task.FromResult(_respond(request));
            }
        }

        private static string Release(string tag, string url = "https://github.com/Elomami1976/K-Win/releases/tag/v1.6.0", bool draft = false, bool pre = false) =>
            $$"""{"tag_name":"{{tag}}","name":"K-Win {{tag}}","body":"Notes","html_url":"{{url}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"published_at":"2026-11-01T10:00:00Z"}""";

        private static FakeHandler Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
            new(_ => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        [Theory]
        [InlineData("v1.5.0", "1.5.0.0")]
        [InlineData("1.5", "1.5.0.0")]
        [InlineData("K-Win-1.5.2", "1.5.2.0")]
        [InlineData("v2.0.1.7", "2.0.1.7")]
        [InlineData("v1.6.0-beta", "1.6.0.0")]
        [InlineData("latest", null)]
        [InlineData("", null)]
        public void ParseVersion(string tag, string? expected) =>
            Assert.Equal(expected, UpdateChecker.ParseVersion(tag)?.ToString());

        [Theory]
        [InlineData("1.5.0", "v1.6.0", true)]
        [InlineData("1.5.0", "v1.5.0", false)]
        [InlineData("1.5.0", "v1.5", false)]
        [InlineData("1.6.0", "v1.5.9", false)]
        [InlineData("1.5.0.0", "v1.5.0.1", true)]
        public async Task ComparesVersionsCorrectly(string current, string tag, bool newer)
        {
            var r = await new UpdateChecker(Json(Release(tag))).CheckAsync(new Version(current));
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(newer, r.Data!.UpdateAvailable);
        }

        [Fact]
        public async Task ReadsOnlyTheOfficialReleaseApi()
        {
            var handler = Json(Release("v1.6.0"));
            var r = await new UpdateChecker(handler).CheckAsync(new Version(1, 5, 0));
            Assert.Equal(HttpMethod.Get, handler.Last!.Method);
            Assert.Equal("https://api.github.com/repos/Elomami1976/K-Win/releases/latest", handler.Last.RequestUri!.ToString());
            Assert.Equal("Notes", r.Data!.Latest!.Notes);
            Assert.Equal(new Uri("https://github.com/Elomami1976/K-Win/releases/tag/v1.6.0"), r.Data.Latest.PageUrl);
        }

        [Theory]
        [InlineData("https://evil.example.com/Elomami1976/K-Win/releases/x")]
        [InlineData("http://github.com/Elomami1976/K-Win/releases/x")]
        [InlineData("https://github.com/someone-else/K-Win/releases/x")]
        public async Task NonOfficialReleasePages_AreRejected(string url)
        {
            var r = await new UpdateChecker(Json(Release("v9.0.0", url))).CheckAsync(new Version(1, 5));
            Assert.False(r.Success);
        }

        [Fact]
        public async Task DraftsAndPrereleases_AreIgnored()
        {
            Assert.False((await new UpdateChecker(Json(Release("v9.0.0", draft: true))).CheckAsync(new Version(1, 5))).Success);
            Assert.False((await new UpdateChecker(Json(Release("v9.0.0", pre: true))).CheckAsync(new Version(1, 5))).Success);
        }

        [Fact]
        public async Task OfflineAndApiErrors_FailGracefully()
        {
            var offline = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
            var r1 = await new UpdateChecker(offline).CheckAsync(new Version(1, 5));
            Assert.False(r1.Success);
            Assert.Contains("offline", r1.ErrorMessage);

            var r2 = await new UpdateChecker(Json("{}", HttpStatusCode.NotFound)).CheckAsync(new Version(1, 5));
            Assert.False(r2.Success);

            var r3 = await new UpdateChecker(Json("not json")).CheckAsync(new Version(1, 5));
            Assert.False(r3.Success);

            var r4 = await new UpdateChecker(Json("{}", HttpStatusCode.Forbidden)).CheckAsync(new Version(1, 5)); // rate limit
            Assert.False(r4.Success);
        }
    }
}
