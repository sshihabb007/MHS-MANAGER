# MHS Manager

**MHS Manager** is a 100% clean-room, MIT-licensed open-source multi-connection download accelerator, HLS media stream merger, batch grabber, scheduler, and browser download manager for Windows.

It compiles directly on any standard Windows 10/11 machine with **zero external NuGet dependencies** into a standalone, unprivileged (`asInvoker`), Per-Monitor DPI-aware executable (`bin\MHSManager.exe`).

Prebuilt `MHSManager.exe` and `MHS-Manager-Portable-x64.zip` bundles are built automatically via GitHub Actions and published under [**GitHub Releases**](https://github.com/sshihabb007/MHS-MANAGER/releases).

---

## Key Features

- **Dynamic Multi-Connection Segment Engine (`1–32` Threads + Work-Stealing)**:
  - Probes HTTP/HTTPS servers (`Range: bytes=0-0` + RFC 5987/6266 `Content-Disposition`), pre-allocates `.mhspart` on disk, and downloads byte ranges concurrently.
  - **In-Flight Dynamic Segment Splitting**: Whenever a connection finishes its assigned segment early, it inspects active workers and dynamically splits the largest remaining chunk (`>= 256 KB`) in half over a fresh `206 Partial Content` connection so tail bottlenecks never stall completion.
- **Safe Checkpoint Pause, Resume & Expired Link Refresh**:
  - Atomic `.mhsmeta` byte-offset checkpoints + `%LOCALAPPDATA%\MHSManager\state.json` persistence allow pausing, closing, or recovering interrupted downloads without losing downloaded bytes.
  - **Remote File Modification Verification**: Re-probes `ETag`, `Last-Modified`, `Content-Length`, and `206` range support on resume so server-side file updates restart cleanly instead of splicing corrupted bytes.
  - **Refresh Download Address**: Swap expired signed/tokenized URLs (`Right-Click → Refresh Download Address...` or `POST /api/v1/refresh-url`) and resume from partial `.mhspart` checkpoints.
- **HLS (`.m3u8`) Stream Downloader & Merger**:
  - Parses HLS Master playlists (`#EXT-X-STREAM-INF`), Media playlists (`#EXTINF`, `#EXT-X-MAP`, `#EXT-X-BYTERANGE`), and standard `#EXT-X-KEY:METHOD=AES-128`, downloading segments concurrently with checkpoint resume and merging into `.ts` or `.mp4` (fMP4). (DRM-protected streams are explicitly not supported.)
- **Complete Desktop Workflow & Floating Drop Box**:
  - Full menu bar (`Tasks`, `File`, `Downloads`, `View`, `Help`), sortable columns, drag-out completed files, **Download File Info** dialog (auto-probes size & resume capability), **Download Complete** dialog (with Drag & Drop box and Open With), and always-on-top **Floating Drop Target** (`🎯 Drop Box`).
  - Import & export both plain URL lists (`.txt`) and **IDM `.ef2`** export files (preserving `Referer`, `Cookie`, and `User-Agent` headers).
- **Queues, Scheduler, Auto-Retry & Post-Queue Power Actions**:
  - Automatic file categorization (*Compressed*, *Documents*, *Music*, *Programs*, *Video*, *General*) into organized subfolders with non-destructive filename deduplication.
  - Configurable queues (*Main Queue*, *Synchronization Queue*) with active processing, automatic retry backoff, day-of-week start/stop schedules, and post-queue actions (*Exit*, *Sleep*, *Hibernate*, *Shut Down*).
  - **Site Logins Vault** (wildcard host HTTP Basic Auth) and configurable **HTTP Proxy** (*System*, *Direct*, or *Manual Proxy*).
- **Authenticated Browser Extension + Native Messaging Host (`com.mhsmanager.host`)**:
  - Authenticated loopback RPC (`127.0.0.1:16888`, protected by CSPRNG `X-MHS-Token` and strict `chrome-extension://` / `moz-extension://` origin validation) + stdio **Native Messaging Host** (`com.mhsmanager.host`) capable of 1-click token pairing and launching `MHSManager.exe` on demand.
  - **Safe Download Takeover**: Pauses browser downloads first and only cancels/erases after confirmed desktop handoff (resumes in browser if offline); supports `Alt`-click bypass and `Insert` / `Ctrl+Shift`-click force capture.
  - Per-tab network media stream sniffer (`.m3u8`, `.mp4`, `.webm`, `.mkv`, `.mp3`, `.m4a`) + auto-hiding **"⬇ Download this video"** stream selector overlay and right-click link/selection/page context menus.

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
- **IDM-Compatible Command-Line Switches**:
  ```powershell
  # /d URL  /p SavePath  /f FileName  /n Silent  /a HoldInQueue  /q QuitWhenDone  /h HangUp  /s StartQueue
  .\bin\MHSManager.exe /d "https://example.com/archive.zip" /p "D:\Downloads" /f "archive.zip" /n
  .\bin\MHSManager.exe /s
  ```
- **Headless Multi-Connection CLI Download**:
  ```powershell
  .\bin\MHSManager.exe --cli-download "https://example.com/large.iso" --out ".\large.iso" --connections 16 --sha256 "<expected_hex>"
  ```
- **Register Native Messaging Host via CLI**:
  ```powershell
  .\bin\MHSManager.exe --register-native-host "<32-char-chromium-extension-id>"
  ```

---

## Loading & Pairing the Browser Extension (Chrome / Edge / Brave)

1. Open `chrome://extensions` or `edge://extensions`, enable **Developer mode**, click **Load unpacked**, and select the `browser-extension` directory.
2. Copy the displayed 32-character **Extension ID** from `chrome://extensions` (or from the extension popup under **Bridge Pairing & Native Host**).
3. In `MHSManager.exe`, open **Options → Browser Integration**, paste the Extension ID, and click **Register Native Host** (or copy the **Pairing Token** into the extension popup).
