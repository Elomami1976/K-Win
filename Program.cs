using KWin.UI;
using KWin.Utils;

namespace KWin
{
    /// <summary>
    /// Application entry point. Performs Windows 11 compatibility check,
    /// administrator privilege verification, language selection, and launches the main form.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Enable modern visual styles
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // Global exception handler
            Application.ThreadException += (s, e) =>
            {
                Logger.Instance.Error("UnhandledException", "Application", e.Exception);
                MessageBox.Show(
                    Loc.Get("program.error_msg", e.Exception.Message),
                    Loc.Get("program.error_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Logger.Instance.Error("FatalException", "AppDomain", ex);
                }
            };

            // Ensure app data directories exist
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-win");
            Directory.CreateDirectory(Path.Combine(appData, "logs"));
            Directory.CreateDirectory(Path.Combine(appData, "backups"));

            // --- Language selection ---
            // Try to load saved preference; if none, show the language picker
            if (!Loc.LoadPreference())
            {
                string chosenLang = LanguagePickerDialog.ShowPicker();
                Loc.SetLanguage(chosenLang);
            }

            // Windows 11 compatibility check
            string? compatError = WindowsVersionChecker.ValidateCompatibility();
            if (compatError != null)
            {
                MessageBox.Show(
                    compatError,
                    Loc.Get("program.compat_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Administrator check
            if (!WindowsVersionChecker.IsRunningAsAdmin())
            {
                MessageBox.Show(
                    Loc.Get("program.admin_msg"),
                    Loc.Get("program.admin_title"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Log startup
            Logger.Instance.Info("Application", "Program",
                $"started_v1.0.0_{WindowsVersionChecker.GetVersionString()}_lang={Loc.CurrentLanguage}");

            // Launch main form
            Application.Run(new MainForm());
        }
    }
}
