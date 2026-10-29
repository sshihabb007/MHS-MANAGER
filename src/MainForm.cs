using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        private readonly StartupLaunchOptions _launch;
        private readonly Dictionary<string, DownloadProgressDialog> _openDialogs;
        private readonly System.Windows.Forms.Timer _uiTimer;

        private MenuStrip _menuBar;
        private Panel _leftNavPanel;
        private Panel _inspectorPanel;
        private TreeView _treeNav;
        private BufferedListView _lvDownloads;
        private TextBox _txtSearch;
        private Button _btnSpeedToggle;
        private Button _btnDropTarget;
        private SegmentVisualizerBar _inspectorSegBar;
        private SpeedGraphControl _inspectorSpeedGraph;
        private TextBox _txtInspectorLog;
        private Label _lblInspectorTitle;
        private Label _lblStatusCounts;
        private Label _lblStatusSpeed;
        private Label _lblStatusBridge;
        private NotifyIcon _trayIcon;
        private DropTargetForm _dropTargetForm;
        private ToolStripMenuItem _miShowDropTargetTasks;
        private ToolStripMenuItem _miShowDropTargetView;

        private string _activeFilterKey = "ALL";
        private int _sortColumnIndex = -1;
        private bool _sortAscending = true;
        private string _lastClipboardSeen = "";
        private bool _clipboardDialogOpen;
        private bool _clipboardRegistered;
        private bool _hadWorkSinceLaunch;
        private bool _forceExit;

        public MainForm(
            DownloadManagerController controller,
            BrowserBridgeServer bridge,
            string startupAddUrl,
            string startupReferer,
            string startupCookies,
            int autoExitMs)
            : this(controller, bridge, new StartupLaunchOptions
            {
                AddUrl = startupAddUrl,
                Referer = startupReferer,
                Cookies = startupCookies,
                AutoExitMs = autoExitMs
            })
        {
        }

        public MainForm(
            DownloadManagerController controller,
            BrowserBridgeServer bridge,
            StartupLaunchOptions launch)
        {
            _controller = controller;
            _bridge = bridge;
            _launch = launch ?? new StartupLaunchOptions();
            _openDialogs = new Dictionary<string, DownloadProgressDialog>(StringComparer.OrdinalIgnoreCase);

            Text = "MHS Manager — Multi-Connection Download Accelerator";
            Size = new Size(1260, 740);
            MinimumSize = new Size(980, 580);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;
            MhsTheme.StyleForm(this);

            TryLoadExecutableIcon();
            BuildLayout();
            InitializeTrayIcon();

            _controller.DownloadStarted += OnControllerDownloadStarted;
            _controller.DownloadCompleted += OnControllerDownloadCompleted;
            _controller.QueuePostActionTriggered += OnQueuePostActionTriggered;

            if (_bridge != null)
            {
                _bridge.RequestShowAddDialog += OnBridgeRequestShowAddDialog;
                _bridge.RequestShowBatchDialog += OnBridgeRequestShowBatchDialog;
            }

            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 500;
            _uiTimer.Tick += delegate { RefreshUiTick(); };
            _uiTimer.Start();

            Shown += OnMainFormShown;
        }

        private bool IsAutomatedSilentSession
        {
            get
            {
                return _launch.AutoExitMs > 0 || _launch.ExitOnComplete || _launch.SilentMode;
            }
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.N)
            {
                e.Handled = true;
                PromptAddDownload(null, null, null);
            }
            else if (e.Control && e.KeyCode == Keys.F)
            {
                e.Handled = true;
                _txtSearch.Focus();
                _txtSearch.SelectAll();
            }
            else if (e.Control && e.KeyCode == Keys.G)
            {
                e.Handled = true;
                OpenGrabberDialog();
            }
            else if (e.Alt && e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                OpenSelectedProperties();
            }
            else if (e.KeyCode == Keys.Delete && !_txtSearch.Focused)
            {
                e.Handled = true;
                DeleteSelected(e.Shift);
            }
        }

        private void BuildLayout()
        {
            // Classic IDM Menu Bar
            _menuBar = BuildMainMenuStrip();

            // Top Toolbar
            Panel topToolbar = new Panel();
            topToolbar.Dock = DockStyle.Top;
            topToolbar.Height = 50;
            topToolbar.BackColor = MhsTheme.BgSurface;
            topToolbar.Padding = new Padding(8, 9, 8, 9);

            Panel searchPanel = new Panel();
            searchPanel.Dock = DockStyle.Right;
            searchPanel.Width = 205;
            _txtSearch = MhsTheme.CreateTextBox(false);
            _txtSearch.SetBounds(4, 4, 196, 25);
            _txtSearch.TextChanged += delegate { RefreshDownloadList(); };
            ToolTip searchTip = new ToolTip();
            searchTip.SetToolTip(_txtSearch, "Search downloads by file name or URL (Ctrl+F)");
            searchPanel.Controls.Add(_txtSearch);

            FlowLayoutPanel flowBtns = new FlowLayoutPanel();
            flowBtns.Dock = DockStyle.Fill;
            flowBtns.WrapContents = false;
            flowBtns.BackColor = Color.Transparent;

            Button btnAdd = MhsTheme.CreateToolbarButton("+ Add URL", MhsTheme.AccentEmerald, 82);
            btnAdd.Click += delegate { PromptAddDownload(null, null, null); };

            Button btnResume = MhsTheme.CreateToolbarButton("▶ Resume", MhsTheme.AccentSky, 76);
            btnResume.Click += delegate { ResumeSelected(); };

            Button btnPause = MhsTheme.CreateToolbarButton("⏸ Stop", MhsTheme.BgElevated, 68);
            btnPause.Click += delegate { PauseSelected(); };

            Button btnStopAll = MhsTheme.CreateToolbarButton("⏹ Stop All", MhsTheme.BgElevated, 78);
            btnStopAll.Click += delegate { _controller.PauseAll(); RefreshDownloadList(); };

            Button btnClearDone = MhsTheme.CreateToolbarButton("✓ Clear Done", MhsTheme.BgElevated, 88);
            btnClearDone.Click += delegate { _controller.ClearCompleted(); RefreshDownloadList(); };

            Button btnStartQ = MhsTheme.CreateToolbarButton("▶ Start Q", MhsTheme.BgElevated, 76);
            btnStartQ.Click += delegate { _controller.StartQueue("Main Queue"); RefreshDownloadList(); };

            Button btnStopQ = MhsTheme.CreateToolbarButton("⏹ Stop Q", MhsTheme.BgElevated, 74);
            btnStopQ.Click += delegate { _controller.StopQueue("Main Queue"); RefreshDownloadList(); };

            Button btnBatch = MhsTheme.CreateToolbarButton("🔢 Batch", MhsTheme.BgElevated, 70);
            btnBatch.Click += delegate { OpenBatchDialog(); };

            Button btnGrabber = MhsTheme.CreateToolbarButton("🕷 Grabber", MhsTheme.BgElevated, 78);
            btnGrabber.Click += delegate { OpenGrabberDialog(); };

            Button btnScheduler = MhsTheme.CreateToolbarButton("⏰ Scheduler", MhsTheme.BgElevated, 86);
            btnScheduler.Click += delegate { OpenSettingsDialog(1); };

            _btnSpeedToggle = MhsTheme.CreateToolbarButton("⚡ Speed Cap", MhsTheme.BgElevated, 88);
            _btnSpeedToggle.Click += delegate
            {
                _controller.Settings.EnableGlobalSpeedLimit = !_controller.Settings.EnableGlobalSpeedLimit;
                _controller.ApplySettingsChanged();
                UpdateSpeedButtonVisual();
            };

            _btnDropTarget = MhsTheme.CreateToolbarButton("🎯 Drop Box", MhsTheme.BgElevated, 82);
            _btnDropTarget.Click += delegate { ToggleDropTarget(); };

            Button btnOptions = MhsTheme.CreateToolbarButton("⚙ Options", MhsTheme.BgElevated, 76);
            btnOptions.Click += delegate { OpenSettingsDialog(0); };

            flowBtns.Controls.AddRange(new Control[]
            {
                btnAdd, btnResume, btnPause, btnStopAll, btnClearDone,
                btnStartQ, btnStopQ, btnBatch, btnGrabber, btnScheduler,
                _btnSpeedToggle, _btnDropTarget, btnOptions
            });
            UpdateSpeedButtonVisual();

            topToolbar.Controls.Add(flowBtns);
            topToolbar.Controls.Add(searchPanel);

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
            _inspectorPanel = new Panel();
            _inspectorPanel.Dock = DockStyle.Bottom;
            _inspectorPanel.Height = 180;
            _inspectorPanel.BackColor = MhsTheme.BgSurface;
            _inspectorPanel.Padding = new Padding(12, 8, 12, 8);

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

            _inspectorPanel.Controls.Add(bottomSplit);
            _inspectorPanel.Controls.Add(_inspectorSegBar);
            _inspectorPanel.Controls.Add(_lblInspectorTitle);

            // Left Category & Queue Tree
            _leftNavPanel = new Panel();
            _leftNavPanel.Dock = DockStyle.Left;
            _leftNavPanel.Width = 205;
            _leftNavPanel.Padding = new Padding(8, 8, 4, 8);
            _leftNavPanel.BackColor = MhsTheme.BgDark;

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
            _leftNavPanel.Controls.Add(_treeNav);

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
            _lvDownloads.Columns.Add("File Name", 245);
            _lvDownloads.Columns.Add("Size", 90);
            _lvDownloads.Columns.Add("Status", 138);
            _lvDownloads.Columns.Add("Transfer Rate", 100);
            _lvDownloads.Columns.Add("Time Left", 80);
            _lvDownloads.Columns.Add("Resume", 64);
            _lvDownloads.Columns.Add("Threads", 66);
            _lvDownloads.Columns.Add("Category", 88);
            _lvDownloads.Columns.Add("Queue", 105);
            _lvDownloads.Columns.Add("Last Try / Added", 130);
            _lvDownloads.ContextMenuStrip = BuildContextMenu();
            _lvDownloads.SelectedIndexChanged += delegate { UpdateInspectorFromSelection(false); };
            _lvDownloads.DoubleClick += OnListDoubleClicked;
            _lvDownloads.ColumnClick += OnColumnClicked;
            _lvDownloads.ItemDrag += OnDownloadItemDrag;
            centerPanel.Controls.Add(_lvDownloads);

            MainMenuStrip = _menuBar;
            Controls.Add(centerPanel);
            Controls.Add(_leftNavPanel);
            Controls.Add(_inspectorPanel);
            Controls.Add(statusBar);
            Controls.Add(topToolbar);
            Controls.Add(_menuBar);
        }

        private MenuStrip BuildMainMenuStrip()
        {
            MenuStrip ms = new MenuStrip();
            ms.BackColor = MhsTheme.BgDark;
            ms.ForeColor = MhsTheme.TextPrimary;
            ms.Renderer = MhsTheme.CreateDarkMenuRenderer();

            // Tasks Menu
            ToolStripMenuItem mTasks = new ToolStripMenuItem("&Tasks");
            mTasks.DropDownItems.Add("Add New Download...\tCtrl+N", null, delegate { PromptAddDownload(null, null, null); });
            mTasks.DropDownItems.Add("Add Batch Download (Wildcard)...", null, delegate { OpenBatchDialog(); });
            mTasks.DropDownItems.Add("Add Batch from Clipboard...", null, delegate { ImportBatchFromClipboard(); });
            mTasks.DropDownItems.Add("Run Site Grabber...\tCtrl+G", null, delegate { OpenGrabberDialog(); });
            mTasks.DropDownItems.Add(new ToolStripSeparator());

            _miShowDropTargetTasks = new ToolStripMenuItem("Show Drop Target");
            _miShowDropTargetTasks.Checked = _controller.Settings.ShowDropTarget;
            _miShowDropTargetTasks.Click += delegate { ToggleDropTarget(); };
            mTasks.DropDownItems.Add(_miShowDropTargetTasks);
            mTasks.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem mImport = new ToolStripMenuItem("Import");
            mImport.DropDownItems.Add("From IDM Export File (.ef2)...", null, delegate { ImportDownloadsFromFile(true); });
            mImport.DropDownItems.Add("From Text File (.txt)...", null, delegate { ImportDownloadsFromFile(false); });
            mTasks.DropDownItems.Add(mImport);

            ToolStripMenuItem mExport = new ToolStripMenuItem("Export");
            mExport.DropDownItems.Add("To IDM Export File (.ef2)...", null, delegate { ExportDownloadsToFile(true); });
            mExport.DropDownItems.Add("To Text File (.txt)...", null, delegate { ExportDownloadsToFile(false); });
            mTasks.DropDownItems.Add(mExport);

            mTasks.DropDownItems.Add(new ToolStripSeparator());
            mTasks.DropDownItems.Add("Exit", null, delegate { _forceExit = true; Close(); });

            // File Menu
            ToolStripMenuItem mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add("Resume / Download Now", null, delegate { ResumeSelected(); });
            mFile.DropDownItems.Add("Stop / Pause Download", null, delegate { PauseSelected(); });
            mFile.DropDownItems.Add("Redownload from Beginning", null, delegate { RedownloadSelected(); });
            mFile.DropDownItems.Add("Refresh Download Address...", null, delegate { RefreshSelectedAddress(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Open", null, delegate { OpenSelectedFile(false); });
            mFile.DropDownItems.Add("Open With...", null, delegate { OpenSelectedFileWith(); });
            mFile.DropDownItems.Add("Open Folder", null, delegate { OpenSelectedFile(true); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Remove\tDel", null, delegate { DeleteSelected(false); });
            mFile.DropDownItems.Add("Remove & Delete File\tShift+Del", null, delegate { DeleteSelected(true); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Properties...\tAlt+Enter", null, delegate { OpenSelectedProperties(); });

            // Downloads Menu
            ToolStripMenuItem mDownloads = new ToolStripMenuItem("&Downloads");
            mDownloads.DropDownItems.Add("Resume All Unfinished", null, delegate { _controller.ResumeAllUnfinished(); RefreshDownloadList(); });
            mDownloads.DropDownItems.Add("Pause / Stop All", null, delegate { _controller.PauseAll(); RefreshDownloadList(); });
            mDownloads.DropDownItems.Add("Delete All Completed", null, delegate { _controller.ClearCompleted(); RefreshDownloadList(); });
            mDownloads.DropDownItems.Add(new ToolStripSeparator());
            mDownloads.DropDownItems.Add("Start Main Queue", null, delegate { _controller.StartQueue("Main Queue"); RefreshDownloadList(); });
            mDownloads.DropDownItems.Add("Stop Main Queue", null, delegate { _controller.StopQueue("Main Queue"); RefreshDownloadList(); });
            mDownloads.DropDownItems.Add("Scheduler & Queues...", null, delegate { OpenSettingsDialog(1); });
            mDownloads.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem mSpeed = new ToolStripMenuItem("Speed Limiter");
            mSpeed.DropDownItems.Add("Turn On / Off", null, delegate
            {
                _controller.Settings.EnableGlobalSpeedLimit = !_controller.Settings.EnableGlobalSpeedLimit;
                _controller.ApplySettingsChanged();
                UpdateSpeedButtonVisual();
            });
            mSpeed.DropDownItems.Add("Speed Limiter Settings...", null, delegate { OpenSettingsDialog(0); });
            mDownloads.DropDownItems.Add(mSpeed);

            mDownloads.DropDownItems.Add("Find...\tCtrl+F", null, delegate { _txtSearch.Focus(); _txtSearch.SelectAll(); });
            mDownloads.DropDownItems.Add(new ToolStripSeparator());
            mDownloads.DropDownItems.Add("Options...", null, delegate { OpenSettingsDialog(0); });

            // View Menu
            ToolStripMenuItem mView = new ToolStripMenuItem("&View");
            ToolStripMenuItem miToggleTree = new ToolStripMenuItem("Show Categories & Queues") { Checked = true };
            miToggleTree.Click += delegate
            {
                miToggleTree.Checked = !miToggleTree.Checked;
                _leftNavPanel.Visible = miToggleTree.Checked;
            };
            mView.DropDownItems.Add(miToggleTree);

            ToolStripMenuItem miToggleInsp = new ToolStripMenuItem("Show Live Segment Inspector") { Checked = true };
            miToggleInsp.Click += delegate
            {
                miToggleInsp.Checked = !miToggleInsp.Checked;
                _inspectorPanel.Visible = miToggleInsp.Checked;
            };
            mView.DropDownItems.Add(miToggleInsp);

            _miShowDropTargetView = new ToolStripMenuItem("Show Drop Target") { Checked = _controller.Settings.ShowDropTarget };
            _miShowDropTargetView.Click += delegate { ToggleDropTarget(); };
            mView.DropDownItems.Add(_miShowDropTargetView);

            // Help Menu
            ToolStripMenuItem mHelp = new ToolStripMenuItem("&Help");
            mHelp.DropDownItems.Add("Browser Extension & Native Host Setup...", null, delegate { OpenSettingsDialog(4); });
            mHelp.DropDownItems.Add("Command-Line Switches (IDM Compatible)...", null, delegate { ShowCliReference(); });
            mHelp.DropDownItems.Add(new ToolStripSeparator());
            mHelp.DropDownItems.Add("About MHS Manager...", null, delegate
            {
                MessageBox.Show(
                    this,
                    "MHS Manager v1.1.0\r\n\r\n" +
                    "Clean-room Multi-Connection Download Accelerator & HLS Stream Merger.\r\n" +
                    "• Dynamic HTTP/1.1 & HTTPS Byte-Range Work Stealing (up to 32 connections)\r\n" +
                    "• ETag / Last-Modified Safe Resume Validation & Refresh Download Address\r\n" +
                    "• HLS (.m3u8) MPEG-TS & fMP4 (#EXT-X-MAP / #EXT-X-BYTERANGE / AES-128) Merger\r\n" +
                    "• Authenticated Browser Extension + Native Messaging Host (com.mhsmanager.host)\r\n" +
                    "• IDM-Compatible CLI Switches (/d /p /f /n /a /q /h /s) & .ef2 Import/Export",
                    "About MHS Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });

            ms.Items.AddRange(new ToolStripItem[] { mTasks, mFile, mDownloads, mView, mHelp });
            return ms;
        }

        private void ShowCliReference()
        {
            MessageBox.Show(
                this,
                "IDM-Compatible Command-Line Switches:\r\n\r\n" +
                "MHSManager.exe /d <URL> [/p <local_path>] [/f <local_filename>] [/n] [/a] [/q] [/h]\r\n" +
                "MHSManager.exe /s\r\n\r\n" +
                "  /d <URL>    Download specified file\r\n" +
                "  /p <path>   Define local folder to save file\r\n" +
                "  /f <name>   Define local file name\r\n" +
                "  /n          Silent mode (start immediately without confirmation dialog)\r\n" +
                "  /a          Add file to download queue without starting immediately\r\n" +
                "  /q          Exit MHS Manager after download completes\r\n" +
                "  /h          Stop queue processing after active downloads finish\r\n" +
                "  /s          Start Main Queue immediately",
                "Command-Line Reference — MHS Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void PopulateNavigationTree()
        {
            string prevTag = _treeNav.SelectedNode != null && _treeNav.SelectedNode.Tag != null
                ? _treeNav.SelectedNode.Tag.ToString()
                : "ALL";

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
            _treeNav.SelectedNode = FindTreeNodeByTag(_treeNav.Nodes, prevTag) ?? nAll;
        }

        private static TreeNode FindTreeNodeByTag(TreeNodeCollection nodes, string tag)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Tag != null && string.Equals(nodes[i].Tag.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    return nodes[i];
                }
                TreeNode child = FindTreeNodeByTag(nodes[i].Nodes, tag);
                if (child != null) return child;
            }
            return null;
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = MhsTheme.CreateDarkMenuRenderer();

            menu.Items.Add("Open", null, delegate { OpenSelectedFile(false); });
            menu.Items.Add("Open With...", null, delegate { OpenSelectedFileWith(); });
            menu.Items.Add("Open Folder", null, delegate { OpenSelectedFile(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Floating Progress Window", null, delegate { OpenSelectedProgressWindow(); });
            menu.Items.Add("Resume / Download Now", null, delegate { ResumeSelected(); });
            menu.Items.Add("Stop / Pause Download", null, delegate { PauseSelected(); });
            menu.Items.Add("Redownload from Beginning", null, delegate { RedownloadSelected(); });
            menu.Items.Add("Refresh Download Address...", null, delegate { RefreshSelectedAddress(); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem mMoveQueue = new ToolStripMenuItem("Move to Queue");
            menu.Opening += delegate
            {
                mMoveQueue.DropDownItems.Clear();
                for (int i = 0; i < _controller.Settings.Queues.Count; i++)
                {
                    string qName = _controller.Settings.Queues[i].Name;
                    mMoveQueue.DropDownItems.Add(qName, null, delegate { MoveSelectedToQueue(qName); });
                }
            };
            menu.Items.Add(mMoveQueue);

            menu.Items.Add("Verify MD5 / SHA-256 Checksum...", null, delegate { VerifySelectedChecksum(); });
            menu.Items.Add("Copy Download URL", null, delegate { CopySelectedUrl(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Remove\tDel", null, delegate { DeleteSelected(false); });
            menu.Items.Add("Remove & Delete File from Disk\tShift+Del", null, delegate { DeleteSelected(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Properties...\tAlt+Enter", null, delegate { OpenSelectedProperties(); });
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
            trayMenu.Renderer = MhsTheme.CreateDarkMenuRenderer();
            trayMenu.Items.Add("Open MHS Manager", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add("Add New Download...", null, delegate { RestoreFromTray(); PromptAddDownload(null, null, null); });
            trayMenu.Items.Add("Show / Hide Drop Target", null, delegate { ToggleDropTarget(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Start Main Queue", null, delegate { _controller.StartQueue("Main Queue"); });
            trayMenu.Items.Add("Stop All Downloads", null, delegate { _controller.PauseAll(); });
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

        private void ToggleDropTarget()
        {
            SetDropTargetVisible(!_controller.Settings.ShowDropTarget);
        }

        private void SetDropTargetVisible(bool show)
        {
            _controller.Settings.ShowDropTarget = show;
            _controller.SaveStateSafe();

            if (_miShowDropTargetTasks != null) _miShowDropTargetTasks.Checked = show;
            if (_miShowDropTargetView != null) _miShowDropTargetView.Checked = show;
            _btnDropTarget.BackColor = show ? MhsTheme.AccentSky : MhsTheme.BgElevated;

            if (show)
            {
                if (_dropTargetForm == null || _dropTargetForm.IsDisposed)
                {
                    _dropTargetForm = new DropTargetForm(
                        _controller,
                        delegate(string url)
                        {
                            RestoreFromTray();
                            PromptAddDownload(url, null, null);
                        },
                        delegate { RestoreFromTray(); },
                        delegate { SetDropTargetVisible(false); });
                }
                _dropTargetForm.Show();
            }
            else if (_dropTargetForm != null && !_dropTargetForm.IsDisposed)
            {
                _dropTargetForm.Close();
                _dropTargetForm = null;
            }
        }

        private void OnMainFormShown(object sender, EventArgs e)
        {
            RefreshDownloadList();

            if (_controller.Settings.ShowDropTarget && !IsAutomatedSilentSession)
            {
                SetDropTargetVisible(true);
            }

            if (_launch.StartQueueNow)
            {
                _controller.StartQueue("Main Queue");
                _hadWorkSinceLaunch = true;
            }

            if (!string.IsNullOrWhiteSpace(_launch.AddUrl))
            {
                if (_launch.SilentMode || _launch.HoldInQueue)
                {
                    bool hasExplicitName = !string.IsNullOrWhiteSpace(_launch.ExplicitFileName);
                    string fn = hasExplicitName
                        ? _launch.ExplicitFileName
                        : CategoryClassifier.GuessFileNameFromUrl(_launch.AddUrl);
                    string cat = CategoryClassifier.ClassifyByFileName(fn);
                    string dir = !string.IsNullOrWhiteSpace(_launch.SaveDirectory)
                        ? Path.GetFullPath(_launch.SaveDirectory)
                        : CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat);

                    DownloadItem cliItem = new DownloadItem
                    {
                        Url = _launch.AddUrl.Trim(),
                        FileName = fn,
                        HasExplicitFileName = hasExplicitName,
                        OverwriteExisting = hasExplicitName,
                        CategoryName = cat,
                        SaveDirectory = dir,
                        Referer = _launch.Referer,
                        Cookies = _launch.Cookies,
                        MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
                    };

                    bool startNow = !_launch.HoldInQueue;
                    _controller.AddDownload(cliItem, startNow, _launch.HoldInQueue);
                    _hadWorkSinceLaunch = startNow;
                    RefreshDownloadList();

                    if (_launch.HoldInQueue && _launch.ExitOnComplete)
                    {
                        _forceExit = true;
                        Close();
                        return;
                    }
                }
                else
                {
                    PromptAddDownload(_launch.AddUrl, _launch.Referer, _launch.Cookies);
                }
            }

            if (_launch.AutoExitMs > 0)
            {
                System.Windows.Forms.Timer autoClose = new System.Windows.Forms.Timer();
                autoClose.Interval = _launch.AutoExitMs;
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

            if (_dropTargetForm != null && !_dropTargetForm.IsDisposed)
            {
                _dropTargetForm.RefreshOverlay();
            }

            int activeNow = _controller.ActiveDownloadCount;
            if (activeNow > 0)
            {
                _hadWorkSinceLaunch = true;
            }

            List<DownloadItem> snap = _controller.GetDownloadsSnapshot();
            _lblStatusCounts.Text = string.Format(
                "Downloads: {0} total  •  {1} active",
                snap.Count,
                activeNow);

            string capNote = _controller.Settings.EnableGlobalSpeedLimit
                ? string.Format(" (Cap: {0})", FormattingHelpers.FormatSpeed(_controller.Settings.GlobalSpeedLimitBytesPerSec))
                : "";
            _lblStatusSpeed.Text = "Total Throughput: " + FormattingHelpers.FormatSpeed(_controller.TotalSpeedBytesPerSec) + capNote;

            if (_bridge != null && _bridge.IsRunning)
            {
                _lblStatusBridge.Text = string.Format("Browser Bridge: 127.0.0.1:{0} ONLINE (Token Protected)", _bridge.Port);
                _lblStatusBridge.ForeColor = MhsTheme.AccentEmerald;
            }
            else
            {
                _lblStatusBridge.Text = "Browser Bridge: OFFLINE";
                _lblStatusBridge.ForeColor = MhsTheme.TextSecondary;
            }

            if (_hadWorkSinceLaunch && !_controller.HasActiveOrQueuedWork())
            {
                if (_launch.StopQueueOnComplete)
                {
                    _controller.StopQueue("Main Queue");
                }
                if (_launch.ExitOnComplete)
                {
                    _forceExit = true;
                    Close();
                }
            }
        }

        private bool MatchesFilter(DownloadItem d, string search)
        {
            if (!string.IsNullOrEmpty(search))
            {
                bool hit = (d.FileName != null && d.FileName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (d.Url != null && d.Url.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (d.Description != null && d.Description.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private void OnColumnClicked(object sender, ColumnClickEventArgs e)
        {
            if (_sortColumnIndex == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumnIndex = e.Column;
                _sortAscending = true;
            }
            RefreshDownloadList();
        }

        private void SortFilteredList(List<DownloadItem> list)
        {
            if (_sortColumnIndex < 0 || list.Count <= 1) return;
            int col = _sortColumnIndex;
            int dir = _sortAscending ? 1 : -1;

            list.Sort(delegate(DownloadItem a, DownloadItem b)
            {
                int cmp = 0;
                switch (col)
                {
                    case 0:
                        cmp = string.Compare(a.FileName ?? "", b.FileName ?? "", StringComparison.OrdinalIgnoreCase);
                        break;
                    case 1:
                        cmp = a.TotalBytes.CompareTo(b.TotalBytes);
                        break;
                    case 2:
                        cmp = a.ProgressPercent.CompareTo(b.ProgressPercent);
                        if (cmp == 0) cmp = a.Status.CompareTo(b.Status);
                        break;
                    case 3:
                        cmp = a.CurrentSpeedBytesPerSec.CompareTo(b.CurrentSpeedBytesPerSec);
                        break;
                    case 5:
                        cmp = a.SupportsRanges.CompareTo(b.SupportsRanges);
                        break;
                    case 7:
                        cmp = string.Compare(a.CategoryName ?? "", b.CategoryName ?? "", StringComparison.OrdinalIgnoreCase);
                        break;
                    case 8:
                        cmp = string.Compare(a.QueueName ?? "", b.QueueName ?? "", StringComparison.OrdinalIgnoreCase);
                        break;
                    case 9:
                        DateTime ta = a.LastTryUtc ?? a.AddedUtc;
                        DateTime tb = b.LastTryUtc ?? b.AddedUtc;
                        cmp = ta.CompareTo(tb);
                        break;
                    default:
                        cmp = a.AddedUtc.CompareTo(b.AddedUtc);
                        break;
                }
                return cmp * dir;
            });
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

            SortFilteredList(filtered);

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
            for (int i = 0; i < 9; i++) lvi.SubItems.Add("");
            return lvi;
        }

        private static void UpdateRowSubItems(ListViewItem row, DownloadItem d)
        {
            row.Tag = d;
            string c0 = d.FileName ?? "download.bin";
            string c1 = d.TotalBytes > 0
                ? FormattingHelpers.FormatBytes(d.TotalBytes)
                : (d.DownloadedBytes > 0 ? FormattingHelpers.FormatBytes(d.DownloadedBytes) : "--");

            string c2;
            if (d.Status == DownloadStatus.Completed)
            {
                c2 = "Complete";
            }
            else if (d.Status == DownloadStatus.Downloading)
            {
                c2 = string.Format("{0:0.1}%", d.ProgressPercent);
            }
            else if (d.Status == DownloadStatus.Error)
            {
                if (d.NextRetryUtc.HasValue)
                {
                    int secLeft = Math.Max(1, (int)(d.NextRetryUtc.Value - DateTime.UtcNow).TotalSeconds);
                    c2 = string.Format("Retry in {0}s", secLeft);
                }
                else
                {
                    c2 = "Error";
                }
            }
            else
            {
                c2 = string.Format("{0} ({1:0.0}%)", d.Status, d.ProgressPercent);
            }

            string c3 = d.Status == DownloadStatus.Downloading ? FormattingHelpers.FormatSpeed(d.CurrentSpeedBytesPerSec) : "--";
            long rem = d.TotalBytes > d.DownloadedBytes ? (d.TotalBytes - d.DownloadedBytes) : 0;
            string c4 = d.Status == DownloadStatus.Downloading ? FormattingHelpers.FormatEta(rem, d.CurrentSpeedBytesPerSec) : "--";
            string c5 = (d.SupportsRanges || d.IsHlsStream) ? "Yes" : (d.TotalBytes > 0 ? "No" : "--");
            string c6 = d.Status == DownloadStatus.Downloading
                ? string.Format("{0}/{1}", d.ActiveConnections, d.MaxConnections)
                : d.MaxConnections.ToString();
            string c7 = d.CategoryName ?? "General";
            string c8 = d.QueueName ?? "Main Queue";
            DateTime stamp = (d.LastTryUtc ?? d.AddedUtc).ToLocalTime();
            string c9 = stamp.ToString("yyyy-MM-dd HH:mm");

            if (row.SubItems[0].Text != c0) row.SubItems[0].Text = c0;
            if (row.SubItems[1].Text != c1) row.SubItems[1].Text = c1;
            if (row.SubItems[2].Text != c2) row.SubItems[2].Text = c2;
            if (row.SubItems[3].Text != c3) row.SubItems[3].Text = c3;
            if (row.SubItems[4].Text != c4) row.SubItems[4].Text = c4;
            if (row.SubItems[5].Text != c5) row.SubItems[5].Text = c5;
            if (row.SubItems[6].Text != c6) row.SubItems[6].Text = c6;
            if (row.SubItems[7].Text != c7) row.SubItems[7].Text = c7;
            if (row.SubItems[8].Text != c8) row.SubItems[8].Text = c8;
            if (row.SubItems[9].Text != c9) row.SubItems[9].Text = c9;
        }

        private void OnDownloadItemDrag(object sender, ItemDragEventArgs e)
        {
            List<DownloadItem> selected = GetAllSelectedItems();
            List<string> files = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].Status == DownloadStatus.Completed && File.Exists(selected[i].FullTargetPath))
                {
                    files.Add(selected[i].FullTargetPath);
                }
            }
            if (files.Count > 0)
            {
                DataObject data = new DataObject(DataFormats.FileDrop, files.ToArray());
                _lvDownloads.DoDragDrop(data, DragDropEffects.Copy);
            }
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

            string[] logs = sel.GetLogsSnapshot();
            string joined = logs.Length > 0 ? string.Join("\r\n", logs) : "";
            if (_txtInspectorLog.Text != joined)
            {
                _txtInspectorLog.Text = joined;
                _txtInspectorLog.SelectionStart = _txtInspectorLog.Text.Length;
                _txtInspectorLog.ScrollToCaret();
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

            using (AddDownloadDialog dlg = new AddDownloadDialog(
                _controller.Settings,
                initialUrl,
                referer,
                cookies,
                _controller.GetDownloadsSnapshot()))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultItem != null)
                {
                    _controller.AddDownload(dlg.ResultItem, dlg.StartImmediately, dlg.HoldPausedInQueue);
                    if (dlg.StartImmediately || !dlg.HoldPausedInQueue)
                    {
                        _hadWorkSinceLaunch = true;
                    }
                    RefreshDownloadList();
                }
            }
        }

        private void OnControllerDownloadStarted(DownloadItem item)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                _hadWorkSinceLaunch = true;
                if (_controller.Settings.ShowProgressDialogOnStart && !IsAutomatedSilentSession)
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

                DownloadProgressDialog openProg;
                if (_openDialogs.TryGetValue(item.Id, out openProg) && openProg != null && !openProg.IsDisposed)
                {
                    openProg.Close();
                }

                try
                {
                    if (_trayIcon != null && !IsAutomatedSilentSession)
                    {
                        _trayIcon.ShowBalloonTip(
                            3000,
                            "Download Complete — MHS Manager",
                            item.FileName + " (" + FormattingHelpers.FormatBytes(item.TotalBytes) + ")",
                            ToolTipIcon.Info);
                    }
                }
                catch { }

                if (_controller.Settings.ShowDownloadCompleteDialog &&
                    !IsAutomatedSilentSession &&
                    item.OnComplete == CompletionAction.None)
                {
                    DownloadCompleteDialog completeDlg = new DownloadCompleteDialog(_controller, item);
                    completeDlg.Show(this);
                }

                if (_launch.ExitOnComplete && !_controller.HasActiveOrQueuedWork())
                {
                    _forceExit = true;
                    Close();
                }
            });
        }

        private void OnQueuePostActionTriggered(QueueDefinition q)
        {
            if (IsDisposed || q == null) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (q.PostCompletionAction == QueuePostAction.ExitApp)
                {
                    _forceExit = true;
                    Close();
                }
                else if (q.PostCompletionAction == QueuePostAction.Sleep)
                {
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                }
                else if (q.PostCompletionAction == QueuePostAction.Hibernate)
                {
                    Application.SetSuspendState(PowerState.Hibernate, false, false);
                }
                else if (q.PostCompletionAction == QueuePostAction.Shutdown)
                {
                    try { Process.Start("shutdown.exe", "/s /t 30"); } catch { }
                }
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

        private void OnBridgeRequestShowBatchDialog(BrowserBatchRequest batch)
        {
            if (IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                RestoreFromTray();
                using (BrowserBatchLinksDialog dlg = new BrowserBatchLinksDialog(_controller.Settings, batch))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        for (int i = 0; i < dlg.SelectedRequests.Count; i++)
                        {
                            BrowserDownloadRequest r = dlg.SelectedRequests[i];
                            string fn = !string.IsNullOrWhiteSpace(r.fileName)
                                ? CategoryClassifier.SanitizeFileName(r.fileName)
                                : CategoryClassifier.GuessFileNameFromUrl(r.url);
                            string cat = CategoryClassifier.ClassifyByFileName(fn);
                            DownloadItem item = new DownloadItem
                            {
                                Url = r.url,
                                FileName = fn,
                                CategoryName = cat,
                                QueueName = dlg.SelectedQueue,
                                SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat),
                                Referer = r.referer,
                                Cookies = r.cookies,
                                UserAgent = r.userAgent,
                                MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
                            };
                            _controller.AddDownload(item, false, !dlg.StartQueueImmediately);
                        }
                        if (dlg.StartQueueImmediately)
                        {
                            _controller.StartQueue(dlg.SelectedQueue);
                        }
                        RefreshDownloadList();
                    }
                }
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

        private void RedownloadSelected()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;
            _controller.RedownloadFromBeginning(sel.Id);
            RefreshDownloadList();
        }

        private void RefreshSelectedAddress()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;
            using (RefreshAddressDialog dlg = new RefreshAddressDialog(_controller, sel))
            {
                dlg.ShowDialog(this);
                RefreshDownloadList();
            }
        }

        private void OpenSelectedProperties()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null) return;
            using (FilePropertiesDialog dlg = new FilePropertiesDialog(_controller, sel))
            {
                dlg.ShowDialog(this);
                RefreshDownloadList();
            }
        }

        private void MoveSelectedToQueue(string queueName)
        {
            List<DownloadItem> items = GetAllSelectedItems();
            for (int i = 0; i < items.Count; i++)
            {
                items[i].QueueName = queueName;
            }
            _controller.SaveStateSafe();
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

        private void OpenSelectedFileWith()
        {
            DownloadItem sel = GetFirstSelectedItem();
            if (sel == null || !File.Exists(sel.FullTargetPath)) return;
            try
            {
                Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + sel.FullTargetPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open With", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void ImportBatchFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show(this, "Clipboard does not contain any text URLs.", "Clipboard Batch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                List<ImportedDownloadSpec> parsed = IdmEf2Serializer.ParseTextUrlList(Clipboard.GetText());
                if (parsed.Count == 0)
                {
                    MessageBox.Show(this, "No valid http:// or https:// URLs found in clipboard.", "Clipboard Batch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                BrowserBatchRequest batch = new BrowserBatchRequest { links = new List<BrowserDownloadRequest>() };
                for (int i = 0; i < parsed.Count; i++)
                {
                    batch.links.Add(new BrowserDownloadRequest { url = parsed[i].Url });
                }
                OnBridgeRequestShowBatchDialog(batch);
            }
            catch { }
        }

        private void ImportDownloadsFromFile(bool ef2Format)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = ef2Format
                    ? "IDM Export Files (*.ef2)|*.ef2|All Files (*.*)|*.*"
                    : "Text URL Lists (*.txt)|*.txt|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string raw = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                    List<ImportedDownloadSpec> list = ef2Format
                        ? IdmEf2Serializer.ParseEf2(raw)
                        : IdmEf2Serializer.ParseTextUrlList(raw);

                    for (int i = 0; i < list.Count; i++)
                    {
                        ImportedDownloadSpec spec = list[i];
                        bool explicitFn = !string.IsNullOrWhiteSpace(spec.FileName);
                        string fn = explicitFn ? spec.FileName : CategoryClassifier.GuessFileNameFromUrl(spec.Url);
                        string cat = CategoryClassifier.ClassifyByFileName(fn);
                        DownloadItem item = new DownloadItem
                        {
                            Url = spec.Url,
                            FileName = fn,
                            HasExplicitFileName = explicitFn,
                            CategoryName = cat,
                            SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat),
                            Referer = spec.Referer,
                            Cookies = spec.Cookies,
                            UserAgent = spec.UserAgent,
                            AuthUser = spec.AuthUser,
                            AuthPassword = spec.AuthPassword,
                            MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
                        };
                        _controller.AddDownload(item, false, false);
                    }
                    RefreshDownloadList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportDownloadsToFile(bool ef2Format)
        {
            List<DownloadItem> targets = GetAllSelectedItems();
            if (targets.Count == 0)
            {
                targets = _controller.GetDownloadsSnapshot();
            }
            if (targets.Count == 0) return;

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = ef2Format
                    ? "IDM Export File (*.ef2)|*.ef2"
                    : "Text URL List (*.txt)|*.txt";
                sfd.FileName = ef2Format ? "downloads.ef2" : "downloads.txt";
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string text = ef2Format
                        ? IdmEf2Serializer.ExportToEf2(targets)
                        : IdmEf2Serializer.ExportToTextList(targets);
                    File.WriteAllText(sfd.FileName, text, StateRepository.Utf8NoBom);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
                        _controller.AddDownload(item, false, !dlg.StartImmediately);
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
                        _controller.AddDownload(item, false, false);
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
                PopulateNavigationTree();
                UpdateSpeedButtonVisual();
                RefreshDownloadList();
            }
        }

        private void HandleClipboardChanged()
        {
            if (!_controller.Settings.EnableClipboardMonitor || _clipboardDialogOpen || IsAutomatedSilentSession)
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
                if (HostExclusionMatcher.IsExcluded(text, null, _controller.Settings.ExcludedHosts)) return;
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
            if (_dropTargetForm != null && !_dropTargetForm.IsDisposed)
            {
                _dropTargetForm.Close();
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}
