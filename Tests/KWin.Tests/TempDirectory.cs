using System.Diagnostics;

namespace KWin.Tests
{
    /// <summary>A unique folder under the test's temp directory, deleted on dispose.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kwin-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string relative, string content = "x", DateTime? lastWriteUtc = null)
        {
            string full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, content);
            if (lastWriteUtc != null) System.IO.File.SetLastWriteTimeUtc(full, lastWriteUtc.Value);
            return full;
        }

        /// <summary>Creates an NTFS junction (works without administrator rights).</summary>
        public static void CreateJunction(string link, string target)
        {
            var psi = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("mklink");
            psi.ArgumentList.Add("/J");
            psi.ArgumentList.Add(link);
            psi.ArgumentList.Add(target);
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            if (p.ExitCode != 0)
                throw new InvalidOperationException("mklink /J failed: " + p.StandardError.ReadToEnd());
        }

        public void Dispose()
        {
            try
            {
                // Remove junctions first without following them.
                foreach (var d in Directory.EnumerateDirectories(Path, "*", new EnumerationOptions
                         { RecurseSubdirectories = true, AttributesToSkip = 0 }))
                {
                    var info = new DirectoryInfo(d);
                    if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        info.Delete(false);
                }
            }
            catch { }
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
