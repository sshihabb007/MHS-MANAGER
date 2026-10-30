# MHS Download Manager (`mdm.exe` v1.0.0)

**MHS Download Manager** (`v1.0.0`) is a clean-room, MIT-licensed x64 Windows Download Accelerator (`mdm.exe`) and Manifest V3 Chromium Browser Extension (`extension/`) developed by **Mehedi Hasan Shihab (`sshihabb007`)**.

It compiles directly on any standard Windows 10/11 system using the built-in .NET Framework 4.8 C# compiler (`csc.exe`) with **zero external NuGet, DLL, or `ffmpeg` dependencies**.

Official Windows x64 Setup Installer (`MHS-Download-Manager-Setup-1.0.0-x64.exe`, which installs `mdm.exe`, `Uninstall.exe`, and the unpacked `extension\` folder inside your installation directory for Chrome / Brave / Edge) is built automatically via GitHub Actions and published under [**GitHub Releases**](https://github.com/sshihabb007/MHS-MANAGER/releases).

---

## Key Features

- **Dynamic In-Flight Segment Splitting (`1–32` Connections)**:
  - Splits downloads dynamically across concurrent HTTP `206 Partial Content` `Range` connections, dividing the largest active remaining chunk whenever a worker finishes early.
  - Supports pause/resume checkpoints, speed limiter, `.ef2` / `.txt` import & export, Site Grabber spider, Queues, and Scheduler.
- **Automatic Browser Download Interception (`extension/`)**:
  - Automatically intercepts direct and redirect/tokenized file downloads (`.mkv`, `.mp4`, `.zip`, `R0*`, `R1*`, `.exe`, `.iso`, etc.) via `chrome.downloads` + `Content-Disposition` inspection, cancelling Chrome's built-in download and bringing `Download File Info` to the foreground even when `mdm.exe` is minimized to the system tray.
  - Configurable `Prevent` (`Alt`) and `Force` (`Ins`) hotkeys, wildcard file types, and excluded sites/addresses.
- **Exact Selected-Video Isolation, Compact Corner Icon (`↻`) & Lossless Audio+Video Muxing**:
  - Sleek icon-first floating Shadow DOM corner widget (`[MHS Icon] [1080p · 4] | [↻] [▾] [×]`) with a built-in **Reloader (`↻`)** button to re-track the selected `<video>` on demand.
  - Direct `<video>` React Fiber / YouTube `#movie_player` inspection + ISO-BMFF `ftyp`/`moof` FNV-1a MSE chunk fingerprinting + aspect-ratio/duration guards isolate **only** the selected video on multi-video feeds (Facebook, Instagram, YouTube, X, TikTok, Reddit, and HTML5 players) with 100% exact proper video filenames.
  - Displays clean captions/titles, `VIDEO` / `AUDIO` badges, exact resolutions (`2160p`, `1440p`, `1080p`, `720p`, `480p`, `360p`), and combined `Video + Audio` file sizes.
  - Pure C# container inspector & lossless muxer (`MehediMediaMuxer`) merges separate DASH video + audio tracks (`MP4/fMP4 + M4A -> Fast-Start .mp4`, `MP4 + WebM Opus -> .mp4`, `WebM + WebM -> .webm`, `WebM + M4A -> .mkv`) without re-encoding. (DRM/EME-protected streams are strictly respected and never bypassed.)
  - Automatic **Browser Fetch Relay** fallback when session-bound CDN streams return `403 Forbidden` to direct desktop connections.
- **Full 9-Tab Configuration Dialog**:
  - `General`, `File types`, `Save to`, `Downloads`, `Connection`, `Proxy / Socks`, `Sites Logins`, `Dial Up / VPN`, and `Sounds`, plus Loopback HTTP Bridge (`127.0.0.1:45779`) and Chrome/Edge Native Messaging stdio host (`com.shihab.mehedi.idm`).

---

## Quick Start

### 1. Build `mdm.exe` from Source

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Launch the compiled desktop app:

```powershell
.\bin\mdm.exe
```

### 2. Load the Browser Extension (Chrome / Edge / Brave / Opera)

1. Open `chrome://extensions` or `edge://extensions`.
2. Enable **Developer mode**.
3. Click **Load unpacked** and select the `extension` folder of this repository.

---

## Command-Line Interface

```text
mdm.exe /d <URL> [/p <LocalPath>] [/f <LocalFileName>] [/n] [/q] [/a] [/s]
```

- `/d <URL>`: Download specified URL
- `/p <LocalPath>`: Local directory to save file
- `/f <LocalFileName>`: Local output file name
- `/n`: Silent mode (start immediately without prompts)
- `/q`: Exit `mdm.exe` after download completes
- `/a`: Add download to queue without starting immediately
- `/s`: Start main download queue immediately

---

## Author

- **Developer**: Mehedi Hasan Shihab (`sshihabb007`)
- **Portfolio**: https://sshihabb007.github.io/
- **Repository**: https://github.com/sshihabb007/MHS-MANAGER
