using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MHSManager
{
    public sealed class MainForm : Form
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private readonly DownloadManagerController _controller;
        private readonly BrowserBridgeServer _bridge;
        private readonly Dictionary<string, DownloadProgressDialog> _openDialogs;
        private readonly System.Windows.Forms.Timer _uiTimer;
        private readonly string _startupAddUrl;
        private readonly string _startupReferer;
        private readonly string _startupCookies;
        private readonly int _autoExitMs;

        private TreeView _treeNav;
        private BufferedListView _lvDownloads;
        private TextBox _txtSearch;
        private Button _btnSpeedToggle;
        private SegmentVisualizerBar _inspectorSegBar;
        private SpeedGraphControl _inspectorSpeedGraph;
        private TextBox _txtInspectorLog;
        private Label _lblInspectorTitle;
        private Label _lblStatusCounts;
        private Label _lblStatusSpeed;
        private Label _lblStatusBridge;
        private NotifyIcon _trayIcon;

        private string _activeFilterKey = "ALL";
        private string _lastClipboardSeen = "";
        private bool _clipboardDialogOpen;
        private bool _clipboardRegistered;
        private bool _forceExit;

        public MainForm(
            DownloadManagerController controller,
            BrowserBridgeServer bridge,
            string startupAddUrl,
            string startupReferer,
            string startupCookies,
            int autoExitMs)
        {
            _controller = controller;
            _bridge = bridge;
            _startupAddUrl = startupAddUrl;
            _startupReferer = startupReferer;
            _startupCookies = startupCookies;
            _autoExitMs = autoExitMs;
            _openDialogs = new Dictionary<string, DownloadProgressDialog>(StringComparer.OrdinalIgnoreCase);

            Text = "MHS Manager — Open-Source Multi-Connection Download Accelerator";
            Size = new Size(1180, 720);
            MinimumSize = new Size(960, 580);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            MhsTheme.StyleForm(this);

            TryLoadExecutableIcon();
            BuildLayout();
            InitializeTrayIcon();

            _controller.DownloadStarted += OnControllerDownloadStarted;
            _controller.DownloadCompleted += OnControllerDownloadCompleted;
            if (_bridge != null)
            {
                _bridge.RequestShowAddDialog += OnBridgeRequestShowAddDialog;
            }

            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 500;
            _uiTimer.Tick += delegate { RefreshUiTick(); };
            _uiTimer.Start();

            Shown += OnMainFormShown;
        }

        private void TryLoadExecutableIcon()
        {
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null)
                {
                    Icon = ico;
                }
            }
            catch
            {
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                _clipboardRegistered = AddClipboardFormatListener(Handle);
            }
            catch
            {
                _clipboardRegistered = false;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_clipboardRegistered)
            {
                try { RemoveClipboardFormatListener(Handle); } catch { }
                _clipboardRegistered = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                HandleClipboardChanged();
            }
            base.WndProc(ref m);
        }

        private void BuildLayout()
        {
            // Top Toolbar
            Panel topToolbar = new Panel();
            topToolbar.Dock = DockStyle.Top;
            topToolbar.Height = 52;
            topToolbar.BackColor = MhsTheme.BgSurface;
            topToolbar.Padding = new Padding(10, 9, 10, 9);

            FlowLayoutPanel flowBtns = new FlowLayoutPanel();
            flowBtns.Dock = DockStyle.Left;
            flowBtns.Width = 900;
            flowBtns.WrapContents = false;
            flowBtns.BackColor = Color.Transparent;

            Button btnAdd = MhsTheme.CreateToolbarButton("+ Add URL", MhsTheme.AccentEmerald, 96);
            btnAdd.Click += delegate { PromptAddDownload(null, null, null); };

            Button btnResume = MhsTheme.CreateToolbarButton("▶ Resume", MhsTheme.AccentSky, 88);
            btnResume.Click += delegate { ResumeSelected(); };

            Button btnPause = MhsTheme.CreateToolbarButton("⏸ Pause", MhsTheme.BgElevated, 82);
            btnPause.Click += delegate { PauseSelected(); };

            Button btnStopAll = MhsTheme.CreateToolbarButton("⏹ Stop All", MhsTheme.BgElevated, 88);
            btnStopAll.Click += delegate { _controller.PauseAll(); RefreshDownloadList(); };

            Button btnClearDone = MhsTheme.CreateToolbarButton("✓ Clear Done", MhsTheme.BgElevated, 98);
            btnClearDone.Click += delegate { _controller.ClearCompleted(); RefreshDownloadList(); };

            Button btnBatch = MhsTheme.CreateToolbarButton("🔢 Batch", MhsTheme.BgElevated, 78);
            btnBatch.Click += delegate { OpenBatchDialog(); };

            Button btnGrabber = MhsTheme.CreateToolbarButton("🕷 Grabber", MhsTheme.BgElevated, 88);
            btnGrabber.Click += delegate { OpenGrabberDialog(); };

            Button btnScheduler = MhsTheme.CreateToolbarButton("⏰ Scheduler", MhsTheme.BgElevated, 96);
            btnScheduler.Click += delegate { OpenSettingsDialog(1); };

            _btnSpeedToggle = MhsTheme.CreateToolbarButton("⚡ Speed Cap", MhsTheme.BgElevated, 98);
            _btnSpeedToggle.Click += delegate
            {
                _controller.Settings.EnableGlobalSpeedLimit = !_controller.Settings.EnableGlobalSpeedLimit;
                _controller.ApplySettingsChanged();
                UpdateSpeedButtonVisual();
            };

            Button btnOptions = MhsTheme.CreateToolbarButton("⚙ Options", MhsTheme.BgElevated, 86);
            btnOptions.Click += delegate { OpenSettingsDialog(0); };

            flowBtns.Controls.AddRange(new Control[]
            {
                btnAdd, btnResume, btnPause, btnStopAll, btnClearDone,
                btnBatch, btnGrabber, btnScheduler, _btnSpeedToggle, btnOptions
            });
            UpdateSpeedButtonVisual();

            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Right;
            searchPanel.Width = 220;
            _txtSearch = MhsTheme.CreateTextBox(false);
            _txtSearch.SetBounds(8, 5, 204, 25);
            _txtSearch.TextChanged += delegate { RefreshDownloadList(); };
            Label lblSearchHint = MhsTheme.CreateLabel("Filter:", true);
            lblSearchHint.Location = new Point(-36, 8);
            searchPanel.Controls.Add(_txtSearch);

            topToolbar.Controls.Add(searchPanel);
            topToolbar.Controls.Add(flowBtns);

            // Bottom Status Bar
            Panel statusBar = new Panel();
            statusBar.Dock = DockStyle.Bottom;
            statusBar.Height = 28;
            statusBar.BackColor = MhsTheme.BgSurface;

            _lblStatusCounts = MhsTheme.CreateLabel("Downloads: 0 total, 0 active", true);
            _lblStatusCounts.Location = new Point(12, 6);
            statusBar.Controls.Add(_lblStatusCounts);

            _lblStatusSpeed = MhsTheme.CreateLabel("Total Throughput: 0 B/s", false);
            _lblStatusSpeed.Font = MhsTheme.FontBold;
            _lblStatusSpeed.ForeColor = MhsTheme.AccentCyan;
            _lblStatusSpeed.Location = new Point(310, 5);
            statusBar.Controls.Add(_lblStatusSpeed);

            _lblStatusBridge = MhsTheme.CreateLabel("Browser Bridge: checking...", true);
            _lblStatusBridge.Location = new Point(620, 6);
            statusBar.Controls.Add(_lblStatusBridge);

            // Bottom Live Segment & Speed Inspector
            Panel inspectorPanel = new Panel();
            inspectorPanel.Dock = DockStyle.Bottom;
            inspectorPanel.Height = 185;
            inspectorPanel.BackColor = MhsTheme.BgSurface;
            inspectorPanel.Padding = new Padding(12, 8, 12, 8);

            _lblInspectorTitle = MhsTheme.CreateLabel("Live Multi-Segment & Throughput Inspector (Select a download above)", false);
            _lblInspectorTitle.Font = MhsTheme.FontBold;
            _lblInspectorTitle.Dock = DockStyle.Top;
            _lblInspectorTitle.Height = 22;

            _inspectorSegBar = new SegmentVisualizerBar();
            _inspectorSegBar.Dock = DockStyle.Top;
            _inspectorSegBar.Height = 28;

            Panel bottomSplit = new Panel();
            bottomSplit.Dock = DockStyle.Fill;
            bottomSplit.Padding = new Padding(0, 8, 0, 0);

            _inspectorSpeedGraph = new SpeedGraphControl();
            _inspectorSpeedGraph.Dock = DockStyle.Left;
            _inspectorSpeedGraph.Width = 420;

            Panel logPad = new Panel();
            logPad.Dock = DockStyle.Fill;
            logPad.Padding = new Padding(10, 0, 0, 0);

            _txtInspectorLog = MhsTheme.CreateTextBox(true);
            _txtInspectorLog.Multiline = true;
            _txtInspectorLog.ScrollBars = ScrollBars.Vertical;
            _txtInspectorLog.Dock = DockStyle.Fill;
            _txtInspectorLog.Font = MhsTheme.FontMono;
            _txtInspectorLog.BackColor = MhsTheme.BgDark;
            logPad.Controls.Add(_txtInspectorLog);

            bottomSplit.Controls.Add(logPad);
            bottomSplit.Controls.Add(_inspectorSpeedGraph);

            inspectorPanel.Controls.Add(bottomSplit);
            inspectorPanel.Controls.Add(_inspectorSegBar);
            inspectorPanel.Controls.Add(_lblInspectorTitle);

            // Left Category & Queue Tree
            Panel leftNavPanel = new Panel();
            leftNavPanel.Dock = DockStyle.Left;
            leftNavPanel.Width = 205;
            leftNavPanel.Padding = new Padding(8, 8, 4, 8);
            leftNavPanel.BackColor = MhsTheme.BgDark;

            _treeNav = new TreeView();
            _treeNav.Dock = DockStyle.Fill;
            _treeNav.BackColor = MhsTheme.BgSurface;
            _treeNav.ForeColor = MhsTheme.TextPrimary;
            _treeNav.BorderStyle = BorderStyle.FixedSingle;
            _treeNav.HideSelection = false;
            _treeNav.ItemHeight = 24;
            _treeNav.ShowLines = false;
            _treeNav.Font = MhsTheme.FontRegular;
            PopulateNavigationTree();
            _treeNav.AfterSelect += delegate
            {
                if (_treeNav.SelectedNode != null && _treeNav.SelectedNode.Tag != null)
                {
                    _activeFilterKey = _treeNav.SelectedNode.Tag.ToString();
                    RefreshDownloadList();
                }
            };
            leftNavPanel.Controls.Add(_treeNav);

            // Center Downloads Table
            Panel centerPanel = new Panel();
            centerPanel.Dock = DockStyle.Fill;
            centerPanel.Padding = new Padding(4, 8, 8, 8);

            _lvDownloads = new BufferedListView();
            _lvDownloads.Dock = DockStyle.Fill;
            _lvDownloads.View = View.Details;
            _lvDownloads.FullRowSelect = true;
            _lvDownloads.MultiSelect = true;
            _lvDownloads.BackColor = MhsTheme.BgSurface;
            _lvDownloads.ForeColor = MhsTheme.TextPrimary;
            _lvDownloads.BorderStyle = BorderStyle.FixedSingle;
            _lvDownloads.Columns.Add("File Name", 265);
            _lvDownloads.Columns.Add("Size", 95);
            _lvDownloads.Columns.Add("Status / Progress", 145);
            _lvDownloads.Columns.Add("Speed", 105);
            _lvDownloads.Columns.Add("ETA", 85);
            _lvDownloads.Columns.Add("Threads", 75);
            _lvDownloads.Columns.Add("Category", 95);
            _lvDownloads.Columns.Add("Queue", 95);
            _lvDownloads.Columns.Add("Added", 125);
            _lvDownloads.ContextMenuStrip = BuildContextMenu();
            _lvDownloads.SelectedIndexChanged += delegate { UpdateInspectorFromSelection(false); };
            _lvDownloads.DoubleClick += OnListDoubleClicked;
            centerPanel.Controls.Add(_lvDownloads);

            Controls.Add(centerPanel);
            Controls.Add(leftNavPanel);
            Controls.Add(inspectorPanel);
            Controls.Add(statusBar);
            Controls.Add(topToolbar);
        }

        private void PopulateNavigationTree()
        {
            _treeNav.Nodes.Clear();

            TreeNode nAll = new TreeNode("All Downloads") { Tag = "ALL" };
            nAll.Nodes.Add(new TreeNode("Compressed") { Tag = "CAT:Compressed" });
            nAll.Nodes.Add(new TreeNode("Documents") { Tag = "CAT:Documents" });
            nAll.Nodes.Add(new TreeNode("Music") { Tag = "CAT:Music" });
            nAll.Nodes.Add(new TreeNode("Programs") { Tag = "CAT:Programs" });
            nAll.Nodes.Add(new TreeNode("Video") { Tag = "CAT:Video" });
            nAll.Nodes.Add(new TreeNode("General") { Tag = "CAT:General" });
            nAll.Expand();

            TreeNode nUnfinished = new TreeNode("Unfinished") { Tag = "UNFINISHED" };
            TreeNode nFinished = new TreeNode("Finished") { Tag = "FINISHED" };

            TreeNode nQueues = new TreeNode("Queues") { Tag = "ALL" };
            for (int i = 0; i < _controller.Settings.Queues.Count; i++)
            {
                string qn = _controller.Settings.Queues[i].Name;
                nQueues.Nodes.Add(new TreeNode(qn) { Tag = "QUEUE:" + qn });
            }
            nQueues.Expand();

            _treeNav.Nodes.Add(nAll);
            _treeNav.Nodes.Add(nUnfinished);
            _treeNav.Nodes.Add(nFinished);
            _treeNav.Nodes.Add(nQueues);
            _treeNav.SelectedNode = nAll;
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = MhsTheme.BgElevated;
            menu.ForeColor = MhsTheme.TextPrimary;

            menu.Items.Add("Open Floating Progress Window", null, delegate { OpenSelectedProgressWindow(); });
            menu.Items.Add("Resume / Retry Download", null, delegate { ResumeSelected(); });
            menu.Items.Add("Pause Download", null, delegate { PauseSelected(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Downloaded File", null, delegate { OpenSelectedFile(false); });
            menu.Items.Add("Open Containing Folder", null, delegate { OpenSelectedFile(true); });
            menu.Items.Add("Verify MD5 / SHA-256 Checksum...", null, delegate { VerifySelectedChecksum(); });
            menu.Items.Add("Copy Download URL", null, delegate { CopySelectedUrl(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Remove from List", null, delegate { DeleteSelected(false); });
            menu.Items.Add("Remove & Delete File from Disk", null, delegate { DeleteSelected(true); });
            return menu;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "MHS Manager";
            _trayIcon.Icon = Icon ?? SystemIcons.Application;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open MHS Manager", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add("Add New URL...", null, delegate { RestoreFromTray(); PromptAddDownload(null, null, null); });
            trayMenu.Items.Add("Pause All Downloads", null, delegate { _controller.PauseAll(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit MHS Manager", null, delegate { _forceExit = true; Close(); });
            _trayIcon.ContextMenuStrip = trayMenu;
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        private void OnMainFormShown(object sender, EventArgs e)
        {
            RefreshDownloadList();

            if (!string.IsNullOrWhiteSpace(_startupAddUrl))
            {
                PromptAddDownload(_startupAddUrl, _startupReferer, _startupCookies);
            }

            if (_autoExitMs > 0)
            {
                System.Windows.Forms.Timer autoClose = new System.Windows.Forms.Timer();
                autoClose.Interval = _autoExitMs;
                autoClose.Tick += delegate
                {
                    autoClose.Stop();
                    _forceExit = true;
                    Close();
                };
                autoClose.Start();
            }
        }

        private void UpdateSpeedButtonVisual()
        {
            if (_controller.Settings.EnableGlobalSpeedLimit)
            {
                _btnSpeedToggle.BackColor = MhsTheme.AccentAmber;
                _btnSpeedToggle.Text = string.Format("⚡ {0} KB/s", _controller.Settings.GlobalSpeedLimitBytesPerSec / 1024);
            }
            else
            {
                _btnSpeedToggle.BackColor = MhsTheme.BgElevated;
                _btnSpeedToggle.Text = "⚡ Speed Cap";
            }
        }

        private void RefreshUiTick()
        {
            if (IsDisposed) return;

            RefreshDownloadList();
            UpdateInspectorFromSelection(true);

            List<DownloadItem> snap = _controller.GetDownloadsSnapshot();
            _lblStatusCounts.Text = string.Format(
                "Downloads: {0} total  •  {1} active",
                snap.Count,
                _controller.ActiveDownloadCount);

            string capNote = _controller.Settings.EnableGlobalSpeedLimit
                ? string.Format(" (Cap: {0})", FormattingHelpers.FormatSpeed(_controller.Settings.GlobalSpeedLimitBytesPerSec))
                : "";
            _lblStatusSpeed.Text = "Total Throughput: " + FormattingHelpers.FormatSpeed(_controller.TotalSpeedBytesPerSec) + capNote;

            if (_bridge != null && _bridge.IsRunning)
            {
                _lblStatusBridge.Text = string.Format("Browser Bridge: 127.0.0.1:{0} ONLINE", _bridge.Port);
                _lblStatusBridge.ForeColor = MhsTheme.AccentEmerald;
            }
            else
            {
                _lblStatusBridge.Text = "Browser Bridge: OFFLINE";
                _lblStatusBridge.ForeColor = MhsTheme.TextSecondary;
            }
        }

        private bool MatchesFilter(DownloadItem d, string search)
        {
            if (!string.IsNullOrEmpty(search))
            {
                bool hit = (d.FileName != null && d.FileName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (d.Url != null && d.Url.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit) return false;
            }

            if (_activeFilterKey == "ALL") return true;
            if (_activeFilterKey == "FINISHED") return d.Status == DownloadStatus.Completed;
            if (_activeFilterKey == "UNFINISHED") return d.Status != DownloadStatus.Completed;
            if (_activeFilterKey.StartsWith("CAT:", StringComparison.OrdinalIgnoreCase))
            {
                string cat = _activeFilterKey.Substring(4);
                return string.Equals(d.CategoryName, cat, StringComparison.OrdinalIgnoreCase);
            }
            if (_activeFilterKey.StartsWith("QUEUE:", StringComparison.OrdinalIgnoreCase))
            {
                string q = _activeFilterKey.Substring(6);
                return string.Equals(d.QueueName, q, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        private void RefreshDownloadList()
        {
            List<DownloadItem> all = _controller.GetDownloadsSnapshot();
            string search = (_txtSearch.Text ?? "").Trim();

            List<DownloadItem> filtered = new List<DownloadItem>();
            for (int i = 0; i < all.Count; i++)
            {
                if (MatchesFilter(all[i], search))
                {
                    filtered.Add(all[i]);
                }
            }

            Dictionary<string, ListViewItem> existingById = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _lvDownloads.Items.Count; i++)
            {
                DownloadItem tagItem = _lvDownloads.Items[i].Tag as DownloadItem;
                if (tagItem != null)
                {
                    existingById[tagItem.Id] = _lvDownloads.Items[i];
                }
            }

            _lvDownloads.BeginUpdate();
            try
            {
                bool sameOrder = _lvDownloads.Items.Count == filtered.Count;
                if (sameOrder)
                {
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        DownloadItem ti = _lvDownloads.Items[i].Tag as DownloadItem;
                        if (ti == null || ti.Id != filtered[i].Id)
                        {
                            sameOrder = false;
                            break;
                        }
                    }
                }

                if (!sameOrder)
                {
                    _lvDownloads.Items.Clear();
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        ListViewItem row;
                        if (!existingById.TryGetValue(filtered[i].Id, out row))
                        {
                            row = CreateBlankRow(filtered[i]);
                        }
                        UpdateRowSubItems(row, filtered[i]);
                        _lvDownloads.Items.Add(row);
                    }
                }
                else
                {
                    for (int i = 0; i < filtered.Count; i++)
                    {
                        UpdateRowSubItems(_lvDownloads.Items[i], filtered[i]);
                    }
                }
            }
            finally
            {
                _lvDownloads.EndUpdate();
            }
        }

        private static ListViewItem CreateBlankRow(DownloadItem item)
        {
            ListViewItem lvi = new ListViewItem(item.FileName ?? "");
            lvi.Tag = item;
            for (int i = 0; i < 8; i++) lvi.SubItems.Add("");
            return lvi;
        }

        private static void UpdateRowSubItems(ListViewItem row, DownloadItem d)
        {
            row.Tag = d;
            string c0 = d.FileName ?? "download.bin";
            string c1 = d.TotalBytes > 0 ? FormattingHelpers.FormatBytes(d.TotalBytes) : (d.DownloadedBytes > 0 ? FormattingHelpers.FormatBytes(d.DownloadedBytes) : "--");

            string c2;
            if (d.Status == DownloadStatus.Completed)
            {
                c2 = "Completed (100%)";
            }
            else if (d.Status == DownloadStatus.Downloading)
            {
                c2 = string.Format("Downloading {0:0.1}%", d.ProgressPercent);
            }
            else if (d.Status == DownloadStatus.Error)
            {
                c2 = "Error";
            }
            else
            {
                c2 = string.Format("{0} ({1:0.0}%)", d.Status, d.ProgressPercent);
            }

            string c3 = d.Status == DownloadStatus.Downloading ? FormattingHelpers.FormatSpeed(d.CurrentSpeedBytesPerSec) : "--";
            long rem = d.TotalBytes > d.DownloadedBytes ? (d.TotalBytes - d.DownloadedBytes) : 0;
            string c4 = d.Status == DownloadStatus.Downloading ? FormattingHelpers.FormatEta(rem, d.CurrentSpeedBytesPerSec) : "--";
            string c5 = d.Status == DownloadStatus.Downloading
                ? string.Format("{0}/{1}", d.ActiveConnections, d.MaxConnections)
                : d.MaxConnections.ToString();
            string c6 = d.CategoryName ?? "General";
            string c7 = d.QueueName ?? "Main Queue";
            string c8 = d.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

            if (row.SubItems[0].Text != c0) row.SubItems[0].Text = c0;
            if (row.SubItems[1].Text != c1) row.SubItems[1].Text = c1;
            if (row.SubItems[2].Text != c2) row.SubItems[2].Text = c2;
            if (row.SubItems[3].Text != c3) row.SubItems[3].Text = c3;
            if (row.SubItems[4].Text != c4) row.SubItems[4].Text = c4;
            if (row.SubItems[5].Text != c5) row.SubItems[5].Text = c5;
            if (row.SubItems[6].Text != c6) row.SubItems[6].Text = c6;
            if (row.SubItems[7].Text != c7) row.SubItems[7].Text = c7;
            if (row.SubItems[8].Text != c8) row.SubItems[8].Text = c8;
        }

        private DownloadItem GetFirstSelectedItem()
        {
            if (_lvDownloads.SelectedItems.Count == 0) return null;
            return _lvDownloads.SelectedItems[0].Tag as DownloadItem;
        }

        private List<DownloadItem> GetAllSelectedItems()
        {
            List<DownloadItem> list = new List<DownloadItem>();
            for (int i = 0; i < _lvDownloads.SelectedItems.Count; i++)
            {
                DownloadItem d = _lvDownloads.SelectedItems[i].Tag as DownloadItem;
                if (d != null) list.Add(d);
            }
            return list;
        }

        private void UpdateInspectorFromSelection(bool pushSpeedSample)
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null)
            {
                _lblInspectorTitle.Text = "Live Multi-Segment & Throughput Inspector — Global Aggregate Stream";
                _inspectorSegBar.BindItem(null);
                if (pushSpeedSample)
                {
                    _inspectorSpeedGraph.PushSample(_controller.TotalSpeedBytesPerSec);
                }
                return;
            }

            _lblInspectorTitle.Text = string.Format(
                "Inspector: {0}  •  {1} / {2}  •  {3}",
                sel.FileName,
                FormattingHelpers.FormatBytes(sel.DownloadedBytes),
                sel.TotalBytes > 0 ? FormattingHelpers.FormatBytes(sel.TotalBytes) : "Unknown",
                sel.Status);

            _inspectorSegBar.BindItem(sel);
            if (pushSpeedSample)
            {
                _inspectorSpeedGraph.PushSample(sel.CurrentSpeedBytesPerSec);
            }

            if (sel.RecentLogs != null)
            {
                string joined;
                lock (sel.RecentLogs)
                {
                    joined = string.Join("\r\n", sel.RecentLogs.ToArray());
                }
                if (_txtInspectorLog.Text != joined)
                {
                    _txtInspectorLog.Text = joined;
                    _txtInspectorLog.SelectionStart = _txtInspectorLog.Text.Length;
                    _txtInspectorLog.ScrollToCaret();
                }
            }
        }

        private void PromptAddDownload(string url, string referer, string cookies)
        {
            string initialUrl = url;
            if (string.IsNullOrWhiteSpace(initialUrl))
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText().Trim();
                        Uri u;
                        if (Uri.TryCreate(clip, UriKind.Absolute, out u) &&
                            (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                        {
                            initialUrl = clip;
                        }
                    }
                }
                catch { }
            }

            using (AddDownloadDialog dlg = new AddDownloadDialog(_controller.Settings, initialUrl, referer, cookies))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultItem != null)
                {
                    _controller.AddDownload(dlg.ResultItem, dlg.StartImmediately);
                    RefreshDownloadList();
                }
            }
        }

        private void OnControllerDownloadStarted(DownloadItem item)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (_controller.Settings.ShowProgressDialogOnStart && _autoExitMs <= 0)
                {
                    ShowFloatingProgressWindow(item);
                }
            });
        }

        private void OnControllerDownloadCompleted(DownloadItem item)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                RefreshDownloadList();
                try
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.ShowBalloonTip(
                            3000,
                            "Download Complete — MHS Manager",
                            item.FileName + " (" + FormattingHelpers.FormatBytes(item.TotalBytes) + ")",
                            ToolTipIcon.Info);
                    }
                }
                catch { }
            });
        }

        private void OnBridgeRequestShowAddDialog(BrowserDownloadRequest req)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                RestoreFromTray();
                PromptAddDownload(req.url, req.referer, req.cookies);
            });
        }

        private void ShowFloatingProgressWindow(DownloadItem item)
        {
            if (item == null) return;
            DownloadProgressDialog existing;
            if (_openDialogs.TryGetValue(item.Id, out existing) && existing != null && !existing.IsDisposed)
            {
                existing.Activate();
                return;
            }

            DownloadProgressDialog dlg = new DownloadProgressDialog(_controller, item);
            _openDialogs[item.Id] = dlg;
            dlg.FormClosed += delegate { _openDialogs.Remove(item.Id); };
            dlg.Show(this);
        }

        private void OpenSelectedProgressWindow()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel != null) ShowFloatingProgressWindow(sel);
        }

        private void ResumeSelected()
        {
            List<DownloadItem> items = GetAllSelectedItems();
            for (int i = 0; i < items.Count; i++)
            {
                _controller.StartDownload(items[i].Id);
            }
            RefreshDownloadList();
        }

        private void PauseSelected()
        {
            List<DownloadItem> items = GetAllSelectedItems();
            for (int i = 0; i < items.Count; i++)
            {
                _controller.PauseDownload(items[i].Id);
            }
            RefreshDownloadList();
        }

        private void DeleteSelected(bool deleteDiskFiles)
        {
            List<DownloadItem> items = GetAllSelectedItems();
            if (items.Count == 0) return;

            if (deleteDiskFiles)
            {
                DialogResult ans = MessageBox.Show(
                    this,
                    string.Format("Delete {0} download entry(s) AND permanently delete downloaded file(s) from disk?", items.Count),
                    "Confirm Disk Deletion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (ans != DialogResult.Yes) return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                _controller.RemoveDownload(items[i].Id, deleteDiskFiles);
            }
            RefreshDownloadList();
        }

        private void OpenSelectedFile(bool openContainingFolder)
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;

            try
            {
                if (openContainingFolder)
                {
                    if (File.Exists(sel.FullTargetPath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + sel.FullTargetPath + "\"");
                    }
                    else if (Directory.Exists(sel.SaveDirectory))
                    {
                        Process.Start("explorer.exe", "\"" + sel.SaveDirectory + "\"");
                    }
                }
                else if (File.Exists(sel.FullTargetPath))
                {
                    Process.Start(new ProcessStartInfo(sel.FullTargetPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open File", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void VerifySelectedChecksum()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;
            if (!File.Exists(sel.FullTargetPath))
            {
                MessageBox.Show(this, "Completed file is not on disk yet.", "Checksum Verifier", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (ChecksumDialog dlg = new ChecksumDialog(sel.FullTargetPath, sel.ExpectedSha256))
            {
                dlg.ShowDialog(this);
            }
        }

        private void CopySelectedUrl()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null || string.IsNullOrEmpty(sel.Url)) return;
            try
            {
                _lastClipboardSeen = sel.Url;
                Clipboard.SetText(sel.Url);
            }
            catch { }
        }

        private void OnListDoubleClicked(object sender, EventArgs e)
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;
            if (sel.Status == DownloadStatus.Completed && File.Exists(sel.FullTargetPath))
            {
                OpenSelectedFile(true);
            }
            else
            {
                ShowFloatingProgressWindow(sel);
            }
        }

        private void OpenBatchDialog()
        {
            using (BatchDownloadDialog dlg = new BatchDownloadDialog(_controller.Settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < dlg.SelectedUrls.Count; i++)
                    {
                        string u = dlg.SelectedUrls[i];
                        string fn = CategoryClassifier.GuessFileNameFromUrl(u);
                        string cat = CategoryClassifier.ClassifyByFileName(fn);
                        DownloadItem item = new DownloadItem
                        {
                            Url = u,
                            FileName = fn,
                            CategoryName = cat,
                            QueueName = dlg.SelectedQueue,
                            SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat),
                            MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
                        };
                        _controller.AddDownload(item, false);
                    }
                    if (dlg.StartImmediately)
                    {
                        _controller.StartQueue(dlg.SelectedQueue);
                    }
                    RefreshDownloadList();
                }
            }
        }

        private void OpenGrabberDialog()
        {
            using (SiteGrabberDialog dlg = new SiteGrabberDialog(_controller.Settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < dlg.ChosenLinks.Count; i++)
                    {
                        GrabbedLink g = dlg.ChosenLinks[i];
                        DownloadItem item = new DownloadItem
                        {
                            Url = g.Url,
                            FileName = g.FileName,
                            CategoryName = g.Category,
                            Referer = dlg.RefererPageUrl,
                            SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_controller.Settings, g.Category),
                            MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
                        };
                        _controller.AddDownload(item, false);
                    }
                    RefreshDownloadList();
                }
            }
        }

        private void OpenSettingsDialog(int tabIndex)
        {
            using (SettingsAndSchedulerDialog dlg = new SettingsAndSchedulerDialog(_controller, _bridge, tabIndex))
            {
                dlg.ShowDialog(this);
                UpdateSpeedButtonVisual();
                RefreshDownloadList();
            }
        }

        private void HandleClipboardChanged()
        {
            if (!_controller.Settings.EnableClipboardMonitor || _clipboardDialogOpen || _autoExitMs > 0)
            {
                return;
            }

            try
            {
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText().Trim();
                if (string.IsNullOrEmpty(text) || text.Length > 2048) return;
                if (text == _lastClipboardSeen) return;
                _lastClipboardSeen = text;

                Uri uri;
                if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) return;
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

                if (!MatchesMonitoredExtension(uri.AbsolutePath)) return;

                _clipboardDialogOpen = true;
                BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        RestoreFromTray();
                        PromptAddDownload(text, null, null);
                    }
                    finally
                    {
                        _clipboardDialogOpen = false;
                    }
                });
            }
            catch
            {
                _clipboardDialogOpen = false;
            }
        }

        private bool MatchesMonitoredExtension(string urlPath)
        {
            if (string.IsNullOrEmpty(urlPath)) return false;
            string ext = Path.GetExtension(urlPath).TrimStart('.');
            if (string.IsNullOrEmpty(ext)) return false;

            string[] allowed = (_controller.Settings.MonitoredExtensions ?? "").Split(
                new char[] { '|', ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < allowed.Length; i++)
            {
                if (string.Equals(allowed[i].Trim().TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void OnFormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string dropped = (e.Data.GetData(DataFormats.UnicodeText) ?? e.Data.GetData(DataFormats.Text) ?? "").ToString().Trim();
                if (!string.IsNullOrEmpty(dropped))
                {
                    PromptAddDownload(dropped, null, null);
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit &&
                e.CloseReason == CloseReason.UserClosing &&
                _controller.Settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _uiTimer.Stop();
            _uiTimer.Dispose();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}

