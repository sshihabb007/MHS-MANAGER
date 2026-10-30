"use strict";

(() => {
  if (window.__shihabMehediPageHookInstalled) {
    try {
      window.postMessage({ __shihabMehediContentPing: true }, "*");
    } catch (_) {}
    return;
  }
  window.__shihabMehediPageHookInstalled = true;

  const shihabMediaSourceToBlobUrl = new WeakMap();
  const mehediSourceBufferMeta = new WeakMap();
  const shihabRecentFetchedSlices = [];
  const shihabChunkHashToStreamUrl = new Map();
  const mehediEmittedManifestKeys = new Set();

  function shihabFnv1aHashSlice(mehediBytes, shihabStart, mehediLen) {
    if (!mehediBytes || mehediBytes.byteLength < shihabStart + 16) return "";
    const shihabEnd = Math.min(mehediBytes.byteLength, shihabStart + mehediLen);
    let mehediHash = 0x811c9dc5;
    for (let shihabI = shihabStart; shihabI < shihabEnd; shihabI++) {
      mehediHash ^= mehediBytes[shihabI];
      mehediHash = Math.imul(mehediHash, 0x01000193) >>> 0;
    }
    return `${shihabStart}:${shihabEnd - shihabStart}:${mehediHash.toString(16)}`;
  }

  function shihabComputeMediaChunkSignatures(mehediRawBuf) {
    const shihabSigs = [];
    if (!mehediRawBuf) return shihabSigs;
    let mehediUint8 = null;
    if (mehediRawBuf instanceof Uint8Array) {
      mehediUint8 = mehediRawBuf;
    } else if (ArrayBuffer.isView(mehediRawBuf)) {
      mehediUint8 = new Uint8Array(mehediRawBuf.buffer, mehediRawBuf.byteOffset, Math.min(160, mehediRawBuf.byteLength));
    } else if (mehediRawBuf instanceof ArrayBuffer) {
      mehediUint8 = new Uint8Array(mehediRawBuf, 0, Math.min(160, mehediRawBuf.byteLength));
    }
    if (!mehediUint8 || mehediUint8.byteLength < 28) return shihabSigs;

    const shihabSig0 = shihabFnv1aHashSlice(mehediUint8, 0, 48);
    const mehediSig8 = shihabFnv1aHashSlice(mehediUint8, 8, 48);
    const shihabSig24 = shihabFnv1aHashSlice(mehediUint8, 24, 64);
    if (shihabSig0) shihabSigs.push(shihabSig0);
    if (mehediSig8) shihabSigs.push(mehediSig8);
    if (shihabSig24) shihabSigs.push(shihabSig24);
    return shihabSigs;
  }

  function mehediIndexChunkBytesForUrl(shihabRawUrl, mehediBytes) {
    const shihabClean = mehediNormalizeHookMediaUrl(shihabRawUrl);
    if (!shihabClean || !shihabLooksLikeMediaSliceUrl(shihabClean)) return;
    const mehediSigs = shihabComputeMediaChunkSignatures(mehediBytes);
    for (const shihabSig of mehediSigs) {
      shihabChunkHashToStreamUrl.set(shihabSig, shihabClean);
      if (shihabChunkHashToStreamUrl.size > 360) {
        const mehediOldestKey = shihabChunkHashToStreamUrl.keys().next().value;
        shihabChunkHashToStreamUrl.delete(mehediOldestKey);
      }
    }
  }

  function mehediExtractEfgIds(shihabCleanUrl) {
    const shihabAltSet = new Set();
    let mehediPrimaryId = "";
    if (!shihabCleanUrl) return { mehediPrimaryId, shihabAltIds: [] };
    try {
      const shihabEfg = new URL(shihabCleanUrl).searchParams.get("efg");
      if (shihabEfg) {
        const mehediB64 = decodeURIComponent(shihabEfg).replace(/-/g, "+").replace(/_/g, "/");
        const shihabPad = mehediB64 + "=".repeat((4 - (mehediB64.length % 4)) % 4);
        const mehediJson = JSON.parse(atob(shihabPad));
        if (mehediJson && typeof mehediJson === "object") {
          for (const shihabKey of ["xpv_asset_id", "video_id", "asset_id"]) {
            if (mehediJson[shihabKey]) {
              const mehediStrVal = String(mehediJson[shihabKey]);
              shihabAltSet.add(mehediStrVal);
              if (!mehediPrimaryId) mehediPrimaryId = mehediStrVal;
            }
          }
        }
      }
    } catch (_) {}
    return {
      mehediPrimaryId,
      shihabAltIds: Array.from(shihabAltSet)
    };
  }

  function shihabPostToContentBridge(mehediKind, shihabPayload) {
    try {
      window.postMessage(
        {
          __shihabMehediPageHook: true,
          mehediKind,
          shihabPayload
        },
        "*"
      );
    } catch (shihabPostErr) {
      // Ignore cross-frame post errors
    }
  }

  function mehediNormalizeHookMediaUrl(shihabRawUrl) {
    if (!shihabRawUrl || typeof shihabRawUrl !== "string") return "";
    try {
      const mehediUrlObj = new URL(shihabRawUrl, location.href);
      if (mehediUrlObj.protocol !== "http:" && mehediUrlObj.protocol !== "https:") {
        return "";
      }
      for (const shihabParam of ["bytestart", "byteend", "alr", "rn", "rbuf"]) {
        mehediUrlObj.searchParams.delete(shihabParam);
      }
      if (mehediUrlObj.searchParams.has("range")) {
        const mehediSparams = (mehediUrlObj.searchParams.get("sparams") || "").split(",");
        if (!mehediSparams.includes("range")) {
          mehediUrlObj.searchParams.delete("range");
        }
      }
      return mehediUrlObj.toString();
    } catch (mehediErr) {
      return "";
    }
  }

  function shihabLooksLikeMediaSliceUrl(mehediUrl) {
    if (!mehediUrl) return false;
    const shihabLower = mehediUrl.toLowerCase();
    if (shihabLower.includes("ump=1") || shihabLower.includes("sabr=1")) return false;
    return (
      shihabLower.includes("/videoplayback") ||
      /\.(mp4|webm|m4a|m4v|mov|m3u8|mpd)(\?|$)/i.test(shihabLower) ||
      shihabLower.includes("efg=") ||
      shihabLower.includes("mime=video") ||
      shihabLower.includes("mime=audio")
    );
  }

  function mehediRememberFetchedSlice(shihabRawUrl, mehediContentType) {
    const shihabClean = mehediNormalizeHookMediaUrl(shihabRawUrl);
    if (!shihabClean || !shihabLooksLikeMediaSliceUrl(shihabClean)) return;
    const mehediCt = String(mehediContentType || "").toLowerCase();
    let shihabTrackType = "";
    if (mehediCt.startsWith("audio/") || shihabClean.includes("mime=audio")) {
      shihabTrackType = "audio";
    } else if (mehediCt.startsWith("video/") || shihabClean.includes("mime=video")) {
      shihabTrackType = "video";
    }
    shihabRecentFetchedSlices.unshift({
      shihabUrl: shihabClean,
      mehediTrackType: shihabTrackType,
      shihabAtMs: Date.now()
    });
    if (shihabRecentFetchedSlices.length > 60) {
      shihabRecentFetchedSlices.pop();
    }
  }

  function shihabDecodeXmlEntities(mehediStr) {
    return String(mehediStr || "")
      .replace(/&amp;/g, "&")
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&quot;/g, '"')
      .replace(/&#39;/g, "'")
      .replace(/&#x2F;/gi, "/")
      .replace(/&#x3D;/gi, "=");
  }

  function mehediHeightToStandardLabel(shihabWidth, mehediHeight) {
    const shihabW = Number(shihabWidth || 0);
    const mehediH = Number(mehediHeight || 0);
    if (shihabW <= 0 && mehediH <= 0) return "";
    const shihabShortEdge = shihabW > 0 && mehediH > 0 ? Math.min(shihabW, mehediH) : Math.max(shihabW, mehediH);
    if (shihabShortEdge >= 2000) return "2160p";
    if (shihabShortEdge >= 1350) return "1440p";
    if (shihabShortEdge >= 1000) return "1080p";
    if (shihabShortEdge >= 680) return "720p";
    if (shihabShortEdge >= 510) return "540p";
    if (shihabShortEdge >= 440) return "480p";
    if (shihabShortEdge >= 320) return "360p";
    if (shihabShortEdge >= 210) return "240p";
    return "144p";
  }

  // Parse DASH MPD XML (Instagram video_dash_manifest, Facebook dash_manifest / dash_manifests, Reddit DASHPlaylist.mpd, Vimeo, etc.)
  function shihabParseDashMpdXml(mehediXmlText, shihabGroupKeyHint, mehediTitleHint, shihabShortcodeHint, mehediBaseUrlHint, shihabExtraAltIds) {
    if (!mehediXmlText || typeof mehediXmlText !== "string" || !mehediXmlText.includes("<MPD")) {
      return;
    }
    if (/<ContentProtection\b|cenc:pssh/i.test(mehediXmlText)) {
      // Respect DRM protections completely
      return;
    }

    try {
      const shihabParser = new DOMParser();
      const mehediDoc = shihabParser.parseFromString(mehediXmlText, "application/xml");
      const shihabMpdEl = mehediDoc.querySelector("MPD");
      if (!shihabMpdEl) return;

      let mehediDurationSec = 0;
      const shihabDurAttr = shihabMpdEl.getAttribute("mediaPresentationDuration") || "";
      const mehediDurMatch = shihabDurAttr.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:([\d.]+)S)?/i);
      if (mehediDurMatch) {
        mehediDurationSec =
          (parseFloat(mehediDurMatch[1] || "0") * 3600) +
          (parseFloat(mehediDurMatch[2] || "0") * 60) +
          parseFloat(mehediDurMatch[3] || "0");
      }

      const shihabReps = Array.from(mehediDoc.querySelectorAll("Representation"));
      const mehediDiscoveredList = [];
      let shihabGroupId = shihabGroupKeyHint || "";
      const mehediSharedAltSet = new Set(Array.isArray(shihabExtraAltIds) ? shihabExtraAltIds.filter(Boolean).map(String) : []);
      if (shihabGroupKeyHint) mehediSharedAltSet.add(String(shihabGroupKeyHint));
      if (shihabShortcodeHint) mehediSharedAltSet.add(String(shihabShortcodeHint));

      for (const mehediRep of shihabReps) {
        const shihabParentAdapt = mehediRep.parentElement;
        if (mehediRep.querySelector("ContentProtection") || (shihabParentAdapt && shihabParentAdapt.querySelector("ContentProtection"))) {
          continue;
        }
        const mehediBaseEl = mehediRep.querySelector("BaseURL");
        if (!mehediBaseEl || !mehediBaseEl.textContent) continue;

        const shihabRawBase = shihabDecodeXmlEntities(mehediBaseEl.textContent.trim());
        let mehediResolvedUrl = "";
        try {
          mehediResolvedUrl = new URL(shihabRawBase, mehediBaseUrlHint || location.href).toString();
        } catch (shihabUrlErr) {
          continue;
        }
        const shihabCleanUrl = mehediNormalizeHookMediaUrl(mehediResolvedUrl);
        if (!shihabCleanUrl) continue;

        const mehediMime = (
          mehediRep.getAttribute("mimeType") ||
          (shihabParentAdapt && shihabParentAdapt.getAttribute("mimeType")) ||
          ""
        ).toLowerCase();
        const shihabCodecs = (
          mehediRep.getAttribute("codecs") ||
          (shihabParentAdapt && shihabParentAdapt.getAttribute("codecs")) ||
          ""
        ).toLowerCase();
        const mehediContentTypeAttr = (
          (shihabParentAdapt && shihabParentAdapt.getAttribute("contentType")) ||
          ""
        ).toLowerCase();

        const shihabWidth = parseInt(mehediRep.getAttribute("width") || "0", 10) || 0;
        const mehediHeight = parseInt(mehediRep.getAttribute("height") || "0", 10) || 0;
        const shihabBandwidth = parseInt(mehediRep.getAttribute("bandwidth") || "0", 10) || 0;
        const mehediQualityLabelAttr = mehediRep.getAttribute("FBQualityLabel") || mehediRep.getAttribute("quality_label") || "";

        const shihabIsAudio =
          mehediMime.startsWith("audio/") ||
          mehediContentTypeAttr === "audio" ||
          (shihabWidth === 0 && mehediHeight === 0 && (shihabCodecs.startsWith("mp4a") || shihabCodecs.includes("opus") || shihabCodecs.includes("aac")));

        let mehediEstBytes = -1;
        const shihabClenParam = (() => {
          try { return parseInt(new URL(shihabCleanUrl).searchParams.get("clen") || "-1", 10); } catch (_) { return -1; }
        })();
        if (shihabClenParam > 0) {
          mehediEstBytes = shihabClenParam;
        } else if (shihabBandwidth > 0 && mehediDurationSec > 0.5) {
          mehediEstBytes = Math.round((shihabBandwidth * mehediDurationSec) / 8);
        }

        const shihabEfgInfo = mehediExtractEfgIds(shihabCleanUrl);
        for (const mehediAlt of shihabEfgInfo.shihabAltIds) {
          mehediSharedAltSet.add(mehediAlt);
        }
        if (!shihabGroupId && shihabEfgInfo.mehediPrimaryId) {
          shihabGroupId = shihabEfgInfo.mehediPrimaryId;
        }

        const mehediResLabel = shihabIsAudio
          ? ""
          : (mehediQualityLabelAttr.match(/\d{3,4}p/i)
              ? mehediQualityLabelAttr.match(/\d{3,4}p/i)[0].toLowerCase()
              : mehediHeightToStandardLabel(shihabWidth, mehediHeight));

        mehediDiscoveredList.push({
          shihabUrl: shihabCleanUrl,
          mehediMimeType: mehediMime || (shihabIsAudio ? "audio/mp4" : "video/mp4"),
          shihabCodecs: shihabCodecs,
          mehediTrackRole: shihabIsAudio ? "audio" : "video_only",
          shihabWidth: shihabWidth,
          mehediHeight: mehediHeight,
          shihabResolution: mehediResLabel,
          mehediBandwidth: shihabBandwidth,
          shihabSizeBytes: mehediEstBytes,
          shihabGroupKey: shihabGroupId || shihabShortcodeHint || "",
          mehediShortcode: shihabShortcodeHint || "",
          mehediAltIds: [],
          shihabTitleHint: mehediTitleHint || ""
        });
      }

      if (mehediDiscoveredList.length > 0) {
        const shihabFinalGroup = shihabGroupId || shihabShortcodeHint || "";
        if (shihabFinalGroup) mehediSharedAltSet.add(shihabFinalGroup);
        const mehediFinalAltList = Array.from(mehediSharedAltSet);
        for (const shihabDiscItem of mehediDiscoveredList) {
          if (!shihabDiscItem.shihabGroupKey && shihabFinalGroup) {
            shihabDiscItem.shihabGroupKey = shihabFinalGroup;
          }
          shihabDiscItem.mehediAltIds = mehediFinalAltList;
        }

        const shihabSig = (shihabFinalGroup || mehediDiscoveredList[0].shihabUrl.slice(0, 80)) + ":" + mehediDiscoveredList.length;
        if (!mehediEmittedManifestKeys.has(shihabSig)) {
          mehediEmittedManifestKeys.add(shihabSig);
          shihabPostToContentBridge("mehedi_manifest_streams", {
            shihabStreams: mehediDiscoveredList,
            mehediGroupKey: shihabFinalGroup,
            shihabShortcode: shihabShortcodeHint || "",
            mehediAltIds: mehediFinalAltList,
            mehediTitleHint: mehediTitleHint || ""
          });
        }
      }
    } catch (mehediXmlErr) {
      // Ignore malformed XML
    }
  }

  // Extract YouTube plain unencrypted streamingData formats (never deciphers proprietary ciphers, skips DRM)
  function mehediInspectYouTubePlayerResponse(shihabPlayerObj) {
    if (!shihabPlayerObj || typeof shihabPlayerObj !== "object") return;
    const mehediStreaming = shihabPlayerObj.streamingData;
    const shihabDetails = shihabPlayerObj.videoDetails || {};
    if (!mehediStreaming || typeof mehediStreaming !== "object") return;

    const mehediVideoId = String(shihabDetails.videoId || "");
    const shihabVideoTitle = String(shihabDetails.title || "");
    const mehediAuthor = String(shihabDetails.author || "");
    const shihabTitleHint = shihabVideoTitle || (mehediAuthor ? `${mehediAuthor} - YouTube Video` : "");

    const mehediOut = [];
    const shihabFormatBuckets = [
      { mehediList: mehediStreaming.formats, shihabDefaultRole: "progressive" },
      { mehediList: mehediStreaming.adaptiveFormats, shihabDefaultRole: "adaptive" }
    ];

    for (const shihabBucket of shihabFormatBuckets) {
      if (!Array.isArray(shihabBucket.mehediList)) continue;
      for (const mehediFmt of shihabBucket.mehediList) {
        if (!mehediFmt || typeof mehediFmt.url !== "string" || !mehediFmt.url.startsWith("http")) {
          // Skip ciphered or DRM-protected entries cleanly
          continue;
        }
        if (mehediFmt.drmFamilies || mehediFmt.type === "FORMAT_STREAM_TYPE_OTF") {
          continue;
        }
        const shihabCleanUrl = mehediNormalizeHookMediaUrl(mehediFmt.url);
        if (!shihabCleanUrl) continue;

        const mehediMimeRaw = String(mehediFmt.mimeType || "").toLowerCase();
        const shihabMimeBase = mehediMimeRaw.split(";")[0].trim();
        const mehediIsAudio = shihabMimeBase.startsWith("audio/");
        const shihabRole = shihabBucket.shihabDefaultRole === "progressive"
          ? "progressive"
          : (mehediIsAudio ? "audio" : "video_only");

        const mehediW = Number(mehediFmt.width || 0);
        const shihabH = Number(mehediFmt.height || 0);
        let mehediRes = "";
        if (!mehediIsAudio) {
          const shihabQMatch = String(mehediFmt.qualityLabel || "").match(/(\d{3,4}p)/i);
          mehediRes = shihabQMatch ? shihabQMatch[1].toLowerCase() : mehediHeightToStandardLabel(mehediW, shihabH);
        }

        let shihabBytes = parseInt(String(mehediFmt.contentLength || "-1"), 10);
        if (!(shihabBytes > 0) && mehediFmt.bitrate && mehediFmt.approxDurationMs) {
          shihabBytes = Math.round((Number(mehediFmt.bitrate) * (Number(mehediFmt.approxDurationMs) / 1000)) / 8);
        }

        mehediOut.push({
          shihabUrl: shihabCleanUrl,
          mehediMimeType: shihabMimeBase || (mehediIsAudio ? "audio/webm" : "video/mp4"),
          shihabCodecs: mehediMimeRaw,
          mehediTrackRole: shihabRole,
          shihabWidth: mehediW,
          mehediHeight: shihabH,
          shihabResolution: mehediRes,
          mehediBandwidth: Number(mehediFmt.bitrate || 0),
          shihabSizeBytes: shihabBytes > 0 ? shihabBytes : -1,
          shihabGroupKey: mehediVideoId ? `yt:${mehediVideoId}` : "",
          mehediShortcode: mehediVideoId,
          mehediAltIds: mehediVideoId ? [mehediVideoId, `yt:${mehediVideoId}`] : [],
          shihabTitleHint: shihabTitleHint
        });
      }
    }

    if (mehediOut.length > 0) {
      shihabPostToContentBridge("mehedi_manifest_streams", {
        shihabStreams: mehediOut,
        mehediGroupKey: mehediVideoId ? `yt:${mehediVideoId}` : "",
        shihabShortcode: mehediVideoId,
        mehediAltIds: mehediVideoId ? [mehediVideoId, `yt:${mehediVideoId}`] : [],
        mehediTitleHint: shihabTitleHint
      });
    }
  }

  function mehediStripHookTranslationSuffix(shihabRawText) {
    if (!shihabRawText) return "";
    return String(shihabRawText)
      .replace(/\s*(?:Hide translation|See translation|See original|Translated from [A-Za-z ]+)\s*$/gi, "")
      .replace(/\s+/g, " ")
      .trim();
  }

  // Walk JSON payloads from Instagram, Facebook, X/Twitter, TikTok, Reddit, Vimeo for unencrypted video manifests & captions
  function shihabWalkJsonForMediaMetadata(mehediRootJson) {
    if (!mehediRootJson || typeof mehediRootJson !== "object") return;

    if (mehediRootJson.streamingData && mehediRootJson.videoDetails) {
      mehediInspectYouTubePlayerResponse(mehediRootJson);
    }

    const shihabStack = [{
      shihabNode: mehediRootJson,
      mehediParentCtx: {
        shihabParentId: "",
        mehediParentCode: "",
        shihabParentUser: "",
        mehediParentCaption: ""
      }
    }];
    let mehediVisited = 0;

    while (shihabStack.length > 0 && mehediVisited < 4500) {
      const mehediFrame = shihabStack.pop();
      mehediVisited++;
      const shihabNode = mehediFrame && mehediFrame.shihabNode;
      const shihabInherited = mehediFrame ? mehediFrame.mehediParentCtx : {};
      if (!shihabNode || typeof shihabNode !== "object") continue;

      if (Array.isArray(shihabNode)) {
        for (let mehediI = shihabNode.length - 1; mehediI >= 0; mehediI--) {
          if (shihabNode[mehediI] && typeof shihabNode[mehediI] === "object") {
            shihabStack.push({
              shihabNode: shihabNode[mehediI],
              mehediParentCtx: shihabInherited
            });
          }
        }
        continue;
      }

      // Extract Instagram / Facebook / Threads / Comet media objects + inherit parent context
      const mehediLocalCode = String(shihabNode.code || shihabNode.shortcode || "");
      const shihabLocalPk = String(shihabNode.pk || shihabNode.video_id || shihabNode.id || "");
      const mehediShortcode = mehediLocalCode || shihabInherited.mehediParentCode || "";
      const shihabAssetPk = (/^\d{5,}$/.test(shihabLocalPk) || (shihabLocalPk && !shihabInherited.shihabParentId))
        ? shihabLocalPk
        : (shihabInherited.shihabParentId || shihabLocalPk);

      let mehediUsername = "";
      if (shihabNode.user && typeof shihabNode.user.username === "string") {
        mehediUsername = shihabNode.user.username;
      } else if (shihabNode.owner && typeof shihabNode.owner.username === "string") {
        mehediUsername = shihabNode.owner.username;
      } else if (shihabNode.owner && typeof shihabNode.owner.name === "string") {
        mehediUsername = shihabNode.owner.name;
      } else if (Array.isArray(shihabNode.actors) && shihabNode.actors[0] && typeof shihabNode.actors[0].name === "string") {
        mehediUsername = shihabNode.actors[0].name;
      } else {
        mehediUsername = shihabInherited.shihabParentUser || "";
      }

      let shihabCaptionTxt = "";
      if (shihabNode.caption && typeof shihabNode.caption.text === "string") {
        shihabCaptionTxt = shihabNode.caption.text;
      } else if (shihabNode.edge_media_to_caption && Array.isArray(shihabNode.edge_media_to_caption.edges) && shihabNode.edge_media_to_caption.edges[0]) {
        const mehediCapNode = shihabNode.edge_media_to_caption.edges[0].node;
        if (mehediCapNode && typeof mehediCapNode.text === "string") shihabCaptionTxt = mehediCapNode.text;
      } else if (shihabNode.message && typeof shihabNode.message.text === "string") {
        shihabCaptionTxt = shihabNode.message.text;
      } else if (shihabNode.savable_description && typeof shihabNode.savable_description.text === "string") {
        shihabCaptionTxt = shihabNode.savable_description.text;
      } else if (shihabNode.creation_story && shihabNode.creation_story.message && typeof shihabNode.creation_story.message.text === "string") {
        shihabCaptionTxt = shihabNode.creation_story.message.text;
      } else if (typeof shihabNode.full_text === "string") {
        shihabCaptionTxt = shihabNode.full_text;
      } else if (typeof shihabNode.title === "string" && shihabNode.title.length > 2) {
        shihabCaptionTxt = shihabNode.title;
      } else {
        shihabCaptionTxt = shihabInherited.mehediParentCaption || "";
      }
      shihabCaptionTxt = mehediStripHookTranslationSuffix(shihabCaptionTxt);

      const mehediCombinedTitle = mehediUsername && shihabCaptionTxt
        ? `${mehediUsername} - ${shihabCaptionTxt.slice(0, 90)}`
        : (shihabCaptionTxt ? shihabCaptionTxt.slice(0, 100) : (mehediUsername ? `${mehediUsername} - Video` : ""));

      const shihabNodeAltIds = Array.from(new Set(
        [shihabLocalPk, shihabAssetPk, mehediLocalCode, mehediShortcode, shihabInherited.shihabParentId, shihabInherited.mehediParentCode]
          .filter((mehediV) => mehediV && String(mehediV).length >= 4)
          .map(String)
      ));

      // 1) Instagram / Facebook DASH XML manifest string + Facebook Comet dash_manifests array
      const shihabDashXmlList = [];
      if (typeof shihabNode.video_dash_manifest === "string" && shihabNode.video_dash_manifest.includes("<MPD")) {
        shihabDashXmlList.push(shihabNode.video_dash_manifest);
      }
      if (typeof shihabNode.dash_manifest === "string" && shihabNode.dash_manifest.includes("<MPD")) {
        shihabDashXmlList.push(shihabNode.dash_manifest);
      }
      if (shihabNode.dash_info && typeof shihabNode.dash_info.video_dash_manifest === "string" && shihabNode.dash_info.video_dash_manifest.includes("<MPD")) {
        shihabDashXmlList.push(shihabNode.dash_info.video_dash_manifest);
      }
      if (Array.isArray(shihabNode.dash_manifests)) {
        for (const mehediDmEntry of shihabNode.dash_manifests) {
          if (mehediDmEntry && typeof mehediDmEntry.manifest_xml === "string" && mehediDmEntry.manifest_xml.includes("<MPD")) {
            shihabDashXmlList.push(mehediDmEntry.manifest_xml);
          }
        }
      }
      for (const mehediXmlItem of shihabDashXmlList) {
        shihabParseDashMpdXml(mehediXmlItem, shihabAssetPk, mehediCombinedTitle, mehediShortcode, location.href, shihabNodeAltIds);
      }

      // 2) Instagram progressive video_versions array (already muxed video+audio!)
      if (Array.isArray(shihabNode.video_versions) && shihabNode.video_versions.length > 0) {
        const mehediProgList = [];
        let shihabInferredGroup = shihabAssetPk;
        const mehediSharedIgAlts = new Set(shihabNodeAltIds);
        for (const mehediVer of shihabNode.video_versions) {
          if (!mehediVer || typeof mehediVer.url !== "string") continue;
          const shihabClean = mehediNormalizeHookMediaUrl(mehediVer.url);
          if (!shihabClean) continue;
          const mehediEfgIds = mehediExtractEfgIds(shihabClean);
          for (const shihabAltId of mehediEfgIds.shihabAltIds) {
            mehediSharedIgAlts.add(shihabAltId);
          }
          if (!shihabInferredGroup && mehediEfgIds.mehediPrimaryId) {
            shihabInferredGroup = mehediEfgIds.mehediPrimaryId;
          }
          const shihabW = Number(mehediVer.width || 0);
          const mehediH = Number(mehediVer.height || 0);
          mehediProgList.push({
            shihabUrl: shihabClean,
            mehediMimeType: "video/mp4",
            shihabCodecs: "avc1,mp4a",
            mehediTrackRole: "progressive",
            shihabWidth: shihabW,
            mehediHeight: mehediH,
            shihabResolution: mehediHeightToStandardLabel(shihabW, mehediH),
            mehediBandwidth: 0,
            shihabSizeBytes: -1,
            shihabGroupKey: shihabInferredGroup || mehediShortcode || "",
            mehediShortcode: mehediShortcode,
            mehediAltIds: [],
            shihabTitleHint: mehediCombinedTitle
          });
        }
        if (mehediProgList.length > 0) {
          const shihabFinalIgGroup = shihabInferredGroup || mehediShortcode || "";
          if (shihabFinalIgGroup) mehediSharedIgAlts.add(shihabFinalIgGroup);
          const mehediIgAltArray = Array.from(mehediSharedIgAlts);
          for (const shihabProgEntry of mehediProgList) {
            if (!shihabProgEntry.shihabGroupKey) shihabProgEntry.shihabGroupKey = shihabFinalIgGroup;
            shihabProgEntry.mehediAltIds = mehediIgAltArray;
          }
          shihabPostToContentBridge("mehedi_manifest_streams", {
            shihabStreams: mehediProgList,
            mehediGroupKey: shihabFinalIgGroup,
            shihabShortcode: mehediShortcode,
            mehediAltIds: mehediIgAltArray,
            mehediTitleHint: mehediCombinedTitle
          });
        }
      }

      // 3) Facebook progressive browser_native_hd_url / playable_url + Facebook Comet progressive_urls array
      const mehediFbProgressiveFields = [
        { shihabField: "browser_native_hd_url", mehediRes: "720p" },
        { shihabField: "playable_url_quality_hd", mehediRes: "720p" },
        { shihabField: "hd_src_no_ratelimit", mehediRes: "720p" },
        { shihabField: "hd_src", mehediRes: "720p" },
        { shihabField: "browser_native_sd_url", mehediRes: "480p" },
        { shihabField: "playable_url", mehediRes: "480p" },
        { shihabField: "sd_src_no_ratelimit", mehediRes: "480p" },
        { shihabField: "sd_src", mehediRes: "480p" }
      ];
      const shihabFbFound = [];
      let mehediFbResolvedGroup = shihabAssetPk;
      const shihabFbAltSet = new Set(shihabNodeAltIds);

      for (const mehediFbSpec of mehediFbProgressiveFields) {
        const shihabVal = shihabNode[mehediFbSpec.shihabField];
        if (typeof shihabVal === "string" && shihabVal.startsWith("http")) {
          const mehediCleanFb = mehediNormalizeHookMediaUrl(shihabVal);
          if (mehediCleanFb) {
            const shihabFbEfg = mehediExtractEfgIds(mehediCleanFb);
            for (const mehediAlt of shihabFbEfg.shihabAltIds) shihabFbAltSet.add(mehediAlt);
            if (!mehediFbResolvedGroup && shihabFbEfg.mehediPrimaryId) {
              mehediFbResolvedGroup = shihabFbEfg.mehediPrimaryId;
            }
            shihabFbFound.push({
              shihabUrl: mehediCleanFb,
              mehediMimeType: "video/mp4",
              shihabCodecs: "avc1,mp4a",
              mehediTrackRole: "progressive",
              shihabWidth: 0,
              mehediHeight: 0,
              shihabResolution: mehediFbSpec.mehediRes,
              mehediBandwidth: 0,
              shihabSizeBytes: -1,
              shihabGroupKey: mehediFbResolvedGroup || mehediShortcode || "",
              mehediShortcode: mehediShortcode || mehediFbResolvedGroup,
              mehediAltIds: [],
              shihabTitleHint: mehediCombinedTitle
            });
          }
        }
      }

      if (Array.isArray(shihabNode.progressive_urls)) {
        for (const mehediProgObj of shihabNode.progressive_urls) {
          const shihabProgRaw = mehediProgObj && typeof mehediProgObj.progressive_url === "string" ? mehediProgObj.progressive_url : "";
          if (!shihabProgRaw.startsWith("http")) continue;
          const mehediCleanProg = mehediNormalizeHookMediaUrl(shihabProgRaw);
          if (!mehediCleanProg) continue;
          const shihabProgEfg = mehediExtractEfgIds(mehediCleanProg);
          for (const mehediAlt of shihabProgEfg.shihabAltIds) shihabFbAltSet.add(mehediAlt);
          if (!mehediFbResolvedGroup && shihabProgEfg.mehediPrimaryId) {
            mehediFbResolvedGroup = shihabProgEfg.mehediPrimaryId;
          }
          const mehediQualStr = String((mehediProgObj.metadata && mehediProgObj.metadata.quality) || "").toUpperCase();
          shihabFbFound.push({
            shihabUrl: mehediCleanProg,
            mehediMimeType: "video/mp4",
            shihabCodecs: "avc1,mp4a",
            mehediTrackRole: "progressive",
            shihabWidth: 0,
            mehediHeight: 0,
            shihabResolution: mehediQualStr === "HD" ? "720p" : "480p",
            mehediBandwidth: 0,
            shihabSizeBytes: -1,
            shihabGroupKey: mehediFbResolvedGroup || mehediShortcode || "",
            mehediShortcode: mehediShortcode || mehediFbResolvedGroup,
            mehediAltIds: [],
            shihabTitleHint: mehediCombinedTitle
          });
        }
      }

      if (shihabFbFound.length > 0) {
        const shihabFinalFbGroup = mehediFbResolvedGroup || mehediShortcode || "";
        if (shihabFinalFbGroup) shihabFbAltSet.add(shihabFinalFbGroup);
        const mehediFbAltArray = Array.from(shihabFbAltSet);
        for (const shihabFbItem of shihabFbFound) {
          if (!shihabFbItem.shihabGroupKey) shihabFbItem.shihabGroupKey = shihabFinalFbGroup;
          shihabFbItem.mehediAltIds = mehediFbAltArray;
        }
        shihabPostToContentBridge("mehedi_manifest_streams", {
          shihabStreams: shihabFbFound,
          mehediGroupKey: shihabFinalFbGroup,
          shihabShortcode: mehediShortcode || shihabFinalFbGroup,
          mehediAltIds: mehediFbAltArray,
          mehediTitleHint: mehediCombinedTitle
        });
      }

      // 4) X / Twitter video_info.variants
      if (shihabNode.video_info && Array.isArray(shihabNode.video_info.variants)) {
        const mehediTwList = [];
        for (const shihabVar of shihabNode.video_info.variants) {
          if (!shihabVar || typeof shihabVar.url !== "string" || !shihabVar.url.startsWith("http")) continue;
          const mehediCt = String(shihabVar.content_type || "").toLowerCase();
          if (mehediCt.includes("mpegurl")) continue;
          const shihabDimMatch = shihabVar.url.match(/\/(\d{2,4})x(\d{2,4})\//);
          const mehediW = shihabDimMatch ? parseInt(shihabDimMatch[1], 10) : 0;
          const shihabH = shihabDimMatch ? parseInt(shihabDimMatch[2], 10) : 0;
          mehediTwList.push({
            shihabUrl: shihabVar.url,
            mehediMimeType: mehediCt || "video/mp4",
            shihabCodecs: "avc1,mp4a",
            mehediTrackRole: "progressive",
            shihabWidth: mehediW,
            mehediHeight: shihabH,
            shihabResolution: mehediHeightToStandardLabel(mehediW, mehediH),
            mehediBandwidth: Number(shihabVar.bitrate || 0),
            shihabSizeBytes: -1,
            shihabGroupKey: shihabAssetPk ? `tw:${shihabAssetPk}` : "",
            mehediShortcode: shihabAssetPk,
            mehediAltIds: shihabNodeAltIds,
            shihabTitleHint: mehediCombinedTitle
          });
        }
        if (mehediTwList.length > 0) {
          shihabPostToContentBridge("mehedi_manifest_streams", {
            shihabStreams: mehediTwList,
            mehediGroupKey: shihabAssetPk ? `tw:${shihabAssetPk}` : "",
            shihabShortcode: shihabAssetPk,
            mehediAltIds: shihabNodeAltIds,
            mehediTitleHint: mehediCombinedTitle
          });
        }
      }

      const mehediNextParentCtx = {
        shihabParentId: shihabAssetPk || shihabInherited.shihabParentId || "",
        mehediParentCode: mehediShortcode || shihabInherited.mehediParentCode || "",
        shihabParentUser: mehediUsername || shihabInherited.shihabParentUser || "",
        mehediParentCaption: shihabCaptionTxt || shihabInherited.mehediParentCaption || ""
      };

      const shihabKeys = Object.keys(shihabNode);
      for (let mehediK = 0; mehediK < shihabKeys.length; mehediK++) {
        const shihabChild = shihabNode[shihabKeys[mehediK]];
        if (shihabChild && typeof shihabChild === "object") {
          shihabStack.push({
            shihabNode: shihabChild,
            mehediParentCtx: mehediNextParentCtx
          });
        }
      }
    }
  }

  function mehediInspectTextBodyIfManifest(shihabText, mehediSourceUrl, shihabContentType) {
    if (!shihabText || shihabText.length < 20 || shihabText.length > 5 * 1024 * 1024) return;
    const mehediCt = String(shihabContentType || "").toLowerCase();
    const shihabTrim = shihabText.trimStart();

    if (shihabTrim.startsWith("<") && shihabText.includes("<MPD")) {
      shihabParseDashMpdXml(shihabText, "", "", "", mehediSourceUrl || location.href, []);
      return;
    }

    const mehediCleanedJson = shihabTrim.startsWith("for (;;);") ? shihabTrim.slice(9).trimStart() : shihabTrim;
    if ((mehediCleanedJson.startsWith("{") || mehediCleanedJson.startsWith("[")) &&
        (mehediCt.includes("json") ||
         mehediCleanedJson.includes("video_dash_manifest") ||
         mehediCleanedJson.includes("dash_manifests") ||
         mehediCleanedJson.includes("manifest_xml") ||
         mehediCleanedJson.includes("progressive_urls") ||
         mehediCleanedJson.includes("video_versions") ||
         mehediCleanedJson.includes("streamingData") ||
         mehediCleanedJson.includes("browser_native_hd_url") ||
         mehediCleanedJson.includes("video_info"))) {
      try {
        const shihabParsed = JSON.parse(mehediCleanedJson);
        shihabWalkJsonForMediaMetadata(shihabParsed);
      } catch (mehediJsonErr) {
        // Ignore non-JSON payloads
      }
    }
  }

  // 1. Hook URL.createObjectURL & MediaSource / SourceBuffer so blob: <video> elements map 1-to-1 to their exact byte-matched streams
  try {
    const shihabOrigCreateObjUrl = URL.createObjectURL;
    if (typeof shihabOrigCreateObjUrl === "function") {
      URL.createObjectURL = function (mehediObj) {
        const shihabBlobUrl = shihabOrigCreateObjUrl.apply(this, arguments);
        try {
          if (typeof MediaSource !== "undefined" && mehediObj instanceof MediaSource) {
            shihabMediaSourceToBlobUrl.set(mehediObj, shihabBlobUrl);
          }
        } catch (_) {}
        return shihabBlobUrl;
      };
    }

    if (typeof MediaSource !== "undefined" && MediaSource.prototype && typeof MediaSource.prototype.addSourceBuffer === "function") {
      const mehediOrigAddSb = MediaSource.prototype.addSourceBuffer;
      MediaSource.prototype.addSourceBuffer = function (shihabMimeCodec) {
        const mehediSb = mehediOrigAddSb.apply(this, arguments);
        try {
          const shihabBlobUrl = shihabMediaSourceToBlobUrl.get(this) || "";
          const mehediMimeStr = String(shihabMimeCodec || "").toLowerCase();
          const shihabTrackKind = mehediMimeStr.startsWith("audio/") ? "audio" : "video";
          mehediSourceBufferMeta.set(mehediSb, {
            mehediMediaSource: this,
            shihabBlobUrl: shihabBlobUrl,
            mehediMimeCodec: mehediMimeStr,
            shihabTrackKind: shihabTrackKind,
            mehediReportedUrls: new Set(),
            shihabExactReportedUrls: new Set()
          });

          const shihabOrigAppend = mehediSb.appendBuffer;
          if (typeof shihabOrigAppend === "function" && !mehediSb.__shihabHookedAppend) {
            mehediSb.__shihabHookedAppend = true;
            mehediSb.appendBuffer = function (mehediBuffer) {
              try {
                const shihabMeta = mehediSourceBufferMeta.get(this);
                if (shihabMeta) {
                  const mehediLiveBlob = shihabMeta.shihabBlobUrl || shihabMediaSourceToBlobUrl.get(shihabMeta.mehediMediaSource) || "";
                  if (mehediLiveBlob) {
                    shihabMeta.shihabBlobUrl = mehediLiveBlob;

                    // Priority A: Exact FNV-1a media chunk byte fingerprint match (eliminates preloaded Reel race conditions!)
                    let shihabExactMatchedUrl = "";
                    const mehediBufSigs = shihabComputeMediaChunkSignatures(mehediBuffer);
                    for (const shihabSig of mehediBufSigs) {
                      const mehediHit = shihabChunkHashToStreamUrl.get(shihabSig);
                      if (mehediHit) {
                        shihabExactMatchedUrl = mehediHit;
                        break;
                      }
                    }

                    if (shihabExactMatchedUrl) {
                      if (!shihabMeta.shihabExactReportedUrls.has(shihabExactMatchedUrl)) {
                        shihabMeta.shihabExactReportedUrls.add(shihabExactMatchedUrl);
                        shihabMeta.mehediReportedUrls.add(shihabExactMatchedUrl);
                        shihabPostToContentBridge("shihab_mse_blob_binding", {
                          shihabBlobUrl: mehediLiveBlob,
                          mehediStreamUrl: shihabExactMatchedUrl,
                          shihabTrackKind: shihabMeta.shihabTrackKind,
                          mehediMimeCodec: shihabMeta.mehediMimeCodec,
                          mehediExactByteMatch: true
                        });
                      }
                    } else if (shihabMeta.shihabExactReportedUrls.size === 0) {
                      // Priority B: Fallback time-window match only when no exact byte fingerprint was seen yet
                      const shihabNow = Date.now();
                      const mehediCandidate = shihabRecentFetchedSlices.find(
                        (shihabItem) =>
                          shihabNow - shihabItem.shihabAtMs < 6000 &&
                          (!shihabItem.mehediTrackType || shihabItem.mehediTrackType === shihabMeta.shihabTrackKind)
                      );
                      if (mehediCandidate && !shihabMeta.mehediReportedUrls.has(mehediCandidate.shihabUrl)) {
                        shihabMeta.mehediReportedUrls.add(mehediCandidate.shihabUrl);
                        shihabPostToContentBridge("shihab_mse_blob_binding", {
                          shihabBlobUrl: mehediLiveBlob,
                          mehediStreamUrl: mehediCandidate.shihabUrl,
                          shihabTrackKind: shihabMeta.shihabTrackKind,
                          mehediMimeCodec: shihabMeta.mehediMimeCodec,
                          mehediExactByteMatch: false
                        });
                      }
                    }
                  }
                }
              } catch (_) {}
              return shihabOrigAppend.apply(this, arguments);
            };
          }
        } catch (_) {}
        return mehediSb;
      };
    }
  } catch (shihabMseErr) {
    // Ignore MSE hook errors
  }

  // 2. Hook window.fetch non-destructively (including 1st-chunk FNV-1a media fingerprinting)
  try {
    const mehediOrigFetch = window.fetch;
    if (typeof mehediOrigFetch === "function") {
      window.fetch = function (shihabInput, mehediInit) {
        let shihabReqUrl = "";
        try {
          shihabReqUrl = typeof shihabInput === "string"
            ? shihabInput
            : (shihabInput && typeof shihabInput.url === "string" ? shihabInput.url : "");
        } catch (_) {}

        const mehediPromise = mehediOrigFetch.apply(this, arguments);
        mehediPromise.then((shihabResp) => {
          try {
            if (!shihabResp || (!shihabResp.ok && shihabResp.status !== 206)) return;
            const mehediFinalUrl = shihabResp.url || shihabReqUrl;
            const shihabCt = (shihabResp.headers && shihabResp.headers.get("content-type")) || "";
            if (shihabLooksLikeMediaSliceUrl(mehediFinalUrl)) {
              mehediRememberFetchedSlice(mehediFinalUrl, shihabCt);
              try {
                const mehediMediaClone = shihabResp.clone();
                if (mehediMediaClone.body && typeof mehediMediaClone.body.getReader === "function") {
                  const shihabReader = mehediMediaClone.body.getReader();
                  shihabReader.read().then((mehediChunkRes) => {
                    if (mehediChunkRes && mehediChunkRes.value && mehediChunkRes.value.byteLength >= 28) {
                      mehediIndexChunkBytesForUrl(mehediFinalUrl, mehediChunkRes.value);
                    }
                    try { shihabReader.cancel().catch(() => {}); } catch (_) {}
                  }).catch(() => {
                    try { shihabReader.cancel().catch(() => {}); } catch (_) {}
                  });
                }
              } catch (_) {}
            }
            const mehediLowerCt = shihabCt.toLowerCase();
            if (
              mehediLowerCt.includes("json") ||
              mehediLowerCt.includes("dash+xml") ||
              mehediLowerCt.includes("xml") ||
              /\/(graphql|api\/v1\/|youtubei\/v1\/player)/i.test(mehediFinalUrl) ||
              /\.mpd(\?|$)/i.test(mehediFinalUrl)
            ) {
              shihabResp.clone().text().then((mehediBodyText) => {
                mehediInspectTextBodyIfManifest(mehediBodyText, mehediFinalUrl, mehediLowerCt);
              }).catch(() => {});
            }
          } catch (_) {}
        }).catch(() => {});

        return mehediPromise;
      };
    }
  } catch (shihabFetchErr) {
    // Ignore fetch hook errors
  }

  // 3. Hook XMLHttpRequest non-destructively (including arraybuffer media slice fingerprinting)
  try {
    const shihabOrigXhrOpen = XMLHttpRequest.prototype.open;
    const mehediOrigXhrSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (shihabMethod, mehediUrl) {
      try {
        this.__shihabReqUrl = typeof mehediUrl === "string" ? mehediUrl : String(mehediUrl || "");
      } catch (_) {}
      return shihabOrigXhrOpen.apply(this, arguments);
    };
    XMLHttpRequest.prototype.send = function () {
      try {
        this.addEventListener("load", function () {
          try {
            const mehediFinalUrl = this.responseURL || this.__shihabReqUrl || "";
            const shihabCt = (this.getResponseHeader && this.getResponseHeader("content-type")) || "";
            if (shihabLooksLikeMediaSliceUrl(mehediFinalUrl)) {
              mehediRememberFetchedSlice(mehediFinalUrl, shihabCt);
              if (this.responseType === "arraybuffer" && this.response instanceof ArrayBuffer && this.response.byteLength >= 28) {
                mehediIndexChunkBytesForUrl(mehediFinalUrl, new Uint8Array(this.response, 0, Math.min(160, this.response.byteLength)));
              }
            }
            if ((this.responseType === "" || this.responseType === "text") && typeof this.responseText === "string") {
              mehediInspectTextBodyIfManifest(this.responseText, mehediFinalUrl, shihabCt);
            } else if (this.responseType === "json" && this.response && typeof this.response === "object") {
              shihabWalkJsonForMediaMetadata(this.response);
            }
          } catch (_) {}
        });
      } catch (_) {}
      return mehediOrigXhrSend.apply(this, arguments);
    };
  } catch (mehediXhrErr) {
    // Ignore XHR hook errors
  }

  // 4. Scan SSR embedded <script type="application/json"> (Instagram & Facebook Comet initial feed/Reel payloads) + YouTube ytInitialPlayerResponse
  function shihabScanInitialDomPayloads() {
    try {
      if (window.ytInitialPlayerResponse && typeof window.ytInitialPlayerResponse === "object") {
        mehediInspectYouTubePlayerResponse(window.ytInitialPlayerResponse);
      }
      const mehediJsonScripts = document.querySelectorAll('script[type="application/json"]');
      for (const shihabScript of mehediJsonScripts) {
        if (shihabScript.__mehediScanned) continue;
        shihabScript.__mehediScanned = true;
        const mehediRaw = shihabScript.textContent || "";
        if (
          mehediRaw.length > 40 &&
          mehediRaw.length < 5 * 1024 * 1024 &&
          (mehediRaw.includes("video_dash_manifest") ||
           mehediRaw.includes("dash_manifests") ||
           mehediRaw.includes("manifest_xml") ||
           mehediRaw.includes("progressive_urls") ||
           mehediRaw.includes("video_versions") ||
           mehediRaw.includes("browser_native_hd_url") ||
           mehediRaw.includes("playable_url"))
        ) {
          mehediInspectTextBodyIfManifest(mehediRaw, location.href, "application/json");
        }
      }
    } catch (_) {}
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", shihabScanInitialDomPayloads, { once: true });
  } else {
    shihabScanInitialDomPayloads();
  }
  window.addEventListener("yt-navigate-finish", () => {
    mehediEmittedManifestKeys.clear();
    setTimeout(shihabScanInitialDomPayloads, 250);
  });
  window.addEventListener("message", (shihabEv) => {
    try {
      if (shihabEv.source !== window || !shihabEv.data || shihabEv.data.__shihabMehediContentPing !== true) {
        return;
      }
      mehediEmittedManifestKeys.clear();
      const mehediScripts = document.querySelectorAll('script[type="application/json"]');
      for (const shihabSc of mehediScripts) {
        shihabSc.__mehediScanned = false;
      }
      shihabScanInitialDomPayloads();
    } catch (_) {}
  });
})();
