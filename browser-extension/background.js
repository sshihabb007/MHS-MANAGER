const NATIVE_HOST_NAME = "com.mhsmanager.host";
const DEFAULT_PORT = 16888;
const DEFAULT_EXTS = [
  "zip", "rar", "7z", "tar", "gz", "xz", "exe", "msi", "msix", "iso", "apk", "dmg",
  "mp4", "mkv", "webm", "avi", "mov", "flv", "m3u8", "ts", "mp3", "flac", "wav",
  "m4a", "ogg", "pdf", "epub", "docx", "xlsx", "pptx"
];

const DEFAULT_PREFS = {
  autoIntercept: true,
  bridgePort: DEFAULT_PORT,
  bridgeToken: "",
  monitoredExtensions: DEFAULT_EXTS.join("|"),
  excludedHosts: ""
};

// Per-tab detected media streams: Map<tabId, Map<url, StreamItem>>
const tabStreams = new Map();

// Click modifier windows signaled by content.js (Alt = bypass takeover, Insert/Ctrl+Shift = force capture)
let altBypassUntil = 0;
let forceCaptureUntil = 0;

function setupContextMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: "mhs-download-link",
      title: "Download with MHS Manager",
      contexts: ["link", "video", "audio"]
    });
    chrome.contextMenus.create({
      id: "mhs-download-selection",
      title: "Download selected links with MHS Manager",
      contexts: ["selection"]
    });
    chrome.contextMenus.create({
      id: "mhs-download-all",
      title: "Download all links with MHS Manager",
      contexts: ["page"]
    });
  });
}

chrome.runtime.onInstalled.addListener(async () => {
  const prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  await chrome.storage.local.set(prefs);
  setupContextMenus();
  await syncViaNativeHost();
});

chrome.runtime.onStartup.addListener(async () => {
  setupContextMenus();
  await syncViaNativeHost();
});

function sendNativeMessagePromise(message) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(NATIVE_HOST_NAME, message, (response) => {
        if (chrome.runtime.lastError || !response) {
          resolve(null);
          return;
        }
        resolve(response);
      });
    } catch {
      resolve(null);
    }
  });
}

async function syncViaNativeHost() {
  const resp = await sendNativeMessagePromise({ type: "get_config" });
  if (!resp || !resp.ok) {
    return { ok: false, config: null };
  }
  const updates = {};
  if (typeof resp.port === "number" && resp.port > 0) {
    updates.bridgePort = resp.port;
  }
  if (typeof resp.token === "string" && resp.token.length > 0) {
    updates.bridgeToken = resp.token;
  }
  if (typeof resp.monitoredExtensions === "string" && resp.monitoredExtensions.length > 0) {
    updates.monitoredExtensions = resp.monitoredExtensions;
  }
  if (typeof resp.excludedHosts === "string") {
    updates.excludedHosts = resp.excludedHosts;
  }
  if (Object.keys(updates).length > 0) {
    await chrome.storage.local.set(updates);
  }
  return { ok: true, config: resp };
}

function getBridgeBaseUrl(port) {
  const p = Number(port) > 0 ? Number(port) : DEFAULT_PORT;
  return `http://127.0.0.1:${p}`;
}

async function pingBridge() {
  let prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 1400);
  try {
    const headers = { "X-MHS-Client": "webextension-mv3" };
    if (prefs.bridgeToken) {
      headers["X-MHS-Token"] = prefs.bridgeToken;
    }
    const resp = await fetch(`${getBridgeBaseUrl(prefs.bridgePort)}/api/v1/ping`, {
      method: "GET",
      headers,
      signal: controller.signal
    });
    clearTimeout(timer);
    if (!resp.ok) return null;
    const data = await resp.json();

    const updates = {};
    if (typeof data.monitoredExtensions === "string" && data.monitoredExtensions) {
      updates.monitoredExtensions = data.monitoredExtensions;
    }
    if (typeof data.excludedHosts === "string") {
      updates.excludedHosts = data.excludedHosts;
    }
    if (Object.keys(updates).length > 0) {
      await chrome.storage.local.set(updates);
    }

    // If desktop app is running, requires token, and our token isn't paired yet, try Native Host auto-sync
    if (data.requiresToken && !data.tokenValid) {
      const synced = await syncViaNativeHost();
      if (synced.ok && synced.config && synced.config.token) {
        data.tokenValid = true;
      }
    }
    return data;
  } catch {
    clearTimeout(timer);
    return null;
  }
}

async function fetchStatusFromBridge() {
  let prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  if (!prefs.bridgeToken) {
    await syncViaNativeHost();
    prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 1400);
  try {
    const headers = { "X-MHS-Client": "webextension-mv3" };
    if (prefs.bridgeToken) {
      headers["X-MHS-Token"] = prefs.bridgeToken;
    }
    const resp = await fetch(`${getBridgeBaseUrl(prefs.bridgePort)}/api/v1/status`, {
      method: "GET",
      headers,
      signal: controller.signal
    });
    clearTimeout(timer);
    if (!resp.ok) return null;
    const data = await resp.json();
    if (!data.tokenValid) {
      const synced = await syncViaNativeHost();
      if (synced.ok && synced.config && synced.config.token && synced.config.token !== prefs.bridgeToken) {
        return await fetchStatusFromBridgeOnce(synced.config.port || prefs.bridgePort, synced.config.token);
      }
    }
    return data;
  } catch {
    clearTimeout(timer);
    return null;
  }
}

async function fetchStatusFromBridgeOnce(port, token) {
  try {
    const resp = await fetch(`${getBridgeBaseUrl(port)}/api/v1/status`, {
      method: "GET",
      headers: {
        "X-MHS-Client": "webextension-mv3",
        "X-MHS-Token": token
      }
    });
    if (!resp.ok) return null;
    return await resp.json();
  } catch {
    return null;
  }
}

async function getCookieHeaderForUrl(targetUrl) {
  try {
    const cookies = await chrome.cookies.getAll({ url: targetUrl });
    if (!cookies || cookies.length === 0) return "";
    return cookies.map((c) => `${c.name}=${c.value}`).join("; ");
  } catch {
    return "";
  }
}

async function deliverToMhs(endpoint, payload, nativeType) {
  let prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  if (!prefs.bridgeToken) {
    await syncViaNativeHost();
    prefs = await chrome.storage.local.get(DEFAULT_PREFS);
  }

  // 1. Try authenticated localhost HTTP RPC bridge first when token is known
  if (prefs.bridgeToken) {
    const httpResult = await postBridgeHttp(prefs.bridgePort, prefs.bridgeToken, endpoint, payload);
    if (httpResult.ok) {
      return { ok: true, data: httpResult.data };
    }
    if (httpResult.status === 401) {
      // Token may have been regenerated in desktop Options -> re-sync via Native Host and retry once
      const synced = await syncViaNativeHost();
      if (synced.ok && synced.config && synced.config.token) {
        const retry = await postBridgeHttp(
          synced.config.port || prefs.bridgePort,
          synced.config.token,
          endpoint,
          payload
        );
        if (retry.ok) {
          return { ok: true, data: retry.data };
        }
      }
    }
  }

  // 2. Fallback to stdio Native Messaging Host (can launch MHSManager.exe on demand like IDMMsgHost.exe)
  if (nativeType) {
    const nativeResp = await sendNativeMessagePromise(Object.assign({ type: nativeType }, payload));
    if (nativeResp && nativeResp.ok) {
      syncViaNativeHost().catch(() => {});
      return { ok: true, data: nativeResp };
    }
  }

  return { ok: false, data: null };
}

async function postBridgeHttp(port, token, endpoint, payload) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 3500);
  try {
    const resp = await fetch(`${getBridgeBaseUrl(port)}${endpoint}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-MHS-Client": "webextension-mv3",
        "X-MHS-Token": token
      },
      body: JSON.stringify(payload),
      signal: controller.signal
    });
    clearTimeout(timer);
    let data = null;
    try {
      data = await resp.json();
    } catch {}
    return { ok: resp.ok && (!data || data.ok !== false), status: resp.status, data };
  } catch {
    clearTimeout(timer);
    return { ok: false, status: 0, data: null };
  }
}

function extractExtension(urlStr, suggestedFilename) {
  if (suggestedFilename) {
    const base = suggestedFilename.split(/[\\/]/).pop() || "";
    const dot = base.lastIndexOf(".");
    if (dot > 0 && dot < base.length - 1) {
      return base.slice(dot + 1).toLowerCase().trim();
    }
  }
  try {
    const pathname = decodeURIComponent(new URL(urlStr).pathname || "");
    const leaf = pathname.split("/").pop() || "";
    const dot = leaf.lastIndexOf(".");
    if (dot > 0 && dot < leaf.length - 1) {
      return leaf.slice(dot + 1).toLowerCase().trim();
    }
  } catch {}
  return "";
}

function wildcardHostMatch(host, pattern) {
  if (!host || !pattern) return false;
  const h = host.toLowerCase();
  const p = pattern.toLowerCase();
  if (h === p) return true;
  if (p.startsWith("*.") && p.length > 2) {
    const suffix = p.slice(2);
    if (h === suffix || h.endsWith("." + suffix)) return true;
  }
  const escaped = p.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*").replace(/\?/g, ".");
  return new RegExp("^" + escaped + "$", "i").test(h);
}

function extractHostname(rawUrl) {
  if (!rawUrl) return "";
  try {
    return new URL(rawUrl).hostname || "";
  } catch {
    return "";
  }
}

function isHostExcluded(targetUrl, refererUrl, excludedPatterns) {
  if (!excludedPatterns || !excludedPatterns.trim()) return false;
  const patterns = excludedPatterns
    .split(/[|,;\s]+/)
    .map((s) => s.trim())
    .filter(Boolean);
  if (patterns.length === 0) return false;

  const h1 = extractHostname(targetUrl);
  const h2 = extractHostname(refererUrl);
  for (const pat of patterns) {
    if (h1 && wildcardHostMatch(h1, pat)) return true;
    if (h2 && wildcardHostMatch(h2, pat)) return true;
  }
  return false;
}

async function sendSingleToMhs({
  url,
  fileName = "",
  referer = "",
  startImmediately = true,
  showDialog = false,
  silent = false
}) {
  const cookies = await getCookieHeaderForUrl(url);
  const payload = {
    url,
    fileName: fileName || "",
    referer: referer || "",
    cookies,
    userAgent: navigator.userAgent,
    startImmediately,
    showDialog,
    silent
  };
  const result = await deliverToMhs("/api/v1/downloads", payload, "download");
  return result.ok;
}

// Safe Download Takeover: Pause -> Verify Handoff -> Cancel/Erase (or Resume on failure!)
chrome.downloads.onCreated.addListener(async (downloadItem) => {
  let pausedByUs = false;
  try {
    const url = downloadItem.finalUrl || downloadItem.url || "";
    if (!url.startsWith("http://") && !url.startsWith("https://")) return;

    const now = Date.now();
    if (now < altBypassUntil) {
      altBypassUntil = 0;
      return; // User held Alt to let the browser handle this download directly
    }

    const forcedByHotkey = now < forceCaptureUntil;
    if (forcedByHotkey) {
      forceCaptureUntil = 0;
    }

    const prefs = await chrome.storage.local.get(DEFAULT_PREFS);
    if (!forcedByHotkey && !prefs.autoIntercept) return;

    const ping = await pingBridge();
    const monitoredRaw = (ping && ping.monitoredExtensions) || prefs.monitoredExtensions || DEFAULT_EXTS.join("|");
    const excludedRaw = ping && typeof ping.excludedHosts === "string" ? ping.excludedHosts : prefs.excludedHosts;

    if (!forcedByHotkey) {
      if (isHostExcluded(url, downloadItem.referrer || "", excludedRaw)) {
        return;
      }
      const allowed = monitoredRaw
        .split(/[|,;\s]+/)
        .map((s) => s.replace(/^\./, "").toLowerCase())
        .filter(Boolean);

      const ext = extractExtension(url, downloadItem.filename);
      if (!ext || !allowed.includes(ext)) return;
    }

    // Pause browser download first so bytes aren't lost while verifying MHS Manager handoff
    try {
      await chrome.downloads.pause(downloadItem.id);
      pausedByUs = true;
    } catch {}

    const suggestedLeaf = downloadItem.filename ? downloadItem.filename.split(/[\\/]/).pop() : "";
    const handedOff = await sendSingleToMhs({
      url,
      fileName: suggestedLeaf,
      referer: downloadItem.referrer || "",
      startImmediately: true,
      showDialog: false,
      silent: false
    });

    if (handedOff) {
      try {
        await chrome.downloads.cancel(downloadItem.id);
      } catch {}
      try {
        await chrome.downloads.erase({ id: downloadItem.id });
      } catch {}
    } else if (pausedByUs) {
      // Desktop app offline & Native Host unavailable -> seamlessly resume browser download!
      try {
        await chrome.downloads.resume(downloadItem.id);
      } catch {}
    }
  } catch {
    if (pausedByUs) {
      try {
        await chrome.downloads.resume(downloadItem.id);
      } catch {}
    }
  }
});

// Media Stream Sniffer (HLS .m3u8 + progressive video/audio streams per tab)
function formatShortBytes(bytes) {
  if (!bytes || bytes <= 0) return "";
  const units = ["B", "KB", "MB", "GB"];
  let val = bytes;
  let idx = 0;
  while (val >= 1024 && idx < units.length - 1) {
    val /= 1024;
    idx++;
  }
  return `${val.toFixed(idx === 0 ? 0 : 1)} ${units[idx]}`;
}

function classifyMediaResponse(urlStr, contentType, contentLength) {
  const ct = (contentType || "").toLowerCase().split(";")[0].trim();
  const ext = extractExtension(urlStr, "");

  // HLS Master/Variant Playlists
  if (
    ext === "m3u8" ||
    ct === "application/vnd.apple.mpegurl" ||
    ct === "application/x-mpegurl" ||
    ct === "audio/mpegurl" ||
    ct === "audio/x-mpegurl"
  ) {
    const resMatch = urlStr.match(/(?:^|[^\d])(2160p|1440p|1080p|720p|480p|360p|240p)(?:[^\d]|$)/i);
    const quality = resMatch ? ` ${resMatch[1]}` : "";
    return { kind: "hls", ext: "m3u8", label: `HLS Stream${quality} (.m3u8)` };
  }

  // Skip granular HLS/DASH chunk segments (.ts / .m4s) so the user gets clean full streams
  if (ext === "ts" || ext === "m4s" || ct === "video/mp2t" || ct === "video/iso.segment") {
    return null;
  }

  const videoExts = ["mp4", "mkv", "webm", "mov", "flv", "avi"];
  const audioExts = ["mp3", "m4a", "flac", "wav", "ogg", "aac", "opus"];
  const isVideo = ct.startsWith("video/") || videoExts.includes(ext);
  const isAudio = ct.startsWith("audio/") || audioExts.includes(ext);

  if (!isVideo && !isAudio) return null;

  // Ignore tiny UI sound effects / tracking pixels (< 150 KB when Content-Length is known)
  if (contentLength > 0 && contentLength < 150 * 1024) {
    return null;
  }

  const resolvedExt =
    ext && (videoExts.includes(ext) || audioExts.includes(ext))
      ? ext
      : ct.includes("webm")
      ? "webm"
      : ct.includes("matroska")
      ? "mkv"
      : ct.includes("mpeg") && isAudio
      ? "mp3"
      : isAudio
      ? "m4a"
      : "mp4";

  const resMatch = urlStr.match(/(?:^|[^\d])(2160p|1440p|1080p|720p|480p|360p|240p)(?:[^\d]|$)/i);
  const quality = resMatch ? ` ${resMatch[1]}` : "";
  const sizeTag = contentLength > 0 ? ` • ${formatShortBytes(contentLength)}` : "";
  const typeLabel = isAudio ? "Audio" : "Video";
  return {
    kind: isAudio ? "audio" : "video",
    ext: resolvedExt,
    label: `${resolvedExt.toUpperCase()} ${typeLabel}${quality}${sizeTag}`
  };
}

function updateTabBadge(tabId) {
  const map = tabStreams.get(tabId);
  const count = map ? map.size : 0;
  try {
    chrome.action.setBadgeBackgroundColor({ tabId, color: "#0284c7" });
    chrome.action.setBadgeText({ tabId, text: count > 0 ? String(count) : "" });
  } catch {}
}

function getTabStreamsArray(tabId) {
  const map = tabStreams.get(tabId);
  if (!map) return [];
  return Array.from(map.values()).sort((a, b) => b.detectedAt - a.detectedAt);
}

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    try {
      if (details.tabId < 0) return;
      if (details.statusCode >= 400 || details.statusCode === 204) return;
      if (!details.url.startsWith("http://") && !details.url.startsWith("https://")) return;

      let contentType = "";
      let contentLength = 0;
      if (Array.isArray(details.responseHeaders)) {
        for (const h of details.responseHeaders) {
          if (!h || !h.name) continue;
          const n = h.name.toLowerCase();
          if (n === "content-type" && h.value) contentType = h.value;
          else if (n === "content-length" && h.value) contentLength = parseInt(h.value, 10) || 0;
        }
      }

      const info = classifyMediaResponse(details.url, contentType, contentLength);
      if (!info) return;

      let map = tabStreams.get(details.tabId);
      if (!map) {
        map = new Map();
        tabStreams.set(details.tabId, map);
      }

      let suggestedName = "";
      try {
        const leaf = decodeURIComponent(new URL(details.url).pathname.split("/").pop() || "");
        if (leaf && leaf.includes(".")) suggestedName = leaf;
      } catch {}

      map.set(details.url, {
        url: details.url,
        label: info.label,
        kind: info.kind,
        ext: info.ext,
        sizeBytes: contentLength,
        fileName: suggestedName,
        detectedAt: Date.now()
      });

      // Bound per-tab retained stream list to 25 entries
      if (map.size > 25) {
        const oldestKey = map.keys().next().value;
        map.delete(oldestKey);
      }

      updateTabBadge(details.tabId);
      const list = getTabStreamsArray(details.tabId);
      chrome.tabs.sendMessage(details.tabId, { type: "MHS_TAB_STREAMS_UPDATED", streams: list }).catch(() => {});
    } catch {}
  },
  { urls: ["http://*/*", "https://*/*"] },
  ["responseHeaders"]
);

chrome.tabs.onRemoved.addListener((tabId) => {
  tabStreams.delete(tabId);
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === "loading" && changeInfo.url) {
    tabStreams.delete(tabId);
    updateTabBadge(tabId);
  }
});

// Context Menu Handlers
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "mhs-download-link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (!targetUrl) return;
    await sendSingleToMhs({
      url: targetUrl,
      referer: (tab && tab.url) || info.pageUrl || "",
      startImmediately: true,
      showDialog: true,
      silent: false
    });
  } else if (info.menuItemId === "mhs-download-selection" && tab && tab.id) {
    await grabAndSendSelectedTabLinks(tab);
  } else if (info.menuItemId === "mhs-download-all" && tab && tab.id) {
    await grabAndSendAllTabLinks(tab, true);
  }
});

async function grabAndSendSelectedTabLinks(tab) {
  try {
    const injected = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0) return [];
        const seen = new Set();
        const links = [];

        for (let i = 0; i < sel.rangeCount; i++) {
          const range = sel.getRangeAt(i);
          const container = range.commonAncestorContainer.nodeType === 1
            ? range.commonAncestorContainer
            : range.commonAncestorContainer.parentElement;
          if (!container) continue;

          const anchors = container.querySelectorAll("a[href]");
          for (const a of anchors) {
            if (!sel.containsNode(a, true)) continue;
            const raw = a.href;
            if (!raw || (!raw.startsWith("http://") && !raw.startsWith("https://"))) continue;
            if (seen.has(raw)) continue;
            seen.add(raw);
            links.push({ url: raw });
          }
        }

        // Also capture plain-text HTTP(S) URLs highlighted in text
        const rawText = sel.toString() || "";
        const urlRegex = /https?:\/\/[^\s"'<>]+/gi;
        let match;
        while ((match = urlRegex.exec(rawText)) !== null) {
          const clean = match[0].replace(/[),.;]+$/, "");
          if (!seen.has(clean)) {
            seen.add(clean);
            links.push({ url: clean });
          }
        }
        return links;
      }
    });

    const list = (injected && injected[0] && injected[0].result) || [];
    if (list.length === 0) return { ok: false, count: 0 };

    const cookies = tab.url ? await getCookieHeaderForUrl(tab.url) : "";
    const res = await deliverToMhs(
      "/api/v1/batch",
      {
        links: list,
        referer: tab.url || "",
        cookies,
        userAgent: navigator.userAgent,
        startImmediately: false,
        showBatchDialog: true
      },
      "batch"
    );
    return { ok: res.ok, count: list.length };
  } catch {
    return { ok: false, count: 0 };
  }
}

async function grabAndSendAllTabLinks(tab, showBatchDialog = true) {
  try {
    const injected = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const seen = new Set();
        const links = [];
        for (const el of document.querySelectorAll("a[href], video[src], audio[src], source[src]")) {
          const raw = el.href || el.src;
          if (!raw || (!raw.startsWith("http://") && !raw.startsWith("https://"))) continue;
          if (seen.has(raw)) continue;
          seen.add(raw);
          links.push({ url: raw });
        }
        return links;
      }
    });

    const seen = new Set();
    const list = [];
    const domLinks = (injected && injected[0] && injected[0].result) || [];
    for (const item of domLinks) {
      if (item && item.url && !seen.has(item.url)) {
        seen.add(item.url);
        list.push(item);
      }
    }

    // Merge sniffed media streams from this tab
    const sniffed = getTabStreamsArray(tab.id);
    for (const s of sniffed) {
      if (s && s.url && !seen.has(s.url)) {
        seen.add(s.url);
        list.push({ url: s.url, fileName: s.fileName || "" });
      }
    }

    if (list.length === 0) return { ok: false, count: 0 };

    const cookies = tab.url ? await getCookieHeaderForUrl(tab.url) : "";
    const res = await deliverToMhs(
      "/api/v1/batch",
      {
        links: list,
        referer: tab.url || "",
        cookies,
        userAgent: navigator.userAgent,
        startImmediately: false,
        showBatchDialog
      },
      "batch"
    );
    const count = (res.data && typeof res.data.addedCount === "number" && !showBatchDialog)
      ? res.data.addedCount
      : list.length;
    return { ok: res.ok, count };
  } catch {
    return { ok: false, count: 0 };
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || !msg.type) return false;

  if (msg.type === "MHS_KEY_MODIFIER") {
    if (msg.bypass) {
      altBypassUntil = Date.now() + 3500;
    }
    if (msg.force) {
      forceCaptureUntil = Date.now() + 3500;
    }
    sendResponse({ ok: true });
    return false;
  }

  if (msg.type === "MHS_GET_TAB_STREAMS") {
    const tabId = msg.tabId || (sender.tab ? sender.tab.id : -1);
    sendResponse({ ok: true, streams: tabId >= 0 ? getTabStreamsArray(tabId) : [] });
    return false;
  }

  if (msg.type === "MHS_DOWNLOAD_MEDIA") {
    const tabTitle = sender.tab && sender.tab.title ? sender.tab.title : "";
    let fileName = msg.fileName || "";
    if (!fileName && tabTitle && msg.ext) {
      const cleanTitle = tabTitle.replace(/[\\/:*?"<>|]+/g, "_").trim().slice(0, 90);
      if (cleanTitle) fileName = `${cleanTitle}.${msg.ext}`;
    }
    sendSingleToMhs({
      url: msg.url,
      fileName,
      referer: msg.referer || (sender.tab ? sender.tab.url : ""),
      startImmediately: true,
      showDialog: Boolean(msg.showDialog),
      silent: false
    })
      .then((ok) => sendResponse({ ok }))
      .catch(() => sendResponse({ ok: false }));
    return true;
  }

  if (msg.type === "MHS_GRAB_ACTIVE_TAB") {
    chrome.tabs.query({ active: true, currentWindow: true }, async (tabs) => {
      if (!tabs || !tabs[0]) {
        sendResponse({ ok: false, count: 0 });
        return;
      }
      const res = await grabAndSendAllTabLinks(tabs[0], true);
      sendResponse(res);
    });
    return true;
  }

  if (msg.type === "MHS_SYNC_NATIVE") {
    syncViaNativeHost()
      .then((res) => sendResponse(res))
      .catch(() => sendResponse({ ok: false }));
    return true;
  }

  if (msg.type === "MHS_GET_POPUP_STATE") {
    (async () => {
      const prefs = await chrome.storage.local.get(DEFAULT_PREFS);
      const status = await fetchStatusFromBridge();
      const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
      const activeTab = tabs && tabs[0] ? tabs[0] : null;
      const streams = activeTab ? getTabStreamsArray(activeTab.id) : [];
      sendResponse({
        ok: true,
        prefs,
        status,
        streams,
        extensionId: chrome.runtime.id
      });
    })().catch(() => sendResponse({ ok: false }));
    return true;
  }

  if (msg.type === "MHS_QUEUE_COMMAND") {
    const endpoint = msg.action === "stop" ? "/api/v1/queue/stop" : "/api/v1/queue/start";
    const nativeType = msg.action === "start" ? "start_queue" : null;
    deliverToMhs(endpoint, { queueName: msg.queueName || "Main Queue" }, nativeType)
      .then((res) => sendResponse({ ok: res.ok }))
      .catch(() => sendResponse({ ok: false }));
    return true;
  }

  return false;
});
