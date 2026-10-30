using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ShihabMehediIDM
{
    public enum ShihabDownloadStatus
    {
        ShihabQueued = 0,
        ShihabConnecting = 1,
        ShihabDownloading = 2,
        ShihabPaused = 3,
        ShihabRebuilding = 4,
        ShihabCompleted = 5,
        ShihabError = 6,
        ShihabWaitingRefresh = 7
    }

    [DataContract]
    public class MehediSegmentChunk
    {
        [DataMember] public int ShihabSegmentIndex { get; set; }
        [DataMember] public long MehediStartByte { get; set; }
        [DataMember] public long ShihabEndByte { get; set; }
        [DataMember] public long MehediDownloadedBytes { get; set; }
        [DataMember] public string ShihabSegmentState { get; set; }
        [DataMember] public string MehediPartFilePath { get; set; }

        [IgnoreDataMember]
        public bool ShihabIsActiveWorker { get; set; }

        public long GetShihabTotalSpan()
        {
            if (ShihabEndByte < 0)
            {
                return -1;
            }
            long mehediSpan = (ShihabEndByte - MehediStartByte) + 1L;
            return mehediSpan < 0 ? 0 : mehediSpan;
        }

        public long GetMehediRemainingBytes()
        {
            if (ShihabEndByte < 0)
            {
                return -1;
            }
            long shihabTotal = GetShihabTotalSpan();
            long mehediRem = shihabTotal - MehediDownloadedBytes;
            return mehediRem < 0 ? 0 : mehediRem;
        }
    }

    [DataContract]
    public class ShihabDownloadItem
    {
        [DataMember] public string ShihabId { get; set; }
        [DataMember] public string MehediUrl { get; set; }
        [DataMember] public string ShihabFileName { get; set; }
        [DataMember] public string MehediSaveDirectory { get; set; }
        [DataMember] public string ShihabCategoryName { get; set; }
        [DataMember] public string MehediQueueName { get; set; }
        [DataMember] public string ShihabDescription { get; set; }
        [DataMember] public string MehediReferer { get; set; }
        [DataMember] public string ShihabCookies { get; set; }
        [DataMember] public string MehediUserAgent { get; set; }
        [DataMember] public string ShihabAuthUser { get; set; }
        [DataMember] public string MehediAuthPassword { get; set; }
        [DataMember] public long ShihabTotalBytes { get; set; }
        [DataMember] public long MehediDownloadedBytes { get; set; }
        [DataMember] public bool ShihabResumeSupported { get; set; }
        [DataMember] public int MehediStatusCode { get; set; }
        [DataMember] public string ShihabStatusText { get; set; }
        [DataMember] public string MehediLastTryDate { get; set; }
        [DataMember] public string ShihabDateAdded { get; set; }
        [DataMember] public int MehediMaxConnections { get; set; }
        [DataMember] public bool ShihabUseItemSpeedLimit { get; set; }
        [DataMember] public int MehediItemSpeedLimitKb { get; set; }
        [DataMember] public bool ShihabOpenWhenComplete { get; set; }
        [DataMember] public bool MehediExitAppOnComplete { get; set; }
        [DataMember] public bool ShihabShowCompleteDialog { get; set; }
        [DataMember] public string MehediSha256Hex { get; set; }
        [DataMember] public string ShihabServerLastModified { get; set; }
        [DataMember] public bool MehediIsHlsStream { get; set; }
        [DataMember] public int ShihabRetryCount { get; set; }
        [DataMember] public string MehediOriginHeader { get; set; }
        [DataMember] public bool MehediOmitCookies { get; set; }
        [DataMember] public string MehediAudioCompanionUrl { get; set; }
        [DataMember] public long ShihabAudioCompanionBytes { get; set; }
        [DataMember] public int ShihabSourceTabId { get; set; }
        [DataMember] public string ShihabQualityLabel { get; set; }
        [DataMember] public List<MehediSegmentChunk> MehediSegments { get; set; }

        [IgnoreDataMember] public double ShihabCurrentSpeedBps { get; set; }
        [IgnoreDataMember] public long MehediEtaSeconds { get; set; }

        public ShihabDownloadItem()
        {
            ShihabId = Guid.NewGuid().ToString("N");
            MehediUrl = "";
            ShihabFileName = "download.bin";
            MehediSaveDirectory = "";
            ShihabCategoryName = "General";
            MehediQueueName = "";
            ShihabDescription = "";
            MehediReferer = "";
            ShihabCookies = "";
            MehediUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            ShihabAuthUser = "";
            MehediAuthPassword = "";
            ShihabTotalBytes = -1;
            MehediDownloadedBytes = 0;
            ShihabResumeSupported = false;
            MehediStatusCode = (int)ShihabDownloadStatus.ShihabPaused;
            ShihabStatusText = "Paused";
            MehediLastTryDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ShihabDateAdded = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            MehediMaxConnections = 8;
            ShihabUseItemSpeedLimit = false;
            MehediItemSpeedLimitKb = 512;
            ShihabOpenWhenComplete = false;
            MehediExitAppOnComplete = false;
            ShihabShowCompleteDialog = true;
            MehediSha256Hex = "";
            ShihabServerLastModified = "";
            MehediIsHlsStream = false;
            ShihabRetryCount = 0;
            MehediOriginHeader = "";
            MehediOmitCookies = false;
            MehediAudioCompanionUrl = "";
            ShihabAudioCompanionBytes = -1;
            ShihabSourceTabId = -1;
            ShihabQualityLabel = "";
            MehediSegments = new List<MehediSegmentChunk>();
            ShihabCurrentSpeedBps = 0;
            MehediEtaSeconds = -1;
        }

        public ShihabDownloadStatus GetShihabStatusEnum()
        {
            return (ShihabDownloadStatus)MehediStatusCode;
        }

        public void SetMehediStatusEnum(ShihabDownloadStatus shihabStatus, string mehediLabel)
        {
            MehediStatusCode = (int)shihabStatus;
            if (!string.IsNullOrEmpty(mehediLabel))
            {
                ShihabStatusText = mehediLabel;
            }
        }

        public string GetShihabFullTargetPath()
        {
            string mehediDir = string.IsNullOrEmpty(MehediSaveDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : MehediSaveDirectory;
            return Path.Combine(mehediDir, ShihabFileName);
        }

        public double GetMehediPercent()
        {
            if (GetShihabStatusEnum() == ShihabDownloadStatus.ShihabCompleted)
            {
                return 100.0;
            }
            if (ShihabTotalBytes <= 0)
            {
                return 0.0;
            }
            double shihabPct = (MehediDownloadedBytes * 100.0) / (double)ShihabTotalBytes;
            if (shihabPct < 0.0) return 0.0;
            if (shihabPct > 100.0) return 100.0;
            return shihabPct;
        }
    }

    [DataContract]
    public class ShihabCategoryRule
    {
        [DataMember] public string ShihabName { get; set; }
        [DataMember] public string MehediExtensionsSpaceSeparated { get; set; }
        [DataMember] public string ShihabDefaultFolder { get; set; }
        [DataMember] public bool MehediRememberLastSavePath { get; set; }

        public ShihabCategoryRule()
        {
            MehediRememberLastSavePath = true;
        }

        [OnDeserializing]
        private void OnShihabDeserializing(StreamingContext mehediCtx)
        {
            MehediRememberLastSavePath = true;
        }

        public bool MatchesMehediExtension(string shihabExtWithoutDot)
        {
            if (string.IsNullOrEmpty(shihabExtWithoutDot) || string.IsNullOrEmpty(MehediExtensionsSpaceSeparated))
            {
                return false;
            }
            string mehediClean = shihabExtWithoutDot.Trim().TrimStart('.').ToLowerInvariant();
            string[] shihabParts = MehediExtensionsSpaceSeparated.Split(new char[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int mehediIdx = 0; mehediIdx < shihabParts.Length; mehediIdx++)
            {
                string shihabRuleTok = shihabParts[mehediIdx].Trim().TrimStart('.');
                if (string.IsNullOrEmpty(shihabRuleTok)) continue;
                if (shihabRuleTok.IndexOf('*') >= 0 || shihabRuleTok.IndexOf('?') >= 0)
                {
                    if (ShihabFormatHelpers.WildcardMatchesMehediPattern(mehediClean, shihabRuleTok))
                    {
                        return true;
                    }
                }
                else if (string.Equals(shihabRuleTok, mehediClean, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    [DataContract]
    public class MehediQueueConfig
    {
        [DataMember] public string ShihabQueueName { get; set; }
        [DataMember] public int MehediMaxConcurrentFiles { get; set; }
        [DataMember] public int ShihabRetriesPerFile { get; set; }
        [DataMember] public bool MehediEnableScheduledStart { get; set; }
        [DataMember] public string ShihabStartTimeHhMm { get; set; }
        [DataMember] public bool MehediEnableScheduledStop { get; set; }
        [DataMember] public string ShihabStopTimeHhMm { get; set; }
        [DataMember] public string MehediActiveDaysCsv { get; set; }
        [DataMember] public bool ShihabStartOnStartup { get; set; }
        [DataMember] public bool MehediExitAppWhenDone { get; set; }
        [DataMember] public int ShihabQuotaMegabytes { get; set; }

        [IgnoreDataMember] public bool MehediIsRunningNow { get; set; }
        [IgnoreDataMember] public string ShihabLastAutoStartMinute { get; set; }
        [IgnoreDataMember] public string MehediLastAutoStopMinute { get; set; }

        public MehediQueueConfig()
        {
            ShihabQueueName = "Main Download Queue";
            MehediMaxConcurrentFiles = 4;
            ShihabRetriesPerFile = 3;
            MehediEnableScheduledStart = false;
            ShihabStartTimeHhMm = "01:00";
            MehediEnableScheduledStop = false;
            ShihabStopTimeHhMm = "07:00";
            MehediActiveDaysCsv = "Sun,Mon,Tue,Wed,Thu,Fri,Sat";
            ShihabStartOnStartup = false;
            MehediExitAppWhenDone = false;
            ShihabQuotaMegabytes = 0;
            MehediIsRunningNow = false;
            ShihabLastAutoStartMinute = "";
            MehediLastAutoStopMinute = "";
        }
    }

    [DataContract]
    public class ShihabSiteLoginCredential
    {
        [DataMember] public string ShihabServerPattern { get; set; }
        [DataMember] public string MehediUserName { get; set; }
        [DataMember] public string ShihabPassword { get; set; }
    }

    [DataContract]
    public class MehediAppConfiguration
    {
        public const string DefaultShihabAutoCaptureExtensions =
            "3GP 7Z AAC ACE AIF APK ARJ ASF AVI BIN BZ2 EXE GZ GZIP IMG ISO LZH M4A M4V MKV MOV MP3 MP4 MPA MPE MPEG MPG MSI MSU OGG OGV PDF PLJ PPS PPT PPTX QT R0* R1* RA RAR RM RMVB SEA SIT SITX TAR TIF TIFF WAV WEBM WMA WMV XZ Z ZIP";

        public const string DefaultMehediExcludedSites =
            "*.update.microsoft.com download.windowsupdate.com *.download.windowsupdate.com siteseal.thawte.com ecom.cimetz.com *.voice2page.com localhost 127.0.0.1 *.local";

        public const string DefaultShihabBrowsersCsv =
            "Apple Safari,Brave Browser,Google Chrome,Internet Explorer,Microsoft Edge,Microsoft Edge Legacy,Mozilla Firefox,Opera,Vivaldi";

        public const string DefaultMehediContextMenuCsv =
            "Download with MHS,Download video with MHS,Download all links with MHS";

        [DataMember] public bool ShihabDarkMode { get; set; }
        [DataMember] public bool MehediShowCategoriesPanel { get; set; }
        [DataMember] public bool ShihabLaunchOnStartup { get; set; }
        [DataMember] public bool ShihabMonitorClipboard { get; set; }
        [DataMember] public bool MehediBrowserIntegrationEnabled { get; set; }
        [DataMember] public string MehediEnabledBrowsersCsv { get; set; }
        [DataMember] public List<string> ShihabCustomBrowserPaths { get; set; }
        [DataMember] public string MehediPreventKeyName { get; set; }
        [DataMember] public string ShihabForceKeyName { get; set; }
        [DataMember] public string MehediContextMenuItemsCsv { get; set; }
        [DataMember] public bool ShihabShowVideoFloatingPanel { get; set; }
        [DataMember] public bool ShihabPanelMiniMode { get; set; }
        [DataMember] public bool MehediPanelSkipAutoStart { get; set; }
        [DataMember] public int MehediMinVideoSizeKb { get; set; }
        [DataMember] public bool ShihabShowStartDownloadDialog { get; set; }
        [DataMember] public bool MehediShowDownloadProgressDialog { get; set; }
        [DataMember] public bool ShihabShowDownloadCompleteDialog { get; set; }
        [DataMember] public bool MehediShowFloatingDropTarget { get; set; }
        [DataMember] public int MehediDuplicateFileMode { get; set; }
        [DataMember] public bool ShihabEnableVirusCheck { get; set; }
        [DataMember] public string MehediVirusScannerExe { get; set; }
        [DataMember] public string ShihabVirusScannerArgs { get; set; }
        [DataMember] public string MehediConnectionProfile { get; set; }
        [DataMember] public int ShihabDefaultMaxConnections { get; set; }
        [DataMember] public int ShihabAutoRetryIntervalSec { get; set; }
        [DataMember] public int MehediMaxRetryAttempts { get; set; }
        [DataMember] public bool MehediGlobalSpeedLimiterEnabled { get; set; }
        [DataMember] public int ShihabGlobalSpeedLimitKb { get; set; }
        [DataMember] public string MehediAutoCaptureExtensions { get; set; }
        [DataMember] public string ShihabExcludedSitesSpaceSeparated { get; set; }
        [DataMember] public string MehediExcludedAddressesSpaceSeparated { get; set; }
        [DataMember] public string MehediTemporaryDirectory { get; set; }
        [DataMember] public string ShihabDefaultUserAgent { get; set; }
        [DataMember] public bool MehediUseHttpProxy { get; set; }
        [DataMember] public string ShihabProxyAddress { get; set; }
        [DataMember] public int MehediProxyPort { get; set; }
        [DataMember] public string ShihabProxyUser { get; set; }
        [DataMember] public string MehediProxyPassword { get; set; }
        [DataMember] public bool ShihabUseSocksProxy { get; set; }
        [DataMember] public string MehediSocksHost { get; set; }
        [DataMember] public int ShihabSocksPort { get; set; }
        [DataMember] public string MehediDialUpConnectionName { get; set; }
        [DataMember] public bool ShihabRedialIfDisconnected { get; set; }
        [DataMember] public bool MehediHangUpWhenDone { get; set; }
        [DataMember] public bool MehediPlaySoundComplete { get; set; }
        [DataMember] public string ShihabSoundCompleteWav { get; set; }
        [DataMember] public bool MehediPlaySoundError { get; set; }
        [DataMember] public string ShihabSoundErrorWav { get; set; }
        [DataMember] public bool ShihabSetServerFileTimestamp { get; set; }
        [DataMember] public bool ShihabSetServerCreationDate { get; set; }
        [DataMember] public int MehediLocalBridgePort { get; set; }
        [DataMember] public List<ShihabCategoryRule> ShihabCategories { get; set; }
        [DataMember] public List<MehediQueueConfig> MehediQueues { get; set; }
        [DataMember] public List<ShihabSiteLoginCredential> ShihabSiteLogins { get; set; }

        [OnDeserializing]
        private void OnMehediConfigDeserializing(StreamingContext shihabCtx)
        {
            ShihabDarkMode = true;
            MehediShowCategoriesPanel = true;
            MehediBrowserIntegrationEnabled = true;
            MehediEnabledBrowsersCsv = DefaultShihabBrowsersCsv;
            ShihabCustomBrowserPaths = new List<string>();
            MehediPreventKeyName = "Alt";
            ShihabForceKeyName = "Ins";
            MehediContextMenuItemsCsv = DefaultMehediContextMenuCsv;
            ShihabShowVideoFloatingPanel = true;
            ShihabPanelMiniMode = false;
            MehediPanelSkipAutoStart = false;
            MehediMinVideoSizeKb = 300;
            ShihabShowStartDownloadDialog = true;
            MehediShowDownloadProgressDialog = true;
            ShihabShowDownloadCompleteDialog = true;
            MehediConnectionProfile = "High speed: Direct connection (Ethernet/Cable) / Wi-Fi / Mobile 4G/5G";
            ShihabDefaultMaxConnections = 8;
            ShihabAutoRetryIntervalSec = 10;
            MehediMaxRetryAttempts = 99;
            ShihabGlobalSpeedLimitKb = 1024;
            MehediAutoCaptureExtensions = DefaultShihabAutoCaptureExtensions;
            ShihabExcludedSitesSpaceSeparated = DefaultMehediExcludedSites;
            MehediExcludedAddressesSpaceSeparated = "";
            ShihabDefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            MehediProxyPort = 8080;
            ShihabSocksPort = 1080;
            MehediVirusScannerExe = "";
            ShihabVirusScannerArgs = "\"%1\"";
            MehediSocksHost = "";
            MehediDialUpConnectionName = "Default System Connection";
            ShihabSoundCompleteWav = "";
            ShihabSoundErrorWav = "";
            ShihabSetServerFileTimestamp = true;
            MehediLocalBridgePort = 45779;
        }

        public static MehediAppConfiguration CreateShihabDefault(string mehediBaseDataDir)
        {
            string shihabDownloadsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            MehediAppConfiguration mehediCfg = new MehediAppConfiguration();
            mehediCfg.ShihabDarkMode = true;
            mehediCfg.MehediShowCategoriesPanel = true;
            mehediCfg.ShihabLaunchOnStartup = false;
            mehediCfg.ShihabMonitorClipboard = false;
            mehediCfg.MehediBrowserIntegrationEnabled = true;
            mehediCfg.MehediEnabledBrowsersCsv = DefaultShihabBrowsersCsv;
            mehediCfg.ShihabCustomBrowserPaths = new List<string>();
            mehediCfg.MehediPreventKeyName = "Alt";
            mehediCfg.ShihabForceKeyName = "Ins";
            mehediCfg.MehediContextMenuItemsCsv = DefaultMehediContextMenuCsv;
            mehediCfg.ShihabShowVideoFloatingPanel = true;
            mehediCfg.ShihabPanelMiniMode = false;
            mehediCfg.MehediPanelSkipAutoStart = false;
            mehediCfg.MehediMinVideoSizeKb = 300;
            mehediCfg.ShihabShowStartDownloadDialog = true;
            mehediCfg.MehediShowDownloadProgressDialog = true;
            mehediCfg.ShihabShowDownloadCompleteDialog = true;
            mehediCfg.MehediShowFloatingDropTarget = false;
            mehediCfg.MehediDuplicateFileMode = 0;
            mehediCfg.ShihabEnableVirusCheck = false;
            mehediCfg.MehediVirusScannerExe = "";
            mehediCfg.ShihabVirusScannerArgs = "\"%1\"";
            mehediCfg.MehediConnectionProfile = "High speed: Direct connection (Ethernet/Cable) / Wi-Fi / Mobile 4G/5G";
            mehediCfg.ShihabDefaultMaxConnections = 8;
            mehediCfg.ShihabAutoRetryIntervalSec = 10;
            mehediCfg.MehediMaxRetryAttempts = 99;
            mehediCfg.MehediGlobalSpeedLimiterEnabled = false;
            mehediCfg.ShihabGlobalSpeedLimitKb = 1024;
            mehediCfg.MehediAutoCaptureExtensions = DefaultShihabAutoCaptureExtensions;
            mehediCfg.ShihabExcludedSitesSpaceSeparated = DefaultMehediExcludedSites;
            mehediCfg.MehediExcludedAddressesSpaceSeparated = "";
            mehediCfg.MehediTemporaryDirectory = Path.Combine(mehediBaseDataDir, "DwnlData");
            mehediCfg.ShihabDefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            mehediCfg.MehediUseHttpProxy = false;
            mehediCfg.ShihabProxyAddress = "";
            mehediCfg.MehediProxyPort = 8080;
            mehediCfg.ShihabProxyUser = "";
            mehediCfg.MehediProxyPassword = "";
            mehediCfg.ShihabUseSocksProxy = false;
            mehediCfg.MehediSocksHost = "";
            mehediCfg.ShihabSocksPort = 1080;
            mehediCfg.MehediDialUpConnectionName = "Default System Connection";
            mehediCfg.ShihabRedialIfDisconnected = false;
            mehediCfg.MehediHangUpWhenDone = false;
            mehediCfg.MehediPlaySoundComplete = false;
            mehediCfg.ShihabSoundCompleteWav = "";
            mehediCfg.MehediPlaySoundError = false;
            mehediCfg.ShihabSoundErrorWav = "";
            mehediCfg.ShihabSetServerFileTimestamp = true;
            mehediCfg.ShihabSetServerCreationDate = false;
            mehediCfg.MehediLocalBridgePort = 45779;

            mehediCfg.ShihabCategories = new List<ShihabCategoryRule>
            {
                new ShihabCategoryRule { ShihabName = "General", MehediExtensionsSpaceSeparated = "", ShihabDefaultFolder = shihabDownloadsRoot, MehediRememberLastSavePath = true },
                new ShihabCategoryRule { ShihabName = "Compressed", MehediExtensionsSpaceSeparated = "zip rar 7z gz bz2 xz tar tgz cab arj lzh ace z r0* r1*", ShihabDefaultFolder = Path.Combine(shihabDownloadsRoot, "Compressed"), MehediRememberLastSavePath = true },
                new ShihabCategoryRule { ShihabName = "Documents", MehediExtensionsSpaceSeparated = "pdf doc docx xls xlsx ppt pptx txt rtf csv epub mobi", ShihabDefaultFolder = Path.Combine(shihabDownloadsRoot, "Documents"), MehediRememberLastSavePath = true },
                new ShihabCategoryRule { ShihabName = "Music", MehediExtensionsSpaceSeparated = "mp3 wav flac aac ogg m4a wma aif opus", ShihabDefaultFolder = Path.Combine(shihabDownloadsRoot, "Music"), MehediRememberLastSavePath = true },
                new ShihabCategoryRule { ShihabName = "Programs", MehediExtensionsSpaceSeparated = "exe msi msu apk dmg iso img bin deb rpm bat ps1", ShihabDefaultFolder = Path.Combine(shihabDownloadsRoot, "Programs"), MehediRememberLastSavePath = true },
                new ShihabCategoryRule { ShihabName = "Video", MehediExtensionsSpaceSeparated = "mp4 mkv webm avi mov flv wmv mpg mpeg m4v 3gp ts m3u8 ogv qt rm rmvb asf", ShihabDefaultFolder = Path.Combine(shihabDownloadsRoot, "Video"), MehediRememberLastSavePath = true }
            };

            MehediQueueConfig shihabMainQueue = new MehediQueueConfig();
            shihabMainQueue.ShihabQueueName = "Main Download Queue";
            shihabMainQueue.MehediMaxConcurrentFiles = 4;

            MehediQueueConfig mehediSyncQueue = new MehediQueueConfig();
            mehediSyncQueue.ShihabQueueName = "Synchronization Queue";
            mehediSyncQueue.MehediMaxConcurrentFiles = 2;

            mehediCfg.MehediQueues = new List<MehediQueueConfig> { shihabMainQueue, mehediSyncQueue };
            mehediCfg.ShihabSiteLogins = new List<ShihabSiteLoginCredential>();
            return mehediCfg;
        }

        public bool MatchesShihabAutoCaptureExtension(string mehediExtWithoutDot)
        {
            if (string.IsNullOrWhiteSpace(mehediExtWithoutDot) || string.IsNullOrWhiteSpace(MehediAutoCaptureExtensions))
            {
                return false;
            }
            string shihabClean = mehediExtWithoutDot.Trim().TrimStart('.');
            if (string.IsNullOrEmpty(shihabClean)) return false;

            string[] mehediTokens = MehediAutoCaptureExtensions.Split(new char[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int shihabI = 0; shihabI < mehediTokens.Length; shihabI++)
            {
                string mehediPat = mehediTokens[shihabI].Trim().TrimStart('.');
                if (string.IsNullOrEmpty(mehediPat)) continue;
                if (mehediPat.IndexOf('*') >= 0 || mehediPat.IndexOf('?') >= 0)
                {
                    if (ShihabFormatHelpers.WildcardMatchesMehediPattern(shihabClean, mehediPat))
                    {
                        return true;
                    }
                }
                else if (string.Equals(mehediPat, shihabClean, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static void SyncShihabWindowsStartupRegistry(bool mehediEnable)
        {
            try
            {
                const string shihabRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
                const string mehediValueName = "MHS_Download_Manager";
                using (RegistryKey shihabKey = Registry.CurrentUser.CreateSubKey(shihabRunSubKey))
                {
                    if (shihabKey == null) return;
                    if (mehediEnable)
                    {
                        string mehediExe = Process.GetCurrentProcess().MainModule.FileName;
                        string shihabDesired = "\"" + mehediExe + "\" --shihab-minimized";
                        string mehediExisting = shihabKey.GetValue(mehediValueName) as string;
                        if (!string.Equals(mehediExisting, shihabDesired, StringComparison.Ordinal))
                        {
                            shihabKey.SetValue(mehediValueName, shihabDesired, RegistryValueKind.String);
                        }
                    }
                    else
                    {
                        object shihabCurVal = shihabKey.GetValue(mehediValueName, null);
                        if (shihabCurVal != null)
                        {
                            shihabKey.DeleteValue(mehediValueName, false);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public ShihabCategoryRule ResolveMehediCategoryForFile(string shihabFileName)
        {
            string mehediExt = "";
            if (!string.IsNullOrEmpty(shihabFileName))
            {
                mehediExt = Path.GetExtension(shihabFileName).TrimStart('.');
            }
            if (ShihabCategories != null)
            {
                for (int shihabIdx = 0; shihabIdx < ShihabCategories.Count; shihabIdx++)
                {
                    ShihabCategoryRule mehediCat = ShihabCategories[shihabIdx];
                    if (mehediCat.MatchesMehediExtension(mehediExt))
                    {
                        return mehediCat;
                    }
                }
                for (int shihabIdx = 0; shihabIdx < ShihabCategories.Count; shihabIdx++)
                {
                    if (string.Equals(ShihabCategories[shihabIdx].ShihabName, "General", StringComparison.OrdinalIgnoreCase))
                    {
                        return ShihabCategories[shihabIdx];
                    }
                }
            }
            return new ShihabCategoryRule
            {
                ShihabName = "General",
                MehediExtensionsSpaceSeparated = "",
                ShihabDefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };
        }

        public ShihabSiteLoginCredential FindShihabCredentialForUrl(string mehediUrl)
        {
            if (string.IsNullOrEmpty(mehediUrl) || ShihabSiteLogins == null)
            {
                return null;
            }
            Uri shihabUri;
            if (!Uri.TryCreate(mehediUrl, UriKind.Absolute, out shihabUri))
            {
                return null;
            }
            string mehediHostAndPath = (shihabUri.Host + shihabUri.AbsolutePath).ToLowerInvariant();
            for (int shihabI = 0; shihabI < ShihabSiteLogins.Count; shihabI++)
            {
                ShihabSiteLoginCredential mehediCred = ShihabSiteLogins[shihabI];
                if (mehediCred != null && !string.IsNullOrEmpty(mehediCred.ShihabServerPattern))
                {
                    string shihabPat = mehediCred.ShihabServerPattern.Trim().ToLowerInvariant();
                    shihabPat = shihabPat.Replace("https://", "").Replace("http://", "");
                    if (mehediHostAndPath.StartsWith(shihabPat) || shihabUri.Host.Equals(shihabPat, StringComparison.OrdinalIgnoreCase))
                    {
                        return mehediCred;
                    }
                }
            }
            return null;
        }
    }

    [DataContract]
    public class ShihabDownloadDatabase
    {
        [DataMember] public List<ShihabDownloadItem> ShihabItems { get; set; }

        public ShihabDownloadDatabase()
        {
            ShihabItems = new List<ShihabDownloadItem>();
        }
    }

    public static class ShihabFormatHelpers
    {
        public static string FormatMehediBytes(long shihabBytes)
        {
            if (shihabBytes < 0)
            {
                return "Unknown";
            }
            if (shihabBytes < 1024L)
            {
                return string.Format("{0} Bytes", shihabBytes);
            }
            double mehediKb = shihabBytes / 1024.0;
            if (mehediKb < 1024.0)
            {
                return string.Format("{0:0.0} KB", mehediKb);
            }
            double shihabMb = mehediKb / 1024.0;
            if (shihabMb < 1024.0)
            {
                return string.Format("{0:0.00} MB", shihabMb);
            }
            double mehediGb = shihabMb / 1024.0;
            return string.Format("{0:0.00} GB", mehediGb);
        }

        public static string FormatShihabSpeed(double mehediBytesPerSec)
        {
            if (mehediBytesPerSec <= 1.0)
            {
                return "";
            }
            if (mehediBytesPerSec < 1024.0)
            {
                return string.Format("{0:0} B/sec", mehediBytesPerSec);
            }
            double shihabKb = mehediBytesPerSec / 1024.0;
            if (shihabKb < 1024.0)
            {
                return string.Format("{0:0.0} KB/sec", shihabKb);
            }
            double mehediMb = shihabKb / 1024.0;
            return string.Format("{0:0.00} MB/sec", mehediMb);
        }

        public static string FormatMehediTimeLeft(long shihabSeconds)
        {
            if (shihabSeconds < 0)
            {
                return "";
            }
            if (shihabSeconds < 60)
            {
                return string.Format("{0} sec", shihabSeconds);
            }
            long mehediMinutes = shihabSeconds / 60;
            long shihabRemSec = shihabSeconds % 60;
            if (mehediMinutes < 60)
            {
                return string.Format("{0} min {1} sec", mehediMinutes, shihabRemSec);
            }
            long shihabHours = mehediMinutes / 60;
            long mehediRemMin = mehediMinutes % 60;
            return string.Format("{0} hr {1} min", shihabHours, mehediRemMin);
        }

        public static string SanitizeShihabFileName(string mehediRawName)
        {
            if (string.IsNullOrWhiteSpace(mehediRawName))
            {
                return "shihab_download.bin";
            }
            string shihabName = mehediRawName.Trim().Trim('"', '\'');
            char[] mehediInvalid = Path.GetInvalidFileNameChars();
            StringBuilder shihabSb = new StringBuilder(shihabName.Length);
            for (int mehediI = 0; mehediI < shihabName.Length; mehediI++)
            {
                char shihabCh = shihabName[mehediI];
                bool mehediBad = false;
                for (int shihabJ = 0; shihabJ < mehediInvalid.Length; shihabJ++)
                {
                    if (shihabCh == mehediInvalid[shihabJ])
                    {
                        mehediBad = true;
                        break;
                    }
                }
                shihabSb.Append(mehediBad ? '_' : shihabCh);
            }
            string mehediResult = shihabSb.ToString().Trim();
            if (string.IsNullOrEmpty(mehediResult) || mehediResult == "." || mehediResult == "..")
            {
                return "shihab_download.bin";
            }
            if (mehediResult.Length > 180)
            {
                string shihabExt = Path.GetExtension(mehediResult);
                string mehediStem = Path.GetFileNameWithoutExtension(mehediResult);
                if (mehediStem.Length > 150)
                {
                    mehediStem = mehediStem.Substring(0, 150);
                }
                mehediResult = mehediStem + shihabExt;
            }
            return mehediResult;
        }

        public static string ExtractMehediFileNameFromUrl(string shihabUrl)
        {
            if (string.IsNullOrWhiteSpace(shihabUrl))
            {
                return "download.bin";
            }
            Uri mehediUri;
            if (Uri.TryCreate(shihabUrl, UriKind.Absolute, out mehediUri))
            {
                string shihabLeaf = Path.GetFileName(Uri.UnescapeDataString(mehediUri.AbsolutePath));
                if (!string.IsNullOrWhiteSpace(shihabLeaf) && shihabLeaf != "/")
                {
                    return SanitizeShihabFileName(shihabLeaf);
                }
                return SanitizeShihabFileName(mehediUri.Host + "_index.html");
            }
            return "download.bin";
        }

        public static bool IsMehediGibberishCdnFileName(string shihabCandidate)
        {
            if (string.IsNullOrWhiteSpace(shihabCandidate)) return true;
            string mehediStem = Path.GetFileNameWithoutExtension(shihabCandidate).Trim();
            if (mehediStem.Length < 2) return true;

            string shihabLower = mehediStem.ToLowerInvariant();
            if (shihabLower == "video" || shihabLower == "videoplayback" || shihabLower == "playback" ||
                shihabLower == "media" || shihabLower == "master" || shihabLower == "index" ||
                shihabLower == "playlist" || shihabLower == "stream" || shihabLower == "watch" ||
                shihabLower == "reel" || shihabLower == "reels" || shihabLower == "download" ||
                shihabLower == "audio" || shihabLower == "clip" || shihabLower == "file" ||
                shihabLower == "chunklist" || shihabLower == "manifest" ||
                shihabLower == "mhs video" || shihabLower == "mhs_video" ||
                shihabLower == "instagram" || shihabLower == "instagram video" ||
                shihabLower == "facebook" || shihabLower == "facebook video" ||
                shihabLower == "youtube" || shihabLower == "youtube video")
            {
                return true;
            }

            if (mehediStem.StartsWith("AQ", StringComparison.Ordinal) && mehediStem.Length >= 12 && mehediStem.IndexOf(' ') < 0)
            {
                return true;
            }
            if (mehediStem.StartsWith("An", StringComparison.Ordinal) && mehediStem.Length >= 12 && mehediStem.IndexOf(' ') < 0)
            {
                return true;
            }
            if (mehediStem.IndexOf(' ') < 0 && mehediStem.Length >= 22)
            {
                int shihabCaseFlips = 0;
                for (int mehediI = 1; mehediI < mehediStem.Length; mehediI++)
                {
                    bool shihabPrevUpper = char.IsUpper(mehediStem[mehediI - 1]);
                    bool mehediCurUpper = char.IsUpper(mehediStem[mehediI]);
                    bool shihabCurDigit = char.IsDigit(mehediStem[mehediI]);
                    if (shihabPrevUpper != mehediCurUpper || shihabCurDigit)
                    {
                        shihabCaseFlips++;
                    }
                }
                if (shihabCaseFlips >= 6) return true;
            }
            return false;
        }

        public static string ResolveSmartMehediFileName(string shihabRawFileName, string mehediUrl, string shihabTabTitle)
        {
            return ResolveSmartMehediFileName(shihabRawFileName, mehediUrl, shihabTabTitle, "");
        }

        public static string ResolveSmartMehediFileName(
            string shihabRawFileName,
            string mehediUrl,
            string shihabTabTitle,
            string mehediQualityLabel)
        {
            string mehediInitial = !string.IsNullOrWhiteSpace(shihabRawFileName)
                ? SanitizeShihabFileName(Path.GetFileName(shihabRawFileName))
                : ExtractMehediFileNameFromUrl(mehediUrl);

            string shihabResolved = mehediInitial;
            if (IsMehediGibberishCdnFileName(mehediInitial) && !string.IsNullOrWhiteSpace(shihabTabTitle))
            {
                string shihabCleanTitle = shihabTabTitle.Trim();
                while (shihabCleanTitle.StartsWith("(") && shihabCleanTitle.IndexOf(')') > 0 && shihabCleanTitle.IndexOf(')') <= 6)
                {
                    shihabCleanTitle = shihabCleanTitle.Substring(shihabCleanTitle.IndexOf(')') + 1).Trim();
                }
                string[] mehediSuffixes = new string[]
                {
                    " | Facebook", " - Facebook", " - YouTube", " | YouTube",
                    " • Instagram", " | Instagram", " - Instagram",
                    " | X", " / X", " | TikTok", " - Vimeo", " : Reddit"
                };
                for (int shihabS = 0; shihabS < mehediSuffixes.Length; shihabS++)
                {
                    if (shihabCleanTitle.EndsWith(mehediSuffixes[shihabS], StringComparison.OrdinalIgnoreCase))
                    {
                        shihabCleanTitle = shihabCleanTitle.Substring(0, shihabCleanTitle.Length - mehediSuffixes[shihabS].Length).Trim();
                    }
                }
                shihabCleanTitle = Regex.Replace(
                    shihabCleanTitle,
                    @"\s*(?:Hide translation|See translation|See original|Translated from [A-Za-z ]+)\s*$",
                    "",
                    RegexOptions.IgnoreCase).Trim();

                if (!string.IsNullOrWhiteSpace(shihabCleanTitle) &&
                    !string.Equals(shihabCleanTitle, "Facebook", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(shihabCleanTitle, "Reels", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(shihabCleanTitle, "YouTube", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(shihabCleanTitle, "Instagram", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(shihabCleanTitle, "MHS Video", StringComparison.OrdinalIgnoreCase))
                {
                    string mehediExt = Path.GetExtension(mehediInitial);
                    if (string.IsNullOrEmpty(mehediExt)) mehediExt = ".mp4";
                    shihabResolved = SanitizeShihabFileName(shihabCleanTitle + mehediExt);
                }
            }

            string mehediStemClean = Regex.Replace(
                Path.GetFileNameWithoutExtension(shihabResolved),
                @"\s*(?:Hide translation|See translation|See original|Translated from [A-Za-z ]+)\s*$",
                "",
                RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrWhiteSpace(mehediStemClean))
            {
                shihabResolved = SanitizeShihabFileName(mehediStemClean + Path.GetExtension(shihabResolved));
            }

            if (!string.IsNullOrWhiteSpace(mehediQualityLabel))
            {
                string shihabQual = mehediQualityLabel.Trim();
                string mehediStem = Path.GetFileNameWithoutExtension(shihabResolved);
                string shihabExt = Path.GetExtension(shihabResolved);
                if (mehediStem.IndexOf(shihabQual, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    shihabResolved = SanitizeShihabFileName(string.Format("{0} [{1}]{2}", mehediStem, shihabQual, shihabExt));
                }
            }

            return shihabResolved;
        }

        public static string EnsureShihabUniqueFilePath(string mehediDirectory, string shihabFileName)
        {
            string mehediSafeFile = SanitizeShihabFileName(shihabFileName);
            string shihabFull = Path.Combine(mehediDirectory, mehediSafeFile);
            if (!File.Exists(shihabFull))
            {
                return mehediSafeFile;
            }
            string mehediStem = Path.GetFileNameWithoutExtension(mehediSafeFile);
            string shihabExt = Path.GetExtension(mehediSafeFile);
            for (int mehediCounter = 2; mehediCounter < 10000; mehediCounter++)
            {
                string shihabCandidate = string.Format("{0}_{1}{2}", mehediStem, mehediCounter, shihabExt);
                if (!File.Exists(Path.Combine(mehediDirectory, shihabCandidate)))
                {
                    return shihabCandidate;
                }
            }
            return string.Format("{0}_{1}{2}", mehediStem, Guid.NewGuid().ToString("N").Substring(0, 6), shihabExt);
        }

        public static bool WildcardMatchesMehediPattern(string shihabText, string mehediWildcardPattern)
        {
            if (string.IsNullOrEmpty(mehediWildcardPattern))
            {
                return false;
            }
            string shihabRegex = "^" + Regex.Escape(mehediWildcardPattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(shihabText ?? "", shihabRegex, RegexOptions.IgnoreCase);
        }
    }

    public class ShihabStateStore
    {
        private readonly object _shihabSyncLock = new object();
        public string MehediBaseDirectory { get; private set; }
        public string ShihabConfigFilePath { get; private set; }
        public string MehediDownloadsFilePath { get; private set; }

        public ShihabStateStore(string shihabCustomDirectory)
        {
            if (!string.IsNullOrWhiteSpace(shihabCustomDirectory))
            {
                MehediBaseDirectory = Path.GetFullPath(shihabCustomDirectory);
            }
            else
            {
                MehediBaseDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShihabMehediIDM");
            }
            Directory.CreateDirectory(MehediBaseDirectory);
            ShihabConfigFilePath = Path.Combine(MehediBaseDirectory, "shihab_config.json");
            MehediDownloadsFilePath = Path.Combine(MehediBaseDirectory, "mehedi_downloads.json");
        }

        private static byte[] StripShihabUtf8Bom(byte[] mehediRaw)
        {
            if (mehediRaw != null && mehediRaw.Length >= 3 &&
                mehediRaw[0] == 0xEF && mehediRaw[1] == 0xBB && mehediRaw[2] == 0xBF)
            {
                byte[] shihabTrimmed = new byte[mehediRaw.Length - 3];
                Buffer.BlockCopy(mehediRaw, 3, shihabTrimmed, 0, shihabTrimmed.Length);
                return shihabTrimmed;
            }
            return mehediRaw;
        }

        public MehediAppConfiguration LoadShihabConfig()
        {
            lock (_shihabSyncLock)
            {
                try
                {
                    if (File.Exists(ShihabConfigFilePath))
                    {
                        byte[] mehediBytes = StripShihabUtf8Bom(File.ReadAllBytes(ShihabConfigFilePath));
                        using (MemoryStream shihabMs = new MemoryStream(mehediBytes))
                        {
                            DataContractJsonSerializer mehediSer = new DataContractJsonSerializer(typeof(MehediAppConfiguration));
                            MehediAppConfiguration shihabLoaded = mehediSer.ReadObject(shihabMs) as MehediAppConfiguration;
                            if (shihabLoaded != null)
                            {
                                if (shihabLoaded.ShihabCategories == null || shihabLoaded.ShihabCategories.Count == 0)
                                {
                                    shihabLoaded.ShihabCategories = MehediAppConfiguration.CreateShihabDefault(MehediBaseDirectory).ShihabCategories;
                                }
                                if (shihabLoaded.MehediQueues == null || shihabLoaded.MehediQueues.Count == 0)
                                {
                                    shihabLoaded.MehediQueues = MehediAppConfiguration.CreateShihabDefault(MehediBaseDirectory).MehediQueues;
                                }
                                if (shihabLoaded.ShihabSiteLogins == null)
                                {
                                    shihabLoaded.ShihabSiteLogins = new List<ShihabSiteLoginCredential>();
                                }
                                if (shihabLoaded.ShihabCustomBrowserPaths == null)
                                {
                                    shihabLoaded.ShihabCustomBrowserPaths = new List<string>();
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.MehediEnabledBrowsersCsv))
                                {
                                    shihabLoaded.MehediEnabledBrowsersCsv = MehediAppConfiguration.DefaultShihabBrowsersCsv;
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.MehediPreventKeyName))
                                {
                                    shihabLoaded.MehediPreventKeyName = "Alt";
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.ShihabForceKeyName))
                                {
                                    shihabLoaded.ShihabForceKeyName = "Ins";
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.MehediAutoCaptureExtensions))
                                {
                                    shihabLoaded.MehediAutoCaptureExtensions = MehediAppConfiguration.DefaultShihabAutoCaptureExtensions;
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.ShihabExcludedSitesSpaceSeparated))
                                {
                                    shihabLoaded.ShihabExcludedSitesSpaceSeparated = MehediAppConfiguration.DefaultMehediExcludedSites;
                                }
                                if (shihabLoaded.MehediLocalBridgePort <= 0)
                                {
                                    shihabLoaded.MehediLocalBridgePort = 45779;
                                }
                                if (shihabLoaded.ShihabDefaultMaxConnections <= 0)
                                {
                                    shihabLoaded.ShihabDefaultMaxConnections = 8;
                                }
                                if (string.IsNullOrWhiteSpace(shihabLoaded.MehediTemporaryDirectory))
                                {
                                    shihabLoaded.MehediTemporaryDirectory = Path.Combine(MehediBaseDirectory, "DwnlData");
                                }
                                Directory.CreateDirectory(shihabLoaded.MehediTemporaryDirectory);
                                return shihabLoaded;
                            }
                        }
                    }
                }
                catch
                {
                }
                MehediAppConfiguration mehediFresh = MehediAppConfiguration.CreateShihabDefault(MehediBaseDirectory);
                Directory.CreateDirectory(mehediFresh.MehediTemporaryDirectory);
                SaveMehediConfig(mehediFresh);
                return mehediFresh;
            }
        }

        public void SaveMehediConfig(MehediAppConfiguration shihabCfg)
        {
            lock (_shihabSyncLock)
            {
                try
                {
                    Directory.CreateDirectory(MehediBaseDirectory);
                    string mehediTmp = ShihabConfigFilePath + ".tmp";
                    using (FileStream shihabFs = new FileStream(mehediTmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        DataContractJsonSerializer mehediSer = new DataContractJsonSerializer(typeof(MehediAppConfiguration));
                        mehediSer.WriteObject(shihabFs, shihabCfg);
                    }
                    if (File.Exists(ShihabConfigFilePath))
                    {
                        File.Delete(ShihabConfigFilePath);
                    }
                    File.Move(mehediTmp, ShihabConfigFilePath);
                }
                catch
                {
                }
            }
        }

        public List<ShihabDownloadItem> LoadMehediDownloads()
        {
            lock (_shihabSyncLock)
            {
                try
                {
                    if (File.Exists(MehediDownloadsFilePath))
                    {
                        byte[] shihabBytes = StripShihabUtf8Bom(File.ReadAllBytes(MehediDownloadsFilePath));
                        using (MemoryStream mehediMs = new MemoryStream(shihabBytes))
                        {
                            DataContractJsonSerializer shihabSer = new DataContractJsonSerializer(typeof(ShihabDownloadDatabase));
                            ShihabDownloadDatabase mehediDb = shihabSer.ReadObject(mehediMs) as ShihabDownloadDatabase;
                            if (mehediDb != null && mehediDb.ShihabItems != null)
                            {
                                for (int shihabI = 0; shihabI < mehediDb.ShihabItems.Count; shihabI++)
                                {
                                    ShihabDownloadItem mehediItem = mehediDb.ShihabItems[shihabI];
                                    if (mehediItem.MehediSegments == null)
                                    {
                                        mehediItem.MehediSegments = new List<MehediSegmentChunk>();
                                    }
                                    ShihabDownloadStatus shihabSt = mehediItem.GetShihabStatusEnum();
                                    if (shihabSt == ShihabDownloadStatus.ShihabDownloading ||
                                        shihabSt == ShihabDownloadStatus.ShihabConnecting ||
                                        shihabSt == ShihabDownloadStatus.ShihabRebuilding)
                                    {
                                        mehediItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabPaused, "Paused");
                                    }
                                }
                                return mehediDb.ShihabItems;
                            }
                        }
                    }
                }
                catch
                {
                }
                return new List<ShihabDownloadItem>();
            }
        }

        public void SaveShihabDownloads(List<ShihabDownloadItem> mehediItems)
        {
            lock (_shihabSyncLock)
            {
                try
                {
                    Directory.CreateDirectory(MehediBaseDirectory);
                    ShihabDownloadDatabase shihabDb = new ShihabDownloadDatabase();
                    shihabDb.ShihabItems = new List<ShihabDownloadItem>(mehediItems);
                    string mehediTmp = MehediDownloadsFilePath + ".tmp";
                    using (FileStream shihabFs = new FileStream(mehediTmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        DataContractJsonSerializer mehediSer = new DataContractJsonSerializer(typeof(ShihabDownloadDatabase));
                        mehediSer.WriteObject(shihabFs, shihabDb);
                    }
                    if (File.Exists(MehediDownloadsFilePath))
                    {
                        File.Delete(MehediDownloadsFilePath);
                    }
                    File.Move(mehediTmp, MehediDownloadsFilePath);
                }
                catch
                {
                }
            }
        }

        public static void ExportToShihabEf2File(string mehediFilePath, IEnumerable<ShihabDownloadItem> shihabItems)
        {
            StringBuilder mehediSb = new StringBuilder();
            foreach (ShihabDownloadItem shihabItem in shihabItems)
            {
                if (string.IsNullOrWhiteSpace(shihabItem.MehediUrl)) continue;
                mehediSb.AppendLine("<");
                mehediSb.AppendLine(shihabItem.MehediUrl.Trim());
                if (!string.IsNullOrWhiteSpace(shihabItem.MehediReferer))
                {
                    mehediSb.AppendLine("referer: " + shihabItem.MehediReferer.Trim());
                }
                if (!string.IsNullOrWhiteSpace(shihabItem.ShihabCookies))
                {
                    mehediSb.AppendLine("cookie: " + shihabItem.ShihabCookies.Trim());
                }
                if (!string.IsNullOrWhiteSpace(shihabItem.MehediUserAgent))
                {
                    mehediSb.AppendLine("user-agent: " + shihabItem.MehediUserAgent.Trim());
                }
                mehediSb.AppendLine(">");
            }
            File.WriteAllText(mehediFilePath, mehediSb.ToString(), Encoding.UTF8);
        }

        public static List<ShihabDownloadItem> ImportFromMehediEf2OrText(string shihabFilePath)
        {
            List<ShihabDownloadItem> mehediImported = new List<ShihabDownloadItem>();
            if (!File.Exists(shihabFilePath))
            {
                return mehediImported;
            }
            string[] shihabLines = File.ReadAllLines(shihabFilePath, Encoding.UTF8);
            bool mehediInsideBlock = false;
            ShihabDownloadItem shihabCurrent = null;

            for (int mehediI = 0; mehediI < shihabLines.Length; mehediI++)
            {
                string shihabLine = shihabLines[mehediI].Trim();
                if (string.IsNullOrEmpty(shihabLine)) continue;
                if (shihabLine == "<")
                {
                    mehediInsideBlock = true;
                    shihabCurrent = new ShihabDownloadItem();
                    continue;
                }
                if (shihabLine == ">")
                {
                    if (shihabCurrent != null && !string.IsNullOrWhiteSpace(shihabCurrent.MehediUrl))
                    {
                        shihabCurrent.ShihabFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(shihabCurrent.MehediUrl);
                        mehediImported.Add(shihabCurrent);
                    }
                    mehediInsideBlock = false;
                    shihabCurrent = null;
                    continue;
                }
                if (mehediInsideBlock && shihabCurrent != null)
                {
                    if (shihabLine.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        shihabLine.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        shihabLine.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabCurrent.MehediUrl = shihabLine;
                    }
                    else if (shihabLine.StartsWith("referer:", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabCurrent.MehediReferer = shihabLine.Substring(8).Trim();
                    }
                    else if (shihabLine.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabCurrent.ShihabCookies = shihabLine.Substring(7).Trim();
                    }
                    else if (shihabLine.StartsWith("user-agent:", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabCurrent.MehediUserAgent = shihabLine.Substring(11).Trim();
                    }
                }
                else if (shihabLine.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         shihabLine.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    ShihabDownloadItem mehediPlain = new ShihabDownloadItem();
                    mehediPlain.MehediUrl = shihabLine;
                    mehediPlain.ShihabFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(shihabLine);
                    mehediImported.Add(mehediPlain);
                }
            }
            return mehediImported;
        }
    }
}

