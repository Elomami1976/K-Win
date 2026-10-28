using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace KWin.Utils
{
    /// <summary>
    /// Least-privilege support. K-Win starts as a normal user; operations that need
    /// administrator rights ask the user to restart K-Win elevated.
    /// </summary>
    public static class ElevationHelper
    {
        private static readonly Lazy<bool> _isAdmin = new(() =>
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        });

        /// <summary>True when the current process is running elevated.</summary>
        public static bool IsAdministrator => _isAdmin.Value;

        /// <summary>Command-line switch used to reopen the same tab after elevating.</summary>
        public const string TabArgument = "--tab";

        /// <summary>
        /// Starts a new elevated instance of K-Win (UAC prompt).
        /// Returns false if the user declined the prompt or the start failed.
        /// </summary>
        public static bool TryRestartElevated(int? tabIndex = null)
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas"
            };
            if (tabIndex is int t && t >= 0)
            {
                psi.ArgumentList.Add(TabArgument);
                psi.ArgumentList.Add(t.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            try
            {
                Process.Start(psi)?.Dispose();
                Logger.Instance.Info("RestartElevated", exe, "started");
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: user said No
            {
                Logger.Instance.Info("RestartElevated", exe, "cancelled_by_user");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("RestartElevated", exe, ex);
                return false;
            }
        }

        /// <summary>Reads "--tab N" from the command line.</summary>
        public static int? ParseTabArgument(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], TabArgument, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int tab) && tab >= 0 && tab < 32)
                    return tab;
            }
            return null;
        }
    }
}
