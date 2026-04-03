# Secure Browser v2.0

**Enterprise Data Protection Browser Demo** — a C# WinForms application built on Microsoft WebView2 that enforces per-user security policies (URL whitelisting, clipboard control, print control, screenshot protection, save-page blocking) entirely from a live PostgreSQL database. Admin policy changes take effect **immediately** — no logout or restart required.

Built as a prototype to demonstrate what an enterprise-controlled browser layer looks like in practice: not just a locked-down browser, but a fully audited, policy-driven access control surface with a real-time admin console.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| Windows | 10 v2004+ / 11 recommended | WinForms + WebView2 requires Windows |
| .NET SDK | 8.0 | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop/ |
| WebView2 Runtime | Any | Pre-installed with Edge on most systems |

---

## Quick Start

```
1.  Open PowerShell in this folder
2.  docker-compose up -d          ← starts PostgreSQL on port 5433
3.  Wait ~5 seconds for DB init
4.  dotnet run -c Release         ← builds and launches
```

Or double-click **`setup.bat`** — it runs all three steps automatically.

For subsequent launches, use **`run.bat`** or `dotnet run -c Release`.

---

## Demo Credentials

| User  | Password  | Role | Access Level |
|-------|-----------|------|---|
| admin | Admin123! | Admin | Full access, clipboard, print, all URLs, all locations, Admin Console |
| alice | Alice123! | User | No clipboard, no print, SSL-only, Office location only, google.com + github.com |
| bob   | Bob123!   | User | Clipboard allowed, no print, SSL-only, Office + Remote, google.com + microsoft.com + stackoverflow.com |

---

## Demo Flow (10 minutes)

**Step 1** — Login as `alice / Alice123!` from Office
- Status bar shows live permission state: `📋 Clipboard: ✗ Blocked   🖨 Print: ✗ Blocked   🔒 SSL-Only: On`
- Navigate to `reddit.com` → blocked by URL whitelist, logged to audit
- Try `http://google.com` → blocked by SSL-only policy, logged
- Press Ctrl+C → blocked at JS layer, logged; try to copy via context menu → copy item is removed
- Press Ctrl+P → blocked at JS layer, logged; try to print via context menu → print item is removed
- Press Print Screen → window content appears solid black in capture
- Press Ctrl+S → blocked at OS hook + JS layer, logged

**Step 2** — Logout → Login as `alice` from **Remote** → denied (location not permitted), logged as `LOCATION_DENIED`

**Step 3** — Login as `admin / Admin123!`
- Click **⚙ Admin Console**
- **Dashboard tab**: stat cards show blocked navs and warnings today; recent event grid updates in real time
- **Users tab**: select alice → enable clipboard → check Remote location → Save
- **URL Whitelist tab**: add `https://reddit.com` for alice
- **Audit Log tab**: filter by user or event type, export entire log as CSV

**Step 4** — Close Admin Console → alice's status bar **immediately** updates (3-second polling timer hits the DB)
- Logout → Login as alice from Remote (now permitted) → clipboard works, reddit loads

---

## Security Architecture

The browser enforces controls through four independent layers. Each layer is a separate enforcement point; a bypass of one does not automatically bypass the others.

### Layer 1 — OS-Level (Windows Native API)

**Screenshot / screen recording protection**
`SetWindowDisplayAffinity(hWnd, WDA_MONITOR)` is applied at window creation and re-applied every time the window is activated. This causes the window's content to render as solid black in any screen capture tool, recording software, or Remote Desktop session. `WDA_MONITOR (0x01)` was deliberately chosen over `WDA_EXCLUDEFROMCAPTURE (0x11)` — the latter makes the window vanish entirely from captures, which looks like a crash rather than a security control.

**Clipboard wipe on focus loss**
`AddClipboardFormatListener` registers a `WM_CLIPBOARDUPDATE` message listener. Any time clipboard contents change while the window is focused, a 50ms-delayed wipe is triggered if the user's clipboard permission is currently blocked in the DB. Additionally, the `Deactivate` event (window loses focus) also triggers a live DB check and wipe. Both checks hit the DB directly so a mid-session admin change takes effect immediately.

**Low-level keyboard hook (Ctrl+S / Save-page blocking)**
`SetWindowsHookEx(WH_KEYBOARD_LL)` installs a process-wide keyboard hook that fires before any application sees a keystroke. When Ctrl+S or Ctrl+Shift+S is detected while the Secure Browser window (or any child, including the WebView2 renderer) is the foreground window, the keypress is swallowed, a `SAVE_BLOCKED` audit event is written, and the status bar updates. This fires even when WebView2 has keyboard focus, where normal WinForms key handling cannot reach.

> **Honest caveat (from the code comments):** `WH_KEYBOARD_LL` runs in user mode. A process running as the same Windows user can bypass it via `SendInput()` (synthetic keystrokes skip all LL hooks by Windows design), by calling `UnhookWindowsHookEx()` with the hook handle, or by installing a competing hook earlier in the chain. True OS-level interception requires a kernel-mode filter driver (WDK). This hook provides best-effort protection against accidental or low-sophistication saves, not a security boundary against a determined same-privilege attacker.

**Session data wipe on logout**
Before the window closes, `CleanupAndCloseAsync()` calls `CoreWebView2.Profile.ClearBrowsingDataAsync` to wipe: all DOM storage, cookies, disk cache, download history, browsing history, and cache storage. A 4-second timeout ensures the window closes even if the API hangs. This prevents session data from persisting to the next user.

### Layer 2 — WebView2 Engine Settings

Applied once at initialization, these disable browser features that could expose data or bypass controls:

| Setting | Value | Effect |
|---|---|---|
| `AreDevToolsEnabled` | false | No F12 / DevTools |
| `AreDefaultContextMenusEnabled` | false | No default context menu (replaced with filtered custom menu) |
| `AreBrowserAcceleratorKeysEnabled` | false | Disables all built-in Ctrl+P, Ctrl+F, Ctrl+U, etc. |
| `IsStatusBarEnabled` | false | No URL preview on hover |
| `IsZoomControlEnabled` | false | No Ctrl+Scroll zoom |
| `IsPasswordAutosaveEnabled` | false | No credential storage |
| `IsGeneralAutofillEnabled` | false | No form autofill |
| `IsSwipeNavigationEnabled` | false | No touch-swipe back/forward |

**New window suppression**: all `target="_blank"` links and popup requests (`NewWindowRequested`) are redirected into the same tab rather than opening a new browser window that would bypass URL whitelisting.

### Layer 3 — JavaScript Injection (Per-Page Policy Enforcement)

After every navigation completes, a JS block is injected into the page using `ExecuteScriptAsync`. It reads the user's current permissions from the DB at injection time and injects a matching policy. This layer acts as a renderer-process enforcement point that catches what the OS hook cannot (e.g., a page trying to call `document.execCommand('copy')` programmatically).

**Always injected (regardless of permissions):**
- `keydown` listener blocks `PrintScreen` at the renderer level (belt-and-suspenders alongside `WDA_MONITOR`)
- `keydown` listener blocks Ctrl+S / Ctrl+Shift+S and posts a `save` message back to the host

**When clipboard is blocked:**
- `copy`, `cut`, `paste` DOM events are cancelled with `stopImmediatePropagation`
- `navigator.clipboard` API is overridden with a no-op stub (writes resolve instantly, reads return empty string)
- Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+A keyboard shortcuts are cancelled
- `document.execCommand('copy')` and `document.execCommand('cut')` are overridden to return `false`
- `dragstart` events are cancelled (drag-to-copy prevention)

**When print is blocked:**
- Ctrl+P is cancelled and a `print` message is posted to the host
- `window.print()` is overridden to post the `print` message instead

**When print is allowed:**
- Since `AreBrowserAcceleratorKeysEnabled` is globally false, Ctrl+P is dead by default. The injection explicitly re-wires it by calling `window.print()` on the `keydown` event.

Web messages (`copy`, `print`, `save`) from the JS layer are received by `OnWebMessageReceived`, which writes an audit event and updates the status bar.

### Layer 4 — Custom Context Menu Filtering

`ContextMenuRequested` fires before any context menu is shown. Items are filtered live against the DB:

- **Always removed**: `saveimageas`, `savelinkas`, `saveas`, `viewsource`
- **Removed when clipboard is blocked**: `copy`, `cut`, `paste`, `copyimage`, `copyimageurl`, `copylink`, `selectall`
- **Removed when print is blocked**: `print`

---

## Policy Engine — Live DB Checks

Every security-sensitive event queries PostgreSQL directly. No permission state is cached after login.

| Event | Method called | DB query |
|---|---|---|
| Navigation starts | `IsUrlAllowed()` | url_whitelist + allowed_locations |
| SSL-only check | `IsSSLOnly()` | permissions.ssl_only |
| Clipboard wipe (blur / WM_CLIPBOARDUPDATE) | `IsClipboardAllowed()` | permissions.allow_clipboard |
| Clipboard event (JS → host) | `IsClipboardAllowed()` | permissions.allow_clipboard |
| Context menu opens | `IsClipboardAllowed()` + `IsPrintAllowed()` | permissions |
| Print attempt (JS → host) | `IsPrintAllowed()` | permissions.allow_print |
| JS injection after page load | `GetUserPermissions()` | permissions + url_whitelist |
| Status bar refresh (3s timer) | `IsClipboardAllowed()` + `IsPrintAllowed()` + `IsSSLOnly()` | permissions |

**URL whitelist matching logic:**
- `about:blank` is always allowed (used internally for block pages and session wipe)
- `data:`, `about:` (other), and `edge:` URIs are always blocked
- Wildcard `*` in the whitelist allows all URLs
- Matching is host-based: `https://www.google.com` whitelists any URL whose host matches `google.com` or any subdomain

---

## Admin Console

Accessible only to users with `Role = 'Admin'`. Opens as a modal dialog from the main browser window; closing it immediately triggers a permission display refresh.

### Dashboard Tab
- Stat cards: blocked navigations today, security warnings today, total audit events today
- Recent events grid: last N events with timestamp, user, event type, details, severity
- Refresh button for manual reload

### Users Tab
- List of all users in the system
- Per-user permission checkboxes: Allow Clipboard, Allow Print, SSL-Only
- Per-user location checkboxes: Office, Remote, Branch
- Save button writes to PostgreSQL immediately; the change is visible to the running browser session within 3 seconds (next status bar poll)

### URL Whitelist Tab
- Per-user dropdown to select target user
- List of currently whitelisted URLs for that user
- Add / Remove URL controls
- Changes take effect on the user's next navigation attempt (live DB check)

### Audit Log Tab
- Filter by user and/or event type
- Event types: LOGIN, LOGOUT, LOGIN_FAILED, NAV_BLOCKED, COPY_BLOCKED, PRINT_BLOCKED, SCREENSHOT, SAVE_BLOCKED, CONFIG_CHANGE, LOCATION_DENIED
- Results show: timestamp, username, event type, details, severity (colour-coded), location
- **Export CSV**: exports the full unfiltered audit log to a timestamped `.csv` file

---

## Audit Events Reference

| Event Type | Severity | Trigger |
|---|---|---|
| LOGIN | Info | Successful authentication |
| LOGOUT | Info | User clicks Logout |
| LOGIN_FAILED | Critical | Wrong password |
| LOCATION_DENIED | Critical | Correct password but location not permitted |
| NAV_BLOCKED | Warning | URL not in whitelist or SSL-only violation |
| COPY_BLOCKED | Warning | Clipboard copy attempt when blocked |
| PRINT_BLOCKED | Warning | Print attempt when blocked |
| SAVE_BLOCKED | Warning | Ctrl+S / Ctrl+Shift+S intercepted |
| SCREENSHOT | Warning | PrintScreen detected (JS layer) |
| CONFIG_CHANGE | Critical | Admin changes any user's permissions or whitelist |

---

## File Structure

```
SecureBrowser/
├── docker-compose.yml      PostgreSQL container (port 5433)
├── init.sql                Schema + seed data (auto-runs on first up)
├── SecureBrowser.csproj    Project config (Npgsql + WebView2 packages)
├── app.manifest            DPI / Windows compatibility settings
├── Program.cs              Entry point: DB check → Login/Logout loop
├── setup.bat               First-time: Docker + restore + build + run
├── run.bat                 Subsequent launches
├── README.md               This file
│
├── Models/
│   └── Models.cs           UserAccount, UserPermissions, AuditEvent, UserSession
│
├── Data/
│   ├── Db.cs               PostgreSQL connection helper (env var override)
│   ├── PolicyEngine.cs     Auth, permissions, URL whitelist — all DB-backed, all live
│   └── AuditLogger.cs      Audit event write + read + stats — all DB-backed
│
└── Forms/
    ├── LoginForm.cs         Login UI: username, password, location selector
    ├── MainForm.cs          Browser window: WebView2 + all four enforcement layers
    └── AdminForm.cs         Admin console: dashboard, users, whitelist, audit + CSV export
```

---

## Database Schema

```sql
users              id, username, password_hash, display_name, role, department, is_active, created_at
permissions        username (FK), allow_clipboard, allow_print, ssl_only, updated_at
url_whitelist      username (FK), url                          -- many rows per user
allowed_locations  username (FK), location                     -- many rows per user
audit_log          id, timestamp, username, event_type, details, severity, location
```

Indexes on `audit_log(timestamp DESC)`, `audit_log(username)`, `audit_log(event_type)` for fast admin console queries.

Passwords are stored as SHA-256 lowercase hex. The DB seed uses PostgreSQL's native `sha256()` function to hash them at init time.

---

## Database Connection

Default (matches the Docker container):
```
Host=localhost;Port=5433;Database=securebrowser;Username=sbadmin;Password=SBDemo2024!
```

Override with an environment variable before running:
```
set DB_CONN=Host=myserver;Port=5432;Database=securebrowser;Username=myuser;Password=mypass
dotnet run -c Release
```

---

## What This Maps To (Enterprise)

| Demo Component | Enterprise Equivalent |
|---|---|
| PostgreSQL + Docker | RDS / Aurora / managed DB service |
| LoginForm + location dropdown | PingFederate OIDC / SAML SSO |
| PolicyEngine (live Npgsql queries) | Policy service REST API / OPA |
| AuditLogger (Npgsql) | Elasticsearch / Splunk audit cluster |
| AdminForm (WinForms) | React-based web admin portal |
| `SetWindowDisplayAffinity` | Same — OS kernel-level, no equivalent workaround |
| URL whitelist in NavigationStarting | DNS/proxy filtering layer (e.g. Zscaler, Netskope) |
| Per-user live DB permissions | Per-user per-app policy engine |
| JS injection for clipboard/print | Same pattern — renderer-process policy enforcement |
| WH_KEYBOARD_LL hook | Kernel-mode filter driver for true interception |
| Session data wipe on logout | Same — OIDC token revocation + browser profile isolation |

---

## Troubleshooting

**"Cannot connect to database"**
→ Run `docker-compose up -d` and wait 5 seconds for PostgreSQL to finish initializing.

**"WebView2 Runtime required"**
→ Download from https://developer.microsoft.com/microsoft-edge/webview2/

**Port 5433 already in use**
→ Edit `docker-compose.yml`, change `5433:5432` to a free port, and update the connection string in `Db.cs` to match.

**Admin changes not appearing immediately**
→ The status bar polls the DB every 3 seconds. Changes also take effect the moment the user triggers a security event (navigation, clipboard, etc.). There is no manual refresh needed for policy enforcement — only the displayed status badge has the 3-second lag.

**Reset all data to factory defaults**
→ `docker-compose down -v` then `docker-compose up -d` — this destroys and recreates the volume, re-running `init.sql` from scratch.