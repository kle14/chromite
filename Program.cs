using System;
using System.Windows.Forms;
using SecureBrowser.Data;
using SecureBrowser.Forms;

namespace SecureBrowser
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
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

            // ── Verify database connection ─────────────────────────────────
            if (!Db.TestConnection(out var dbError))
            {
                var result = MessageBox.Show(
                    "Cannot connect to the PostgreSQL database.\n\n" +
                    "Make sure Docker is running and the container is up:\n" +
                    "  docker-compose up -d\n\n" +
                    $"Error: {dbError}\n\n" +
                    "Retry?",
                    "Database Connection Failed",
                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel) return;

                // One retry
                if (!Db.TestConnection(out dbError))
                {
                    MessageBox.Show(
                        "Still cannot connect.\n\n" +
                        "Please run 'docker-compose up -d' in the project folder\n" +
                        "and wait a few seconds for PostgreSQL to start.\n\n" +
                        $"Error: {dbError}",
                        "Database Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            // ── Login → Browser → Logout → Login loop ─────────────────────
            while (true)
            {
                var loginForm = new LoginForm();
                if (loginForm.ShowDialog() != DialogResult.OK)
                    break;

                var session = loginForm.CurrentSession;
                if (session == null) break;

                var mainForm = new MainForm(session);
                Application.Run(mainForm);

                if (!mainForm.ShouldLogout)
                    break;
            }
        }
    }
}
