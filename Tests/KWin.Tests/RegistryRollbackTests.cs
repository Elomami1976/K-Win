using KWin.Core;
using KWin.Models;
using KWin.Services;
using Microsoft.Win32;
using Xunit;

namespace KWin.Tests
{
    /// <summary>
    /// Registry tests run only inside a throwaway key: HKCU\Software\K-win-Tests\{guid}.
    /// The key is deleted afterwards. Nothing outside it is touched.
    /// </summary>
    [Collection("Serial")]
    public sealed class RegistryRollbackTests : IDisposable
    {
        private readonly string _sub = $@"Software\K-win-Tests\{Guid.NewGuid():N}";
        private readonly TempDirectory _dir = new();
        private readonly RegistryHelper _reg;

        private string Key(string rel = "") => rel.Length == 0 ? $@"HKCU\{_sub}" : $@"HKCU\{_sub}\{rel}";

        public RegistryRollbackTests()
        {
            _reg = new RegistryHelper(Path.Combine(_dir.Path, "backups"));
            // The shared parent exists up front, so the test key is the first key K-Win creates.
            Registry.CurrentUser.CreateSubKey(@"Software\K-win-Tests")?.Dispose();
        }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(_sub, false); } catch { }
            try
            {
                using var parent = Registry.CurrentUser.OpenSubKey(@"Software\K-win-Tests", true);
                if (parent != null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
                    Registry.CurrentUser.DeleteSubKey(@"Software\K-win-Tests", false);
            }
            catch { }
            _dir.Dispose();
        }

        private RegistryChange SetAndCapture(string key, string name, object value, RegistryValueKind kind)
        {
            RegistryChange? captured = null;
            void Handler(RegistryChange c) => captured = c;
            _reg.ChangeRecorded += Handler;
            var r = _reg.SetValueWithBackup(key, name, value, kind);
            _reg.ChangeRecorded -= Handler;
            Assert.True(r.Success, r.ErrorMessage);
            Assert.NotNull(captured);
            return captured!;
        }

        [Fact]
        public async Task NewValueInNewKeys_RevertDeletesValueAndCreatedKeys()
        {
            var change = SetAndCapture(Key(@"a\b\c"), "Setting", 1, RegistryValueKind.DWord);

            Assert.False(change.KeyExisted);
            Assert.False(change.Before.Exists);
            Assert.Equal(Key(), change.CreatedKeyRoot, ignoreCase: true);
            Assert.Equal(1, Registry.CurrentUser.OpenSubKey($@"{_sub}\a\b\c")!.GetValue("Setting"));

            var undo = await _reg.RevertChangeAsync(change);

            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.Null(Registry.CurrentUser.OpenSubKey(_sub)); // every key K-Win created is gone
        }

        [Fact]
        public async Task CreatedKeys_AreKeptIfSomethingElseWasAddedLater()
        {
            var change = SetAndCapture(Key(@"a\b"), "Setting", 1, RegistryValueKind.DWord);
            using (var k = Registry.CurrentUser.CreateSubKey($@"{_sub}\a"))
                k.SetValue("OtherProgram", "keep");

            var undo = await _reg.RevertChangeAsync(change);

            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_sub}\a\b"));
            Assert.Equal("keep", Registry.CurrentUser.OpenSubKey($@"{_sub}\a")!.GetValue("OtherProgram"));
        }

        [Fact]
        public async Task ExistingValue_IsBackedUpAndRestoredWithItsType()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(_sub))
                k.SetValue("Path", @"%SystemRoot%\x", RegistryValueKind.ExpandString);

            var change = SetAndCapture(Key(), "Path", "plain", RegistryValueKind.String);

            Assert.True(change.KeyExisted);
            Assert.True(File.Exists(change.BackupFile), "verified .reg backup must exist");
            Assert.True(new FileInfo(change.BackupFile!).Length > 0);

            var undo = await _reg.RevertChangeAsync(change);
            Assert.True(undo.Success, undo.ErrorMessage);

            using var key = Registry.CurrentUser.OpenSubKey(_sub)!;
            Assert.Equal(RegistryValueKind.ExpandString, key.GetValueKind("Path"));
            Assert.Equal(@"%SystemRoot%\x", key.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }

        [Theory]
        [InlineData(RegistryValueKind.MultiString)]
        [InlineData(RegistryValueKind.Binary)]
        [InlineData(RegistryValueKind.QWord)]
        public async Task OtherValueKinds_RoundTrip(RegistryValueKind kind)
        {
            object original = kind switch
            {
                RegistryValueKind.MultiString => new[] { "one", "two", "ünïcødé" },
                RegistryValueKind.Binary => new byte[] { 0, 1, 2, 255 },
                _ => 1234567890123L
            };
            using (var k = Registry.CurrentUser.CreateSubKey(_sub))
                k.SetValue("V", original, kind);

            var change = SetAndCapture(Key(), "V", 7, RegistryValueKind.DWord);
            var undo = await _reg.RevertChangeAsync(change);
            Assert.True(undo.Success, undo.ErrorMessage);

            using var key = Registry.CurrentUser.OpenSubKey(_sub)!;
            Assert.Equal(kind, key.GetValueKind("V"));
            Assert.Equal(original, key.GetValue("V"));
        }

        [Fact]
        public async Task DeletedValue_IsRestored()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(_sub))
                k.SetValue("StartupApp", "\"C:\\App\\app.exe\" --tray");

            RegistryChange? change = null;
            _reg.ChangeRecorded += c => change = c;
            var r = _reg.DeleteValueWithBackup(Key(), "StartupApp");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Null(Registry.CurrentUser.OpenSubKey(_sub)!.GetValue("StartupApp"));

            var undo = await _reg.RevertChangeAsync(change!);
            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.Equal("\"C:\\App\\app.exe\" --tray", Registry.CurrentUser.OpenSubKey(_sub)!.GetValue("StartupApp"));
        }

        [Fact]
        public async Task DeletedKeyTree_IsRestoredFromVerifiedBackup()
        {
            using (var k = Registry.CurrentUser.CreateSubKey($@"{_sub}\tree\child"))
                k.SetValue("", "");

            RegistryChange? change = null;
            _reg.ChangeRecorded += c => change = c;
            var r = _reg.DeleteKeyTreeWithBackup(Key("tree"));
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_sub}\tree"));

            var undo = await _reg.RevertChangeAsync(change!);
            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.NotNull(Registry.CurrentUser.OpenSubKey($@"{_sub}\tree\child"));
        }

        [Fact]
        public void DeletingMissingValue_IsANoOp()
        {
            int events = 0;
            _reg.ChangeRecorded += _ => events++;
            var r = _reg.DeleteValueWithBackup(Key("nothing-here"), "nope");
            Assert.True(r.Success);
            Assert.Equal(0, events);
        }

        [Theory]
        [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa")]
        [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy")]
        [InlineData(@"HKCR\exefile\shell\open\command")]
        [InlineData(@"HKU\.DEFAULT\Software")]
        [InlineData("")]
        public void BlockedOrInvalidPaths_AreRefused_AndReportedAsFailures(string path)
        {
            string? failure = null;
            _reg.ChangeFailed += (target, reason) => failure = reason;
            var r = _reg.SetValueWithBackup(path, "x", 1, RegistryValueKind.DWord);
            Assert.False(r.Success);
            Assert.NotNull(failure);
        }

        [Fact]
        public async Task SafetyEngine_CapturesChanges_PersistsUndo_AndUndoesAfterRestart()
        {
            string data = Path.Combine(_dir.Path, "data");
            var engine = new SafetyEngine(_reg, data);

            engine.BeginOperation();
            _reg.SetValueWithBackup(Key(), "One", 1, RegistryValueKind.DWord);
            _reg.SetValueWithBackup(Key(), "Two", "2", RegistryValueKind.String);
            var capture = engine.EndOperation("Test operation"); // no explicit PushUndoAction: auto entry

            Assert.Equal(2, capture.Changes.Count);
            Assert.NotNull(capture.UndoId);
            Assert.Empty(capture.Failures);
            Assert.True(File.Exists(Path.Combine(data, "undo.json")));

            // Simulate an app restart: a fresh engine loads the saved undo history.
            var reloaded = new SafetyEngine(new RegistryHelper(Path.Combine(_dir.Path, "backups")), data);
            Assert.Equal(1, reloaded.UndoCount);
            Assert.Equal("Test operation", reloaded.LastUndoDescription);

            var undo = await reloaded.UndoLastAction();
            Assert.True(undo.Success, undo.ErrorMessage);
            Assert.Null(Registry.CurrentUser.OpenSubKey(_sub)); // values and created key removed
            Assert.Equal(0, new SafetyEngine(_reg, data).UndoCount);
        }

        [Fact]
        public async Task SafetyEngine_SessionOnlyStep_IsReportedAsPartialAfterRestart()
        {
            string data = Path.Combine(_dir.Path, "data2");
            var engine = new SafetyEngine(_reg, data);

            engine.BeginOperation();
            _reg.SetValueWithBackup(Key(), "Plan", 1, RegistryValueKind.DWord);
            engine.PushUndoAction(new UndoAction
            {
                Description = "With session step",
                UndoDelegate = () => Task.FromResult(Result<bool>.Ok(true))
            });
            var capture = engine.EndOperation("With session step");
            Assert.True(capture.HasSessionStep);

            var reloaded = new SafetyEngine(_reg, data);
            Assert.True(reloaded.PeekUndo()!.IsPartial);

            var undo = await reloaded.UndoLastAction();
            Assert.False(undo.Success); // honest: the non-registry part could not be undone
            Assert.Null(Registry.CurrentUser.OpenSubKey(_sub)?.GetValue("Plan")); // registry part was reverted
        }

        [Fact]
        public void SafetyEngine_RecordsFailuresDuringOperation()
        {
            var engine = new SafetyEngine(_reg, Path.Combine(_dir.Path, "data3"));
            engine.BeginOperation();
            _reg.SetValueWithBackup(@"HKLM\SYSTEM\CurrentControlSet\Control\Lsa", "x", 1, RegistryValueKind.DWord);
            var capture = engine.EndOperation("Blocked");
            Assert.Single(capture.Failures);
            Assert.Empty(capture.Changes);
            Assert.Null(capture.UndoId);
        }

        [Fact]
        public void SafetyEngine_CorruptUndoFile_IsQuarantined()
        {
            string data = Path.Combine(_dir.Path, "data4");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "undo.json"), "{ not json");

            var engine = new SafetyEngine(_reg, data);

            Assert.Equal(0, engine.UndoCount);
            Assert.NotEmpty(Directory.GetFiles(data, "undo.json.corrupt-*"));
        }

        [Fact]
        public void OperationHistory_PersistsAndCaps()
        {
            string data = Path.Combine(_dir.Path, "hist");
            var history = new OperationHistoryService(data);
            for (int i = 0; i < OperationHistoryService.MaxEntries + 5; i++)
                history.Add(new OperationRecord { Name = $"op{i}", Status = OperationStatus.Completed });

            var reloaded = new OperationHistoryService(data);
            Assert.Equal(OperationHistoryService.MaxEntries, reloaded.Count);
            Assert.Equal($"op{OperationHistoryService.MaxEntries + 4}", reloaded.GetRecent(1)[0].Name);
        }
    }
}
