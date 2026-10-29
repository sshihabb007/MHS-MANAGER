const BRIDGE_BASE = "http://127.0.0.1:16888";
const DEFAULT_EXTS = [
  "zip", "rar", "7z", "tar", "gz", "xz", "exe", "msi", "msix", "iso", "apk", "dmg",
  "mp4", "mkv", "webm", "avi", "mov", "flv", "m3u8", "ts", "mp3", "flac", "wav",
  "m4a", "ogg", "pdf", "epub", "docx", "xlsx", "pptx"
];

chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.get({ autoIntercept: true }, (prefs) => {
    chrome.storage.local.set({ autoIntercept: prefs.autoIntercept });
  });

  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: "mhs-download-link",
      title: "Download with MHS Manager",
      contexts: ["link", "video", "audio"]
    });
    chrome.contextMenus.create({
      id: "mhs-download-all",
      title: "Download All Links with MHS Manager",
      contexts: ["page"]
    });
  });
});

async function pingBridge() {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 1200);
  try {
    const resp = await fetch(`${BRIDGE_BASE}/api/v1/ping`, {
      method: "GET",
      headers: { "X-MHS-Client": "webextension-mv3" },
      signal: controller.signal
    });
    clearTimeout(timer);
    if (!resp.ok) return null;
    return await resp.json();
  } catch {
    clearTimeout(timer);
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

function extractExtension(urlStr, suggestedFilename) {
  const candidate = suggestedFilename || (() => {
    try {
      return new URL(urlStr).pathname;
    } catch {
      return "";
    }
  })();
  const dot = candidate.lastIndexOf(".");
  if (dot < 0) return "";
  return candidate.slice(dot + 1).toLowerCase().trim();
}

async function sendSingleToMhs({ url, fileName, referer, startImmediately = true, showDialog = false }) {
  const cookies = await getCookieHeaderForUrl(url);
  const resp = await fetch(`${BRIDGE_BASE}/api/v1/downloads`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-MHS-Client": "webextension-mv3"
    },
    body: JSON.stringify({
      url,
      fileName: fileName || "",
      referer: referer || "",
      cookies,
      userAgent: navigator.userAgent,
      startImmediately,
      showDialog
    })
  });
  return resp.ok;
}

chrome.downloads.onCreated.addListener(async (downloadItem) => {
  try {
    const url = downloadItem.finalUrl || downloadItem.url || "";
    if (!url.startsWith("http://") && !url.startsWith("https://")) return;

    const { autoIntercept } = await chrome.storage.local.get({ autoIntercept: true });
    if (!autoIntercept) return;

    const ping = await pingBridge();
    if (!ping || !ping.ok) return; // Desktop app offline -> let browser handle download normally

    const allowed = (ping.monitoredExtensions || DEFAULT_EXTS.join("|"))
      .split(/[|,;\s]+/)
      .map((s) => s.replace(/^\./, "").toLowerCase())
      .filter(Boolean);

    const ext = extractExtension(url, downloadItem.filename);
    if (!ext || !allowed.includes(ext)) return;

    await chrome.downloads.cancel(downloadItem.id);
    try {
      await chrome.downloads.erase({ id: downloadItem.id });
    } catch {}

    await sendSingleToMhs({
      url,
      fileName: downloadItem.filename ? downloadItem.filename.split(/[\\/]/).pop() : "",
      referer: downloadItem.referrer || "",
      startImmediately: true,
      showDialog: false
    });
  } catch {
    // Fail open if anything goes wrong
  }
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "mhs-download-link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (!targetUrl) return;
    await sendSingleToMhs({
      url: targetUrl,
      referer: tab && tab.url ? tab.url : info.pageUrl || "",
      startImmediately: true,
      showDialog: false
    });
  } else if (info.menuItemId === "mhs-download-all" && tab && tab.id) {
    await grabAndSendAllTabLinks(tab);
  }
});

async function grabAndSendAllTabLinks(tab) {
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
    const list = (injected && injected[0] && injected[0].result) || [];
    if (list.length === 0) return { ok: false, count: 0 };

    const cookies = tab.url ? await getCookieHeaderForUrl(tab.url) : "";
    const resp = await fetch(`${BRIDGE_BASE}/api/v1/batch`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        links: list,
        referer: tab.url || "",
        cookies,
        userAgent: navigator.userAgent,
        startImmediately: false
      })
    });
    const data = await resp.json();
    return { ok: resp.ok, count: data.addedCount || 0 };
  } catch {
    return { ok: false, count: 0 };
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || !msg.type) return false;

  if (msg.type === "MHS_DOWNLOAD_MEDIA") {
    sendSingleToMhs({
      url: msg.url,
      fileName: msg.fileName || "",
      referer: msg.referer || (sender.tab ? sender.tab.url : ""),
      startImmediately: true,
      showDialog: false
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
      const res = await grabAndSendAllTabLinks(tabs[0]);
      sendResponse(res);
    });
    return true;
  }

  return false;
});

