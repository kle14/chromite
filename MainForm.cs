using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SecureBrowser
{
    public class MainForm : Form
    {
        // ═══════════════════════════════════════════════════════════════════
        //  WIN32  (screenshot protection)
        // ═══════════════════════════════════════════════════════════════════

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        private const uint WDA_MONITOR = 0x00000001; // Shows as BLACK in all captures

        // ═══════════════════════════════════════════════════════════════════
        //  STATE
        // ═══════════════════════════════════════════════════════════════════

        public  bool        ShouldLogout { get; private set; } = false;
        private UserSession _session;
        private bool        _webViewReady = false;
        private bool        _isNavigating = false;

        // ── Controls ──────────────────────────────────────────────────────
        private WebView2 _webView       = null!;
        private TextBox  _urlBar        = null!;
        private Button   _btnBack       = null!;
        private Button   _btnForward    = null!;
        private Button   _btnRefresh    = null!;
        private Button   _btnGo         = null!;
        private Button   _btnAdmin      = null!;
        private Button   _btnLogout     = null!;
        private Label    _lblUser       = null!;
        private Label    _lblSecure     = null!;
        private Label    _statusLabel   = null!;
        private Panel    _toolbar       = null!;
        private Panel    _statusBar     = null!;
        private Label    _loadingLabel  = null!;

        // ── Colours ───────────────────────────────────────────────────────
        private static readonly Color BG      = Color.FromArgb(28,  28,  32);
        private static readonly Color Surface = Color.FromArgb(38,  38,  44);
        private static readonly Color Accent  = Color.FromArgb(0,  120, 215);
        private static readonly Color Green   = Color.FromArgb(63, 185,  80);
        private static readonly Color Orange  = Color.FromArgb(210,153,  34);
        private static readonly Color Red     = Color.FromArgb(248, 81,  73);
        private static readonly Color Purple  = Color.FromArgb(188,140, 255);

        // ═══════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════

        public MainForm(UserSession session)
        {
            _session = session;
            BuildUI();

            this.HandleCreated += OnHandleCreated;
            this.FormClosing   += OnFormClosing;
            this.Deactivate    += OnDeactivate;
            this.Activated     += OnActivated;
            this.Load          += async (s, e) => await InitWebViewAsync();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SCREENSHOT PROTECTION  (OS kernel level via WDA_MONITOR)
        // ═══════════════════════════════════════════════════════════════════

        private void OnHandleCreated(object? sender, EventArgs e)
        {
            SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);
            AddClipboardFormatListener(this.Handle);
            AuditLogger.LogScreenshotAttempt(_session.Account.Username);
            // Note: we log "protection applied" rather than an actual attempt here
        }

        private void OnActivated(object? sender, EventArgs e)
            => SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);

        // ═══════════════════════════════════════════════════════════════════
        //  CLIPBOARD PROTECTION  (wipe on focus loss)
        // ═══════════════════════════════════════════════════════════════════

        private void OnDeactivate(object? sender, EventArgs e)
            => ClearOsClipboard();

        private void ClearOsClipboard()
        {
            try
            {
                if (OpenClipboard(IntPtr.Zero)) { EmptyClipboard(); CloseClipboard(); }
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CLIPBOARDUPDATE = 0x031D;
            if (m.Msg == WM_CLIPBOARDUPDATE && this.ContainsFocus)
            {
                Task.Delay(50).ContinueWith(_ =>
                    this.BeginInvoke(ClearOsClipboard));
            }
            base.WndProc(ref m);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WEBVIEW2 INITIALISATION
        // ═══════════════════════════════════════════════════════════════════

        private async Task InitWebViewAsync()
        {
            try
            {
                _loadingLabel.Visible = true;

                var env = await CoreWebView2Environment.CreateAsync(null, null, null);
                await _webView.EnsureCoreWebView2Async(env);

                var s = _webView.CoreWebView2.Settings;
                s.AreDevToolsEnabled               = false;
                s.AreDefaultContextMenusEnabled    = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsStatusBarEnabled               = false;
                s.IsZoomControlEnabled             = false;
                s.IsPasswordAutosaveEnabled        = false;
                s.IsGeneralAutofillEnabled         = false;

                _webView.CoreWebView2.NavigationStarting  += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _webView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
                _webView.CoreWebView2.NewWindowRequested  += (s, e) =>
                {
                    e.Handled = true;
                    _webView.CoreWebView2.Navigate(e.Uri);
                };

                _webView.CoreWebView2.Navigate("https://www.google.com");

                _webViewReady         = true;
                _loadingLabel.Visible = false;
            }
            catch (Exception ex)
            {
                _loadingLabel.Visible = false;
                MessageBox.Show(
                    "WebView2 Runtime is required.\n\n" +
                    "Download the Evergreen Bootstrapper from:\n" +
                    "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                    $"Detail: {ex.Message}",
                    "WebView2 Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Exit();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NAVIGATION  (URL whitelist + SSL enforcement)
        // ═══════════════════════════════════════════════════════════════════

        private void OnNavigationStarting(object? sender,
            CoreWebView2NavigationStartingEventArgs e)
        {
            _isNavigating = true;
            var url      = e.Uri;
            var username = _session.Account.Username;

            // ── SSL-Only check ────────────────────────────────────────────
            if (_session.Permissions.SSLOnly &&
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                AuditLogger.LogNavBlocked(username, url, "SSL-only policy — HTTP not permitted");
                ShowBlockPage(url, "SSL Only Policy",
                    "Your security policy requires HTTPS connections only.<br>" +
                    "HTTP sites are not permitted.");
                return;
            }

            // ── URL whitelist check ───────────────────────────────────────
            if (!PolicyEngine.IsUrlAllowed(username, url))
            {
                e.Cancel = true;
                AuditLogger.LogNavBlocked(username, url, "Not on URL whitelist");
                ShowBlockPage(url, "URL Not Whitelisted",
                    "This URL is not on your approved access list.<br>" +
                    "Contact your administrator to request access.");
                return;
            }

            this.BeginInvoke(() =>
            {
                _urlBar.Text      = url;
                _statusLabel.Text = $"Loading…  {url}";
                _btnRefresh.Text  = "✕";
            });
        }

        private void OnNavigationCompleted(object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
        {
            _isNavigating = false;
            this.BeginInvoke(() =>
            {
                _urlBar.Text        = _webView.CoreWebView2.Source ?? "";
                _btnRefresh.Text    = "↻";
                _btnBack.Enabled    = _webView.CoreWebView2.CanGoBack;
                _btnForward.Enabled = _webView.CoreWebView2.CanGoForward;
                _statusLabel.Text   = e.IsSuccess
                    ? $"Ready  •  {_webView.CoreWebView2.DocumentTitle}"
                    : $"Error  •  {e.WebErrorStatus}";
            });

            InjectSecurityScripts();
        }

        private void ShowBlockPage(string url, string reason, string message)
        {
            var html = $@"<!DOCTYPE html><html>
<head><meta charset='utf-8'>
<style>
  * {{ margin:0; padding:0; box-sizing:border-box; }}
  body {{ background:#0d1117; color:#c9d1d9; font-family:'Segoe UI',sans-serif;
         display:flex; align-items:center; justify-content:center;
         height:100vh; flex-direction:column; gap:16px; }}
  .icon  {{ font-size:72px; }}
  .title {{ font-size:28px; font-weight:700; color:#f85149; }}
  .box   {{ background:#161b22; border:1px solid #30363d; border-radius:12px;
            padding:32px 48px; max-width:560px; text-align:center; }}
  .msg   {{ color:#8b949e; font-size:15px; line-height:1.6; margin:12px 0; }}
  .url   {{ font-family:monospace; background:#0d1117; border:1px solid #30363d;
            border-radius:4px; padding:8px 14px; font-size:13px;
            color:#58a6ff; margin:8px 0; word-break:break-all; }}
  .user  {{ color:#3fb950; font-size:13px; margin-top:8px; }}
  .log   {{ color:#d29922; font-size:12px; margin-top:4px; }}
</style></head><body>
<div class='box'>
  <div class='icon'>🛡️</div>
  <div class='title'>Access Blocked</div>
  <div class='msg'>{message}</div>
  <div class='url'>{System.Net.WebUtility.HtmlEncode(url)}</div>
  <div class='user'>User: {_session.Account.DisplayName}  •  Location: {_session.Location}</div>
  <div class='log'>⚠ This attempt has been logged and will be reviewed.</div>
</div></body></html>";

            if (_webViewReady && _webView.CoreWebView2 != null)
                _webView.CoreWebView2.NavigateToString(html);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  JAVASCRIPT INJECTION  (clipboard + keyboard blocking)
        // ═══════════════════════════════════════════════════════════════════

        private void InjectSecurityScripts()
        {
            if (!_webViewReady || _webView.CoreWebView2 == null) return;

            // If this user has clipboard allowed — only inject minimal script
            if (_session.Permissions.AllowClipboard)
            {
                // Still block print screen at JS level
                _ = _webView.CoreWebView2.ExecuteScriptAsync(@"
(function(){
  document.addEventListener('keydown', function(e){
    if(e.key==='PrintScreen'){e.preventDefault();e.stopImmediatePropagation();}
  }, true);
})();");
                return;
            }

            // Full clipboard lockdown
            var js = $@"
(function(){{
  'use strict';

  // Block copy event — log it to host
  document.addEventListener('copy', function(e){{
    e.preventDefault(); e.stopImmediatePropagation();
    try{{ window.chrome.webview.postMessage(JSON.stringify({{type:'copy'}})); }}catch(err){{}}
  }}, true);

  document.addEventListener('cut', function(e){{
    e.preventDefault(); e.stopImmediatePropagation();
  }}, true);

  document.addEventListener('paste', function(e){{
    e.preventDefault(); e.stopImmediatePropagation();
  }}, true);

  // Override clipboard API
  try{{
    Object.defineProperty(navigator, 'clipboard', {{
      get: function(){{
        return {{
          writeText: async function(){{ return Promise.resolve(); }},
          readText:  async function(){{ return Promise.resolve(''); }},
          write:     async function(){{ return Promise.resolve(); }},
          read:      async function(){{ return Promise.resolve([]); }}
        }};
      }},
      configurable: false
    }});
  }}catch(err){{}}

  // Block keyboard shortcuts
  document.addEventListener('keydown', function(e){{
    if(e.ctrlKey || e.metaKey){{
      var k = (e.key||'').toLowerCase();
      if(k==='c'||k==='x'||k==='v'||k==='a'){{
        e.preventDefault(); e.stopImmediatePropagation();
        if(k==='c') try{{window.chrome.webview.postMessage(JSON.stringify({{type:'copy'}}));}}catch(err){{}}
      }}
    }}
    if(e.key==='PrintScreen'){{e.preventDefault();e.stopImmediatePropagation();}}
  }}, true);

  // Block execCommand
  var _orig = document.execCommand.bind(document);
  document.execCommand = function(cmd){{
    if((cmd||'').toLowerCase()==='copy'||(cmd||'').toLowerCase()==='cut') return false;
    return _orig.apply(document,arguments);
  }};

  // Block drag
  document.addEventListener('dragstart', function(e){{
    e.preventDefault();
  }}, true);

}})();";

            _ = _webView.CoreWebView2.ExecuteScriptAsync(js);
            _webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                try
                {
                    var msg = e.TryGetWebMessageAsString();
                    if (msg != null && msg.Contains("\"type\":\"copy\""))
                    {
                        AuditLogger.LogCopyBlocked(_session.Account.Username);
                        this.BeginInvoke(() =>
                            _statusLabel.Text = $"⚠  Copy blocked  •  " +
                            $"{DateTime.Now:HH:mm:ss}  •  Logged");
                    }
                }
                catch { }
            };
        }

        private void OnContextMenuRequested(object? sender,
            CoreWebView2ContextMenuRequestedEventArgs e)
        {
            // Remove all clipboard and sensitive items from right-click menu
            var items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var name = (items[i].Name ?? "").ToLower();
                if (name is "copy" or "cut" or "paste" or "copyimage" or
                    "copyimageurl" or "copylink" or "saveimageas" or
                    "savelinkas" or "saveas" or "print" or
                    "viewsource" or "selectall")
                    items.RemoveAt(i);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NAVIGATION  (URL bar)
        // ═══════════════════════════════════════════════════════════════════

        private void Navigate(string input)
        {
            if (!_webViewReady) return;
            var url = input.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Contains('.') && !url.Contains(' ')
                    ? "https://" + url
                    : "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
            }

            _webView.CoreWebView2.Navigate(url);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CLEANUP
        // ═══════════════════════════════════════════════════════════════════

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            RemoveClipboardFormatListener(this.Handle);
            ClearOsClipboard();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UI CONSTRUCTION
        // ═══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            var isAdmin = _session.Account.Role == "Admin";

            Text          = $"Secure Browser  —  {_session.Account.DisplayName}";
            Size          = new Size(1400, 900);
            MinimumSize   = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = BG;
            ForeColor     = Color.FromArgb(220, 220, 220);
            Font          = new Font("Segoe UI", 9.5f);

            // ── Toolbar ───────────────────────────────────────────────────
            _toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 54,
                BackColor = Surface
            };

            _btnBack    = MakeNavBtn("◀",  8, "Go back");
            _btnForward = MakeNavBtn("▶", 46, "Go forward");
            _btnRefresh = MakeNavBtn("↻", 84, "Refresh");

            _btnBack.Click    += (s, e) => { if (_webViewReady) _webView.CoreWebView2.GoBack(); };
            _btnForward.Click += (s, e) => { if (_webViewReady) _webView.CoreWebView2.GoForward(); };
            _btnRefresh.Click += (s, e) =>
            {
                if (!_webViewReady) return;
                if (_isNavigating) _webView.CoreWebView2.Stop();
                else               _webView.CoreWebView2.Reload();
            };

            _urlBar = new TextBox
            {
                Left      = 126, Top = 14,
                Height    = 26,
                Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.FromArgb(230, 230, 230),
                BorderStyle = BorderStyle.FixedSingle,
                Font      = new Font("Segoe UI", 10f)
            };
            _urlBar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return)
                { Navigate(_urlBar.Text); e.SuppressKeyPress = true; }
            };

            _btnGo = new Button
            {
                Text      = "Go",
                Top = 14, Width = 38, Height = 26,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand, TabStop = false
            };
            _btnGo.FlatAppearance.BorderSize = 0;
            _btnGo.Click += (s, e) => Navigate(_urlBar.Text);

            // User badge
            var userColor = _session.Account.Role == "Admin" ? Purple : Green;
            _lblUser = new Label
            {
                Text      = $"  {(_session.Account.Role == "Admin" ? "⚙" : "👤")}  " +
                            $"{_session.Account.DisplayName}  [{_session.Account.Role}]  " +
                            $"•  {_session.Location}",
                Dock      = DockStyle.Right,
                Width     = 340,
                TextAlign = ContentAlignment.MiddleLeft,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = userColor,
                BackColor = Color.FromArgb(30, 30, 36),
                Padding   = new Padding(6, 0, 0, 0)
            };

            // Admin button (admin only)
            _btnAdmin = new Button
            {
                Text      = "⚙ Admin Console",
                Dock      = DockStyle.Right,
                Width     = 130,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 40, 80),
                ForeColor = Purple,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand, TabStop = false,
                Visible   = isAdmin
            };
            _btnAdmin.FlatAppearance.BorderSize  = 0;
            _btnAdmin.FlatAppearance.BorderColor = Purple;
            _btnAdmin.Click += (s, e) =>
            {
                var af = new AdminForm(_session.Account.Username);
                af.ShowDialog(this);
            };

            // Logout button
            _btnLogout = new Button
            {
                Text      = "↩ Logout",
                Dock      = DockStyle.Right,
                Width     = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 30, 30),
                ForeColor = Color.FromArgb(248, 81, 73),
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor    = Cursors.Hand, TabStop = false
            };
            _btnLogout.FlatAppearance.BorderSize = 0;
            _btnLogout.Click += (s, e) =>
            {
                AuditLogger.LogLogout(_session.Account.Username, _session.Location);
                ShouldLogout = true;
                Close();
            };

            _toolbar.SizeChanged += (s, e) =>
            {
                // Compute right edge of fixed right-docked controls
                int rightEdge = _toolbar.Width - _lblUser.Width -
                    (isAdmin ? _btnAdmin.Width : 0) - _btnLogout.Width;
                _btnGo.Left  = rightEdge - _btnGo.Width - 4;
                _urlBar.Width = _btnGo.Left - _urlBar.Left - 4;
            };

            _toolbar.Controls.AddRange(new Control[]
            {
                _btnBack, _btnForward, _btnRefresh,
                _urlBar, _btnGo,
                _lblUser,
                _btnAdmin,
                _btnLogout
            });

            // ── Status bar ────────────────────────────────────────────────
            _statusBar = new Panel
            {
                Dock = DockStyle.Bottom, Height = 26,
                BackColor = Surface
            };
            _statusLabel = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(140, 140, 150),
                Font      = new Font("Segoe UI", 8.5f),
                Padding   = new Padding(10, 0, 0, 0),
                Text      = "Ready"
            };

            // Permission indicators
            var perms = _session.Permissions;
            var permText = string.Join("  •  ", new[]
            {
                $"📋 Clipboard: {(perms.AllowClipboard ? "✓ Allowed" : "✗ Blocked")}",
                $"🖨 Print: {(perms.AllowPrint ? "✓ Allowed" : "✗ Blocked")}",
                $"🔒 SSL-Only: {(perms.SSLOnly ? "On" : "Off")}",
                $"🛡 Screenshot: BLOCKED"
            });

            var lblPerms = new Label
            {
                Dock      = DockStyle.Right,
                Width     = 700,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(80, 160, 80),
                Font      = new Font("Segoe UI", 8f),
                Padding   = new Padding(0, 0, 10, 0),
                Text      = permText
            };

            _statusBar.Controls.AddRange(new Control[] { _statusLabel, lblPerms });

            // ── WebView2 + Loading label ───────────────────────────────────
            _webView = new WebView2 { Dock = DockStyle.Fill };
            _loadingLabel = new Label
            {
                Text      = "Starting secure browser engine…",
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 13f),
                ForeColor = Color.FromArgb(100, 180, 255),
                BackColor = BG,
                Visible   = false
            };

            Controls.Add(_webView);
            Controls.Add(_loadingLabel);
            Controls.Add(_toolbar);
            Controls.Add(_statusBar);
        }

        private Button MakeNavBtn(string text, int left, string tip)
        {
            var btn = new Button
            {
                Text      = text, Left = left, Top = 12,
                Width     = 34, Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.FromArgb(210, 210, 210),
                Font      = new Font("Segoe UI", 10f),
                Cursor    = Cursors.Hand, TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            new ToolTip().SetToolTip(btn, tip);
            return btn;
        }
    }
}
