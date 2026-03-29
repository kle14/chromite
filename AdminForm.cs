using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SecureBrowser
{
    public class AdminForm : Form
    {
        private readonly string _adminUser;

        private Button _navDashboard = null!;
        private Button _navUsers     = null!;
        private Button _navWhitelist = null!;
        private Button _navAudit     = null!;

        private Panel _pnlContent   = null!;
        private Panel _pnlDashboard = null!;
        private Panel _pnlUsers     = null!;
        private Panel _pnlWhitelist = null!;
        private Panel _pnlAudit     = null!;

        private ListBox  _lstUsers     = null!;
        private CheckBox _chkClipboard = null!;
        private CheckBox _chkPrint     = null!;
        private CheckBox _chkSSL       = null!;
        private CheckBox _chkOffice    = null!;
        private CheckBox _chkRemote    = null!;
        private CheckBox _chkBranch    = null!;
        private Button   _btnSavePerm  = null!;
        private Label    _lblPermUser  = null!;

        private ComboBox _cmbWLUser = null!;
        private ListBox  _lstUrls   = null!;
        private TextBox  _txtNewUrl = null!;
        private Button   _btnAddUrl = null!;
        private Button   _btnRemUrl = null!;

        private DataGridView _gridAudit  = null!;
        private ComboBox     _cmbAudUser = null!;
        private ComboBox     _cmbAudType = null!;
        private Button       _btnRefresh = null!;
        private DataGridView _gridRecent = null!;

        // CL prefix avoids conflicts with inherited Form properties
        private static readonly Color ClBg      = Color.FromArgb(13,  17,  23);
        private static readonly Color ClSurface = Color.FromArgb(22,  27,  34);
        private static readonly Color ClPanel2  = Color.FromArgb(33,  38,  45);
        private static readonly Color ClBorder  = Color.FromArgb(48,  54,  61);
        private static readonly Color ClAccent  = Color.FromArgb(88,  166, 255);
        private static readonly Color ClGreen   = Color.FromArgb(63,  185,  80);
        private static readonly Color ClOrange  = Color.FromArgb(210, 153,  34);
        private static readonly Color ClRed     = Color.FromArgb(248,  81,  73);
        private static readonly Color ClPurple  = Color.FromArgb(188, 140, 255);
        private static readonly Color ClText    = Color.FromArgb(201, 209, 217);
        private static readonly Color ClTextDim = Color.FromArgb(139, 148, 158);

        public AdminForm(string adminUser)
        {
            _adminUser = adminUser;
            BuildUI();
            ShowPanel(_pnlDashboard, _navDashboard);
        }

        private void BuildUI()
        {
            this.Text     = "Secure Browser - Admin Console";
            Size          = new Size(1100, 720);
            MinimumSize   = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor     = ClBg;
            ForeColor     = ClText;
            Font          = new Font("Segoe UI", 9.5f);

            var topBar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(22, 27, 34) };
            topBar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(ClBorder), 0, 51, topBar.Width, 51);

            var lblTop = new Label
            {
                Text = "ADMIN CONSOLE", Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = ClPurple, TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Left, Width = 260, Padding = new Padding(20, 0, 0, 0)
            };
            var lblAdmin = new Label
            {
                Text = $"Logged in as: {_adminUser}  -  {DateTime.Now:dd MMM yyyy HH:mm}",
                Font = new Font("Segoe UI", 8.5f), ForeColor = ClTextDim,
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Right, Width = 360, Padding = new Padding(0, 0, 20, 0)
            };
            topBar.Controls.Add(lblAdmin);
            topBar.Controls.Add(lblTop);

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 180, BackColor = Color.FromArgb(16, 20, 28) };
            sidebar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(ClBorder), 179, 0, 179, sidebar.Height);

            _navDashboard = MakeNavBtn("Dashboard",    10);
            _navUsers     = MakeNavBtn("Users",         60);
            _navWhitelist = MakeNavBtn("URL Whitelist", 110);
            _navAudit     = MakeNavBtn("Audit Log",    160);

            _navDashboard.Click += (s, e) => ShowPanel(_pnlDashboard, _navDashboard);
            _navUsers.Click     += (s, e) => ShowPanel(_pnlUsers,     _navUsers);
            _navWhitelist.Click += (s, e) => ShowPanel(_pnlWhitelist, _navWhitelist);
            _navAudit.Click     += (s, e) => ShowPanel(_pnlAudit,     _navAudit);

            sidebar.Controls.AddRange(new Control[] { _navDashboard, _navUsers, _navWhitelist, _navAudit });

            _pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = ClBg };

            BuildDashboardPanel();
            BuildUsersPanel();
            BuildWhitelistPanel();
            BuildAuditPanel();

            _pnlContent.Controls.AddRange(new Control[] { _pnlDashboard, _pnlUsers, _pnlWhitelist, _pnlAudit });

            Controls.Add(_pnlContent);
            Controls.Add(sidebar);
            Controls.Add(topBar);
        }

        // ── Dashboard ─────────────────────────────────────────────────────

        private void BuildDashboardPanel()
        {
            _pnlDashboard = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("Security Dashboard");
            title.Top = 20; title.Left = 24;
            _pnlDashboard.Controls.Add(title);

            var stats = new[]
            {
                ("Total Users",   PolicyEngine.GetAllUsers().Count.ToString(),              ClAccent),
                ("Blocked Today", AuditLogger.CountWarningsToday().ToString(),              ClOrange),
                ("Copy Blocks",   AuditLogger.CountTodayByType("COPY_BLOCKED").ToString(), ClRed),
                ("Logins Today",  AuditLogger.CountTodayByType("LOGIN").ToString(),        ClGreen),
            };

            int x = 24;
            foreach (var (lbl, val, col) in stats)
            {
                var card = StatCard(lbl, val, col);
                card.Left = x; card.Top = 60;
                _pnlDashboard.Controls.Add(card);
                x += 200;
            }

            var lblRecent = SectionTitle("Recent Security Events");
            lblRecent.Top = 175; lblRecent.Left = 24;
            _pnlDashboard.Controls.Add(lblRecent);

            _gridRecent = MakeDarkGrid();
            _gridRecent.SetBounds(24, 210, 840, 340);
            _gridRecent.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { HeaderText = "Timestamp", Width = 150 },
                new DataGridViewTextBoxColumn { HeaderText = "User",      Width = 120 },
                new DataGridViewTextBoxColumn { HeaderText = "Event",     Width = 150 },
                new DataGridViewTextBoxColumn { HeaderText = "Details",   AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
                new DataGridViewTextBoxColumn { HeaderText = "Severity",  Width = 90  }
            });
            _pnlDashboard.Controls.Add(_gridRecent);

            var btnRef = MakeButton("Refresh", 24, 560, 110, 32, ClAccent);
            btnRef.Click += (s, e) => RefreshDashboard();
            _pnlDashboard.Controls.Add(btnRef);

            _pnlDashboard.VisibleChanged += (s, e) => { if (_pnlDashboard.Visible) RefreshDashboard(); };
        }

        private void RefreshDashboard()
        {
            _gridRecent.Rows.Clear();
            foreach (var ev in AuditLogger.GetRecentEvents(50))
            {
                int row = _gridRecent.Rows.Add(
                    ev.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    ev.Username, ev.EventType, ev.Details, ev.Severity);

                _gridRecent.Rows[row].DefaultCellStyle.BackColor = ev.Severity switch
                {
                    "Critical" => Color.FromArgb(60, 20, 20),
                    "Warning"  => Color.FromArgb(55, 40, 10),
                    _          => Color.FromArgb(22, 27, 34)
                };
            }
        }

        // ── Users ─────────────────────────────────────────────────────────

        private void BuildUsersPanel()
        {
            _pnlUsers = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("User Permissions Management");
            title.Top = 20; title.Left = 24;
            _pnlUsers.Controls.Add(title);

            var lblList = MakeLbl("SELECT USER", 24, 62, 160, 18, ClTextDim, bold: true);
            _lstUsers = new ListBox
            {
                Left = 24, Top = 84, Width = 200, Height = 380,
                BackColor = ClSurface, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f)
            };
            _lstUsers.SelectedIndexChanged += OnUserSelected;
            _pnlUsers.Controls.AddRange(new Control[] { lblList, _lstUsers });

            var pnlEdit = new Panel { Left = 244, Top = 62, Width = 600, Height = 440, BackColor = ClSurface };
            pnlEdit.Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, pnlEdit.ClientRectangle, ClBorder, ButtonBorderStyle.Solid);

            _lblPermUser = new Label
            {
                Left = 16, Top = 16, Width = 560, Height = 28,
                Text = "Select a user to edit permissions",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = ClAccent, BackColor = Color.Transparent
            };

            _chkClipboard = MakeCheck("Allow Clipboard  (copy/paste enabled)", 16, 60);
            _chkPrint     = MakeCheck("Allow Printing",                         16, 92);
            _chkSSL       = MakeCheck("SSL Only  (HTTPS required)",             16, 124);

            var lblLoc = MakeLbl("ALLOWED LOCATIONS", 16, 166, 300, 18, ClTextDim, bold: true);
            _chkOffice = MakeCheck("Office", 16, 190);
            _chkRemote = MakeCheck("Remote", 16, 216);
            _chkBranch = MakeCheck("Branch", 16, 242);

            _btnSavePerm = MakeButton("Save Permissions", 16, 300, 200, 38, ClGreen);
            _btnSavePerm.Click += OnSavePermissions;
            _btnSavePerm.Enabled = false;

            pnlEdit.Controls.AddRange(new Control[]
            {
                _lblPermUser, _chkClipboard, _chkPrint, _chkSSL,
                lblLoc, _chkOffice, _chkRemote, _chkBranch, _btnSavePerm
            });
            _pnlUsers.Controls.Add(pnlEdit);

            var btnRef = MakeButton("Refresh", 24, 475, 110, 32, ClAccent);
            btnRef.Click += (s, e) => RefreshUserList();
            _pnlUsers.Controls.Add(btnRef);

            _pnlUsers.VisibleChanged += (s, e) => { if (_pnlUsers.Visible) RefreshUserList(); };
        }

        private void RefreshUserList()
        {
            _lstUsers.Items.Clear();
            foreach (var u in PolicyEngine.GetAllUsers())
                _lstUsers.Items.Add(u.Username);
        }

        private void OnUserSelected(object? sender, EventArgs e)
        {
            if (_lstUsers.SelectedItem == null) return;
            var username = _lstUsers.SelectedItem.ToString()!;
            var perms    = PolicyEngine.GetUserPermissions(username);

            _lblPermUser.Text     = $"Editing: {username}";
            _chkClipboard.Checked = perms.AllowClipboard;
            _chkPrint.Checked     = perms.AllowPrint;
            _chkSSL.Checked       = perms.SSLOnly;
            _chkOffice.Checked    = perms.AllowedLocations.Contains("Office");
            _chkRemote.Checked    = perms.AllowedLocations.Contains("Remote");
            _chkBranch.Checked    = perms.AllowedLocations.Contains("Branch");
            _btnSavePerm.Enabled  = true;
        }

        private void OnSavePermissions(object? sender, EventArgs e)
        {
            if (_lstUsers.SelectedItem == null) return;
            var username = _lstUsers.SelectedItem.ToString()!;
            var existing = PolicyEngine.GetUserPermissions(username);

            var locations = new List<string>();
            if (_chkOffice.Checked) locations.Add("Office");
            if (_chkRemote.Checked) locations.Add("Remote");
            if (_chkBranch.Checked) locations.Add("Branch");

            var updated = new UserPermissions
            {
                AllowClipboard   = _chkClipboard.Checked,
                AllowPrint       = _chkPrint.Checked,
                SSLOnly          = _chkSSL.Checked,
                AllowedUrls      = existing.AllowedUrls,
                AllowedLocations = locations
            };

            PolicyEngine.UpdateUserPermissions(username, updated);
            AuditLogger.LogConfigChange(_adminUser, username,
                $"Clipboard={updated.AllowClipboard}, Print={updated.AllowPrint}, " +
                $"SSL={updated.SSLOnly}, Locations=[{string.Join(",", locations)}]");

            MessageBox.Show($"Permissions saved for '{username}'.\nChanges take effect on next login.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Whitelist ─────────────────────────────────────────────────────

        private void BuildWhitelistPanel()
        {
            _pnlWhitelist = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("URL Whitelist Management");
            title.Top = 20; title.Left = 24;
            _pnlWhitelist.Controls.Add(title);

            var lblU = MakeLbl("SELECT USER", 24, 62, 160, 18, ClTextDim, bold: true);
            _cmbWLUser = new ComboBox
            {
                Left = 24, Top = 84, Width = 200, Height = 30,
                BackColor = ClSurface, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10f)
            };
            _cmbWLUser.SelectedIndexChanged += OnWLUserChanged;

            var lblU2 = MakeLbl("WHITELISTED URLs", 244, 62, 500, 18, ClTextDim, bold: true);
            _lstUrls = new ListBox
            {
                Left = 244, Top = 84, Width = 520, Height = 300,
                BackColor = ClSurface, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9.5f)
            };

            _txtNewUrl = new TextBox
            {
                Left = 244, Top = 396, Width = 370, Height = 28,
                BackColor = ClPanel2, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f), PlaceholderText = "https://example.com"
            };

            _btnAddUrl = MakeButton("Add URL", 624, 394, 100, 32, ClGreen);
            _btnAddUrl.Click += OnAddUrl;

            _btnRemUrl = MakeButton("Remove", 244, 436, 110, 32, ClRed);
            _btnRemUrl.Click += OnRemoveUrl;

            var note = MakeLbl("Use '*' to allow all URLs  -  Domain matching: 'google.com' covers all subdomains",
                244, 476, 600, 18, ClTextDim);

            _pnlWhitelist.Controls.AddRange(new Control[]
                { title, lblU, _cmbWLUser, lblU2, _lstUrls, _txtNewUrl, _btnAddUrl, _btnRemUrl, note });

            _pnlWhitelist.VisibleChanged += (s, e) => { if (_pnlWhitelist.Visible) RefreshWhitelistUsers(); };
        }

        private void RefreshWhitelistUsers()
        {
            _cmbWLUser.Items.Clear();
            foreach (var u in PolicyEngine.GetAllUsers())
                _cmbWLUser.Items.Add(u.Username);
            if (_cmbWLUser.Items.Count > 0) _cmbWLUser.SelectedIndex = 0;
        }

        private void OnWLUserChanged(object? sender, EventArgs e)
        {
            if (_cmbWLUser.SelectedItem == null) return;
            var perms = PolicyEngine.GetUserPermissions(_cmbWLUser.SelectedItem.ToString()!);
            _lstUrls.Items.Clear();
            foreach (var url in perms.AllowedUrls)
                _lstUrls.Items.Add(url);
        }

        private void OnAddUrl(object? sender, EventArgs e)
        {
            if (_cmbWLUser.SelectedItem == null) return;
            var url = _txtNewUrl.Text.Trim();
            var username = _cmbWLUser.SelectedItem.ToString()!;
            if (string.IsNullOrWhiteSpace(url)) return;

            PolicyEngine.AddUrlToWhitelist(username, url);
            AuditLogger.LogConfigChange(_adminUser, username, $"Added URL: {url}");
            _txtNewUrl.Clear();
            OnWLUserChanged(sender, e);
        }

        private void OnRemoveUrl(object? sender, EventArgs e)
        {
            if (_cmbWLUser.SelectedItem == null || _lstUrls.SelectedItem == null) return;
            var url = _lstUrls.SelectedItem.ToString()!;
            var username = _cmbWLUser.SelectedItem.ToString()!;

            PolicyEngine.RemoveUrlFromWhitelist(username, url);
            AuditLogger.LogConfigChange(_adminUser, username, $"Removed URL: {url}");
            OnWLUserChanged(sender, e);
        }

        // ── Audit Log ─────────────────────────────────────────────────────

        private void BuildAuditPanel()
        {
            _pnlAudit = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("Audit Log - All Security Events");
            title.Top = 20; title.Left = 24;

            var lblUser = MakeLbl("FILTER USER", 24, 62, 120, 18, ClTextDim, bold: true);
            _cmbAudUser = new ComboBox
            {
                Left = 24, Top = 84, Width = 150, Height = 28,
                BackColor = ClSurface, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };

            var lblType = MakeLbl("FILTER TYPE", 186, 62, 120, 18, ClTextDim, bold: true);
            _cmbAudType = new ComboBox
            {
                Left = 186, Top = 84, Width = 160, Height = 28,
                BackColor = ClSurface, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };
            _cmbAudType.Items.AddRange(new object[]
            {
                "ALL", "LOGIN", "LOGOUT", "NAV_BLOCKED", "COPY_BLOCKED",
                "SCREENSHOT", "CONFIG_CHANGE", "LOGIN_FAILED", "LOCATION_DENIED"
            });
            _cmbAudType.SelectedIndex = 0;

            _btnRefresh = MakeButton("Refresh", 362, 82, 100, 32, ClAccent);
            _btnRefresh.Click += (s, e) => RefreshAuditLog();

            var btnExport = MakeButton("Export CSV", 472, 82, 110, 32, Color.FromArgb(55, 55, 62));
            btnExport.Click += OnExportCsv;

            _gridAudit = MakeDarkGrid();
            _gridAudit.SetBounds(24, 126, 840, 460);
            _gridAudit.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { HeaderText = "Timestamp",  Width = 155 },
                new DataGridViewTextBoxColumn { HeaderText = "User",       Width = 100 },
                new DataGridViewTextBoxColumn { HeaderText = "Location",   Width = 80  },
                new DataGridViewTextBoxColumn { HeaderText = "Event Type", Width = 140 },
                new DataGridViewTextBoxColumn { HeaderText = "Details",    AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
                new DataGridViewTextBoxColumn { HeaderText = "Severity",   Width = 80  }
            });

            _pnlAudit.Controls.AddRange(new Control[]
                { title, lblUser, _cmbAudUser, lblType, _cmbAudType, _btnRefresh, btnExport, _gridAudit });

            _pnlAudit.VisibleChanged += (s, e) =>
            {
                if (!_pnlAudit.Visible) return;
                _cmbAudUser.Items.Clear();
                _cmbAudUser.Items.Add("ALL");
                foreach (var u in PolicyEngine.GetAllUsers())
                    _cmbAudUser.Items.Add(u.Username);
                _cmbAudUser.SelectedIndex = 0;
                RefreshAuditLog();
            };
        }

        private void RefreshAuditLog()
        {
            _gridAudit.Rows.Clear();
            var events = AuditLogger.GetAllEvents();

            var userFilter = _cmbAudUser.SelectedItem?.ToString() ?? "ALL";
            var typeFilter = _cmbAudType.SelectedItem?.ToString() ?? "ALL";

            if (userFilter != "ALL")
                events = events.Where(e =>
                    e.Username.Equals(userFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (typeFilter != "ALL")
                events = events.Where(e =>
                    e.EventType.Equals(typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var ev in events)
            {
                int row = _gridAudit.Rows.Add(
                    ev.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    ev.Username, ev.Location, ev.EventType, ev.Details, ev.Severity);

                _gridAudit.Rows[row].DefaultCellStyle.BackColor = ev.Severity switch
                {
                    "Critical" => Color.FromArgb(55, 15, 15),
                    "Warning"  => Color.FromArgb(50, 35,  5),
                    _          => Color.FromArgb(22, 27, 34)
                };
                _gridAudit.Rows[row].DefaultCellStyle.ForeColor = ev.Severity switch
                {
                    "Critical" => ClRed,
                    "Warning"  => ClOrange,
                    _          => ClText
                };
            }
        }

        private void OnExportCsv(object? sender, EventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter   = "CSV files|*.csv",
                FileName = $"SecureBrowser_Audit_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var events = AuditLogger.GetAllEvents();
            var lines  = new List<string> { "Timestamp,Username,Location,EventType,Details,Severity" };
            foreach (var ev in events)
                lines.Add($"{ev.Timestamp:yyyy-MM-dd HH:mm:ss},{ev.Username},{ev.Location}," +
                    $"{ev.EventType},\"{ev.Details.Replace("\"", "''")}\"," + ev.Severity);

            System.IO.File.WriteAllLines(dlg.FileName, lines);
            MessageBox.Show($"Exported {events.Count} events to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Panel navigation ──────────────────────────────────────────────

        private Button? _activeNav;

        private void ShowPanel(Panel panel, Button navBtn)
        {
            foreach (Control c in _pnlContent.Controls)
                if (c is Panel p) p.Visible = false;

            panel.Visible = true;

            if (_activeNav != null)
            {
                _activeNav.BackColor = Color.Transparent;
                _activeNav.ForeColor = ClTextDim;
            }
            navBtn.BackColor = Color.FromArgb(33, 38, 45);
            navBtn.ForeColor = ClAccent;
            _activeNav = navBtn;
        }

        // ── UI helpers ────────────────────────────────────────────────────

        private Button MakeNavBtn(string text, int top)
        {
            var b = new Button
            {
                Text = text, Left = 0, Top = top, Width = 180, Height = 44,
                FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent,
                ForeColor = ClTextDim, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f), Cursor = Cursors.Hand,
                TabStop = false, Padding = new Padding(16, 0, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, Color bg)
        {
            var b = new Button
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                FlatStyle = FlatStyle.Flat, BackColor = bg,
                ForeColor = Color.White, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand, TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private static CheckBox MakeCheck(string text, int x, int y)
        {
            return new CheckBox
            {
                Text = text, Left = x, Top = y, Width = 500, Height = 24,
                ForeColor = Color.FromArgb(201, 209, 217),
                Font = new Font("Segoe UI", 9.5f), BackColor = Color.Transparent
            };
        }

        private static Label MakeLbl(string text, int x, int y, int w, int h,
            Color color, bool bold = false)
        {
            return new Label
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                ForeColor = color, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", bold ? 7.5f : 9f,
                    bold ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        private static Label SectionTitle(string text)
        {
            return new Label
            {
                Text = text, Width = 600, Height = 28,
                ForeColor = Color.White, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };
        }

        private static Panel StatCard(string label, string value, Color color)
        {
            var p = new Panel { Width = 185, Height = 90, BackColor = Color.FromArgb(22, 27, 34) };
            p.Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, p.ClientRectangle, color, ButtonBorderStyle.Solid);

            p.Controls.Add(new Label
            {
                Text = value, Font = new Font("Segoe UI", 24f, FontStyle.Bold),
                ForeColor = color, Left = 14, Top = 8, Width = 160, Height = 44,
                BackColor = Color.Transparent
            });
            p.Controls.Add(new Label
            {
                Text = label, Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(139, 148, 158),
                Left = 14, Top = 56, Width = 160, Height = 22,
                BackColor = Color.Transparent
            });
            return p;
        }

        private static DataGridView MakeDarkGrid()
        {
            var g = new DataGridView
            {
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.FromArgb(13, 17, 23),
                GridColor = Color.FromArgb(48, 54, 61),
                BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 8.5f),
                EnableHeadersVisualStyles = false
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 27, 34);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(139, 148, 158);
            g.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 8f, FontStyle.Bold);
            g.ColumnHeadersHeight                     = 30;
            g.DefaultCellStyle.BackColor              = Color.FromArgb(22, 27, 34);
            g.DefaultCellStyle.ForeColor              = Color.FromArgb(201, 209, 217);
            g.DefaultCellStyle.SelectionBackColor     = Color.FromArgb(33, 55, 88);
            g.DefaultCellStyle.SelectionForeColor     = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(18, 22, 28);
            g.RowTemplate.Height = 26;
            return g;
        }
    }
}
