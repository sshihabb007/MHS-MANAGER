# MHS Manager

**MHS Manager** is a 100% clean-room, MIT-licensed open-source multi-connection download accelerator, HLS media stream merger, batch grabber, and browser download manager for Windows.

It compiles directly on any standard Windows 10/11 machine with **zero external NuGet dependencies** into a standalone, unprivileged (`asInvoker`), Per-Monitor DPI-aware executable (`bin\MHSManager.exe`).

---

## Key Features

- **Dynamic Multi-Connection Segment Engine (`1–32` Threads + Work-Stealing)**:
  - Probes HTTP/HTTPS servers (`Range: bytes=0-0` + RFC 5987/6266 `Content-Disposition`), pre-allocates `.mhspart` on disk, and downloads byte ranges concurrently.
  - **In-Flight Dynamic Segment Splitting**: Whenever a connection finishes its assigned segment early, it inspects active workers and dynamically splits the largest remaining chunk (`>= 256 KB`) in half over a fresh `206 Partial Content` connection so tail bottlenecks never stall completion.
- **Crash-Safe Pause & Resume**:
  - Atomic `.mhsmeta` byte-offset checkpoints + `%LOCALAPPDATA%\MHSManager\state.json` persistence allow pausing, closing, or recovering interrupted downloads without losing downloaded bytes.
- **HLS (`.m3u8`) Stream Downloader & `.ts` Merger**:
  - Parses HLS Master playlists (`#EXT-X-STREAM-INF`, selecting highest bandwidth) and Media playlists (`#EXTINF`, `#EXT-X-MAP`, standard `#EXT-X-KEY:METHOD=AES-128`), downloads segments concurrently with checkpoint resume, and merges them into a single playable `.ts` stream. (DRM-protected streams are explicitly not supported.)
- **Live Floating Per-Download Progress Window & Dark Slate UI**:
  - Custom GDI+ **Dynamic Segment Visualizer Bar** showing exact downloaded spans and active connection heads in real time.
  - Rolling 60-sample **Throughput Area Sparkline**, per-connection byte-range table, per-download speed limiter, and post-completion actions.
- **Queues, Daily Scheduler & Token-Bucket Speed Limiter**:
  - Automatic file categorization (*Compressed*, *Documents*, *Music*, *Programs*, *Video*, *General*) into organized subfolders.
  - Configurable queues (*Main Queue*, *Night Queue*) with max concurrent downloads and scheduled daily start/stop times.
  - Global and per-download token-bucket bandwidth caps.
- **Batch Wildcard Generator & Site Link Grabber**:
  - Expand numeric (`[01-50]`, `[1-100:2]`) and alphabetic (`[a-z]`) URL patterns, or scan any webpage for downloadable archives, documents, audio, and video links.
- **Clipboard URL Monitor, MD5/SHA-256 Checksum Verifier & System Tray**:
  - Native Win32 `AddClipboardFormatListener` integration, streaming dual MD5 + SHA-256 verifier, single-instance forwarding, and tray background operation.
- **Manifest V3 Browser Extension + Localhost RPC Bridge (`127.0.0.1:16888`)**:
  - Unprivileged loopback bridge + ready-to-load Chrome/Edge/Brave extension (`browser-extension\`) with automatic download takeover, cookie/referer forwarding, right-click context menus, and floating HTML5 `<video>` download pill.

---

## Building from Source

No Visual Studio or external SDK install is required — `build.ps1` uses the built-in Windows .NET Framework 4.8 compiler (`csc.exe`):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output binary: `.\bin\MHSManager.exe`

---

## Command-Line Interface

- **Launch Desktop GUI**:
  ```powershell
  .\bin\MHSManager.exe
  ```
- **Forward URL to Running GUI (or Open Add Dialog)**:
  ```powershell
  .\bin\MHSManager.exe --add-url "https://example.com/archive.zip" --referer "https://example.com/"
  ```
- **Headless Multi-Connection CLI Download**:
  ```powershell
  .\bin\MHSManager.exe --cli-download "https://example.com/large.iso" --out ".\large.iso" --connections 16 --sha256 "<expected_hex>"
  ```

---

## Loading the Browser Extension (Chrome / Edge / Brave)

1. Launch `bin\MHSManager.exe` (starts the local bridge on `http://127.0.0.1:16888`).
2. Open `chrome://extensions` or `edge://extensions` and enable **Developer mode**.
3. Click **Load unpacked** and select the `browser-extension` directory inside this project.

