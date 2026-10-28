using KWin.Core;
using KWin.Utils;
using Xunit;

namespace KWin.Tests
{
    [Collection("Serial")]
    public class CleanupSafetyTests
    {
        [Theory]
        [InlineData(@"C:\Temp\a.txt", @"C:\Temp", true)]
        [InlineData(@"C:\Temp\sub\a.txt", @"C:\Temp\", true)]
        [InlineData(@"C:\Temp2\a.txt", @"C:\Temp", false)]   // prefix trick
        [InlineData(@"C:\Temp", @"C:\Temp", false)]          // the root itself
        [InlineData(@"C:\Temp\..\Windows\a.txt", @"C:\Temp", false)] // traversal
        [InlineData(@"c:\temp\A.TXT", @"C:\Temp", true)]     // case-insensitive
        public void IsUnderDirectory_ComparesWholeSegments(string path, string dir, bool expected)
        {
            Assert.Equal(expected, Validator.IsUnderDirectory(path, dir));
        }

        [Fact]
        public void IsSafeToDelete_RefusesFilesOutsideAllowedRoots()
        {
            using var t = new TempDirectory();
            string inside = t.File(@"cache\a.bin");
            string outside = t.File(@"other\b.bin");
            string root = Path.Combine(t.Path, "cache");

            Assert.True(Validator.IsSafeToDelete(inside, new[] { root }, out _));
            Assert.False(Validator.IsSafeToDelete(outside, new[] { root }, out _));
        }

        [Fact]
        public void IsSafeToDelete_RefusesProtectedFolders_EvenIfAllowed()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string sys32 = Environment.SystemDirectory;

            // Even a misconfigured target covering the whole profile cannot reach user files.
            Assert.False(Validator.IsSafeToDelete(Path.Combine(docs, "report.docx"), new[] { profile }, out _));
            Assert.False(Validator.IsSafeToDelete(Path.Combine(desktop, "photo.jpg"), new[] { profile }, out _));
            Assert.False(Validator.IsSafeToDelete(Path.Combine(profile, "Downloads", "setup.exe"), new[] { profile }, out _));
            Assert.False(Validator.IsSafeToDelete(Path.Combine(sys32, "kernel32.dll"), new[] { @"C:\" }, out _));
        }

        [Fact]
        public void Walker_DoesNotFollowJunctions()
        {
            using var t = new TempDirectory();
            string precious = t.File(@"precious\keep-me.txt");
            t.File(@"cache\junk.tmp");
            TempDirectory.CreateJunction(Path.Combine(t.Path, "cache", "link"), Path.Combine(t.Path, "precious"));

            var files = SafeFileWalker.EnumerateFiles(Path.Combine(t.Path, "cache")).Select(f => f.FullName).ToList();

            Assert.Single(files);
            Assert.EndsWith("junk.tmp", files[0]);
            Assert.DoesNotContain(files, f => f.EndsWith("keep-me.txt"));
        }

        [Fact]
        public void Clean_DeletesOnlyTargetFiles_AndNeverFilesBehindAJunction()
        {
            using var t = new TempDirectory();
            string precious = t.File(@"precious\keep-me.txt", "important");
            string junk1 = t.File(@"cache\junk1.tmp", "aaaa");
            string junk2 = t.File(@"cache\deep\junk2.tmp", "bbbbbb");
            string cacheRoot = Path.Combine(t.Path, "cache");
            TempDirectory.CreateJunction(Path.Combine(cacheRoot, "link"), Path.Combine(t.Path, "precious"));

            var report = CleanupManager.CleanTargets(new[] { new CleanupTarget("test", cacheRoot, null) });

            Assert.True(File.Exists(precious));                     // reachable only through the junction
            Assert.False(File.Exists(junk1));
            Assert.False(File.Exists(junk2));
            Assert.Equal(2, report.FilesDeleted);
            Assert.Equal(10, report.BytesFreed);
            Assert.True(Directory.Exists(cacheRoot));               // the target folder itself is kept
            Assert.False(Directory.Exists(Path.Combine(cacheRoot, "deep"))); // emptied subfolder removed
        }

        [Fact]
        public void Clean_RespectsMinimumAge_AndPreviewMatchesCleanup()
        {
            using var t = new TempDirectory();
            string old = t.File("old.tmp", "12345", DateTime.UtcNow.AddDays(-3));
            string fresh = t.File("fresh.tmp", "1234567890");
            var target = new CleanupTarget("test", t.Path, TimeSpan.FromHours(24));

            var (estimate, _) = CleanupManager.Measure(new[] { target });
            var report = CleanupManager.CleanTargets(new[] { target });

            Assert.Equal(5, estimate);                 // preview counts only eligible files
            Assert.Equal(estimate, report.BytesFreed); // ...and matches what was freed
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
        }

        [Fact]
        public void Clean_CountsLockedFilesAsSkipped()
        {
            using var t = new TempDirectory();
            string locked = t.File("locked.tmp", "abc");
            t.File("free.tmp", "abc");

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var report = CleanupManager.CleanTargets(new[] { new CleanupTarget("test", t.Path, null) });
                Assert.Equal(1, report.FilesDeleted);
                Assert.Equal(1, report.FilesSkipped);
            }
            Assert.True(File.Exists(locked));
        }

        [Fact]
        public void Clean_MissingFolder_IsIgnored()
        {
            var report = CleanupManager.CleanTargets(new[]
            {
                new CleanupTarget("missing", Path.Combine(Path.GetTempPath(), "kwin-does-not-exist-" + Guid.NewGuid()), null)
            });
            Assert.Equal(0, report.FilesDeleted);
        }

        [Fact]
        public void TempTargets_DoNotIncludeUserFolders()
        {
            var protectedDirs = Validator.GetProtectedDirectories();
            foreach (var target in CleanupManager.GetTempTargets().Concat(CleanupManager.GetBrowserTargets()))
            {
                Assert.DoesNotContain(protectedDirs, p =>
                    string.Equals(Path.GetFullPath(p).TrimEnd('\\'), target.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                    Validator.IsUnderDirectory(target.Path, p));
            }
        }
    }
}
