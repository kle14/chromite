using System;
using System.Drawing;
using System.Windows.Forms;
using SecureBrowser.Data;
using SecureBrowser.Models;

namespace SecureBrowser.Forms
{
    public class LoginForm : Form
    {
        public UserSession? CurrentSession { get; private set; }

        private TextBox  _txtUsername = null!;
        private TextBox  _txtPassword = null!;
        private ComboBox _cmbLocation = null!;
        private Button   _btnLogin    = null!;
        private Label    _lblError    = null!;

        private static readonly Color BG      = Color.FromArgb(13,  17,  23);
        private static readonly Color Surface = Color.FromArgb(22,  27,  34);
        private static readonly Color Border  = Color.FromArgb(48,  54,  61);
        private static readonly Color Accent  = Color.FromArgb(88, 166, 255);
        private static readonly Color TextC   = Color.FromArgb(201, 209, 217);
        private static readonly Color TextDim = Color.FromArgb(139, 148, 158);
        private static readonly Color Danger  = Color.FromArgb(248,  81,  73);
        private static readonly Color GreenC  = Color.FromArgb( 63, 185,  80);

        public LoginForm() => BuildUI();

        private void BuildUI()
        {
            Text            = "Secure Browser — Login";
            Size            = new Size(520, 680);
            MinimumSize     = Size;
            MaximumSize     = Size;
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = BG;
            ForeColor       = TextC;
            Font            = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;

            // ── Header ────────────────────────────────────────────────────
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 160, BackColor = BG };

            pnlHeader.Controls.Add(new Label
            {
                Text = "Enterprise Data Protection Platform",
                Font = new Font("Segoe UI", 9f), ForeColor = TextDim,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 24
            });
            pnlHeader.Controls.Add(new Label
            {
                Text = "SECURE BROWSER",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 36
            });
            pnlHeader.Controls.Add(new Label
            {
                Text = "🛡", Font = new Font("Segoe UI Emoji", 42f), ForeColor = Accent,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 90, Padding = new Padding(0, 16, 0, 0)
            });

            // ── Login panel ───────────────────────────────────────────────
            var pnlLogin = new Panel
            {
                Width = 380, Height = 320, BackColor = Surface, Left = 70, Top = 175
            };
            pnlLogin.Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, pnlLogin.ClientRectangle, Border, ButtonBorderStyle.Solid);

            AddLabel(pnlLogin, "USERNAME", 24, 24);
            _txtUsername = AddTextBox(pnlLogin, 24, 46);

            AddLabel(pnlLogin, "PASSWORD", 24, 90);
            _txtPassword = AddTextBox(pnlLogin, 24, 112);
            _txtPassword.PasswordChar = '●';

            AddLabel(pnlLogin, "LOCATION", 24, 156);
            _cmbLocation = new ComboBox
            {
                Left = 24, Top = 178, Width = 332, Height = 32,
                BackColor = Color.FromArgb(33, 38, 45), ForeColor = TextC,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbLocation.Items.AddRange(new object[] { "Office", "Remote", "Branch" });
            _cmbLocation.SelectedIndex = 0;
            pnlLogin.Controls.Add(_cmbLocation);

            _lblError = new Label
            {
                Left = 24, Top = 222, Width = 332, Height = 22,
                ForeColor = Danger, Font = new Font("Segoe UI", 8.5f)
            };
            pnlLogin.Controls.Add(_lblError);

            _btnLogin = new Button
            {
                Left = 24, Top = 254, Width = 332, Height = 42,
                Text = "AUTHENTICATE", FlatStyle = FlatStyle.Flat,
                BackColor = Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand
            };
            _btnLogin.FlatAppearance.BorderSize = 0;
            _btnLogin.Click += OnLogin;
            pnlLogin.Controls.Add(_btnLogin);

            _txtUsername.KeyDown += (s, e) => { if (e.KeyCode == Keys.Return) _txtPassword.Focus(); };
            _txtPassword.KeyDown += (s, e) => { if (e.KeyCode == Keys.Return) OnLogin(s, e); };

            // ── Demo credentials ──────────────────────────────────────────
            var pnlDemo = new Panel
            {
                Width = 380, Height = 120, BackColor = Color.FromArgb(20, 30, 20),
                Left = 70, Top = 510
            };
            pnlDemo.Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, pnlDemo.ClientRectangle,
                    Color.FromArgb(40, 100, 40), ButtonBorderStyle.Solid);

            pnlDemo.Controls.Add(new Label
            {
                Text = "  DEMO CREDENTIALS",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = GreenC,
                Left = 0, Top = 10, Width = 380, Height = 20
            });
            pnlDemo.Controls.Add(new Label
            {
                Text = "  admin / Admin123!   →  Full access + Admin Console\r\n" +
                       "  alice / Alice123!    →  Restricted: no clipboard, limited URLs\r\n" +
                       "  bob   / Bob123!      →  Partial: clipboard allowed, limited URLs",
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(150, 200, 150),
                Left = 0, Top = 34, Width = 380, Height = 76
            });

            // ── Footer ────────────────────────────────────────────────────
            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Surface };
            pnlFooter.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = TextDim, Font = new Font("Segoe UI", 7.5f),
                Text = "All sessions are monitored and recorded  •  PostgreSQL-backed  •  Secure Browser v2.0"
            });

            Controls.Add(pnlHeader);
            Controls.Add(pnlLogin);
            Controls.Add(pnlDemo);
            Controls.Add(pnlFooter);
        }

        private static void AddLabel(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y, Width = 332, Height = 18,
                ForeColor = Color.FromArgb(139, 148, 158),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold)
            });
        }

        private static TextBox AddTextBox(Panel parent, int x, int y)
        {
            var tb = new TextBox
            {
                Left = x, Top = y, Width = 332, Height = 32,
                BackColor = Color.FromArgb(33, 38, 45),
                ForeColor = Color.FromArgb(201, 209, 217),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10.5f)
            };
            parent.Controls.Add(tb);
            return tb;
        }

        private void OnLogin(object? sender, EventArgs e)
        {
            _lblError.Text    = "";
            _btnLogin.Enabled = false;
            _btnLogin.Text    = "Authenticating...";

            var username = _txtUsername.Text.Trim();
            var password = _txtPassword.Text;
            var location = _cmbLocation.SelectedItem?.ToString() ?? "Office";

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ShowError("Please enter username and password.");
                return;
            }

            var session = PolicyEngine.AuthenticateUser(username, password, location);

            if (session == null)
            {
                // Determine reason
                var user = PolicyEngine.GetUser(username);
                if (user != null && user.PasswordHash == PolicyEngine.Hash(password))
                {
                    AuditLogger.LogLocationDenied(username, location);
                    ShowError($"Access denied: '{location}' is not an approved location.");
                }
                else
                {
                    AuditLogger.Log(username, "LOGIN_FAILED",
                        $"Failed login attempt for '{username}'", "Critical", location);
                    ShowError("Invalid username or password.");
                }
                return;
            }

            AuditLogger.LogLogin(username, location);
            CurrentSession = session;
            DialogResult   = DialogResult.OK;
            Close();
        }

        private void ShowError(string msg)
        {
            _lblError.Text    = msg;
            _btnLogin.Enabled = true;
            _btnLogin.Text    = "AUTHENTICATE";
        }
    }
}
