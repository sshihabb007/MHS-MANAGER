const BRIDGE_BASE = "http://127.0.0.1:16888";

function formatSpeed(bps) {
  if (!bps || bps < 1) return "0 B/s";
  const units = ["B/s", "KB/s", "MB/s", "GB/s"];
  let v = bps;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(i === 0 ? 0 : 2)} ${units[i]}`;
}

async function refreshBridgeStatus() {
  const badge = document.getElementById("statusBadge");
  const stats = document.getElementById("statsLine");
  try {
    const resp = await fetch(`${BRIDGE_BASE}/api/v1/status`);
    if (!resp.ok) throw new Error("Offline");
    const data = await resp.json();
    badge.textContent = "Connected";
    badge.className = "badge ok";
    stats.textContent = `Active: ${data.activeCount} / ${data.totalCount} • Speed: ${formatSpeed(data.totalSpeedBps)}`;
  } catch {
    badge.textContent = "App Offline";
    badge.className = "badge off";
    stats.textContent = "Launch MHSManager.exe to enable takeover";
  }
}

document.addEventListener("DOMContentLoaded", () => {
  const chkAuto = document.getElementById("chkAuto");
  chrome.storage.local.get({ autoIntercept: true }, (res) => {
    chkAuto.checked = Boolean(res.autoIntercept);
  });
  chkAuto.addEventListener("change", () => {
    chrome.storage.local.set({ autoIntercept: chkAuto.checked });
  });

  document.getElementById("btnSendUrl").addEventListener("click", () => {
    const input = document.getElementById("txtQuickUrl");
    const url = (input.value || "").trim();
    if (!url) return;
    chrome.runtime.sendMessage({ type: "MHS_DOWNLOAD_MEDIA", url }, (res) => {
      if (res && res.ok) {
        input.value = "";
        refreshBridgeStatus();
      }
    });
  });

  document.getElementById("btnGrabAll").addEventListener("click", () => {
    const btn = document.getElementById("btnGrabAll");
    btn.textContent = "Scanning tab links...";
    chrome.runtime.sendMessage({ type: "MHS_GRAB_ACTIVE_TAB" }, (res) => {
      btn.textContent = res && res.ok ? `Queued ${res.count} links!` : "Failed (Ensure MHS is open)";
      setTimeout(() => {
        btn.textContent = "Queue All Links on This Page";
        refreshBridgeStatus();
      }, 2000);
    });
  });

  refreshBridgeStatus();
});

