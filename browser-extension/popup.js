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

function renderStreams(streams) {
  const container = document.getElementById("streamList");
  container.textContent = "";
  if (!Array.isArray(streams) || streams.length === 0) {
    const empty = document.createElement("div");
    empty.className = "empty-hint";
    empty.textContent = "No media streams detected on this tab yet.";
    container.appendChild(empty);
    return;
  }

  for (const s of streams.slice(0, 10)) {
    const row = document.createElement("div");
    row.className = "stream-item";

    const lbl = document.createElement("div");
    lbl.className = "stream-label";
    lbl.title = s.url;
    lbl.textContent = s.label || s.url;

    const btn = document.createElement("button");
    btn.className = "small";
    btn.textContent = "Download";
    btn.addEventListener("click", () => {
      btn.textContent = "Sending...";
      chrome.runtime.sendMessage(
        {
          type: "MHS_DOWNLOAD_MEDIA",
          url: s.url,
          ext: s.ext || "",
          fileName: s.fileName || "",
          showDialog: true
        },
        (res) => {
          btn.textContent = res && res.ok ? "✓ Sent" : "Failed";
          setTimeout(() => {
            btn.textContent = "Download";
            refreshPopupState();
          }, 1500);
        }
      );
    });

    row.appendChild(lbl);
    row.appendChild(btn);
    container.appendChild(row);
  }
}

function refreshPopupState() {
  const badge = document.getElementById("statusBadge");
  const stats = document.getElementById("statsLine");
  const chkAuto = document.getElementById("chkAuto");
  const txtPort = document.getElementById("txtPort");
  const txtToken = document.getElementById("txtToken");
  const lblExtId = document.getElementById("lblExtId");
  const pairingDetails = document.getElementById("pairingDetails");

  chrome.runtime.sendMessage({ type: "MHS_GET_POPUP_STATE" }, (res) => {
    if (!res || !res.ok) {
      badge.textContent = "Error";
      badge.className = "badge off";
      return;
    }

    lblExtId.textContent = res.extensionId || chrome.runtime.id;
    chkAuto.checked = Boolean(res.prefs.autoIntercept);
    if (!txtPort.matches(":focus")) {
      txtPort.value = res.prefs.bridgePort || 16888;
    }
    if (!txtToken.matches(":focus") && res.prefs.bridgeToken) {
      txtToken.value = res.prefs.bridgeToken;
    }

    renderStreams(res.streams || []);

    const st = res.status;
    if (!st || !st.ok) {
      badge.textContent = "App Offline";
      badge.className = "badge off";
      stats.textContent = "Launch MHSManager.exe (or register Native Host for auto-launch)";
    } else if (!st.tokenValid) {
      badge.textContent = "Pairing Needed";
      badge.className = "badge warn";
      stats.textContent = "Pair token below or register Native Host in MHS Options";
      pairingDetails.open = true;
    } else {
      badge.textContent = "Connected";
      badge.className = "badge ok";
      stats.textContent = `Active: ${st.activeCount} / ${st.totalCount} • Speed: ${formatSpeed(st.totalSpeedBps)}`;
    }
  });
}

document.addEventListener("DOMContentLoaded", () => {
  const chkAuto = document.getElementById("chkAuto");
  chkAuto.addEventListener("change", () => {
    chrome.storage.local.set({ autoIntercept: chkAuto.checked });
  });

  document.getElementById("btnSendUrl").addEventListener("click", () => {
    const input = document.getElementById("txtQuickUrl");
    const url = (input.value || "").trim();
    if (!url) return;
    chrome.runtime.sendMessage({ type: "MHS_DOWNLOAD_MEDIA", url, showDialog: true }, (res) => {
      if (res && res.ok) {
        input.value = "";
        refreshPopupState();
      }
    });
  });

  document.getElementById("btnGrabAll").addEventListener("click", () => {
    const btn = document.getElementById("btnGrabAll");
    btn.textContent = "Scanning tab links...";
    chrome.runtime.sendMessage({ type: "MHS_GRAB_ACTIVE_TAB" }, (res) => {
      btn.textContent = res && res.ok ? `Sent ${res.count} links to MHS!` : "Failed (Check pairing/app)";
      setTimeout(() => {
        btn.textContent = "Download All Links on This Page";
        refreshPopupState();
      }, 2000);
    });
  });

  document.getElementById("btnStartQueue").addEventListener("click", () => {
    chrome.runtime.sendMessage({ type: "MHS_QUEUE_COMMAND", action: "start" }, () => {
      refreshPopupState();
    });
  });

  document.getElementById("btnStopQueue").addEventListener("click", () => {
    chrome.runtime.sendMessage({ type: "MHS_QUEUE_COMMAND", action: "stop" }, () => {
      refreshPopupState();
    });
  });

  document.getElementById("btnSyncNative").addEventListener("click", () => {
    const btn = document.getElementById("btnSyncNative");
    btn.textContent = "Syncing with com.mhsmanager.host...";
    chrome.runtime.sendMessage({ type: "MHS_SYNC_NATIVE" }, (res) => {
      btn.textContent = res && res.ok ? "✓ Paired via Native Host!" : "Native Host Not Registered Yet";
      setTimeout(() => {
        btn.textContent = "Sync Automatically via Native Host";
        refreshPopupState();
      }, 2000);
    });
  });

  document.getElementById("btnSavePairing").addEventListener("click", () => {
    const port = parseInt(document.getElementById("txtPort").value, 10) || 16888;
    const token = (document.getElementById("txtToken").value || "").trim();
    const btn = document.getElementById("btnSavePairing");
    chrome.storage.local.set({ bridgePort: port, bridgeToken: token }, () => {
      btn.textContent = "✓ Saved!";
      setTimeout(() => {
        btn.textContent = "Save Port & Pairing Token";
        refreshPopupState();
      }, 1200);
    });
  });

  refreshPopupState();
});
