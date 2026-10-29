(function () {
  const BADGE_ATTR = "data-mhs-bound";
  let panelEl = null;
  let btnDownloadEl = null;
  let selectStreamEl = null;
  let trackedVideo = null;
  let hoveringVideo = false;
  let hoveringPanel = false;
  let hideTimer = null;
  let sniffedStreams = [];
  let dismissedForKey = "";
  let insertKeyHeld = false;

  function safeSendMessage(payload, callback) {
    try {
      if (!chrome || !chrome.runtime || !chrome.runtime.id) return;
      chrome.runtime.sendMessage(payload, (resp) => {
        if (chrome.runtime.lastError) {
          if (callback) callback(null);
          return;
        }
        if (callback) callback(resp);
      });
    } catch {
      if (callback) callback(null);
    }
  }

  // Track Insert key + Alt / Ctrl+Shift click modifiers (IDM hotkey parity)
  window.addEventListener(
    "keydown",
    (ev) => {
      if (ev.key === "Insert") insertKeyHeld = true;
    },
    { capture: true, passive: true }
  );

  window.addEventListener(
    "keyup",
    (ev) => {
      if (ev.key === "Insert") insertKeyHeld = false;
    },
    { capture: true, passive: true }
  );

  window.addEventListener(
    "blur",
    () => {
      insertKeyHeld = false;
    },
    { passive: true }
  );

  function notifyClickModifiers(ev) {
    if (ev.altKey && !ev.ctrlKey && !ev.metaKey) {
      safeSendMessage({ type: "MHS_KEY_MODIFIER", bypass: true, force: false });
    } else if (insertKeyHeld || (ev.ctrlKey && ev.shiftKey)) {
      safeSendMessage({ type: "MHS_KEY_MODIFIER", bypass: false, force: true });
    }
  }

  window.addEventListener("mousedown", notifyClickModifiers, { capture: true, passive: true });
  window.addEventListener("click", notifyClickModifiers, { capture: true, passive: true });

  // Request already-sniffed network streams for this tab
  safeSendMessage({ type: "MHS_GET_TAB_STREAMS" }, (res) => {
    if (res && Array.isArray(res.streams)) {
      sniffedStreams = res.streams;
    }
  });

  try {
    chrome.runtime.onMessage.addListener((msg) => {
      if (msg && msg.type === "MHS_TAB_STREAMS_UPDATED" && Array.isArray(msg.streams)) {
        sniffedStreams = msg.streams;
        if (panelEl && panelEl.style.display !== "none" && trackedVideo) {
          populateStreamsForVideo(trackedVideo);
        }
      }
    });
  } catch {}

  function collectDirectVideoSources(video) {
    const out = [];
    if (!video) return out;
    const seen = new Set();
    const rawCandidates = [video.currentSrc, video.src];
    const sourceTags = video.querySelectorAll("source[src]");
    for (const s of sourceTags) {
      rawCandidates.push(s.src);
    }

    for (const c of rawCandidates) {
      if (typeof c === "string" && (c.startsWith("http://") || c.startsWith("https://")) && !seen.has(c)) {
        seen.add(c);
        let ext = "mp4";
        try {
          const leaf = decodeURIComponent(new URL(c).pathname.split("/").pop() || "");
          const dot = leaf.lastIndexOf(".");
          if (dot > 0) ext = leaf.slice(dot + 1).toLowerCase();
        } catch {}
        const label = ext === "m3u8" ? "Direct HLS Stream (.m3u8)" : `Direct ${ext.toUpperCase()} Video`;
        out.push({ url: c, label, ext });
      }
    }
    return out;
  }

  function buildCombinedStreamList(video) {
    const seen = new Set();
    const combined = [];

    for (const d of collectDirectVideoSources(video)) {
      if (!seen.has(d.url)) {
        seen.add(d.url);
        combined.push(d);
      }
    }

    for (const s of sniffedStreams) {
      if (s && s.url && !seen.has(s.url)) {
        seen.add(s.url);
        combined.push({
          url: s.url,
          label: s.label || "Detected Stream",
          ext: s.ext || "mp4",
          fileName: s.fileName || ""
        });
      }
    }
    return combined;
  }

  function clearHideTimer() {
    if (hideTimer) {
      clearTimeout(hideTimer);
      hideTimer = null;
    }
  }

  function hidePanelImmediate() {
    clearHideTimer();
    if (panelEl) {
      panelEl.style.display = "none";
    }
  }

  function scheduleHidePanel() {
    clearHideTimer();
    hideTimer = setTimeout(() => {
      if (!hoveringVideo && !hoveringPanel) {
        hidePanelImmediate();
      }
    }, 350);
  }

  function ensurePanel() {
    if (panelEl) return panelEl;

    const wrap = document.createElement("div");
    Object.assign(wrap.style, {
      position: "fixed",
      zIndex: "2147483646",
      display: "none",
      alignItems: "center",
      gap: "4px",
      padding: "4px 6px",
      borderRadius: "6px",
      border: "1px solid rgba(56, 189, 248, 0.45)",
      background: "rgba(15, 23, 42, 0.94)",
      color: "#f8fafc",
      fontFamily: "Segoe UI, system-ui, sans-serif",
      fontSize: "12px",
      boxShadow: "0 6px 18px rgba(2, 6, 23, 0.65)",
      backdropFilter: "blur(6px)"
    });

    const dlBtn = document.createElement("button");
    dlBtn.type = "button";
    dlBtn.textContent = "⬇ Download this video";
    Object.assign(dlBtn.style, {
      border: "none",
      borderRadius: "4px",
      padding: "4px 9px",
      background: "linear-gradient(135deg, #0284c7, #059669)",
      color: "#ffffff",
      fontWeight: "600",
      fontSize: "12px",
      cursor: "pointer",
      whiteSpace: "nowrap"
    });

    const sel = document.createElement("select");
    Object.assign(sel.style, {
      background: "#1e293b",
      color: "#e2e8f0",
      border: "1px solid #475569",
      borderRadius: "4px",
      padding: "3px 5px",
      fontSize: "11px",
      maxWidth: "185px",
      cursor: "pointer"
    });

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.textContent = "×";
    closeBtn.title = "Hide video download bar";
    Object.assign(closeBtn.style, {
      border: "none",
      background: "transparent",
      color: "#94a3b8",
      fontSize: "15px",
      fontWeight: "700",
      lineHeight: "1",
      padding: "2px 5px",
      cursor: "pointer"
    });

    dlBtn.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      const chosenUrl = sel.value;
      if (!chosenUrl) return;
      const opt = sel.options[sel.selectedIndex];
      const ext = opt ? opt.getAttribute("data-ext") || "" : "";
      const fname = opt ? opt.getAttribute("data-filename") || "" : "";

      dlBtn.textContent = "Sending...";
      safeSendMessage(
        {
          type: "MHS_DOWNLOAD_MEDIA",
          url: chosenUrl,
          ext,
          fileName: fname,
          referer: location.href,
          showDialog: true
        },
        (res) => {
          dlBtn.textContent = res && res.ok ? "✓ Sent to MHS" : "MHS Offline";
          setTimeout(() => {
            dlBtn.textContent = "⬇ Download this video";
          }, 2000);
        }
      );
    });

    closeBtn.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      dismissedForKey = location.href + "::" + (trackedVideo ? trackedVideo.currentSrc || "" : "");
      hidePanelImmediate();
    });

    wrap.addEventListener("mouseenter", () => {
      hoveringPanel = true;
      clearHideTimer();
    });

    wrap.addEventListener("mouseleave", () => {
      hoveringPanel = false;
      scheduleHidePanel();
    });

    wrap.appendChild(dlBtn);
    wrap.appendChild(sel);
    wrap.appendChild(closeBtn);
    document.documentElement.appendChild(wrap);

    panelEl = wrap;
    btnDownloadEl = dlBtn;
    selectStreamEl = sel;
    return wrap;
  }

  function populateStreamsForVideo(video) {
    const streams = buildCombinedStreamList(video);
    if (streams.length === 0) return false;

    ensurePanel();
    const prevVal = selectStreamEl.value;
    selectStreamEl.textContent = "";

    for (const s of streams) {
      const opt = document.createElement("option");
      opt.value = s.url;
      opt.textContent = s.label;
      opt.setAttribute("data-ext", s.ext || "");
      opt.setAttribute("data-filename", s.fileName || "");
      selectStreamEl.appendChild(opt);
    }

    if (prevVal) {
      for (let i = 0; i < selectStreamEl.options.length; i++) {
        if (selectStreamEl.options[i].value === prevVal) {
          selectStreamEl.selectedIndex = i;
          break;
        }
      }
    }

    selectStreamEl.style.display = streams.length > 1 ? "inline-block" : "inline-block";
    return true;
  }

  function positionPanelOver(video) {
    if (!video || !video.isConnected || document.fullscreenElement || document.hidden) {
      hidePanelImmediate();
      return;
    }

    const dismissKey = location.href + "::" + (video.currentSrc || "");
    if (dismissedForKey && dismissedForKey === dismissKey) {
      return;
    }

    const rect = video.getBoundingClientRect();
    if (rect.width < 150 || rect.height < 85 || rect.bottom < 20 || rect.top > window.innerHeight - 20) {
      hidePanelImmediate();
      return;
    }

    if (!populateStreamsForVideo(video)) {
      hidePanelImmediate();
      return;
    }

    trackedVideo = video;
    panelEl.style.display = "flex";
    const panelWidth = panelEl.offsetWidth || 280;
    const top = Math.max(8, rect.top + 8);
    const left = Math.max(8, Math.min(window.innerWidth - panelWidth - 10, rect.right - panelWidth - 8));
    panelEl.style.top = `${top}px`;
    panelEl.style.left = `${left}px`;
  }

  function bindVideos() {
    const videos = document.querySelectorAll("video");
    for (const v of videos) {
      if (v.hasAttribute(BADGE_ATTR)) continue;
      v.setAttribute(BADGE_ATTR, "1");

      v.addEventListener(
        "mouseenter",
        () => {
          hoveringVideo = true;
          clearHideTimer();
          positionPanelOver(v);
        },
        { passive: true }
      );

      v.addEventListener(
        "mousemove",
        () => {
          hoveringVideo = true;
          clearHideTimer();
          if (!panelEl || panelEl.style.display === "none") {
            positionPanelOver(v);
          }
        },
        { passive: true }
      );

      v.addEventListener(
        "mouseleave",
        () => {
          hoveringVideo = false;
          scheduleHidePanel();
        },
        { passive: true }
      );
    }
  }

  bindVideos();
  const obs = new MutationObserver(() => {
    bindVideos();
    if (trackedVideo && (!trackedVideo.isConnected || document.fullscreenElement)) {
      hidePanelImmediate();
    }
  });
  obs.observe(document.documentElement, { childList: true, subtree: true });

  document.addEventListener("fullscreenchange", () => {
    if (document.fullscreenElement) hidePanelImmediate();
  });

  document.addEventListener("visibilitychange", () => {
    if (document.hidden) hidePanelImmediate();
  });

  const repositionIfVisible = () => {
    if (panelEl && panelEl.style.display !== "none" && trackedVideo) {
      positionPanelOver(trackedVideo);
    }
  };

  window.addEventListener("scroll", repositionIfVisible, { passive: true });
  window.addEventListener("resize", repositionIfVisible, { passive: true });
})();
