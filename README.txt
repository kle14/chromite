╔══════════════════════════════════════════════════════════════════════╗
║              SECURE BROWSER  —  Setup & Usage Guide                 ║
╚══════════════════════════════════════════════════════════════════════╝

WHAT THIS APP DOES
──────────────────
  ✓ Full working browser (Chromium engine via WebView2)
  ✓ Blocks ALL screenshots  (Print Screen, Win+Shift+S, Snipping Tool,
    OBS, any capture API) — works at the Windows DWM driver level
  ✓ Blocks copy/paste out of the browser to other apps
  ✓ Clears OS clipboard when you switch away from the app
  ✓ Disables DevTools (F12)
  ✓ Disables right-click Copy / Cut / Paste
  ✓ Blocks Ctrl+C, Ctrl+X, Ctrl+V keyboard shortcuts
  ✓ Blocks drag-and-drop data exfiltration
  ✓ No extensions allowed
  ✓ Opens new tabs in the same window


STEP 1 — INSTALL .NET 8 SDK  (one-time)
─────────────────────────────────────────
  1. Go to: https://dotnet.microsoft.com/download/dotnet/8.0
  2. Download ".NET 8.0 SDK" for Windows x64
  3. Run the installer — takes about 2 minutes
  4. Open a new Command Prompt and type:  dotnet --version
     You should see something like: 8.0.xxx


STEP 2 — INSTALL WEBVIEW2 RUNTIME  (one-time, usually already installed)
──────────────────────────────────────────────────────────────────────────
  WebView2 is the embedded Chromium browser engine.
  It is ALREADY installed on most Windows 10/11 machines
  (it comes with Microsoft Edge).

  If the app shows a "WebView2 Required" error:
  1. Go to: https://developer.microsoft.com/microsoft-edge/webview2/
  2. Download "Evergreen Bootstrapper"
  3. Run it — it installs in seconds


STEP 3 — BUILD AND RUN
────────────────────────
  Option A — First time:
    Double-click  build.bat
    (This restores packages, builds, and launches the app)

  Option B — After first build:
    Double-click  run.bat
    (Faster — skips rebuild)

  Option C — From Command Prompt:
    cd C:\path\to\SecureBrowser
    dotnet run --project SecureBrowser.csproj -c Release


HOW THE PROTECTION WORKS
─────────────────────────
  SCREENSHOT PROTECTION  (OS kernel level)
  ─────────────────────────────────────────
  The app calls the Windows API function:
    SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE)

  This tells the Windows Display Window Manager (DWM) — which runs
  at the OS kernel level — to EXCLUDE this window from any capture.

  Result:
  • Print Screen → the app area appears BLACK or transparent
  • Win+Shift+S (Snipping Tool overlay) → app area is blank
  • Snipping Tool app → cannot capture the window
  • OBS Studio → app window is invisible
  • Any screen recording software → app is not captured
  • Remote Desktop → app area appears black to the viewer

  This is the SAME technology used by Netflix and Disney+ to prevent
  screen recording of their video players on Windows.

  Works on: Windows 10 version 2004 (build 19041) and later.
  Fallback:  On older Win10, shows as solid black in captures.

  CLIPBOARD PROTECTION  (two layers)
  ────────────────────────────────────
  Layer 1 — JavaScript injection:
  The app injects a script into every webpage that intercepts the
  'copy', 'cut', and 'paste' browser events BEFORE they reach the
  Chromium clipboard layer. Ctrl+C is swallowed at the JS level.
  The navigator.clipboard API is also overridden to a no-op.

  Layer 2 — OS-level clipboard wipe:
  Whenever you click away from the Secure Browser (the app loses
  focus), it immediately calls EmptyClipboard() to wipe anything
  that may have leaked to the OS clipboard.

  Result: You CANNOT paste anything from the Secure Browser into
  another application such as Notepad, Word, a chat app, etc.


IF THE WEBVIEW2 VERSION NUMBER IS WRONG
─────────────────────────────────────────
  Edit  SecureBrowser.csproj  and find this line:
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2849.39" />

  Go to https://www.nuget.org/packages/Microsoft.Web.WebView2 and
  copy the latest stable version number, then paste it in.
  Save the file and run build.bat again.


REQUIREMENTS
────────────
  • Windows 10 version 2004 or later (Windows 11 recommended)
  • .NET 8 SDK (for building)
  • WebView2 Runtime (usually pre-installed)
  • Internet connection on first build (to download WebView2 NuGet package)


FILES IN THIS FOLDER
─────────────────────
  SecureBrowser.csproj  — project configuration
  Program.cs            — app entry point
  MainForm.cs           — all the browser and security logic
  app.manifest          — DPI awareness settings
  build.bat             — builds and runs the app (use first time)
  run.bat               — just runs after first build
  README.txt            — this file
