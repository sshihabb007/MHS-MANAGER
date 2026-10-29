(function () {
  const BADGE_ATTR = "data-mhs-bound";
  let activeOverlay = null;
  let trackedVideo = null;

  function resolvePlayableHttpSource(video) {
    if (!video) return null;
    const candidates = [video.currentSrc, video.src];
    const sources = video.querySelectorAll("source[src]");
    for (const s of sources) candidates.push(s.src);

    for (const c of candidates) {
      if (typeof c === "string" && (c.startsWith("http://") || c.startsWith("https://"))) {
        return c;
      }
    }
    return null;
  }

  function ensureOverlay() {
    if (activeOverlay) return activeOverlay;
    const pill = document.createElement("button");
    pill.type = "button";
    pill.textContent = "⬇ Download Video with MHS";
    Object.assign(pill.style, {
      position: "fixed",
      zIndex: "2147483646",
      display: "none",
      padding: "6px 12px",
      borderRadius: "6px",
      border: "1px solid rgba(255,255,255,0.28)",
      background: "linear-gradient(135deg, #0ea5e9, #10b981)",
      color: "#ffffff",
      fontFamily: "Segoe UI, system-ui, sans-serif",
      fontSize: "12px",
      fontWeight: "600",
      cursor: "pointer",
      boxShadow: "0 4px 14px rgba(15, 23, 42, 0.55)"
    });

    pill.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      const src = resolvePlayableHttpSource(trackedVideo);
      if (!src) return;
      pill.textContent = "Sending to MHS...";
      chrome.runtime.sendMessage(
        {
          type: "MHS_DOWNLOAD_MEDIA",
          url: src,
          referer: location.href
        },
        (res) => {
          pill.textContent = res && res.ok ? "✓ Sent to MHS Manager" : "MHS Desktop Offline";
          setTimeout(() => {
            pill.textContent = "⬇ Download Video with MHS";
          }, 2200);
        }
      );
    });

    document.documentElement.appendChild(pill);
    activeOverlay = pill;
    return pill;
  }

  function positionOverlayOver(video) {
    const src = resolvePlayableHttpSource(video);
    if (!src) return;
    const rect = video.getBoundingClientRect();
    if (rect.width < 160 || rect.height < 90) return;

    const pill = ensureOverlay();
    trackedVideo = video;
    pill.style.top = `${Math.max(8, rect.top + 10)}px`;
    pill.style.left = `${Math.max(8, rect.right - 195)}px`;
    pill.style.display = "block";
  }

  function bindVideos() {
    const videos = document.querySelectorAll("video");
    for (const v of videos) {
      if (v.hasAttribute(BADGE_ATTR)) continue;
      v.setAttribute(BADGE_ATTR, "1");
      v.addEventListener("mouseenter", () => positionOverlayOver(v), { passive: true });
    }
  }

  bindVideos();
  const obs = new MutationObserver(() => bindVideos());
  obs.observe(document.documentElement, { childList: true, subtree: true });

  window.addEventListener(
    "scroll",
    () => {
      if (activeOverlay && activeOverlay.style.display === "block" && trackedVideo) {
        positionOverlayOver(trackedVideo);
      }
    },
    { passive: true }
  );
})();

