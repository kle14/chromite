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
        //  WINDOWS NATIVE API  (P/Invoke)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sets how a window's content is treated during screen capture.
        /// WDA_MONITOR makes the window INVISIBLE to ALL capture tools.
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        /// <summary>Registers this window to receive WM_CLIPBOARDUPDATE messages.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        /// <summary>Unregisters the clipboard listener.</summary>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        // ── Display Affinity Flags ──────────────────────────────────────
        //   WDA_NONE              = 0x00  → no protection
        //   WDA_MONITOR           = 0x01  → shows black in captures (Win7+)
        //   WDA_MONITOR= 0x11  → COMPLETELY excluded (Win10 2004+)
        //                                   Works against: PrintScreen, Win+Shift+S,
        //                                   Snipping Tool, OBS, any BitBlt capture,
        //                                   Remote Desktop mirroring
        private const uint WDA_NONE               = 0x00000000;
        private const uint WDA_MONITOR             = 0x00000001;
        

        // Windows messages
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        // ═══════════════════════════════════════════════════════════════════
        //  FIELDS
        // ═══════════════════════════════════════════════════════════════════

        // UI controls
        private WebView2 _webView       = null!;
        private TextBox  _urlBar        = null!;
        private Button   _btnBack       = null!;
        private Button   _btnForward    = null!;
        private Button   _btnRefresh    = null!;
        private Button   _btnGo         = null!;
        private Label    _statusLabel   = null!;
        private Label    _secureLabel   = null!;
        private Panel    _toolbar       = null!;
        private Panel    _statusBar     = null!;
        private Label    _loadingLabel  = null!;

        // State
        private bool   _screenProtected     = false;
        private bool   _webViewReady        = false;
        private string _internalClipboard   = "";   // stays inside app, never hits OS clipboard
        private bool   _isNavigating        = false;

        // ═══════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════

        public MainForm()
        {
            BuildUI();

            // After handle is created, apply OS-level protections
            this.HandleCreated += OnHandleCreated;
            this.FormClosing   += OnFormClosing;
            this.Deactivate    += OnDeactivate;
            this.Activated     += OnActivated;

            // Init WebView2 asynchronously after form loads
            this.Load += async (s, e) => await InitWebViewAsync();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  OS-LEVEL SCREENSHOT PROTECTION
        // ═══════════════════════════════════════════════════════════════════

        private void OnHandleCreated(object? sender, EventArgs e)
        {
            ApplyScreenProtection();
            AddClipboardFormatListener(this.Handle); // start listening for clipboard changes
        }

        private void ApplyScreenProtection()
        {
            // Try the strongest protection first: WDA_MONITOR
            // This makes the window COMPLETELY invisible to any capture tool.
            // Works on Windows 10 version 2004 (build 19041) and later.
            bool ok = SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);

            if (!ok)
            {
                // Older Windows 10: fall back to WDA_MONITOR.
                // The window appears solid BLACK in any screenshot or screen recorder.
                ok = SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);
            }

            _screenProtected = ok;
            UpdateSecureLabel();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CLIPBOARD PROTECTION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// When the user switches away from our app, wipe the OS clipboard.
        /// This ensures anything copied inside our browser cannot be pasted elsewhere.
        /// </summary>
        private void OnDeactivate(object? sender, EventArgs e)
        {
            ClearOsClipboard();
        }

        private void OnActivated(object? sender, EventArgs e)
        {
            // Ensure screen protection is still active
            // (some DWM operations can reset it)
            if (_screenProtected)
                SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);
        }

        private void ClearOsClipboard()
        {
            try
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    EmptyClipboard();
                    CloseClipboard();
                }
            }
            catch { /* silently ignore — clipboard might be in use */ }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WndProc  (intercept Windows messages)
        // ═══════════════════════════════════════════════════════════════════

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                // Clipboard was changed while we are active.
                // If WebView2 put something there (shouldn't due to JS injection),
                // clear it immediately as a safety net.
                if (this.ContainsFocus)
                {
                    // Small delay so WebView2's own paste operations aren't broken
                    // then wipe any stale clipboard data from the browser
                    Task.Delay(50).ContinueWith(_ =>
                    {
                        this.BeginInvoke(ClearOsClipboard);
                    });
                }
            }
            base.WndProc(ref m);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WEBVIEW2 INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        private async Task InitWebViewAsync()
        {
            try
            {
                _loadingLabel.Visible = true;
                _loadingLabel.Text    = "Starting secure browser engine...";

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,   // use installed WebView2 runtime
                    userDataFolder: null,            // default temp folder
                    options: null
                );

                await _webView.EnsureCoreWebView2Async(env);

                // ── WebView2 Security Settings ──────────────────────────
                var s = _webView.CoreWebView2.Settings;

                s.AreDevToolsEnabled                  = false;  // no F12
                s.AreDefaultContextMenusEnabled       = false;  // custom menu only
                s.AreBrowserAcceleratorKeysEnabled    = false;  // no Ctrl+U, Ctrl+S etc.
                s.IsStatusBarEnabled                  = false;  // no link previews
                s.IsZoomControlEnabled                = false;  // no Ctrl+scroll zoom
                s.IsBuiltInErrorPageEnabled           = false;
                s.IsPasswordAutosaveEnabled           = false;
                s.IsGeneralAutofillEnabled            = false;
                s.IsSwipeNavigationEnabled            = false;  // no swipe back/fwd

                // ── Event Hooks ─────────────────────────────────────────
                _webView.CoreWebView2.NavigationStarting   += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;
                _webView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
                _webView.CoreWebView2.WebMessageReceived   += OnWebMessageReceived;
                _webView.CoreWebView2.NewWindowRequested   += OnNewWindowRequested;

                // ── Load homepage ───────────────────────────────────────
                _webView.CoreWebView2.Navigate("https://www.google.com");

                _webViewReady         = true;
                _loadingLabel.Visible = false;
                _btnBack.Enabled      = false;
                _btnForward.Enabled   = false;
                _btnRefresh.Enabled   = true;
                _btnGo.Enabled        = true;
            }
            catch (Exception ex)
            {
                _loadingLabel.Visible = false;

                string webview2Url = "https://developer.microsoft.com/microsoft-edge/webview2/";
                string msg =
                    "WebView2 Runtime is required but was not found.\n\n" +
                    "Please install it (it's free from Microsoft):\n" +
                    webview2Url + "\n\n" +
                    "→ Download the 'Evergreen Bootstrapper'\n" +
                    "→ Run it, it installs automatically\n" +
                    "→ Restart this app\n\n" +
                    $"Technical detail: {ex.Message}";

                MessageBox.Show(msg, "WebView2 Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Exit();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NAVIGATION EVENTS
        // ═══════════════════════════════════════════════════════════════════

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _isNavigating = true;
            this.BeginInvoke(() =>
            {
                _urlBar.Text        = e.Uri;
                _statusLabel.Text   = $"Loading {e.Uri}";
                _btnRefresh.Text    = "✕";   // becomes stop button
            });
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _isNavigating = false;
            this.BeginInvoke(() =>
            {
                string url = _webView.CoreWebView2.Source ?? "";
                _urlBar.Text        = url;
                _btnRefresh.Text    = "↻";
                _btnBack.Enabled    = _webView.CoreWebView2.CanGoBack;
                _btnForward.Enabled = _webView.CoreWebView2.CanGoForward;

                if (e.IsSuccess)
                    _statusLabel.Text = "Ready  •  " + _webView.CoreWebView2.DocumentTitle;
                else
                    _statusLabel.Text = $"Failed to load — {e.WebErrorStatus}";
            });

            // Inject clipboard blocking JS on every page (including after redirects)
            InjectClipboardBlockerScript();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  JAVASCRIPT CLIPBOARD BLOCKER INJECTION
        //  This runs inside the webpage and prevents copy from ever reaching
        //  the OS clipboard at the browser engine level.
        // ═══════════════════════════════════════════════════════════════════

        private void InjectClipboardBlockerScript()
        {
            const string js = @"
(function() {
    'use strict';

    // ── Block copy event ────────────────────────────────────────────────
    // Intercepts BEFORE the browser engine can write to the OS clipboard.
    document.addEventListener('copy', function(e) {
        e.preventDefault();
        e.stopImmediatePropagation();

        // Capture selected text into the app's internal buffer via postMessage
        var selectedText = '';
        try {
            var sel = window.getSelection();
            if (sel) selectedText = sel.toString();
        } catch(err) {}

        // Send to host app (stored in memory only, never in OS clipboard)
        try {
            window.chrome.webview.postMessage(
                JSON.stringify({ type: 'copy', text: selectedText })
            );
        } catch(err) {}
    }, true);

    // ── Block cut event ─────────────────────────────────────────────────
    document.addEventListener('cut', function(e) {
        e.preventDefault();
        e.stopImmediatePropagation();
    }, true);

    // ── Block paste event from OS clipboard ─────────────────────────────
    // (Allows internal paste from our internal buffer if needed,
    //  but prevents OS clipboard content from being pasted in)
    document.addEventListener('paste', function(e) {
        e.preventDefault();
        e.stopImmediatePropagation();
    }, true);

    // ── Override navigator.clipboard API ───────────────────────────────
    // Some modern sites use this API directly instead of clipboard events.
    try {
        Object.defineProperty(navigator, 'clipboard', {
            get: function() {
                return {
                    writeText:  async function(text) { return Promise.resolve(); },
                    readText:   async function()     { return Promise.resolve(''); },
                    write:      async function(data) { return Promise.resolve(); },
                    read:       async function()     { return Promise.resolve([]); }
                };
            },
            configurable: false,
            enumerable:   true
        });
    } catch(err) { /* already sealed — that's fine */ }

    // ── Block keyboard shortcuts ────────────────────────────────────────
    document.addEventListener('keydown', function(e) {
        // Block Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+A (select all → then copy)
        if (e.ctrlKey || e.metaKey) {
            var key = e.key ? e.key.toLowerCase() : '';
            if (key === 'c' || key === 'x' || key === 'v' || key === 'a') {
                e.preventDefault();
                e.stopImmediatePropagation();
            }
        }
        // Block Print Screen at JS level (belt-and-suspenders alongside OS level)
        if (e.key === 'PrintScreen') {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);

    // ── Block execCommand copy/cut/paste ────────────────────────────────
    var _origExecCommand = document.execCommand.bind(document);
    document.execCommand = function(command) {
        var cmd = (command || '').toLowerCase();
        if (cmd === 'copy' || cmd === 'cut' || cmd === 'paste') {
            return false;  // silently refuse
        }
        return _origExecCommand.apply(document, arguments);
    };

    // ── Disable drag-and-drop data exfiltration ─────────────────────────
    document.addEventListener('dragstart', function(e) {
        e.preventDefault();
    }, true);

})();
";
            if (_webViewReady && _webView.CoreWebView2 != null)
            {
                _ = _webView.CoreWebView2.ExecuteScriptAsync(js);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CONTEXT MENU  (remove Copy/Cut/Paste items)
        // ═══════════════════════════════════════════════════════════════════

        private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var items = e.MenuItems;

            // Walk backwards so removing items doesn't mess up indices
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var name = (item.Name ?? "").ToLowerInvariant();

                // Remove any clipboard-related menu entries
                if (name is "copy"       or "cut"         or "paste"
                         or "copyimage"  or "copylink"    or "copyimageurl"
                         or "saveimageas" or "savelinkas" or "selectall"
                         or "print"      or "saveas"      or "viewsource")
                {
                    items.RemoveAt(i);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WEB MESSAGE (internal clipboard from JS)
        // ═══════════════════════════════════════════════════════════════════

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // Store the copied text in app memory only — never written to OS clipboard
                var raw = e.TryGetWebMessageAsString();
                _internalClipboard = raw ?? "";
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NEW WINDOW (open in same window, not external)
        // ═══════════════════════════════════════════════════════════════════

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Redirect new window requests into the same browser tab
            e.Handled = true;
            _webView.CoreWebView2.Navigate(e.Uri);
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
        //  NAVIGATION HELPERS
        // ═══════════════════════════════════════════════════════════════════

        private void Navigate(string input)
        {
            if (!_webViewReady) return;

            string url = input.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Looks like a domain (e.g. "google.com")
                if (url.Contains('.') && !url.Contains(' '))
                    url = "https://" + url;
                else
                    // Search query
                    url = "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
            }

            _webView.CoreWebView2.Navigate(url);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UI  CONSTRUCTION
        // ═══════════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // ── Form ──────────────────────────────────────────────────────
            this.Text            = "Secure Browser";
            this.Size            = new Size(1400, 860);
            this.MinimumSize     = new Size(900, 600);
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.BackColor       = Color.FromArgb(28, 28, 32);
            this.ForeColor       = Color.FromArgb(220, 220, 220);
            this.Font            = new Font("Segoe UI", 9.5f);

            // ── Toolbar panel ─────────────────────────────────────────────
            _toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = Color.FromArgb(38, 38, 44),
                Padding   = new Padding(0)
            };

            // Back button
            _btnBack = MakeNavButton("◀", 8, 11, 34, 30, "Go back");
            _btnBack.Enabled = false;
            _btnBack.Click  += (s, e) => { if (_webViewReady) _webView.CoreWebView2.GoBack(); };

            // Forward button
            _btnForward = MakeNavButton("▶", 46, 11, 34, 30, "Go forward");
            _btnForward.Enabled = false;
            _btnForward.Click  += (s, e) => { if (_webViewReady) _webView.CoreWebView2.GoForward(); };

            // Refresh / Stop button
            _btnRefresh = MakeNavButton("↻", 84, 11, 34, 30, "Refresh page");
            _btnRefresh.Enabled = false;
            _btnRefresh.Click  += (s, e) =>
            {
                if (!_webViewReady) return;
                if (_isNavigating)
                    _webView.CoreWebView2.Stop();
                else
                    _webView.CoreWebView2.Reload();
            };

            // URL bar
            _urlBar = new TextBox
            {
                Left        = 126,
                Top         = 12,
                Height      = 28,
                Width       = 1000,         // will resize via Anchor
                Anchor      = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                BackColor   = Color.FromArgb(55, 55, 62),
                ForeColor   = Color.FromArgb(230, 230, 230),
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Segoe UI", 10.5f),
                TabIndex    = 0
            };
            _urlBar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return)
                {
                    Navigate(_urlBar.Text);
                    e.SuppressKeyPress = true;
                }
            };

            // Go button
            _btnGo = new Button
            {
                Text      = "Go",
                Top       = 12,
                Width     = 42,
                Height    = 28,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor    = Cursors.Hand,
                TabStop   = false,
                Enabled   = false
            };
            _btnGo.FlatAppearance.BorderSize = 0;
            _btnGo.Click += (s, e) => Navigate(_urlBar.Text);

            // Secure badge label (top-right of toolbar)
            _secureLabel = new Label
            {
                Text      = "⏳ Initializing...",
                Dock      = DockStyle.Right,
                Width     = 200,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.FromArgb(50, 50, 57),
                Padding   = new Padding(0, 0, 8, 0)
            };

            // Position Go button just right of URL bar
            // Done after toolbar is added via Resize event
            _toolbar.SizeChanged += (s, e) =>
            {
                _btnGo.Left = _urlBar.Right + 4;
            };

            _toolbar.Controls.AddRange(new Control[]
            {
                _btnBack, _btnForward, _btnRefresh,
                _urlBar, _btnGo, _secureLabel
            });

            // ── Status bar ────────────────────────────────────────────────
            _statusBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 26,
                BackColor = Color.FromArgb(38, 38, 44)
            };

            _statusLabel = new Label
            {
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(140, 140, 150),
                Font      = new Font("Segoe UI", 8.5f),
                Padding   = new Padding(10, 0, 0, 0),
                Text      = "Starting..."
            };

            var shieldLabel = new Label
            {
                Dock      = DockStyle.Right,
                Width     = 320,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(80, 200, 120),
                Font      = new Font("Segoe UI", 8.5f),
                Padding   = new Padding(0, 0, 12, 0),
                Text      = "🛡  Screenshot · Clipboard · Recording  BLOCKED"
            };

            _statusBar.Controls.AddRange(new Control[] { _statusLabel, shieldLabel });

            // ── WebView2 ──────────────────────────────────────────────────
            _webView = new WebView2
            {
                Dock     = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 32)
            };

            // ── Loading overlay label ─────────────────────────────────────
            _loadingLabel = new Label
            {
                Text      = "Starting secure browser engine...",
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font("Segoe UI", 14f),
                ForeColor = Color.FromArgb(100, 180, 255),
                BackColor = Color.FromArgb(28, 28, 32),
                Visible   = false
            };

            // ── Add to form (order matters for docking) ───────────────────
            this.Controls.Add(_webView);
            this.Controls.Add(_loadingLabel);
            this.Controls.Add(_toolbar);
            this.Controls.Add(_statusBar);
        }

        private static Button MakeNavButton(string text, int left, int top, int w, int h, string tooltip)
        {
            var btn = new Button
            {
                Text      = text,
                Left      = left,
                Top       = top,
                Width     = w,
                Height    = h,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.FromArgb(210, 210, 210),
                Font      = new Font("Segoe UI", 10f),
                Cursor    = Cursors.Hand,
                TabStop   = false
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(72, 72, 80);
            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);
            return btn;
        }

        private void UpdateSecureLabel()
        {
            if (_secureLabel == null || _secureLabel.IsDisposed) return;

            if (_screenProtected)
            {
                _secureLabel.Text      = "🔒  SECURE MODE  ON";
                _secureLabel.ForeColor = Color.FromArgb(80, 220, 100);
                _secureLabel.BackColor = Color.FromArgb(20, 55, 25);
            }
            else
            {
                _secureLabel.Text      = "⚠  PROTECTION FAILED";
                _secureLabel.ForeColor = Color.FromArgb(255, 165, 0);
                _secureLabel.BackColor = Color.FromArgb(60, 40, 0);
            }
        }
    }
}
