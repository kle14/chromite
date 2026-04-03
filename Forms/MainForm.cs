using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SecureBrowser.Data;
using SecureBrowser.Models;
using System.Diagnostics;

namespace SecureBrowser.Forms
{
    public class MainForm : Form
    {
        // ═══════════════════════════════════════════════════════════════════
        //  WINDOWS NATIVE API
        // ═══════════════════════════════════════════════════════════════════

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern bool CloseClipboard();

        // Low-level keyboard hook — intercepts keys OS-wide while our window is foreground
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool   UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] private static extern short  GetKeyState(int nVirtKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        private const uint WDA_NONE                = 0x00;
        private const uint WDA_MONITOR             = 0x01;   // window appears SOLID BLACK in captures
        private const uint WDA_EXCLUDEFROMCAPTURE  = 0x11;   // window vanishes from captures (NOT used — we want visible black)

        // SECURITY NOTE — WH_KEYBOARD_LL runs in user mode, not kernel mode.
        // A process running as the same Windows user can bypass it by:
        //   • Calling SendInput() — synthetic keystrokes skip all LL hooks by design (Win32 documented behavior)
        //   • Calling UnhookWindowsHookEx() with our hook handle (obtainable via tool scanning)
        //   • Installing a competing WH_KEYBOARD_LL hook earlier in the chain
        // True OS-level interception requires a kernel-mode filter driver (Windows Driver Kit).
        // This hook therefore provides a best-effort layer for accidental or low-sophistication saves,
        // not a security boundary against a determined same-privilege attacker.
        private const int  WH_KEYBOARD_LL  = 13;
        private const int  WM_KEYDOWN      = 0x0100;
        private const int  WM_SYSKEYDOWN   = 0x0104;
        private const uint GA_ROOT         = 2;

        // ═══════════════════════════════════════════════════════════════════
        //  STATE
        // ═══════════════════════════════════════════════════════════════════

        private readonly UserSession _session;
        public  bool ShouldLogout { get; private set; } = false;

        private WebView2 _webView       = null!;
        private TextBox  _urlBar        = null!;
        private Button   _btnBack       = null!;
        private Button   _btnForward    = null!;
        private Button   _btnRefresh    = null!;
        private Button   _btnGo         = null!;
        private Button   _btnAdmin      = null!;
        private Button   _btnLogout     = null!;
        private Label    _lblUser       = null!;
        private Label    _statusLabel   = null!;
        private Label    _lblPerms      = null!;
        private Panel    _toolbar       = null!;
        private Panel    _statusBar     = null!;
        private Label    _loadingLabel  = null!;

        private bool _webViewReady = false;
        private bool _isNavigating = false;
        private bool _cleanupDone  = false;

        private IntPtr                 _keyboardHookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc?  _keyboardProc;

        // Colors
        private static readonly Color BG      = Color.FromArgb(13,  17,  23);
        private static readonly Color Surface = Color.FromArgb(22,  27,  34);
        private static readonly Color Accent  = Color.FromArgb(88, 166, 255);
        private static readonly Color GreenC  = Color.FromArgb(63,  185,  80);
        private static readonly Color Purple  = Color.FromArgb(188, 140, 255);

        // ═══════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════════

        public MainForm(UserSession session)
        {
            _session = session;
            BuildUI();

            this.HandleCreated += (s, e) =>
            {
                ApplyScreenProtection();
                AddClipboardFormatListener(this.Handle);
                InstallKeyboardHook();
            };
            this.FormClosing += OnFormClosing;
            this.Deactivate  += (s, e) => WipeClipboardIfBlocked();
            this.Activated   += (s, e) => SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);
            this.Load        += async (s, e) => await InitWebViewAsync();

            // Periodically refresh the permissions display so admin changes show up
            var permTimer = new Timer { Interval = 3000 };
            permTimer.Tick += (s, e) => RefreshPermDisplay();
            permTimer.Start();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  OS-LEVEL PROTECTIONS
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyScreenProtection()
        {
            // WDA_MONITOR (0x01): the window content is replaced with SOLID BLACK in any
            // screen-capture or recording tool — the window frame is still visible so it
            // does not look like the app disappeared, but no sensitive content is leaked.
            // WDA_EXCLUDEFROMCAPTURE (0x11) was intentionally NOT used here because it
            // makes the window vanish entirely from captures, which looks like a bug rather
            // than a clear security signal.
            SetWindowDisplayAffinity(this.Handle, WDA_MONITOR);
        }

        /// <summary>
        /// Only wipes clipboard if current DB policy says clipboard is blocked.
        /// This means the moment admin enables clipboard, the wipe stops.
        /// </summary>
        private void WipeClipboardIfBlocked()
        {
            try
            {
                if (!PolicyEngine.IsClipboardAllowed(_session.Account.Username))
                    ClearOsClipboard();
            }
            catch { }
        }

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
                // Only wipe if clipboard is blocked for this user RIGHT NOW
                if (!PolicyEngine.IsClipboardAllowed(_session.Account.Username))
                {
                    Task.Delay(50).ContinueWith(_ => this.BeginInvoke(ClearOsClipboard));
                }
            }
            base.WndProc(ref m);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            RemoveClipboardFormatListener(this.Handle);
            UninstallKeyboardHook();
            ClearOsClipboard();

            // On first close attempt: cancel, wipe all cached session data, then re-close.
            if (!_cleanupDone)
            {
                e.Cancel = true;
                _ = CleanupAndCloseAsync();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  LOW-LEVEL KEYBOARD HOOK  (OS-level save-shortcut blocking)
        // ═══════════════════════════════════════════════════════════════════

        private void InstallKeyboardHook()
        {
            _keyboardProc = LowLevelKeyboardCallback;
            using var proc   = Process.GetCurrentProcess();
            using var module = proc.MainModule!;
            _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc,
                GetModuleHandle(module.ModuleName!), 0);
        }

        private void UninstallKeyboardHook()
        {
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookHandle);
                _keyboardHookHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Intercepts Ctrl+S / Ctrl+Shift+S at OS level — fires even when WebView2 has
        /// keyboard focus, and regardless of any user-side key remapping (AutoHotkey, etc.)
        /// because we match on the final virtual-key code that Windows resolves.
        /// </summary>
        private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                // Only intercept when OUR window (or any child — e.g. WebView2) is active
                var fgRoot = GetAncestor(GetForegroundWindow(), GA_ROOT);
                if (fgRoot == this.Handle)
                {
                    var kb   = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool ctrl = (GetKeyState((int)Keys.ControlKey) & 0x8000) != 0;

                    // Block Ctrl+S and Ctrl+Shift+S (Save / Save As)
                    if (ctrl && kb.vkCode == (uint)Keys.S)
                    {
                        AuditLogger.LogSaveBlocked(_session.Account.Username);
                        this.BeginInvoke(() =>
                            _statusLabel.Text = $"⚠  Save blocked  •  {DateTime.Now:HH:mm:ss}  •  Logged");
                        return (IntPtr)1;   // swallow — do not pass to any app
                    }
                }
            }
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// Catches save shortcuts when a WinForms control (URL bar, toolbar) has focus,
        /// providing a second layer on top of the low-level hook and the JS injection.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Control) != 0 && (keyData & ~Keys.Modifiers) == Keys.S)
            {
                AuditLogger.LogSaveBlocked(_session.Account.Username);
                _statusLabel.Text = $"⚠  Save blocked  •  {DateTime.Now:HH:mm:ss}  •  Logged";
                return true;   // consumed
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SESSION CACHE WIPE  (runs on logout / window close)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Asynchronously wipes all WebView2 session data (cookies, disk cache, DOM
        /// storage, download history, browsing history) before allowing the form to close.
        /// A 4-second timeout ensures the window still closes even if the API hangs.
        /// </summary>
        private async Task CleanupAndCloseAsync()
        {
            try
            {
                if (_webViewReady && _webView.CoreWebView2 != null)
                {
                    // Stop any active page so its data is flushed before we wipe
                    _webView.CoreWebView2.Navigate("about:blank");
                    await Task.Delay(200);

                    var clearTask = _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.AllDomStorage   |
                        CoreWebView2BrowsingDataKinds.Cookies          |
                        CoreWebView2BrowsingDataKinds.DiskCache        |
                        CoreWebView2BrowsingDataKinds.DownloadHistory  |
                        CoreWebView2BrowsingDataKinds.BrowsingHistory  |
                        CoreWebView2BrowsingDataKinds.CacheStorage);

                    await Task.WhenAny(clearTask, Task.Delay(4000));
                }
            }
            catch { }
            finally
            {
                _cleanupDone = true;
                this.BeginInvoke(Close);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  WEBVIEW2 INITIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        private async Task InitWebViewAsync()
        {
            try
            {
                _loadingLabel.Visible = true;
                _loadingLabel.Text    = "Starting secure browser engine…";

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
                s.IsSwipeNavigationEnabled         = false;

                _webView.CoreWebView2.NavigationStarting   += OnNavigationStarting;
                _webView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;
                _webView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
                _webView.CoreWebView2.WebMessageReceived   += OnWebMessageReceived;
                _webView.CoreWebView2.NewWindowRequested   += (s, e) =>
                {
                    e.Handled = true;
                    _webView.CoreWebView2.Navigate(e.Uri);
                };

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
                MessageBox.Show(
                    "WebView2 Runtime is required.\n\n" +
                    "Download from:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                    $"Detail: {ex.Message}",
                    "WebView2 Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Application.Exit();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NAVIGATION — live policy checks from DB
        // ═══════════════════════════════════════════════════════════════════

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            _isNavigating = true;
            var url      = e.Uri;
            var username = _session.Account.Username;

            // SSL-only check (live from DB)
            if (PolicyEngine.IsSSLOnly(username) &&
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                AuditLogger.LogNavBlocked(username, url, "SSL-only policy — HTTP not permitted");
                ShowBlockPage(url, "SSL Only Policy",
                    "Your security policy requires HTTPS connections only.<br>HTTP sites are not permitted.");
                return;
            }

            // URL whitelist check (live from DB)
            if (!PolicyEngine.IsUrlAllowed(username, url))
            {
                e.Cancel = true;
                AuditLogger.LogNavBlocked(username, url, "Not on URL whitelist");
                ShowBlockPage(url, "URL Not Whitelisted",
                    "This URL is not on your approved access list.<br>Contact your administrator to request access.");
                return;
            }

            this.BeginInvoke(() =>
            {
                _urlBar.Text      = url;
                _statusLabel.Text = $"Loading…  {url}";
                _btnRefresh.Text  = "✕";
            });
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
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

            // Re-inject scripts based on CURRENT DB permissions
            InjectSecurityScripts();
            RefreshPermDisplay();
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
        //  JAVASCRIPT INJECTION — checks LIVE permissions every page load
        // ═══════════════════════════════════════════════════════════════════

        private void InjectSecurityScripts()
        {
            if (!_webViewReady || _webView.CoreWebView2 == null) return;

            var username = _session.Account.Username;
            bool clipAllowed  = PolicyEngine.IsClipboardAllowed(username);
            bool printAllowed = PolicyEngine.IsPrintAllowed(username);

            // ── Core block — ALWAYS injected, regardless of policy ──────────
            // PrintScreen + Ctrl+S/Ctrl+Shift+S are unconditionally blocked so that
            // no page-level JS or key-remapping trick can trigger a save or screenshot.
            var js = @"(function(){
'use strict';

// PrintScreen — belt-and-suspenders alongside WDA_MONITOR (OS-level protection)
document.addEventListener('keydown', function(e){
  if(e.key==='PrintScreen'){e.preventDefault();e.stopImmediatePropagation();}
}, true);

// Ctrl+S / Ctrl+Shift+S — block Save Page As from within the renderer process
document.addEventListener('keydown', function(e){
  if((e.ctrlKey||e.metaKey)&&(e.key||'').toLowerCase()==='s'){
    e.preventDefault();e.stopImmediatePropagation();
    try{window.chrome.webview.postMessage(JSON.stringify({type:'save'}));}catch(err){}
  }
}, true);

";
            // ── Clipboard block (policy-controlled) ────────────────────────
            if (!clipAllowed)
            {
                js += @"
document.addEventListener('copy', function(e){
  e.preventDefault();e.stopImmediatePropagation();
  try{window.chrome.webview.postMessage(JSON.stringify({type:'copy'}));}catch(err){}
},true);
document.addEventListener('cut',  function(e){e.preventDefault();e.stopImmediatePropagation();},true);
document.addEventListener('paste',function(e){e.preventDefault();e.stopImmediatePropagation();},true);
try{
  Object.defineProperty(navigator,'clipboard',{get:function(){return{
    writeText:async function(){return Promise.resolve();},
    readText: async function(){return Promise.resolve('');},
    write:    async function(){return Promise.resolve();},
    read:     async function(){return Promise.resolve([]);}
  };},configurable:false});
}catch(err){}
document.addEventListener('keydown',function(e){
  if(e.ctrlKey||e.metaKey){
    var k=(e.key||'').toLowerCase();
    if(k==='c'||k==='x'||k==='v'||k==='a'){
      e.preventDefault();e.stopImmediatePropagation();
      if(k==='c')try{window.chrome.webview.postMessage(JSON.stringify({type:'copy'}));}catch(err){}
    }
  }
},true);
var _orig=document.execCommand.bind(document);
document.execCommand=function(cmd){
  if((cmd||'').toLowerCase()==='copy'||(cmd||'').toLowerCase()==='cut')return false;
  return _orig.apply(document,arguments);
};
document.addEventListener('dragstart',function(e){e.preventDefault();},true);
";
            }

            // ── Print (policy-controlled) ──────────────────────────────────
            // AreBrowserAcceleratorKeysEnabled=false kills Ctrl+P at the WebView2 level
            // unconditionally, so we must explicitly re-wire it when print is allowed,
            // and explicitly block + log it when print is not allowed.
            if (printAllowed)
            {
                js += @"
// Re-enable Ctrl+P: accelerator keys are globally disabled, so we restore
// the print action manually via window.print().
document.addEventListener('keydown',function(e){
  if((e.ctrlKey||e.metaKey)&&(e.key||'').toLowerCase()==='p'){
    e.preventDefault();
    window.print();
  }
},true);
";
            }
            else
            {
                js += @"
// Block Ctrl+P and override window.print() when print is not permitted.
document.addEventListener('keydown',function(e){
  if((e.ctrlKey||e.metaKey)&&(e.key||'').toLowerCase()==='p'){
    e.preventDefault();e.stopImmediatePropagation();
    try{window.chrome.webview.postMessage(JSON.stringify({type:'print'}));}catch(err){}
  }
},true);
window.print=function(){
  try{window.chrome.webview.postMessage(JSON.stringify({type:'print'}));}catch(err){}
};
";
            }

            js += "})();";
            _ = _webView.CoreWebView2.ExecuteScriptAsync(js);
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = e.TryGetWebMessageAsString();
                if (msg == null) return;

                if (msg.Contains("\"type\":\"copy\""))
                {
                    AuditLogger.LogCopyBlocked(_session.Account.Username);
                    this.BeginInvoke(() =>
                        _statusLabel.Text = $"⚠  Copy blocked  •  {DateTime.Now:HH:mm:ss}  •  Logged");
                }
                else if (msg.Contains("\"type\":\"print\""))
                {
                    AuditLogger.LogPrintBlocked(_session.Account.Username);
                    this.BeginInvoke(() =>
                        _statusLabel.Text = $"⚠  Print blocked  •  {DateTime.Now:HH:mm:ss}  •  Logged");
                }
                else if (msg.Contains("\"type\":\"save\""))
                {
                    AuditLogger.LogSaveBlocked(_session.Account.Username);
                    this.BeginInvoke(() =>
                        _statusLabel.Text = $"⚠  Save blocked  •  {DateTime.Now:HH:mm:ss}  •  Logged");
                }
            }
            catch { }
        }

        private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            var username = _session.Account.Username;
            bool clipAllowed  = PolicyEngine.IsClipboardAllowed(username);
            bool printAllowed = PolicyEngine.IsPrintAllowed(username);

            var items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var name = (items[i].Name ?? "").ToLower();

                // Always remove dangerous items
                if (name is "saveimageas" or "savelinkas" or "saveas" or "viewsource")
                { items.RemoveAt(i); continue; }

                // Remove clipboard items if blocked
                if (!clipAllowed && name is "copy" or "cut" or "paste" or
                    "copyimage" or "copyimageurl" or "copylink" or "selectall")
                { items.RemoveAt(i); continue; }

                // Remove print if blocked
                if (!printAllowed && name is "print")
                { items.RemoveAt(i); continue; }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  NAVIGATION (URL bar)
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
        //  PERMISSION DISPLAY REFRESH
        // ═══════════════════════════════════════════════════════════════════

        private void RefreshPermDisplay()
        {
            try
            {
                var username = _session.Account.Username;
                bool clip  = PolicyEngine.IsClipboardAllowed(username);
                bool print = PolicyEngine.IsPrintAllowed(username);
                bool ssl   = PolicyEngine.IsSSLOnly(username);

                this.BeginInvoke(() =>
                {
                    _lblPerms.Text =
                        $"📋 Clipboard: {(clip  ? "✓ Allowed" : "✗ Blocked")}   " +
                        $"🖨 Print: {(print ? "✓ Allowed" : "✗ Blocked")}   " +
                        $"🔒 SSL-Only: {(ssl ? "On" : "Off")}   " +
                        $"🛡 Screenshot: BLOCKED";
                });
            }
            catch { }
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
            _toolbar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Surface };

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
                Left = 126, Top = 14, Height = 26,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.FromArgb(230, 230, 230),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            _urlBar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return) { Navigate(_urlBar.Text); e.SuppressKeyPress = true; }
            };

            _btnGo = new Button
            {
                Text = "Go", Top = 14, Width = 38, Height = 26,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, TabStop = false
            };
            _btnGo.FlatAppearance.BorderSize = 0;
            _btnGo.Click += (s, e) => Navigate(_urlBar.Text);

            var userColor = isAdmin ? Purple : GreenC;
            _lblUser = new Label
            {
                Text = $"  {(isAdmin ? "⚙" : "👤")}  {_session.Account.DisplayName}  [{_session.Account.Role}]  •  {_session.Location}",
                Dock = DockStyle.Right, Width = 340, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = userColor, BackColor = Color.FromArgb(30, 30, 36),
                Padding = new Padding(6, 0, 0, 0)
            };

            _btnAdmin = new Button
            {
                Text = "⚙ Admin Console", Dock = DockStyle.Right, Width = 130,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 40, 80),
                ForeColor = Purple, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, TabStop = false, Visible = isAdmin
            };
            _btnAdmin.FlatAppearance.BorderSize = 0;
            _btnAdmin.Click += (s, e) =>
            {
                var af = new AdminForm(_session.Account.Username);
                af.ShowDialog(this);
                // After admin closes, immediately refresh the permissions display
                RefreshPermDisplay();
            };

            _btnLogout = new Button
            {
                Text = "↩ Logout", Dock = DockStyle.Right, Width = 90,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 30, 30),
                ForeColor = Color.FromArgb(248, 81, 73),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, TabStop = false
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
                int rightEdge = _toolbar.Width - _lblUser.Width -
                    (isAdmin ? _btnAdmin.Width : 0) - _btnLogout.Width - 10;
                _urlBar.Width = rightEdge - _urlBar.Left - _btnGo.Width - 8;
                _btnGo.Left   = _urlBar.Right + 4;
            };

            _toolbar.Controls.AddRange(new Control[]
                { _btnBack, _btnForward, _btnRefresh, _urlBar, _btnGo, _lblUser, _btnAdmin, _btnLogout });

            // ── Status bar ────────────────────────────────────────────────
            _statusBar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Surface };
            _statusBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(48, 54, 61)), 0, 0, _statusBar.Width, 0);

            _statusLabel = new Label
            {
                Dock = DockStyle.Left, Width = 500,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(139, 148, 158),
                Font = new Font("Segoe UI", 8f),
                Padding = new Padding(8, 0, 0, 0),
                Text = "Starting…"
            };

            _lblPerms = new Label
            {
                Dock = DockStyle.Right, Width = 700,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(80, 160, 80),
                Font = new Font("Segoe UI", 8f),
                Padding = new Padding(0, 0, 10, 0)
            };
            _statusBar.Controls.AddRange(new Control[] { _statusLabel, _lblPerms });

            // ── WebView2 + Loading ────────────────────────────────────────
            _webView = new WebView2 { Dock = DockStyle.Fill };
            _loadingLabel = new Label
            {
                Text = "Starting secure browser engine…",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 13f),
                ForeColor = Color.FromArgb(100, 180, 255), BackColor = BG,
                Visible = false
            };

            Controls.Add(_webView);
            Controls.Add(_loadingLabel);
            Controls.Add(_toolbar);
            Controls.Add(_statusBar);

            RefreshPermDisplay();
        }

        private Button MakeNavBtn(string text, int left, string tip)
        {
            var btn = new Button
            {
                Text = text, Left = left, Top = 12, Width = 34, Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 55, 62),
                ForeColor = Color.FromArgb(210, 210, 210),
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand, TabStop = false
            };
            btn.FlatAppearance.BorderSize = 0;
            new ToolTip().SetToolTip(btn, tip);
            return btn;
        }
    }
}
