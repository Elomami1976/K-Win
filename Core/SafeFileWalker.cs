namespace KWin.Core
{
    /// <summary>
    /// Enumerates files under a folder without ever following junctions or symbolic links
    /// (reparse points). A link inside a temp folder that points to, say, Documents is skipped
    /// instead of being walked into, so cleanup can never reach files outside the target folder.
    /// Unreadable folders are skipped silently.
    /// </summary>
    public static class SafeFileWalker
    {
        public static bool IsReparsePoint(FileSystemInfo info)
        {
            try { return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint); }
            catch { return true; } // if we can't tell, treat it as unsafe
        }

        /// <summary>Yields every regular (non-link) file below <paramref name="root"/>.</summary>
        public static IEnumerable<FileInfo> EnumerateFiles(string root)
        {
            var rootInfo = new DirectoryInfo(root);
            if (!rootInfo.Exists || IsReparsePoint(rootInfo))
                yield break;

            var stack = new Stack<DirectoryInfo>();
            stack.Push(rootInfo);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                FileSystemInfo[] entries;
                try
                {
                    entries = dir.GetFileSystemInfos();
                }
                catch
                {
                    continue; // access denied, deleted meanwhile, etc.
                }

                foreach (var entry in entries)
                {
                    if (IsReparsePoint(entry))
                        continue; // never follow or touch links

                    if (entry is DirectoryInfo sub)
                        stack.Push(sub);
                    else if (entry is FileInfo file)
                        yield return file;
                }
            }
        }

        /// <summary>
        /// Yields real (non-link) subdirectories below <paramref name="root"/>, deepest first,
        /// so they can be removed bottom-up once empty. The root itself is not included.
        /// </summary>
        public static IEnumerable<DirectoryInfo> EnumerateDirectoriesDeepestFirst(string root)
        {
            var rootInfo = new DirectoryInfo(root);
            if (!rootInfo.Exists || IsReparsePoint(rootInfo))
                return Array.Empty<DirectoryInfo>();

            var all = new List<(DirectoryInfo Dir, int Depth)>();
            var stack = new Stack<(DirectoryInfo, int)>();
            stack.Push((rootInfo, 0));
            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                DirectoryInfo[] subs;
                try { subs = dir.GetDirectories(); }
                catch { continue; }

                foreach (var sub in subs)
                {
                    if (IsReparsePoint(sub)) continue;
                    all.Add((sub, depth + 1));
                    stack.Push((sub, depth + 1));
                }
            }
            return all.OrderByDescending(d => d.Depth).Select(d => d.Dir);
        }

        /// <summary>Total size of regular files below <paramref name="root"/> (links excluded).</summary>
        public static long GetSize(string root, Func<FileInfo, bool>? filter = null)
        {
            long size = 0;
            foreach (var f in EnumerateFiles(root))
            {
                try
                {
                    if (filter == null || filter(f))
                        size += f.Length;
                }
                catch { /* file vanished */ }
            }
            return size;
        }
    }
}
