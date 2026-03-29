using System;
using System.Windows.Forms;

namespace SecureBrowser
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Show crash details instead of silent exits
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Error:\n\n" + e.Exception.Message +
                    "\n\n" + e.Exception.StackTrace,
                    "SecureBrowser Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show("Fatal:\n\n" + e.ExceptionObject,
                    "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // Initialise default users and policy on first run
            PolicyEngine.EnsureDefaultData();

            // ── Login → Browser → Logout → Login loop ─────────────────────
            // Allows switching users without restarting the app (great for demo)
            while (true)
            {
                var loginForm = new LoginForm();
                if (loginForm.ShowDialog() != DialogResult.OK)
                    break;  // User closed login form — exit

                var session   = loginForm.CurrentSession;
                if (session == null) break;

                var mainForm  = new MainForm(session);
                Application.Run(mainForm);

                // If the user clicked "Logout", loop back to login screen
                if (!mainForm.ShouldLogout)
                    break;  // Window was closed normally — exit app
            }
        }
    }
}
