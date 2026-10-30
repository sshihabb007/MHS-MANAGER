"use strict";

document.addEventListener("DOMContentLoaded", () => {
  const shihabQuery = new URLSearchParams(location.search);
  const mehediHost = shihabQuery.get("host");
  const shihabMsgEl = document.getElementById("shihabTransferredText");
  if (mehediHost && shihabMsgEl) {
    shihabMsgEl.textContent = "File download from " + mehediHost + " has been transferred to MHS Download Manager (mdm.exe).";
  }
  const mehediBackBtn = document.getElementById("mehediBackButton");
  if (mehediBackBtn) {
    mehediBackBtn.addEventListener("click", () => {
      if (history.length > 1) {
        history.back();
      } else {
        window.close();
      }
    });
  }
});
