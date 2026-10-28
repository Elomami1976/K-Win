namespace KWin.Utils
{
    /// <summary>Locations of K-Win's own data files, plus small file helpers.</summary>
    public static class AppPaths
    {
        /// <summary>%AppData%\K-win</summary>
        public static string DataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-win");

        /// <summary>
        /// Writes a file through a temporary file and a replace, so a crash never leaves a half-written file.
        /// </summary>
        public static void WriteAllTextAtomic(string path, string contents)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        /// <summary>Renames an unreadable file to *.corrupt-&lt;timestamp&gt; so it is kept but no longer loaded.</summary>
        public static void QuarantineCorruptFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
            catch { /* best effort */ }
        }
    }
}
