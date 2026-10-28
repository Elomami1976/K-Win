using System.Text.RegularExpressions;
using KWin.Models;

namespace KWin.Core
{
    /// <summary>A parsed, validated uninstall command that is run directly (never through a shell).</summary>
    public sealed record UninstallCommand(string FileName, string Arguments, bool IsMsi);

    /// <summary>
    /// Turns an UninstallString from the registry into an executable + arguments without
    /// ever passing it to cmd.exe or PowerShell.
    ///
    /// Rules:
    ///  - MSI: only "msiexec /x {GUID}" is allowed; other msiexec arguments are discarded.
    ///  - Otherwise the executable must be a fully qualified path to an .exe file.
    ///  - Script hosts and shells (cmd, powershell, rundll32, mshta, ...) are refused.
    ///  - Control characters are refused.
    /// Uninstall strings that fail these rules must be removed through Windows Settings instead.
    /// </summary>
    public static class UninstallCommandParser
    {
        private static readonly Regex MsiGuid = new(
            @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}",
            RegexOptions.Compiled);

        /// <summary>Executables that can run arbitrary code from their arguments.</summary>
        private static readonly HashSet<string> BlockedExecutables = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe",
            "wscript.exe", "cscript.exe", "mshta.exe",
            "rundll32.exe", "regsvr32.exe", "regsvcs.exe", "regasm.exe",
            "bash.exe", "wsl.exe", "conhost.exe", "explorer.exe",
            "msbuild.exe", "installutil.exe", "certutil.exe", "bitsadmin.exe",
            "forfiles.exe", "schtasks.exe", "at.exe", "wmic.exe", "reg.exe", "sc.exe"
        };

        public static Result<UninstallCommand> Parse(string? uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString))
                return Result<UninstallCommand>.Fail("No uninstall command is registered for this app.");

            string s = uninstallString.Trim();
            if (s.Any(c => char.IsControl(c)))
                return Result<UninstallCommand>.Fail("The uninstall command contains invalid characters.");

            // --- MSI ---
            string firstToken = FirstToken(s);
            if (Path.GetFileName(firstToken.Trim('"')).StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                var m = MsiGuid.Match(s);
                if (!m.Success)
                    return Result<UninstallCommand>.Fail("The MSI uninstall command has no product code.");
                string msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
                return Result<UninstallCommand>.Ok(new UninstallCommand(msiexec, $"/x {m.Value} /passive /norestart", true));
            }

            // --- Plain executable ---
            string exe, args;
            if (s.StartsWith('"'))
            {
                int end = s.IndexOf('"', 1);
                if (end < 0)
                    return Result<UninstallCommand>.Fail("The uninstall command has an unmatched quote.");
                exe = s[1..end];
                args = s[(end + 1)..].Trim();
            }
            else
            {
                int idx = IndexOfExeEnd(s);
                if (idx < 0)
                    return Result<UninstallCommand>.Fail("The uninstall command does not point to an .exe file.");
                exe = s[..idx];
                args = s[idx..].Trim();
            }

            exe = Environment.ExpandEnvironmentVariables(exe.Trim());

            if (exe.Contains('"') || exe.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return Result<UninstallCommand>.Fail("The uninstall program path is not valid.");
            if (!Path.IsPathFullyQualified(exe))
                return Result<UninstallCommand>.Fail("The uninstall program path is not a full path.");
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Result<UninstallCommand>.Fail("The uninstall program is not an .exe file.");

            string name = Path.GetFileName(exe);
            if (BlockedExecutables.Contains(name))
                return Result<UninstallCommand>.Fail($"The uninstall command uses {name}, which K-Win does not run for safety.");

            return Result<UninstallCommand>.Ok(new UninstallCommand(Path.GetFullPath(exe), args, false));
        }

        private static string FirstToken(string s)
        {
            if (s.StartsWith('"'))
            {
                int end = s.IndexOf('"', 1);
                return end > 0 ? s[..(end + 1)] : s;
            }
            int sp = s.IndexOf(' ');
            return sp < 0 ? s : s[..sp];
        }

        /// <summary>Index just after the first ".exe" that is followed by end-of-string or a space.</summary>
        private static int IndexOfExeEnd(string s)
        {
            int from = 0;
            while (true)
            {
                int i = s.IndexOf(".exe", from, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return -1;
                int end = i + 4;
                if (end == s.Length || s[end] == ' ') return end;
                from = end;
            }
        }
    }
}
