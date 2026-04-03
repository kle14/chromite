using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SecureBrowser.Data;
using SecureBrowser.Models;

namespace SecureBrowser.Forms
{
    public class AdminForm : Form
    {
        private readonly string _adminUser;

        // Sidebar
        private Button _navDashboard = null!;
        private Button _navUsers     = null!;
        private Button _navWhitelist = null!;
        private Button _navAudit     = null!;

        // Content panels
        private Panel _pnlContent   = null!;
        private Panel _pnlDashboard = null!;
        private Panel _pnlUsers     = null!;
        private Panel _pnlWhitelist = null!;
        private Panel _pnlAudit     = null!;

        // Users panel
        private ListBox  _lstUsers     = null!;
        private CheckBox _chkClipboard = null!;
        private CheckBox _chkPrint     = null!;
        private CheckBox _chkSSL       = null!;
        private CheckBox _chkOffice    = null!;
        private CheckBox _chkRemote    = null!;
        private CheckBox _chkBranch    = null!;
        private Button   _btnSavePerm  = null!;
        private Label    _lblPermUser  = null!;

        // Whitelist panel
        private ComboBox _cmbWLUser = null!;
        private ListBox  _lstUrls   = null!;
        private TextBox  _txtNewUrl = null!;
        private Button   _btnAddUrl = null!;
        private Button   _btnRemUrl = null!;

        // Audit panel
        private DataGridView _gridAudit  = null!;
        private ComboBox     _cmbAudUser = null!;
        private ComboBox     _cmbAudType = null!;
        private Button       _btnRefAudit = null!;

        // Dashboard
        private DataGridView _gridRecent = null!;

        // Colors
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

        // ══════════════════════════════════════════════════════════════════
        //  UI CONSTRUCTION
        // ══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            this.Text     = "Secure Browser — Admin Console";
            Size          = new Size(1100, 720);
            MinimumSize   = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor     = ClBg;
            ForeColor     = ClText;
            Font          = new Font("Segoe UI", 9.5f);

            // Top bar
            var topBar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = ClSurface };
            topBar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(ClBorder), 0, 51, topBar.Width, 51);

            topBar.Controls.Add(new Label
            {
                Text = "ADMIN CONSOLE", Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = ClPurple, TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill, Padding = new Padding(20, 0, 0, 0)
            });
            topBar.Controls.Add(new Label
            {
                Text = "PostgreSQL-backed  •  Changes take effect immediately",
                Font = new Font("Segoe UI", 8f), ForeColor = ClTextDim,
                TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right,
                Width = 340, Padding = new Padding(0, 0, 16, 0)
            });

            // Sidebar
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 180, BackColor = ClSurface };
            sidebar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(ClBorder), sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);

            _navDashboard = MakeNavBtn("📊  Dashboard", 10);
            _navUsers     = MakeNavBtn("👥  Users",      52);
            _navWhitelist = MakeNavBtn("🔗  URL Whitelist", 94);
            _navAudit     = MakeNavBtn("📋  Audit Log", 136);

            _navDashboard.Click += (s, e) => ShowPanel(_pnlDashboard, _navDashboard);
            _navUsers.Click     += (s, e) => ShowPanel(_pnlUsers, _navUsers);
            _navWhitelist.Click += (s, e) => ShowPanel(_pnlWhitelist, _navWhitelist);
            _navAudit.Click     += (s, e) => ShowPanel(_pnlAudit, _navAudit);

            sidebar.Controls.AddRange(new Control[] { _navDashboard, _navUsers, _navWhitelist, _navAudit });

            // Content area
            _pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Padding = new Padding(0) };

            BuildDashboard();
            BuildUsersPanel();
            BuildWhitelistPanel();
            BuildAuditPanel();

            _pnlContent.Controls.AddRange(new Control[] { _pnlDashboard, _pnlUsers, _pnlWhitelist, _pnlAudit });

            Controls.Add(_pnlContent);
            Controls.Add(sidebar);
            Controls.Add(topBar);
        }

        private void ShowPanel(Panel target, Button navBtn)
        {
            _pnlDashboard.Visible = false;
            _pnlUsers.Visible     = false;
            _pnlWhitelist.Visible = false;
            _pnlAudit.Visible     = false;
            target.Visible        = true;

            foreach (var btn in new[] { _navDashboard, _navUsers, _navWhitelist, _navAudit })
                btn.BackColor = ClSurface;
            navBtn.BackColor = Color.FromArgb(33, 38, 45);
        }

        // ── Dashboard ─────────────────────────────────────────────────────

        private void BuildDashboard()
        {
            _pnlDashboard = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("Security Dashboard");
            title.Top = 20; title.Left = 24;
            _pnlDashboard.Controls.Add(title);

            // Stat cards
            var statsPanel = new Panel { Left = 24, Top = 60, Width = 800, Height = 80 };
            _pnlDashboard.Controls.Add(statsPanel);

            // Recent events grid
            _gridRecent = MakeGrid(24, 160, 820, 380);
            _gridRecent.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "Time",     HeaderText = "Timestamp",  Width = 150 },
                new DataGridViewTextBoxColumn { Name = "User",     HeaderText = "User",       Width = 100 },
                new DataGridViewTextBoxColumn { Name = "Type",     HeaderText = "Event Type", Width = 120 },
                new DataGridViewTextBoxColumn { Name = "Details",  HeaderText = "Details",    Width = 350 },
                new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity",   Width = 90  }
            });
            _pnlDashboard.Controls.Add(_gridRecent);

            var btnRef = MakeButton("↻  Refresh", 24, 550, 120, 32, ClAccent);
            btnRef.Click += (s, e) => RefreshDashboard();
            _pnlDashboard.Controls.Add(btnRef);

            _pnlDashboard.VisibleChanged += (s, e) =>
                { if (_pnlDashboard.Visible) RefreshDashboard(); };
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
                    _          => ClSurface
                };
            }
        }

        // ── Users / Permissions ───────────────────────────────────────────

        private void BuildUsersPanel()
        {
            _pnlUsers = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("User Permissions Management");
            title.Top = 20; title.Left = 24;
            _pnlUsers.Controls.Add(title);

            // User list
            Lbl("SELECT USER", 24, 62, 160, 18, ClTextDim, true, _pnlUsers);
            _lstUsers = new ListBox
            {
                Left = 24, Top = 84, Width = 200, Height = 380,
                BackColor = ClSurface, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            _lstUsers.SelectedIndexChanged += OnUserSelected;
            _pnlUsers.Controls.Add(_lstUsers);

            // Edit panel
            var pnlEdit = new Panel
            {
                Left = 244, Top = 62, Width = 580, Height = 440,
                BackColor = ClSurface
            };
            pnlEdit.Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, pnlEdit.ClientRectangle, ClBorder, ButtonBorderStyle.Solid);

            _lblPermUser = new Label
            {
                Text = "Select a user to edit", Left = 20, Top = 16,
                Width = 540, Height = 30, ForeColor = ClAccent,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold)
            };
            pnlEdit.Controls.Add(_lblPermUser);

            Lbl("SECURITY PERMISSIONS", 20, 56, 200, 18, ClTextDim, true, pnlEdit);
            _chkClipboard = MakeCheck("Allow Clipboard (copy/paste)", 20, 80, pnlEdit);
            _chkPrint     = MakeCheck("Allow Printing", 20, 108, pnlEdit);
            _chkSSL       = MakeCheck("SSL Only (HTTPS required)", 20, 136, pnlEdit);

            Lbl("ALLOWED LOCATIONS", 20, 180, 200, 18, ClTextDim, true, pnlEdit);
            _chkOffice = MakeCheck("Office", 20, 204, pnlEdit);
            _chkRemote = MakeCheck("Remote", 20, 232, pnlEdit);
            _chkBranch = MakeCheck("Branch", 20, 260, pnlEdit);

            _btnSavePerm = MakeButton("💾  Save Permissions", 20, 310, 200, 38, ClGreen);
            _btnSavePerm.Click += OnSavePermissions;
            pnlEdit.Controls.Add(_btnSavePerm);

            _pnlUsers.Controls.Add(pnlEdit);

            _pnlUsers.VisibleChanged += (s, e) =>
                { if (_pnlUsers.Visible) LoadUserList(); };
        }

        private void LoadUserList()
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
        }

        private void OnSavePermissions(object? sender, EventArgs e)
        {
            if (_lstUsers.SelectedItem == null)
            {
                MessageBox.Show("Select a user first.", "No User", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var username = _lstUsers.SelectedItem.ToString()!;

            // Save base permissions
            PolicyEngine.UpdatePermissions(username,
                _chkClipboard.Checked, _chkPrint.Checked, _chkSSL.Checked);

            // Save locations
            var locs = new List<string>();
            if (_chkOffice.Checked) locs.Add("Office");
            if (_chkRemote.Checked) locs.Add("Remote");
            if (_chkBranch.Checked) locs.Add("Branch");
            PolicyEngine.SetAllowedLocations(username, locs);

            // Audit log
            var changes = new List<string>();
            changes.Add($"clipboard={_chkClipboard.Checked}");
            changes.Add($"print={_chkPrint.Checked}");
            changes.Add($"ssl={_chkSSL.Checked}");
            changes.Add($"locations=[{string.Join(",", locs)}]");
            AuditLogger.LogConfigChange(_adminUser, username, string.Join("; ", changes));

            MessageBox.Show(
                $"Permissions saved for '{username}'.\n\n" +
                "Changes take effect immediately — no logout required.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── URL Whitelist ─────────────────────────────────────────────────

        private void BuildWhitelistPanel()
        {
            _pnlWhitelist = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("URL Whitelist Management");
            title.Top = 20; title.Left = 24;
            _pnlWhitelist.Controls.Add(title);

            Lbl("SELECT USER", 24, 62, 160, 18, ClTextDim, true, _pnlWhitelist);
            _cmbWLUser = new ComboBox
            {
                Left = 24, Top = 84, Width = 200, Height = 30,
                BackColor = ClPanel2, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10f)
            };
            _cmbWLUser.SelectedIndexChanged += (s, e) => RefreshUrlList();
            _pnlWhitelist.Controls.Add(_cmbWLUser);

            Lbl("WHITELISTED URLS", 24, 126, 200, 18, ClTextDim, true, _pnlWhitelist);
            _lstUrls = new ListBox
            {
                Left = 24, Top = 148, Width = 600, Height = 280,
                BackColor = ClSurface, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f)
            };
            _pnlWhitelist.Controls.Add(_lstUrls);

            _txtNewUrl = new TextBox
            {
                Left = 24, Top = 440, Width = 440, Height = 30,
                BackColor = ClPanel2, ForeColor = ClText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            _txtNewUrl.GotFocus += (s, e) =>
                { if (_txtNewUrl.Text == "https://example.com") _txtNewUrl.Text = ""; };
            _txtNewUrl.Text = "https://example.com";
            _pnlWhitelist.Controls.Add(_txtNewUrl);

            _btnAddUrl = MakeButton("+ Add URL", 474, 440, 100, 30, ClGreen);
            _btnAddUrl.Click += OnAddUrl;
            _pnlWhitelist.Controls.Add(_btnAddUrl);

            _btnRemUrl = MakeButton("− Remove", 580, 440, 100, 30, ClRed);
            _btnRemUrl.Click += OnRemoveUrl;
            _pnlWhitelist.Controls.Add(_btnRemUrl);

            _pnlWhitelist.VisibleChanged += (s, e) =>
            {
                if (!_pnlWhitelist.Visible) return;
                _cmbWLUser.Items.Clear();
                foreach (var u in PolicyEngine.GetAllUsers())
                    _cmbWLUser.Items.Add(u.Username);
                if (_cmbWLUser.Items.Count > 0) _cmbWLUser.SelectedIndex = 0;
            };
        }

        private void RefreshUrlList()
        {
            _lstUrls.Items.Clear();
            if (_cmbWLUser.SelectedItem == null) return;
            var username = _cmbWLUser.SelectedItem.ToString()!;
            foreach (var url in PolicyEngine.GetUrlWhitelist(username))
                _lstUrls.Items.Add(url);
        }

        private void OnAddUrl(object? sender, EventArgs e)
        {
            if (_cmbWLUser.SelectedItem == null) return;
            var username = _cmbWLUser.SelectedItem.ToString()!;
            var url      = _txtNewUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url) || url == "https://example.com") return;

            PolicyEngine.AddUrlToWhitelist(username, url);
            AuditLogger.LogConfigChange(_adminUser, username, $"Added URL: {url}");
            _txtNewUrl.Text = "";
            RefreshUrlList();
        }

        private void OnRemoveUrl(object? sender, EventArgs e)
        {
            if (_cmbWLUser.SelectedItem == null || _lstUrls.SelectedItem == null) return;
            var username = _cmbWLUser.SelectedItem.ToString()!;
            var url      = _lstUrls.SelectedItem.ToString()!;

            PolicyEngine.RemoveUrlFromWhitelist(username, url);
            AuditLogger.LogConfigChange(_adminUser, username, $"Removed URL: {url}");
            RefreshUrlList();
        }

        // ── Audit Log ─────────────────────────────────────────────────────

        private void BuildAuditPanel()
        {
            _pnlAudit = new Panel { Dock = DockStyle.Fill, BackColor = ClBg, Visible = false };

            var title = SectionTitle("Audit Log");
            title.Top = 20; title.Left = 24;
            _pnlAudit.Controls.Add(title);

            // Filters
            Lbl("USER", 24, 62, 60, 18, ClTextDim, true, _pnlAudit);
            _cmbAudUser = new ComboBox
            {
                Left = 24, Top = 84, Width = 150, Height = 30,
                BackColor = ClPanel2, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList
            };
            _pnlAudit.Controls.Add(_cmbAudUser);

            Lbl("EVENT TYPE", 190, 62, 100, 18, ClTextDim, true, _pnlAudit);
            _cmbAudType = new ComboBox
            {
                Left = 190, Top = 84, Width = 150, Height = 30,
                BackColor = ClPanel2, ForeColor = ClText,
                FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbAudType.Items.AddRange(new object[]
            {
                "All", "LOGIN", "LOGOUT", "LOGIN_FAILED", "NAV_BLOCKED",
                "COPY_BLOCKED", "PRINT_BLOCKED", "SCREENSHOT",
                "CONFIG_CHANGE", "LOCATION_DENIED"
            });
            _cmbAudType.SelectedIndex = 0;
            _pnlAudit.Controls.Add(_cmbAudType);

            _btnRefAudit = MakeButton("🔍  Filter", 360, 84, 100, 28, ClAccent);
            _btnRefAudit.Click += (s, e) => RefreshAuditGrid();
            _pnlAudit.Controls.Add(_btnRefAudit);

            var btnExport = MakeButton("📥  Export CSV", 470, 84, 120, 28, ClOrange);
            btnExport.Click += OnExportCsv;
            _pnlAudit.Controls.Add(btnExport);

            _gridAudit = MakeGrid(24, 124, 820, 440);
            _gridAudit.Columns.AddRange(new DataGridViewColumn[]
            {
                new DataGridViewTextBoxColumn { Name = "Time",     HeaderText = "Timestamp",  Width = 150 },
                new DataGridViewTextBoxColumn { Name = "User",     HeaderText = "User",       Width = 100 },
                new DataGridViewTextBoxColumn { Name = "Type",     HeaderText = "Event Type", Width = 120 },
                new DataGridViewTextBoxColumn { Name = "Details",  HeaderText = "Details",    Width = 300 },
                new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity",   Width = 80  },
                new DataGridViewTextBoxColumn { Name = "Location", HeaderText = "Location",   Width = 80  }
            });
            _pnlAudit.Controls.Add(_gridAudit);

            _pnlAudit.VisibleChanged += (s, e) =>
            {
                if (!_pnlAudit.Visible) return;
                _cmbAudUser.Items.Clear();
                _cmbAudUser.Items.Add("All");
                foreach (var u in PolicyEngine.GetAllUsers())
                    _cmbAudUser.Items.Add(u.Username);
                _cmbAudUser.SelectedIndex = 0;
                RefreshAuditGrid();
            };
        }

        private void RefreshAuditGrid()
        {
            _gridAudit.Rows.Clear();

            var userFilter = _cmbAudUser.SelectedItem?.ToString();
            var typeFilter = _cmbAudType.SelectedItem?.ToString();

            var events = AuditLogger.GetFilteredEvents(userFilter, typeFilter);

            foreach (var ev in events)
            {
                int row = _gridAudit.Rows.Add(
                    ev.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    ev.Username, ev.EventType, ev.Details, ev.Severity, ev.Location);

                _gridAudit.Rows[row].DefaultCellStyle.BackColor = ev.Severity switch
                {
                    "Critical" => Color.FromArgb(60, 20, 20),
                    "Warning"  => Color.FromArgb(55, 40, 10),
                    _          => ClSurface
                };
            }
        }

        private void OnExportCsv(object? sender, EventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"audit_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,Username,EventType,Details,Severity,Location");

            var events = AuditLogger.GetAllEvents();
            foreach (var ev in events)
            {
                sb.AppendLine(
                    $"\"{ev.Timestamp:yyyy-MM-dd HH:mm:ss}\"," +
                    $"\"{ev.Username}\"," +
                    $"\"{ev.EventType}\"," +
                    $"\"{ev.Details.Replace("\"", "\"\"")}\"," +
                    $"\"{ev.Severity}\"," +
                    $"\"{ev.Location}\"");
            }

            File.WriteAllText(sfd.FileName, sb.ToString());
            MessageBox.Show($"Exported {events.Count} events.", "Export Complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        private Label SectionTitle(string text)
        {
            return new Label
            {
                Text = text, Width = 600, Height = 30, AutoSize = false,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Color.White
            };
        }

        private Button MakeNavBtn(string text, int top)
        {
            var btn = new Button
            {
                Text = text, Left = 0, Top = top, Width = 180, Height = 36,
                FlatStyle = FlatStyle.Flat, BackColor = ClSurface, ForeColor = ClText,
                Font = new Font("Segoe UI", 9.5f), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0), Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Button MakeButton(string text, int left, int top, int w, int h, Color bg)
        {
            var btn = new Button
            {
                Text = text, Left = left, Top = top, Width = w, Height = h,
                FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void Lbl(string text, int x, int y, int w, int h, Color color, bool bold, Panel parent)
        {
            parent.Controls.Add(new Label
            {
                Text = text, Left = x, Top = y, Width = w, Height = h,
                ForeColor = color,
                Font = new Font("Segoe UI", 7.5f, bold ? FontStyle.Bold : FontStyle.Regular)
            });
        }

        private CheckBox MakeCheck(string text, int left, int top, Panel parent)
        {
            var chk = new CheckBox
            {
                Text = text, Left = left, Top = top, Width = 300, Height = 24,
                ForeColor = ClText, Font = new Font("Segoe UI", 9.5f)
            };
            parent.Controls.Add(chk);
            return chk;
        }

        private DataGridView MakeGrid(int left, int top, int w, int h)
        {
            var g = new DataGridView
            {
                Left = left, Top = top, Width = w, Height = h,
                BackgroundColor = ClSurface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = ClBorder, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                EnableHeadersVisualStyles = false
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = ClPanel2;
            g.ColumnHeadersDefaultCellStyle.ForeColor = ClText;
            g.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 8f, FontStyle.Bold);
            g.ColumnHeadersHeight                     = 30;
            g.DefaultCellStyle.BackColor              = ClSurface;
            g.DefaultCellStyle.ForeColor              = ClText;
            g.DefaultCellStyle.SelectionBackColor     = Color.FromArgb(33, 55, 88);
            g.DefaultCellStyle.SelectionForeColor     = Color.White;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(18, 22, 28);
            g.RowTemplate.Height = 26;
            return g;
        }
    }
}
