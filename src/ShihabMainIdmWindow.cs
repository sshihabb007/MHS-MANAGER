using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace ShihabMehediIDM
{
    public class ShihabMainIdmWindow : Form
    {
        private readonly ShihabStateStore _shihabStore;
        private readonly MehediAppConfiguration _mehediConfig;
        private readonly MehediDynamicSegmentEngine _shihabEngine;
        private readonly MehediBrowserBridgeServer _mehediBridge;
        private readonly ShihabCliParsedArguments _shihabStartupCli;
        private readonly Dictionary<string, ShihabDownloadProgressForm> _mehediOpenProgressForms;

        private MenuStrip _shihabMenuBar;
        private ToolStrip _mehediToolBar;
        private Panel _shihabSearchBarPanel;
        private TextBox _mehediSearchBox;
        private Panel _mehediBodyPaddingHost;
        private SplitContainer _shihabSplitContainer;
        private Panel _shihabCategoryFramePanel;
        private Panel _mehediCategoryHeaderStrip;
        private Label _shihabCategoryHeaderTitle;
        private Button _mehediCloseCategoriesBtn;
        private Panel _shihabListFramePanel;
        private MehediModernTreeView _mehediCategoryTree;
        private ShihabModernListView _shihabDownloadsListView;
        private ToolStripButton _mehediResumeToolBtn;
        private ToolStripButton _shihabStopToolBtn;
        private ToolStripButton _mehediStopAllToolBtn;
        private ToolStripButton _shihabDeleteToolBtn;
        private StatusStrip _mehediStatusBar;
        private ToolStripStatusLabel _shihabStatusSummaryLabel;
        private ToolStripStatusLabel _mehediBridgeStatusLabel;
        private ToolStripStatusLabel _shihabSpeedLimiterBadge;
        private NotifyIcon _mehediTrayIcon;
        private ContextMenuStrip _shihabTrayMenuRef;
        private MehediFloatingDropTargetForm _shihabDropTargetForm;
        private Timer _mehediUiTickTimer;

        private string _shihabActiveTreeFilter = "ALL";
        private string _mehediSortColumnKey = "ORDER";
        private bool _shihabSortAscending = false;
        private string _shihabLastClipboardText = "";
        private bool _mehediBridgeListening = false;
        private bool _shihabExitingExplicitly = false;

        public ShihabMainIdmWindow(
            ShihabStateStore shihabStore,
            MehediAppConfiguration mehediConfig,
            MehediDynamicSegmentEngine shihabEngine,
            MehediBrowserBridgeServer mehediBridge,
            ShihabCliParsedArguments shihabStartupCli)
        {
            _shihabStore = shihabStore;
            _mehediConfig = mehediConfig;
            _shihabEngine = shihabEngine;
            _mehediBridge = mehediBridge;
            _shihabStartupCli = shihabStartupCli;
            _mehediOpenProgressForms = new Dictionary<string, ShihabDownloadProgressForm>();

            Text = "MHS Download Manager 1.0.0";
            Size = new Size(1060, 650);
            MinimumSize = new Size(860, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            Icon = ShihabThemePalette.CreateShihabAppIcon();
            KeyPreview = true;
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, _mehediConfig.ShihabDarkMode);

            BuildShihabLayoutControls();
            ApplyMehediTheme();
            PopulateShihabCategoryTree();
            RefreshMehediDownloadsGrid();

            _mehediBridgeListening = _mehediBridge.StartShihabBridge();
            _mehediBridge.OnShihabSingleCaptureReceived += HandleBrowserShihabSingleCapture;
            _mehediBridge.OnMehediBatchCaptureReceived += HandleBrowserMehediBatchCapture;
            _mehediBridge.OnShihabStartMainQueueRequested += delegate
            {
                SafeShihabUiInvoke(delegate { _shihabEngine.StartShihabQueue("Main Download Queue"); });
            };

            _shihabEngine.OnShihabItemUpdated += delegate
            {
                SafeShihabUiInvoke(delegate { RefreshMehediDownloadsGrid(); });
            };
            _shihabEngine.OnMehediItemCompleted += HandleShihabItemCompleted;
            _shihabEngine.OnShihabItemFailed += HandleMehediItemFailed;

            if (_shihabStartupCli != null && _shihabStartupCli.ShihabStartMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }

            _mehediUiTickTimer = new Timer { Interval = 650 };
            _mehediUiTickTimer.Tick += delegate { OnMehediPeriodicTimerTick(); };
            _mehediUiTickTimer.Start();

            KeyDown += delegate(object shihabS, KeyEventArgs mehediE)
            {
                if (mehediE.Control && mehediE.KeyCode == Keys.F)
                {
                    _shihabSearchBarPanel.Visible = !_shihabSearchBarPanel.Visible;
                    if (_shihabSearchBarPanel.Visible) _mehediSearchBox.Focus();
                    mehediE.Handled = true;
                }
                else if (mehediE.KeyCode == Keys.Delete)
                {
                    DeleteSelectedShihabDownloads(false);
                }
            };

            Shown += delegate { OnShihabMainFormFirstShown(); };
            FormClosing += HandleMehediFormClosing;
        }

        private void BuildShihabLayoutControls()
        {
            _shihabMenuBar = new MenuStrip();

            // Tasks menu
            ToolStripMenuItem mehediTasksMenu = new ToolStripMenuItem("&Tasks");
            mehediTasksMenu.DropDownItems.Add("Add new download...", null, delegate { PromptAndAddNewShihabUrl(""); });
            mehediTasksMenu.DropDownItems.Add("Add batch download (Wildcard *)...", null, delegate { OpenShihabBatchWildcardDialog(); });
            mehediTasksMenu.DropDownItems.Add("Add batch download from clipboard", null, delegate { ImportShihabClipboardUrlsBatch(); });
            mehediTasksMenu.DropDownItems.Add("Run site grabber...", null, delegate { OpenShihabSiteGrabberDialog(); });
            mehediTasksMenu.DropDownItems.Add(new ToolStripSeparator());
            mehediTasksMenu.DropDownItems.Add("Show drop target", null, delegate { ToggleMehediFloatingDropTarget(); });
            mehediTasksMenu.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem shihabExportSub = new ToolStripMenuItem("Export");
            shihabExportSub.DropDownItems.Add("To IDM export file (.ef2)...", null, delegate { ExportShihabList(true); });
            shihabExportSub.DropDownItems.Add("To text file (.txt)...", null, delegate { ExportShihabList(false); });
            mehediTasksMenu.DropDownItems.Add(shihabExportSub);

            ToolStripMenuItem mehediImportSub = new ToolStripMenuItem("Import");
            mehediImportSub.DropDownItems.Add("From IDM export file (.ef2)...", null, delegate { ImportMehediList(); });
            mehediImportSub.DropDownItems.Add("From text file (.txt)...", null, delegate { ImportMehediList(); });
            mehediTasksMenu.DropDownItems.Add(mehediImportSub);

            mehediTasksMenu.DropDownItems.Add(new ToolStripSeparator());
            mehediTasksMenu.DropDownItems.Add("Exit", null, delegate { ExitShihabApplicationCleanly(); });

            // File menu
            ToolStripMenuItem shihabFileMenu = new ToolStripMenuItem("&File");
            shihabFileMenu.DropDownItems.Add("Download Now / Resume", null, delegate { ResumeSelectedShihabDownloads(); });
            shihabFileMenu.DropDownItems.Add("Stop Download", null, delegate { PauseSelectedMehediDownloads(); });
            shihabFileMenu.DropDownItems.Add("Redownload from beginning", null, delegate { RedownloadSelectedShihabItem(); });
            shihabFileMenu.DropDownItems.Add("Refresh download address...", null, delegate { RefreshSelectedMehediAddress(); });
            shihabFileMenu.DropDownItems.Add("Remove", null, delegate { DeleteSelectedShihabDownloads(false); });
            shihabFileMenu.DropDownItems.Add(new ToolStripSeparator());
            shihabFileMenu.DropDownItems.Add("Properties...", null, delegate { OpenSelectedShihabProperties(); });

            // Downloads menu
            ToolStripMenuItem mehediDownloadsMenu = new ToolStripMenuItem("&Downloads");
            mehediDownloadsMenu.DropDownItems.Add("Pause All", null, delegate { _shihabEngine.StopAllMehediDownloads(); });
            mehediDownloadsMenu.DropDownItems.Add("Stop All", null, delegate { _shihabEngine.StopAllMehediDownloads(); });
            mehediDownloadsMenu.DropDownItems.Add("Delete All Completed", null, delegate { DeleteAllCompletedMehediDownloads(); });
            mehediDownloadsMenu.DropDownItems.Add(new ToolStripSeparator());
            mehediDownloadsMenu.DropDownItems.Add("Find (Ctrl+F)", null, delegate
            {
                _shihabSearchBarPanel.Visible = !_shihabSearchBarPanel.Visible;
                if (_shihabSearchBarPanel.Visible) _mehediSearchBox.Focus();
            });
            mehediDownloadsMenu.DropDownItems.Add("Scheduler and queues...", null, delegate { OpenMehediSchedulerDialog(); });
            mehediDownloadsMenu.DropDownItems.Add("Start Main Queue", null, delegate { _shihabEngine.StartShihabQueue("Main Download Queue"); });
            mehediDownloadsMenu.DropDownItems.Add("Stop Main Queue", null, delegate { _shihabEngine.StopShihabQueue("Main Download Queue"); });

            ToolStripMenuItem shihabLimiterMenu = new ToolStripMenuItem("Speed Limiter");
            shihabLimiterMenu.DropDownItems.Add("Turn On", null, delegate
            {
                _mehediConfig.MehediGlobalSpeedLimiterEnabled = true;
                _shihabStore.SaveMehediConfig(_mehediConfig);
                UpdateShihabStatusBadges();
            });
            shihabLimiterMenu.DropDownItems.Add("Turn Off", null, delegate
            {
                _mehediConfig.MehediGlobalSpeedLimiterEnabled = false;
                _shihabStore.SaveMehediConfig(_mehediConfig);
                UpdateShihabStatusBadges();
            });
            shihabLimiterMenu.DropDownItems.Add("Settings...", null, delegate { OpenShihabOptionsDialog(); });
            mehediDownloadsMenu.DropDownItems.Add(shihabLimiterMenu);
            mehediDownloadsMenu.DropDownItems.Add(new ToolStripSeparator());
            mehediDownloadsMenu.DropDownItems.Add("Options...", null, delegate { OpenShihabOptionsDialog(); });

            // View menu
            ToolStripMenuItem shihabViewMenu = new ToolStripMenuItem("&View");
            shihabViewMenu.DropDownItems.Add("Toggle Categories Sidebar", null, delegate
            {
                _mehediConfig.MehediShowCategoriesPanel = !_mehediConfig.MehediShowCategoriesPanel;
                _shihabSplitContainer.Panel1Collapsed = !_mehediConfig.MehediShowCategoriesPanel;
                _shihabStore.SaveMehediConfig(_mehediConfig);
            });
            shihabViewMenu.DropDownItems.Add("Toggle Dark Mode / Classic IDM Light Theme", null, delegate
            {
                _mehediConfig.ShihabDarkMode = !_mehediConfig.ShihabDarkMode;
                _shihabStore.SaveMehediConfig(_mehediConfig);
                ApplyMehediTheme();
            });

            ToolStripMenuItem mehediArrangeSub = new ToolStripMenuItem("Arrange files");
            mehediArrangeSub.DropDownItems.Add("By Order Of Addition", null, delegate { _mehediSortColumnKey = "ORDER"; RefreshMehediDownloadsGrid(); });
            mehediArrangeSub.DropDownItems.Add("By File Name", null, delegate { _mehediSortColumnKey = "NAME"; RefreshMehediDownloadsGrid(); });
            mehediArrangeSub.DropDownItems.Add("By Size", null, delegate { _mehediSortColumnKey = "SIZE"; RefreshMehediDownloadsGrid(); });
            mehediArrangeSub.DropDownItems.Add("By Status", null, delegate { _mehediSortColumnKey = "STATUS"; RefreshMehediDownloadsGrid(); });
            mehediArrangeSub.DropDownItems.Add("By Transfer Rate", null, delegate { _mehediSortColumnKey = "SPEED"; RefreshMehediDownloadsGrid(); });
            shihabViewMenu.DropDownItems.Add(mehediArrangeSub);

            // Help menu + About menu
            ToolStripMenuItem mehediHelpMenu = new ToolStripMenuItem("&Help");
            mehediHelpMenu.DropDownItems.Add("Browser Extension Setup Guide", null, delegate { ShowShihabExtensionInstructions(); });
            mehediHelpMenu.DropDownItems.Add("Command-Line Switches (mdm.exe /d /s /p /f /n /q /a)", null, delegate { ShowMehediCliHelp(); });
            mehediHelpMenu.DropDownItems.Add(new ToolStripSeparator());
            mehediHelpMenu.DropDownItems.Add("Developer Portfolio (sshihabb007.github.io)", null, delegate
            {
                try { Process.Start(new ProcessStartInfo("https://sshihabb007.github.io/") { UseShellExecute = true }); } catch { }
            });
            mehediHelpMenu.DropDownItems.Add("About MHS Download Manager...", null, delegate { ShowShihabAboutDialog(); });

            ToolStripMenuItem shihabTopAboutMenu = new ToolStripMenuItem("&About");
            shihabTopAboutMenu.Click += delegate { ShowShihabAboutDialog(); };

            _shihabMenuBar.Padding = new Padding(4, 2, 4, 2);
            _shihabMenuBar.Items.AddRange(new ToolStripItem[]
            {
                mehediTasksMenu, shihabFileMenu, mehediDownloadsMenu, shihabViewMenu, mehediHelpMenu, shihabTopAboutMenu
            });

            // MHS Download Manager 1.0.0 Large Outlined Pastel-Neon Toolbar
            _mehediToolBar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Height = 76,
                ImageScalingSize = new Size(36, 36),
                Padding = new Padding(8, 4, 8, 4)
            };

            AddMehediToolbarBtn("Add URL", "add", true, "Add new download address manually", delegate { PromptAndAddNewShihabUrl(""); });
            _mehediResumeToolBtn = AddMehediToolbarBtn("Resume", "resume", false, "Start/Resume selected download(s)", delegate { ResumeSelectedShihabDownloads(); });
            _shihabStopToolBtn = AddMehediToolbarBtn("Stop", "stop", false, "Stop/Pause selected download(s)", delegate { PauseSelectedMehediDownloads(); });
            _mehediStopAllToolBtn = AddMehediToolbarBtn("Stop All", "stopall", false, "Stop all active downloads", delegate { _shihabEngine.StopAllMehediDownloads(); });
            _shihabDeleteToolBtn = AddMehediToolbarBtn("Delete", "delete", false, "Remove selected download(s)", delegate { DeleteSelectedShihabDownloads(false); });
            AddMehediToolbarBtn("Delete Co...", "clean", true, "Remove all completed files from list", delegate { DeleteAllCompletedMehediDownloads(); });
            AddMehediToolbarBtn("Options", "options", true, "Browser integration, File types, Connections, Proxy", delegate { OpenShihabOptionsDialog(); });
            AddMehediToolbarBtn("Scheduler", "scheduler", true, "Scheduler and download queues", delegate { OpenMehediSchedulerDialog(); });
            AddShihabQueueSplitBtn("Start Qu...", "startq", "Start Download Queue", true);
            AddShihabQueueSplitBtn("Stop Qu...", "stopq", "Stop Download Queue", false);
            AddMehediToolbarBtn("Grabber", "grabber", true, "Run MHS Site Grabber", delegate { OpenShihabSiteGrabberDialog(); });

            // Search Bar (Ctrl+F)
            _shihabSearchBarPanel = new Panel { Dock = DockStyle.Top, Height = 34, Visible = false };
            Label shihabFindLbl = new Label { Text = "Find (Name / URL / Desc):", Location = new Point(12, 8), AutoSize = true };
            _mehediSearchBox = new TextBox { Location = new Point(178, 5), Width = 340, BorderStyle = BorderStyle.FixedSingle };
            _mehediSearchBox.TextChanged += delegate { RefreshMehediDownloadsGrid(); };
            Button shihabCloseFindBtn = new Button { Text = "×", Location = new Point(526, 4), Size = new Size(28, 24), FlatStyle = FlatStyle.Flat };
            shihabCloseFindBtn.Click += delegate { _mehediSearchBox.Text = ""; _shihabSearchBarPanel.Visible = false; };
            _shihabSearchBarPanel.Controls.AddRange(new Control[] { shihabFindLbl, _mehediSearchBox, shihabCloseFindBtn });

            // Framed Body Container (Categories Left Card | Downloads Right Card)
            _mehediBodyPaddingHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5, 5, 5, 4)
            };

            _shihabSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = new Size(1040, 520),
                SplitterWidth = 5,
                SplitterDistance = 190,
                Panel1Collapsed = !_mehediConfig.MehediShowCategoriesPanel
            };

            _shihabCategoryFramePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };

            _mehediCategoryHeaderStrip = new Panel
            {
                Dock = DockStyle.Top,
                Height = 23
            };
            _shihabCategoryHeaderTitle = new Label
            {
                Text = "Categories",
                Location = new Point(5, 3),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Regular)
            };
            _mehediCloseCategoriesBtn = new Button
            {
                Text = "✕",
                Dock = DockStyle.Right,
                Width = 24,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.8f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _mehediCloseCategoriesBtn.FlatAppearance.BorderSize = 0;
            _mehediCloseCategoriesBtn.Click += delegate
            {
                _mehediConfig.MehediShowCategoriesPanel = false;
                _shihabSplitContainer.Panel1Collapsed = true;
                _shihabStore.SaveMehediConfig(_mehediConfig);
            };
            _mehediCategoryHeaderStrip.Controls.Add(_shihabCategoryHeaderTitle);
            _mehediCategoryHeaderStrip.Controls.Add(_mehediCloseCategoriesBtn);

            _mehediCategoryTree = new MehediModernTreeView
            {
                Dock = DockStyle.Fill
            };
            _mehediCategoryTree.AfterSelect += delegate
            {
                if (_mehediCategoryTree.SelectedNode != null && _mehediCategoryTree.SelectedNode.Tag != null)
                {
                    string shihabSelectedTag = _mehediCategoryTree.SelectedNode.Tag.ToString();
                    if (shihabSelectedTag == "GRABBER_PROJECTS")
                    {
                        return;
                    }
                    _shihabActiveTreeFilter = shihabSelectedTag;
                    RefreshMehediDownloadsGrid();
                }
            };
            _mehediCategoryTree.NodeMouseDoubleClick += delegate(object shihabSender, TreeNodeMouseClickEventArgs mehediArgs)
            {
                if (mehediArgs.Node != null && mehediArgs.Node.Tag != null && mehediArgs.Node.Tag.ToString() == "GRABBER_PROJECTS")
                {
                    OpenShihabSiteGrabberDialog();
                }
            };
            _shihabCategoryFramePanel.Controls.Add(_mehediCategoryTree);
            _shihabCategoryFramePanel.Controls.Add(_mehediCategoryHeaderStrip);
            _shihabSplitContainer.Panel1.Controls.Add(_shihabCategoryFramePanel);

            _shihabListFramePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };

            _shihabDownloadsListView = new ShihabModernListView
            {
                Dock = DockStyle.Fill,
                MultiSelect = true,
                AllowDrop = true,
                MehediSortedColumnIndex = 6,
                ShihabSortAscending = false
            };
            _shihabDownloadsListView.Columns.Add("File Name", 265);
            _shihabDownloadsListView.Columns.Add("Q", 32);
            _shihabDownloadsListView.Columns.Add("Size", 88);
            _shihabDownloadsListView.Columns.Add("Status", 108);
            _shihabDownloadsListView.Columns.Add("Time left", 92);
            _shihabDownloadsListView.Columns.Add("Transfer rate", 105);
            _shihabDownloadsListView.Columns.Add("Last Try Date", 112);
            _shihabDownloadsListView.Columns.Add("Description", 195);

            _shihabDownloadsListView.SelectedIndexChanged += delegate { UpdateMehediToolbarActionIcons(); };
            _shihabDownloadsListView.ColumnClick += delegate(object shihabS, ColumnClickEventArgs mehediE)
            {
                string shihabNextKey = "ORDER";
                if (mehediE.Column == 0) shihabNextKey = "NAME";
                else if (mehediE.Column == 2) shihabNextKey = "SIZE";
                else if (mehediE.Column == 3) shihabNextKey = "STATUS";
                else if (mehediE.Column == 5) shihabNextKey = "SPEED";
                else shihabNextKey = "ORDER";

                if (_mehediSortColumnKey == shihabNextKey)
                {
                    _shihabSortAscending = !_shihabSortAscending;
                }
                else
                {
                    _mehediSortColumnKey = shihabNextKey;
                    _shihabSortAscending = (shihabNextKey == "NAME" || shihabNextKey == "STATUS");
                }
                _shihabDownloadsListView.MehediSortedColumnIndex = mehediE.Column;
                _shihabDownloadsListView.ShihabSortAscending = _shihabSortAscending;
                RefreshMehediDownloadsGrid();
            };

            _shihabDownloadsListView.DoubleClick += delegate { HandleDoubleClickOnShihabRow(); };
            _shihabDownloadsListView.DragEnter += delegate(object shihabS, DragEventArgs mehediE)
            {
                if (mehediE.Data.GetDataPresent(DataFormats.UnicodeText) || mehediE.Data.GetDataPresent(DataFormats.Text))
                {
                    mehediE.Effect = DragDropEffects.Copy;
                }
            };
            _shihabDownloadsListView.DragDrop += delegate(object shihabS, DragEventArgs mehediE)
            {
                string shihabDropped = (mehediE.Data.GetData(DataFormats.UnicodeText) ?? mehediE.Data.GetData(DataFormats.Text) ?? "").ToString().Trim();
                if (!string.IsNullOrEmpty(shihabDropped)) PromptAndAddNewShihabUrl(shihabDropped);
            };
            _shihabDownloadsListView.ContextMenuStrip = BuildMehediRowContextMenu();
            _shihabListFramePanel.Controls.Add(_shihabDownloadsListView);
            _shihabSplitContainer.Panel2.Controls.Add(_shihabListFramePanel);
            _mehediBodyPaddingHost.Controls.Add(_shihabSplitContainer);

            // Status Bar
            _mehediStatusBar = new StatusStrip { SizingGrip = true };
            _shihabStatusSummaryLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _shihabSpeedLimiterBadge = new ToolStripStatusLabel("Speed Limiter: OFF") { IsLink = false };
            _shihabSpeedLimiterBadge.Click += delegate
            {
                _mehediConfig.MehediGlobalSpeedLimiterEnabled = !_mehediConfig.MehediGlobalSpeedLimiterEnabled;
                _shihabStore.SaveMehediConfig(_mehediConfig);
                UpdateShihabStatusBadges();
            };
            _mehediBridgeStatusLabel = new ToolStripStatusLabel("Browser Bridge: Starting...");
            _mehediStatusBar.Items.AddRange(new ToolStripItem[] { _shihabStatusSummaryLabel, _shihabSpeedLimiterBadge, _mehediBridgeStatusLabel });

            // System Tray Icon
            _shihabTrayMenuRef = new ContextMenuStrip();
            _shihabTrayMenuRef.Items.Add("Restore MHS Download Manager", null, delegate { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); });
            _shihabTrayMenuRef.Items.Add("Add new download...", null, delegate { Show(); ShowInTaskbar = true; PromptAndAddNewShihabUrl(""); });
            _shihabTrayMenuRef.Items.Add("Show drop target", null, delegate { ToggleMehediFloatingDropTarget(); });
            _shihabTrayMenuRef.Items.Add("Start Main Queue", null, delegate { _shihabEngine.StartShihabQueue("Main Download Queue"); });
            _shihabTrayMenuRef.Items.Add("Stop All Downloads", null, delegate { _shihabEngine.StopAllMehediDownloads(); });
            _shihabTrayMenuRef.Items.Add(new ToolStripSeparator());
            _shihabTrayMenuRef.Items.Add("About MHS Download Manager...", null, delegate { Show(); ShowShihabAboutDialog(); });
            _shihabTrayMenuRef.Items.Add("Exit", null, delegate { ExitShihabApplicationCleanly(); });

            _mehediTrayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "MHS Download Manager 1.0.0 (mdm.exe)",
                Visible = true,
                ContextMenuStrip = _shihabTrayMenuRef
            };
            _mehediTrayIcon.DoubleClick += delegate { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); };

            Controls.Add(_mehediBodyPaddingHost);
            Controls.Add(_shihabSearchBarPanel);
            Controls.Add(_mehediToolBar);
            Controls.Add(_shihabMenuBar);
            Controls.Add(_mehediStatusBar);
            MainMenuStrip = _shihabMenuBar;
        }

        private ToolStripButton AddMehediToolbarBtn(string shihabLabel, string mehediGlyph, bool shihabActiveInitial, string mehediTooltip, EventHandler shihabHandler)
        {
            ToolStripButton mehediBtn = new ToolStripButton
            {
                Text = shihabLabel,
                Image = ShihabThemePalette.DrawMehediLargeToolbarIcon(mehediGlyph, shihabActiveInitial),
                ImageScaling = ToolStripItemImageScaling.None,
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = false,
                Size = new Size(76, 66),
                ToolTipText = mehediTooltip,
                Margin = new Padding(2, 1, 2, 1),
                Tag = mehediGlyph
            };
            mehediBtn.Click += shihabHandler;
            _mehediToolBar.Items.Add(mehediBtn);
            return mehediBtn;
        }

        private void AddShihabQueueSplitBtn(string mehediLabel, string shihabGlyph, string mehediTooltip, bool shihabStartAction)
        {
            ToolStripSplitButton mehediSplit = new ToolStripSplitButton
            {
                Text = mehediLabel,
                Image = ShihabThemePalette.DrawMehediLargeToolbarIcon(shihabGlyph, true),
                ImageScaling = ToolStripItemImageScaling.None,
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = false,
                Size = new Size(86, 66),
                ToolTipText = mehediTooltip,
                Margin = new Padding(2, 1, 2, 1)
            };
            mehediSplit.ButtonClick += delegate
            {
                if (shihabStartAction) _shihabEngine.StartShihabQueue("Main Download Queue");
                else _shihabEngine.StopShihabQueue("Main Download Queue");
            };
            mehediSplit.DropDownOpening += delegate
            {
                mehediSplit.DropDownItems.Clear();
                for (int shihabI = 0; shihabI < _mehediConfig.MehediQueues.Count; shihabI++)
                {
                    string mehediQueueName = _mehediConfig.MehediQueues[shihabI].ShihabQueueName;
                    mehediSplit.DropDownItems.Add(
                        (shihabStartAction ? "Start " : "Stop ") + mehediQueueName,
                        null,
                        delegate
                        {
                            if (shihabStartAction) _shihabEngine.StartShihabQueue(mehediQueueName);
                            else _shihabEngine.StopShihabQueue(mehediQueueName);
                        });
                }
                mehediSplit.DropDownItems.Add(new ToolStripSeparator());
                mehediSplit.DropDownItems.Add("Scheduler and queues...", null, delegate { OpenMehediSchedulerDialog(); });
            };
            _mehediToolBar.Items.Add(mehediSplit);
        }

        private void UpdateMehediToolbarActionIcons()
        {
            if (_mehediResumeToolBtn == null) return;
            bool shihabHasSelection = _shihabDownloadsListView.SelectedItems.Count > 0;
            bool mehediCanResume = false;
            bool shihabCanStop = false;

            foreach (ListViewItem mehediSel in _shihabDownloadsListView.SelectedItems)
            {
                ShihabDownloadItem shihabIt = mehediSel.Tag as ShihabDownloadItem;
                if (shihabIt == null) continue;
                if (_shihabEngine.IsMehediItemActive(shihabIt.ShihabId))
                {
                    shihabCanStop = true;
                }
                else if (shihabIt.GetShihabStatusEnum() != ShihabDownloadStatus.ShihabCompleted)
                {
                    mehediCanResume = true;
                }
            }

            bool mehediAnyActive = false;
            List<ShihabDownloadItem> shihabAll = _shihabEngine.GetShihabDownloadsSnapshot();
            for (int shihabI = 0; shihabI < shihabAll.Count; shihabI++)
            {
                if (_shihabEngine.IsMehediItemActive(shihabAll[shihabI].ShihabId))
                {
                    mehediAnyActive = true;
                    break;
                }
            }

            ReplaceShihabButtonImage(_mehediResumeToolBtn, "resume", mehediCanResume);
            ReplaceShihabButtonImage(_shihabStopToolBtn, "stop", shihabCanStop);
            ReplaceShihabButtonImage(_mehediStopAllToolBtn, "stopall", mehediAnyActive);
            ReplaceShihabButtonImage(_shihabDeleteToolBtn, "delete", shihabHasSelection);
        }

        private static void ReplaceShihabButtonImage(ToolStripButton mehediBtn, string shihabGlyph, bool mehediActive)
        {
            if (mehediBtn == null) return;
            string shihabStateKey = shihabGlyph + (mehediActive ? ":1" : ":0");
            if (Equals(mehediBtn.AccessibleDescription, shihabStateKey)) return;
            Image mehediOld = mehediBtn.Image;
            mehediBtn.Image = ShihabThemePalette.DrawMehediLargeToolbarIcon(shihabGlyph, mehediActive);
            mehediBtn.AccessibleDescription = shihabStateKey;
            if (mehediOld != null) mehediOld.Dispose();
        }

        private ContextMenuStrip BuildMehediRowContextMenu()
        {
            ContextMenuStrip shihabCtx = new ContextMenuStrip();
            shihabCtx.Items.Add("Resume Download", null, delegate { ResumeSelectedShihabDownloads(); });
            shihabCtx.Items.Add("Stop Download", null, delegate { PauseSelectedMehediDownloads(); });
            shihabCtx.Items.Add("Show Live Progress Window", null, delegate { OpenSelectedMehediProgressWindow(); });
            shihabCtx.Items.Add(new ToolStripSeparator());
            shihabCtx.Items.Add("Open", null, delegate { OpenSelectedShihabCompletedFile(false); });
            shihabCtx.Items.Add("Open With...", null, delegate { OpenSelectedShihabCompletedFile(true); });
            shihabCtx.Items.Add("Open Folder", null, delegate { OpenSelectedMehediContainingFolder(); });
            shihabCtx.Items.Add(new ToolStripSeparator());
            shihabCtx.Items.Add("Refresh Download Address...", null, delegate { RefreshSelectedMehediAddress(); });
            shihabCtx.Items.Add("Redownload from Beginning", null, delegate { RedownloadSelectedShihabItem(); });

            ToolStripMenuItem mehediMoveQueue = new ToolStripMenuItem("Move to Queue");
            shihabCtx.Opening += delegate
            {
                mehediMoveQueue.DropDownItems.Clear();
                mehediMoveQueue.DropDownItems.Add("(None — Direct)", null, delegate { AssignSelectedShihabToQueue(""); });
                for (int shihabQ = 0; shihabQ < _mehediConfig.MehediQueues.Count; shihabQ++)
                {
                    string mehediQName = _mehediConfig.MehediQueues[shihabQ].ShihabQueueName;
                    mehediMoveQueue.DropDownItems.Add(mehediQName, null, delegate { AssignSelectedShihabToQueue(mehediQName); });
                }
            };
            shihabCtx.Items.Add(mehediMoveQueue);

            shihabCtx.Items.Add(new ToolStripSeparator());
            shihabCtx.Items.Add("Remove from List", null, delegate { DeleteSelectedShihabDownloads(false); });
            shihabCtx.Items.Add("Remove + Delete File from Disk", null, delegate { DeleteSelectedShihabDownloads(true); });
            shihabCtx.Items.Add(new ToolStripSeparator());
            shihabCtx.Items.Add("Properties...", null, delegate { OpenSelectedShihabProperties(); });
            return shihabCtx;
        }

        private void ApplyMehediTheme()
        {
            bool shihabDark = _mehediConfig.ShihabDarkMode;
            Color mehediWin = ShihabThemePalette.GetMehediWindowBg(shihabDark);
            Color shihabSurf = ShihabThemePalette.GetShihabSurfaceBg(shihabDark);
            Color mehediHead = ShihabThemePalette.GetMehediHeaderBg(shihabDark);
            Color shihabFg = ShihabThemePalette.GetShihabPrimaryText(shihabDark);
            Color mehediFrameBorder = shihabDark ? Color.FromArgb(108, 108, 112) : Color.FromArgb(185, 195, 208);

            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, shihabDark);
            BackColor = mehediWin;
            ForeColor = shihabFg;

            MehediDarkToolStripRenderer shihabMenuRenderer = new MehediDarkToolStripRenderer(shihabDark, false);
            MehediDarkToolStripRenderer mehediToolbarRenderer = new MehediDarkToolStripRenderer(shihabDark, true);

            _shihabMenuBar.Renderer = shihabMenuRenderer;
            _mehediToolBar.Renderer = mehediToolbarRenderer;
            _mehediStatusBar.Renderer = shihabMenuRenderer;
            if (_shihabDownloadsListView.ContextMenuStrip != null)
            {
                _shihabDownloadsListView.ContextMenuStrip.Renderer = shihabMenuRenderer;
            }
            if (_shihabTrayMenuRef != null)
            {
                _shihabTrayMenuRef.Renderer = shihabMenuRenderer;
            }

            _mehediBodyPaddingHost.BackColor = mehediWin;
            _shihabSplitContainer.BackColor = mehediWin;
            _shihabCategoryFramePanel.BackColor = mehediFrameBorder;
            _shihabListFramePanel.BackColor = mehediFrameBorder;

            _mehediCategoryHeaderStrip.BackColor = shihabDark ? Color.FromArgb(34, 34, 36) : Color.FromArgb(234, 240, 248);
            _shihabCategoryHeaderTitle.ForeColor = shihabFg;
            _mehediCloseCategoriesBtn.BackColor = _mehediCategoryHeaderStrip.BackColor;
            _mehediCloseCategoriesBtn.ForeColor = shihabFg;

            _shihabSearchBarPanel.BackColor = mehediHead;
            _mehediSearchBox.BackColor = shihabSurf;
            _mehediSearchBox.ForeColor = shihabFg;

            _mehediCategoryTree.ShihabDarkMode = shihabDark;
            _mehediCategoryTree.BackColor = shihabDark ? Color.FromArgb(28, 28, 28) : Color.White;
            _mehediCategoryTree.ForeColor = shihabFg;
            _mehediCategoryTree.Invalidate();

            _shihabDownloadsListView.MehediDarkMode = shihabDark;
            _shihabDownloadsListView.BackColor = shihabSurf;
            _shihabDownloadsListView.ForeColor = shihabFg;
            _shihabDownloadsListView.Invalidate();

            UpdateShihabStatusBadges();
        }

        private void PopulateShihabCategoryTree()
        {
            _mehediCategoryTree.BeginUpdate();
            _mehediCategoryTree.Nodes.Clear();

            TreeNode shihabAllNode = new TreeNode("All Downloads") { Tag = "ALL" };
            AddCategorySubNodes(shihabAllNode, "CAT:");

            TreeNode mehediUnfinNode = new TreeNode("Unfinished") { Tag = "UNFINISHED" };
            AddCategorySubNodes(mehediUnfinNode, "UNFIN_CAT:");

            TreeNode shihabFinNode = new TreeNode("Finished") { Tag = "FINISHED" };
            AddCategorySubNodes(shihabFinNode, "FIN_CAT:");

            TreeNode mehediGrabberNode = new TreeNode("Grabber projects") { Tag = "GRABBER_PROJECTS" };

            TreeNode mehediQueuesNode = new TreeNode("Queues") { Tag = "QUEUES_ALL" };
            for (int shihabQ = 0; shihabQ < _mehediConfig.MehediQueues.Count; shihabQ++)
            {
                string mehediQName = _mehediConfig.MehediQueues[shihabQ].ShihabQueueName;
                mehediQueuesNode.Nodes.Add(new TreeNode(mehediQName) { Tag = "QUEUE:" + mehediQName });
            }

            _mehediCategoryTree.Nodes.Add(shihabAllNode);
            _mehediCategoryTree.Nodes.Add(mehediUnfinNode);
            _mehediCategoryTree.Nodes.Add(shihabFinNode);
            _mehediCategoryTree.Nodes.Add(mehediGrabberNode);
            _mehediCategoryTree.Nodes.Add(mehediQueuesNode);
            shihabAllNode.Expand();
            _mehediCategoryTree.SelectedNode = shihabAllNode;
            _mehediCategoryTree.EndUpdate();
        }

        private void AddCategorySubNodes(TreeNode shihabParent, string mehediPrefix)
        {
            for (int shihabI = 0; shihabI < _mehediConfig.ShihabCategories.Count; shihabI++)
            {
                string mehediName = _mehediConfig.ShihabCategories[shihabI].ShihabName;
                shihabParent.Nodes.Add(new TreeNode(mehediName) { Tag = mehediPrefix + mehediName });
            }
        }

        private bool MatchesShihabTreeAndSearch(ShihabDownloadItem mehediItem)
        {
            ShihabDownloadStatus shihabSt = mehediItem.GetShihabStatusEnum();
            bool mehediIsDone = shihabSt == ShihabDownloadStatus.ShihabCompleted;

            if (_shihabActiveTreeFilter == "UNFINISHED" && mehediIsDone) return false;
            if (_shihabActiveTreeFilter == "FINISHED" && !mehediIsDone) return false;
            if (_shihabActiveTreeFilter == "GRABBER_PROJECTS" && (mehediItem.ShihabDescription ?? "").IndexOf("Grabber", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (_shihabActiveTreeFilter == "QUEUES_ALL" && string.IsNullOrEmpty(mehediItem.MehediQueueName)) return false;

            if (_shihabActiveTreeFilter.StartsWith("CAT:"))
            {
                string shihabC = _shihabActiveTreeFilter.Substring(4);
                if (!string.Equals(mehediItem.ShihabCategoryName, shihabC, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (_shihabActiveTreeFilter.StartsWith("UNFIN_CAT:"))
            {
                string mehediC = _shihabActiveTreeFilter.Substring(10);
                if (mehediIsDone || !string.Equals(mehediItem.ShihabCategoryName, mehediC, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (_shihabActiveTreeFilter.StartsWith("FIN_CAT:"))
            {
                string shihabC = _shihabActiveTreeFilter.Substring(8);
                if (!mehediIsDone || !string.Equals(mehediItem.ShihabCategoryName, shihabC, StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (_shihabActiveTreeFilter.StartsWith("QUEUE:"))
            {
                string mehediQ = _shihabActiveTreeFilter.Substring(6);
                if (!string.Equals(mehediItem.MehediQueueName, mehediQ, StringComparison.OrdinalIgnoreCase)) return false;
            }

            string shihabSearch = _mehediSearchBox.Text.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(shihabSearch))
            {
                bool mehediHit =
                    (mehediItem.ShihabFileName ?? "").ToLowerInvariant().Contains(shihabSearch) ||
                    (mehediItem.MehediUrl ?? "").ToLowerInvariant().Contains(shihabSearch) ||
                    (mehediItem.ShihabDescription ?? "").ToLowerInvariant().Contains(shihabSearch) ||
                    (mehediItem.MehediReferer ?? "").ToLowerInvariant().Contains(shihabSearch);
                if (!mehediHit) return false;
            }
            return true;
        }

        private void RefreshMehediDownloadsGrid()
        {
            if (IsDisposed) return;

            List<ShihabDownloadItem> shihabAll = _shihabEngine.GetShihabDownloadsSnapshot();
            List<ShihabDownloadItem> mehediFiltered = new List<ShihabDownloadItem>();
            for (int shihabI = 0; shihabI < shihabAll.Count; shihabI++)
            {
                if (MatchesShihabTreeAndSearch(shihabAll[shihabI]))
                {
                    mehediFiltered.Add(shihabAll[shihabI]);
                }
            }

            if (_mehediSortColumnKey == "ORDER")
            {
                if (_shihabSortAscending)
                {
                    mehediFiltered.Reverse();
                }
            }
            else
            {
                int mehediDir = _shihabSortAscending ? 1 : -1;
                mehediFiltered.Sort(delegate(ShihabDownloadItem shihabA, ShihabDownloadItem mehediB)
                {
                    if (_mehediSortColumnKey == "NAME") return mehediDir * string.Compare(shihabA.ShihabFileName, mehediB.ShihabFileName, StringComparison.OrdinalIgnoreCase);
                    if (_mehediSortColumnKey == "SIZE") return mehediDir * shihabA.ShihabTotalBytes.CompareTo(mehediB.ShihabTotalBytes);
                    if (_mehediSortColumnKey == "STATUS") return mehediDir * string.Compare(shihabA.ShihabStatusText, mehediB.ShihabStatusText, StringComparison.OrdinalIgnoreCase);
                    if (_mehediSortColumnKey == "SPEED") return mehediDir * shihabA.ShihabCurrentSpeedBps.CompareTo(mehediB.ShihabCurrentSpeedBps);
                    return 0;
                });
            }

            HashSet<string> shihabSelectedIds = new HashSet<string>();
            foreach (ListViewItem mehediSel in _shihabDownloadsListView.SelectedItems)
            {
                ShihabDownloadItem shihabTag = mehediSel.Tag as ShihabDownloadItem;
                if (shihabTag != null) shihabSelectedIds.Add(shihabTag.ShihabId);
            }

            _shihabDownloadsListView.BeginUpdate();
            while (_shihabDownloadsListView.Items.Count < mehediFiltered.Count)
            {
                _shihabDownloadsListView.Items.Add(new ListViewItem(new string[] { "", "", "", "", "", "", "", "" }));
            }
            while (_shihabDownloadsListView.Items.Count > mehediFiltered.Count)
            {
                _shihabDownloadsListView.Items.RemoveAt(_shihabDownloadsListView.Items.Count - 1);
            }

            double mehediAggregateSpeed = 0;
            int shihabActiveCount = 0;

            for (int mehediIdx = 0; mehediIdx < mehediFiltered.Count; mehediIdx++)
            {
                ShihabDownloadItem shihabIt = mehediFiltered[mehediIdx];
                ListViewItem mehediRow = _shihabDownloadsListView.Items[mehediIdx];
                mehediRow.Tag = shihabIt;

                string shihabQueueShort = "";
                if (!string.IsNullOrEmpty(shihabIt.MehediQueueName))
                {
                    shihabQueueShort = shihabIt.MehediQueueName.StartsWith("Main", StringComparison.OrdinalIgnoreCase) ? "Main" : "Q";
                }

                mehediRow.SubItems[0].Text = shihabIt.ShihabFileName;
                mehediRow.SubItems[1].Text = shihabQueueShort;
                mehediRow.SubItems[2].Text = shihabIt.ShihabTotalBytes >= 0 ? ShihabFormatHelpers.FormatMehediBytes(shihabIt.ShihabTotalBytes) : "";
                mehediRow.SubItems[3].Text = shihabIt.ShihabStatusText ?? "";
                mehediRow.SubItems[4].Text = shihabIt.MehediEtaSeconds >= 0 ? ShihabFormatHelpers.FormatMehediTimeLeft(shihabIt.MehediEtaSeconds) : "";
                mehediRow.SubItems[5].Text = ShihabFormatHelpers.FormatShihabSpeed(shihabIt.ShihabCurrentSpeedBps);
                mehediRow.SubItems[6].Text = shihabIt.MehediLastTryDate ?? "";
                mehediRow.SubItems[7].Text = shihabIt.ShihabDescription ?? "";
                mehediRow.Selected = shihabSelectedIds.Contains(shihabIt.ShihabId);

                if (_shihabEngine.IsMehediItemActive(shihabIt.ShihabId))
                {
                    shihabActiveCount++;
                    mehediAggregateSpeed += shihabIt.ShihabCurrentSpeedBps;
                }
            }
            _shihabDownloadsListView.EndUpdate();

            _shihabStatusSummaryLabel.Text = string.Format("Total: {0} files  |  Active: {1}  |  Live Speed: {2}",
                shihabAll.Count,
                shihabActiveCount,
                mehediAggregateSpeed > 5.0 ? ShihabFormatHelpers.FormatShihabSpeed(mehediAggregateSpeed) : "0 KB/sec");

            if (_shihabDropTargetForm != null && !_shihabDropTargetForm.IsDisposed && _shihabDropTargetForm.Visible)
            {
                _shihabDropTargetForm.UpdateShihabLiveSpeed(mehediAggregateSpeed);
            }
            UpdateMehediToolbarActionIcons();
            UpdateShihabStatusBadges();
        }

        private void UpdateShihabStatusBadges()
        {
            _shihabSpeedLimiterBadge.Text = _mehediConfig.MehediGlobalSpeedLimiterEnabled
                ? string.Format("Speed Limiter: ON ({0} KB/s)", _mehediConfig.ShihabGlobalSpeedLimitKb)
                : "Speed Limiter: OFF";

            _mehediBridgeStatusLabel.Text = _mehediBridgeListening
                ? string.Format("Browser Bridge: Online (127.0.0.1:{0})", _mehediConfig.MehediLocalBridgePort)
                : "Browser Bridge: Port Busy";
        }

        private void OnShihabMainFormFirstShown()
        {
            if (_mehediConfig.MehediShowFloatingDropTarget)
            {
                ToggleMehediFloatingDropTarget();
            }

            if (_shihabStartupCli != null)
            {
                if (_shihabStartupCli.ShihabStartMinimized)
                {
                    Hide();
                }

                if (_shihabStartupCli.MehediAutoCloseMs > 0)
                {
                    Timer mehediAutoClose = new Timer { Interval = Math.Max(400, _shihabStartupCli.MehediAutoCloseMs) };
                    mehediAutoClose.Tick += delegate
                    {
                        mehediAutoClose.Stop();
                        ExitShihabApplicationCleanly();
                    };
                    mehediAutoClose.Start();
                }

                if (!string.IsNullOrWhiteSpace(_shihabStartupCli.ShihabDirectUrl))
                {
                    ShihabCapturedLinkDto shihabCliDto = new ShihabCapturedLinkDto
                    {
                        ShihabUrl = _shihabStartupCli.ShihabDirectUrl,
                        ShihabFileName = _shihabStartupCli.ShihabLocalFileName ?? "",
                        ShihabSaveDirectory = _shihabStartupCli.MehediLocalSavePath ?? "",
                        ShihabSilentMode = _shihabStartupCli.MehediSilentNoPrompt,
                        MehediAddToQueueOnly = _shihabStartupCli.MehediAddToQueueOnly,
                        MehediExitWhenDone = _shihabStartupCli.ShihabExitOnDone
                    };
                    HandleBrowserShihabSingleCapture(shihabCliDto);
                }

                if (_shihabStartupCli.ShihabStartMainQueue)
                {
                    _shihabEngine.StartShihabQueue("Main Download Queue");
                }
            }
        }

        private void OnMehediPeriodicTimerTick()
        {
            _shihabEngine.TickMehediQueuesAndScheduler();
            RefreshMehediDownloadsGrid();

            if (_mehediConfig.ShihabMonitorClipboard)
            {
                PollShihabClipboardForDownloadUrl();
            }
        }

        private void PollShihabClipboardForDownloadUrl()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string mehediText = Clipboard.GetText().Trim();
                if (string.IsNullOrEmpty(mehediText) || mehediText == _shihabLastClipboardText || mehediText.Length > 1500)
                {
                    return;
                }
                _shihabLastClipboardText = mehediText;

                Uri shihabUri;
                if (!Uri.TryCreate(mehediText, UriKind.Absolute, out shihabUri)) return;
                if (shihabUri.Scheme != "http" && shihabUri.Scheme != "https") return;

                string mehediExt = Path.GetExtension(shihabUri.AbsolutePath).TrimStart('.');
                if (string.IsNullOrEmpty(mehediExt)) return;

                if (_mehediConfig.MatchesShihabAutoCaptureExtension(mehediExt))
                {
                    ShihabCapturedLinkDto shihabClipDto = new ShihabCapturedLinkDto
                    {
                        ShihabUrl = mehediText,
                        ShihabFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(mehediText),
                        ShihabSilentMode = false
                    };
                    HandleBrowserShihabSingleCapture(shihabClipDto);
                }
            }
            catch { }
        }

        private IWin32Window GetShihabDialogOwnerOrNull()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                return this;
            }
            return null;
        }

        private static void PlayShihabConfiguredSound(bool mehediEnabled, string shihabWavPath, bool mehediIsError)
        {
            if (!mehediEnabled) return;
            try
            {
                if (!string.IsNullOrWhiteSpace(shihabWavPath) && File.Exists(shihabWavPath))
                {
                    using (SoundPlayer mehediPlayer = new SoundPlayer(shihabWavPath))
                    {
                        mehediPlayer.Play();
                    }
                    return;
                }
                if (mehediIsError) SystemSounds.Hand.Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        private void HandleBrowserShihabSingleCapture(ShihabCapturedLinkDto mehediDto)
        {
            SafeShihabUiInvoke(delegate
            {
                ShihabDownloadItem shihabNewItem = new ShihabDownloadItem();
                shihabNewItem.MehediUrl = mehediDto.ShihabUrl.Trim();
                shihabNewItem.MehediReferer = mehediDto.MehediReferrer ?? "";
                shihabNewItem.ShihabCookies = mehediDto.ShihabCookies ?? "";
                shihabNewItem.MehediOriginHeader = mehediDto.ShihabOrigin ?? "";
                shihabNewItem.MehediOmitCookies = mehediDto.MehediOmitCookies;
                shihabNewItem.MehediAudioCompanionUrl = mehediDto.ShihabAudioUrl ?? "";
                shihabNewItem.ShihabAudioCompanionBytes = mehediDto.MehediAudioSize > 0 ? mehediDto.MehediAudioSize : -1L;
                shihabNewItem.ShihabSourceTabId = mehediDto.ShihabTabId >= 0 ? mehediDto.ShihabTabId : -1;
                shihabNewItem.ShihabQualityLabel = mehediDto.MehediQualityLabel ?? "";
                if (!string.IsNullOrWhiteSpace(mehediDto.MehediUserAgent))
                {
                    shihabNewItem.MehediUserAgent = mehediDto.MehediUserAgent;
                }
                shihabNewItem.ShihabDescription = mehediDto.MehediTabTitle ?? "";
                shihabNewItem.ShihabTotalBytes = mehediDto.ShihabFileSize > 0 ? mehediDto.ShihabFileSize : -1L;
                shihabNewItem.MehediMaxConnections = _mehediConfig.ShihabDefaultMaxConnections;
                shihabNewItem.MehediExitAppOnComplete = mehediDto.MehediExitWhenDone;
                shihabNewItem.ShihabShowCompleteDialog = _mehediConfig.ShihabShowDownloadCompleteDialog && !mehediDto.ShihabSilentMode;

                string mehediRawName = ShihabFormatHelpers.ResolveSmartMehediFileName(
                    mehediDto.ShihabFileName,
                    shihabNewItem.MehediUrl,
                    mehediDto.MehediTabTitle,
                    shihabNewItem.ShihabQualityLabel);
                shihabNewItem.ShihabFileName = mehediRawName;

                ShihabCategoryRule shihabCat = _mehediConfig.ResolveMehediCategoryForFile(mehediRawName);
                shihabNewItem.ShihabCategoryName = shihabCat.ShihabName;
                string mehediTargetDir = !string.IsNullOrWhiteSpace(mehediDto.ShihabSaveDirectory)
                    ? mehediDto.ShihabSaveDirectory
                    : shihabCat.ShihabDefaultFolder;
                Directory.CreateDirectory(mehediTargetDir);
                shihabNewItem.MehediSaveDirectory = mehediTargetDir;
                shihabNewItem.ShihabFileName = (_mehediConfig.MehediDuplicateFileMode == 1)
                    ? ShihabFormatHelpers.SanitizeShihabFileName(mehediRawName)
                    : ShihabFormatHelpers.EnsureShihabUniqueFilePath(mehediTargetDir, mehediRawName);

                if (mehediDto.MehediAddToQueueOnly)
                {
                    shihabNewItem.MehediQueueName = "Main Download Queue";
                    shihabNewItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabQueued, "Queued");
                    _shihabEngine.AddShihabDownloadItem(shihabNewItem, false);
                    return;
                }

                if (mehediDto.ShihabSilentMode || !_mehediConfig.ShihabShowStartDownloadDialog)
                {
                    _shihabEngine.AddShihabDownloadItem(shihabNewItem, true);
                    if (_mehediConfig.MehediShowDownloadProgressDialog && !mehediDto.ShihabSilentMode)
                    {
                        ShowProgressWindowForShihabItem(shihabNewItem);
                    }
                    return;
                }

                using (MehediDownloadFileInfoForm shihabInfoDlg = new MehediDownloadFileInfoForm(shihabNewItem, _mehediConfig, _shihabEngine))
                {
                    IWin32Window mehediOwner = GetShihabDialogOwnerOrNull();
                    if (mehediOwner == null) shihabInfoDlg.StartPosition = FormStartPosition.CenterScreen;
                    DialogResult shihabDlgRes = mehediOwner != null ? shihabInfoDlg.ShowDialog(mehediOwner) : shihabInfoDlg.ShowDialog();
                    if (shihabDlgRes == DialogResult.OK)
                    {
                        _shihabStore.SaveMehediConfig(_mehediConfig);
                        _shihabEngine.AddShihabDownloadItem(shihabNewItem, shihabInfoDlg.ShihabStartImmediatelySelected);
                        if (shihabInfoDlg.ShihabStartImmediatelySelected && _mehediConfig.MehediShowDownloadProgressDialog)
                        {
                            ShowProgressWindowForShihabItem(shihabNewItem);
                        }
                    }
                }
            });
        }

        private void HandleBrowserMehediBatchCapture(ShihabBatchCaptureDto shihabBatch)
        {
            SafeShihabUiInvoke(delegate
            {
                using (MehediDownloadAllLinksForm mehediDlg = new MehediDownloadAllLinksForm(shihabBatch, _mehediConfig.ShihabDarkMode))
                {
                    IWin32Window shihabOwner = GetShihabDialogOwnerOrNull();
                    DialogResult mehediRes = shihabOwner != null ? mehediDlg.ShowDialog(shihabOwner) : mehediDlg.ShowDialog();
                    if (mehediRes == DialogResult.OK)
                    {
                        for (int shihabI = 0; shihabI < mehediDlg.ShihabChosenLinks.Count; shihabI++)
                        {
                            MehediBatchLinkEntryDto mehediLink = mehediDlg.ShihabChosenLinks[shihabI];
                            EnqueueSimpleShihabUrl(
                                mehediLink.ShihabUrl,
                                shihabBatch.ShihabPageUrl ?? "",
                                shihabBatch.ShihabCookies ?? "",
                                shihabBatch.MehediUserAgent ?? "",
                                mehediLink.MehediText ?? "",
                                mehediDlg.MehediStartNow);
                        }
                    }
                }
            });
        }

        private void EnqueueSimpleShihabUrl(
            string mehediUrl,
            string shihabReferer,
            string mehediCookies,
            string shihabUserAgent,
            string mehediDesc,
            bool shihabStartNow)
        {
            ShihabDownloadItem mehediItem = new ShihabDownloadItem();
            mehediItem.MehediUrl = mehediUrl;
            mehediItem.MehediReferer = shihabReferer;
            mehediItem.ShihabCookies = mehediCookies;
            if (!string.IsNullOrWhiteSpace(shihabUserAgent)) mehediItem.MehediUserAgent = shihabUserAgent;
            mehediItem.ShihabDescription = mehediDesc;
            mehediItem.ShihabFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(mehediUrl);
            ShihabCategoryRule shihabCat = _mehediConfig.ResolveMehediCategoryForFile(mehediItem.ShihabFileName);
            mehediItem.ShihabCategoryName = shihabCat.ShihabName;
            Directory.CreateDirectory(shihabCat.ShihabDefaultFolder);
            mehediItem.MehediSaveDirectory = shihabCat.ShihabDefaultFolder;
            mehediItem.ShihabFileName = ShihabFormatHelpers.EnsureShihabUniqueFilePath(shihabCat.ShihabDefaultFolder, mehediItem.ShihabFileName);
            mehediItem.MehediMaxConnections = _mehediConfig.ShihabDefaultMaxConnections;
            if (!shihabStartNow)
            {
                mehediItem.MehediQueueName = "Main Download Queue";
                mehediItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabQueued, "Queued");
            }
            _shihabEngine.AddShihabDownloadItem(mehediItem, shihabStartNow);
        }

        private void HandleShihabItemCompleted(ShihabDownloadItem mehediItem)
        {
            SafeShihabUiInvoke(delegate
            {
                RefreshMehediDownloadsGrid();
                PlayShihabConfiguredSound(_mehediConfig.MehediPlaySoundComplete, _mehediConfig.ShihabSoundCompleteWav, false);
                if (mehediItem.ShihabOpenWhenComplete)
                {
                    try
                    {
                        string shihabTarget = mehediItem.GetShihabFullTargetPath();
                        if (File.Exists(shihabTarget)) Process.Start(shihabTarget);
                    }
                    catch { }
                }
                if (mehediItem.MehediExitAppOnComplete)
                {
                    ExitShihabApplicationCleanly();
                    return;
                }
                if (mehediItem.ShihabShowCompleteDialog && _mehediConfig.ShihabShowDownloadCompleteDialog)
                {
                    MehediDownloadCompleteForm shihabDoneForm = new MehediDownloadCompleteForm(mehediItem, _mehediConfig);
                    IWin32Window mehediOwner = GetShihabDialogOwnerOrNull();
                    if (mehediOwner != null) shihabDoneForm.Show(mehediOwner);
                    else shihabDoneForm.Show();
                }
            });
        }

        private void HandleMehediItemFailed(ShihabDownloadItem shihabFailedItem)
        {
            SafeShihabUiInvoke(delegate
            {
                PlayShihabConfiguredSound(_mehediConfig.MehediPlaySoundError, _mehediConfig.ShihabSoundErrorWav, true);
            });
        }

        private void PromptAndAddNewShihabUrl(string mehediPrefillUrl)
        {
            using (MehediAddUrlAuthDialog shihabAddDlg = new MehediAddUrlAuthDialog(_mehediConfig.ShihabDarkMode, mehediPrefillUrl))
            {
                if (shihabAddDlg.ShowDialog(this) == DialogResult.OK)
                {
                    ShihabDownloadItem mehediDraft = new ShihabDownloadItem
                    {
                        MehediUrl = shihabAddDlg.ShihabEnteredUrl,
                        MehediReferer = shihabAddDlg.MehediEnteredReferer,
                        ShihabAuthUser = shihabAddDlg.MehediEnteredUser,
                        MehediAuthPassword = shihabAddDlg.ShihabEnteredPassword,
                        ShihabFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(shihabAddDlg.ShihabEnteredUrl),
                        MehediMaxConnections = _mehediConfig.ShihabDefaultMaxConnections
                    };
                    using (MehediDownloadFileInfoForm shihabInfo = new MehediDownloadFileInfoForm(mehediDraft, _mehediConfig, _shihabEngine))
                    {
                        if (shihabInfo.ShowDialog(this) == DialogResult.OK)
                        {
                            _shihabEngine.AddShihabDownloadItem(mehediDraft, shihabInfo.ShihabStartImmediatelySelected);
                            if (shihabInfo.ShihabStartImmediatelySelected && _mehediConfig.MehediShowDownloadProgressDialog)
                            {
                                ShowProgressWindowForShihabItem(mehediDraft);
                            }
                        }
                    }
                }
            }
        }

        private void ShowProgressWindowForShihabItem(ShihabDownloadItem mehediItem)
        {
            ShihabDownloadProgressForm shihabExisting;
            if (_mehediOpenProgressForms.TryGetValue(mehediItem.ShihabId, out shihabExisting) && !shihabExisting.IsDisposed)
            {
                shihabExisting.BringToFront();
                return;
            }
            ShihabDownloadProgressForm mehediProg = new ShihabDownloadProgressForm(mehediItem, _shihabEngine, _mehediConfig);
            _mehediOpenProgressForms[mehediItem.ShihabId] = mehediProg;
            mehediProg.FormClosed += delegate { _mehediOpenProgressForms.Remove(mehediItem.ShihabId); };
            IWin32Window shihabOwner = GetShihabDialogOwnerOrNull();
            if (shihabOwner != null) mehediProg.Show(shihabOwner);
            else mehediProg.Show();
            try { mehediProg.Activate(); mehediProg.BringToFront(); } catch { }
        }

        private List<ShihabDownloadItem> GetSelectedShihabItems()
        {
            List<ShihabDownloadItem> mehediList = new List<ShihabDownloadItem>();
            foreach (ListViewItem shihabRow in _shihabDownloadsListView.SelectedItems)
            {
                if (shihabRow.Tag is ShihabDownloadItem)
                {
                    mehediList.Add((ShihabDownloadItem)shihabRow.Tag);
                }
            }
            return mehediList;
        }

        private void ResumeSelectedShihabDownloads()
        {
            List<ShihabDownloadItem> mehediSel = GetSelectedShihabItems();
            for (int shihabI = 0; shihabI < mehediSel.Count; shihabI++)
            {
                if (mehediSel[shihabI].GetShihabStatusEnum() != ShihabDownloadStatus.ShihabCompleted)
                {
                    _shihabEngine.StartOrResumeMehediDownload(mehediSel[shihabI]);
                }
            }
        }

        private void PauseSelectedMehediDownloads()
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            for (int mehediI = 0; mehediI < shihabSel.Count; mehediI++)
            {
                _shihabEngine.PauseShihabDownload(shihabSel[mehediI]);
            }
        }

        private void DeleteSelectedShihabDownloads(bool mehediAlsoDeleteDiskFile)
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            if (shihabSel.Count == 0) return;
            for (int mehediI = 0; mehediI < shihabSel.Count; mehediI++)
            {
                _shihabEngine.RemoveShihabDownload(shihabSel[mehediI], mehediAlsoDeleteDiskFile);
            }
            RefreshMehediDownloadsGrid();
        }

        private void DeleteAllCompletedMehediDownloads()
        {
            List<ShihabDownloadItem> shihabAll = _shihabEngine.GetShihabDownloadsSnapshot();
            for (int mehediI = 0; mehediI < shihabAll.Count; mehediI++)
            {
                if (shihabAll[mehediI].GetShihabStatusEnum() == ShihabDownloadStatus.ShihabCompleted)
                {
                    _shihabEngine.RemoveShihabDownload(shihabAll[mehediI], false);
                }
            }
            RefreshMehediDownloadsGrid();
        }

        private void RedownloadSelectedShihabItem()
        {
            List<ShihabDownloadItem> mehediSel = GetSelectedShihabItems();
            if (mehediSel.Count == 0) return;
            ShihabDownloadItem shihabItem = mehediSel[0];
            _shihabEngine.PauseShihabDownload(shihabItem);
            shihabItem.MehediSegments.Clear();
            shihabItem.MehediDownloadedBytes = 0;
            shihabItem.MehediSha256Hex = "";
            _shihabEngine.StartOrResumeMehediDownload(shihabItem);
            if (_mehediConfig.MehediShowDownloadProgressDialog) ShowProgressWindowForShihabItem(shihabItem);
        }

        private void RefreshSelectedMehediAddress()
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            if (shihabSel.Count == 0) return;
            ShihabDownloadItem mehediItem = shihabSel[0];
            using (MehediAddUrlAuthDialog shihabRefreshDlg = new MehediAddUrlAuthDialog(_mehediConfig.ShihabDarkMode, mehediItem.MehediUrl))
            {
                shihabRefreshDlg.Text = "Refresh Expired Download Address — " + mehediItem.ShihabFileName;
                if (shihabRefreshDlg.ShowDialog(this) == DialogResult.OK)
                {
                    mehediItem.MehediUrl = shihabRefreshDlg.ShihabEnteredUrl;
                    if (!string.IsNullOrEmpty(shihabRefreshDlg.MehediEnteredReferer))
                    {
                        mehediItem.MehediReferer = shihabRefreshDlg.MehediEnteredReferer;
                    }
                    _shihabEngine.PersistShihabState();
                    _shihabEngine.StartOrResumeMehediDownload(mehediItem);
                }
            }
        }

        private void AssignSelectedShihabToQueue(string mehediQueueName)
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            for (int mehediI = 0; mehediI < shihabSel.Count; mehediI++)
            {
                shihabSel[mehediI].MehediQueueName = mehediQueueName;
            }
            _shihabEngine.PersistShihabState();
            RefreshMehediDownloadsGrid();
        }

        private void OpenSelectedMehediProgressWindow()
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            if (shihabSel.Count > 0) ShowProgressWindowForShihabItem(shihabSel[0]);
        }

        private void OpenSelectedShihabCompletedFile(bool mehediOpenWith)
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            if (shihabSel.Count == 0) return;
            string mehediPath = shihabSel[0].GetShihabFullTargetPath();
            if (!File.Exists(mehediPath)) return;
            try
            {
                if (mehediOpenWith) Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + mehediPath);
                else Process.Start(mehediPath);
            }
            catch { }
        }

        private void OpenSelectedMehediContainingFolder()
        {
            List<ShihabDownloadItem> shihabSel = GetSelectedShihabItems();
            if (shihabSel.Count == 0) return;
            string mehediPath = shihabSel[0].GetShihabFullTargetPath();
            try
            {
                if (File.Exists(mehediPath)) Process.Start("explorer.exe", "/select,\"" + mehediPath + "\"");
                else if (Directory.Exists(shihabSel[0].MehediSaveDirectory)) Process.Start("explorer.exe", "\"" + shihabSel[0].MehediSaveDirectory + "\"");
            }
            catch { }
        }

        private void OpenSelectedShihabProperties()
        {
            List<ShihabDownloadItem> mehediSel = GetSelectedShihabItems();
            if (mehediSel.Count == 0) return;
            using (ShihabFilePropertiesForm shihabProp = new ShihabFilePropertiesForm(mehediSel[0], _mehediConfig.ShihabDarkMode))
            {
                if (shihabProp.ShowDialog(this) == DialogResult.OK)
                {
                    _shihabEngine.PersistShihabState();
                    RefreshMehediDownloadsGrid();
                }
            }
        }

        private void HandleDoubleClickOnShihabRow()
        {
            List<ShihabDownloadItem> mehediSel = GetSelectedShihabItems();
            if (mehediSel.Count == 0) return;
            if (mehediSel[0].GetShihabStatusEnum() == ShihabDownloadStatus.ShihabCompleted)
            {
                OpenSelectedShihabCompletedFile(false);
            }
            else
            {
                ShowProgressWindowForShihabItem(mehediSel[0]);
            }
        }

        private void OpenShihabOptionsDialog()
        {
            using (ShihabOptionsConfigForm mehediOpts = new ShihabOptionsConfigForm(_mehediConfig, _shihabStore, _mehediBridge))
            {
                if (mehediOpts.ShowDialog(this) == DialogResult.OK)
                {
                    if (mehediOpts.ShihabBridgeWasRestarted)
                    {
                        _mehediBridgeListening = mehediOpts.MehediBridgeRestartSucceeded;
                    }
                    ApplyMehediTheme();
                    PopulateShihabCategoryTree();
                    RefreshMehediDownloadsGrid();
                    UpdateShihabStatusBadges();
                }
            }
        }

        private void OpenMehediSchedulerDialog()
        {
            using (MehediSchedulerQueuesForm shihabSched = new MehediSchedulerQueuesForm(_mehediConfig, _shihabEngine, _shihabStore))
            {
                shihabSched.ShowDialog(this);
                PopulateShihabCategoryTree();
                RefreshMehediDownloadsGrid();
            }
        }

        private void OpenShihabBatchWildcardDialog()
        {
            using (ShihabBatchDownloadForm mehediBatch = new ShihabBatchDownloadForm(_mehediConfig.ShihabDarkMode))
            {
                if (mehediBatch.ShowDialog(this) == DialogResult.OK)
                {
                    for (int shihabI = 0; shihabI < mehediBatch.ShihabGeneratedUrls.Count; shihabI++)
                    {
                        EnqueueSimpleShihabUrl(mehediBatch.ShihabGeneratedUrls[shihabI], "", "", "", "Batch wildcard", mehediBatch.MehediStartImmediately);
                    }
                }
            }
        }

        private void ImportShihabClipboardUrlsBatch()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show(this, "Cannot find any web address in the clipboard!", "MHS Download Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string[] mehediTokens = Clipboard.GetText().Split(new char[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                ShihabBatchCaptureDto shihabDto = new ShihabBatchCaptureDto { ShihabLinks = new List<MehediBatchLinkEntryDto>() };
                for (int mehediI = 0; mehediI < mehediTokens.Length; mehediI++)
                {
                    if (mehediTokens[mehediI].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        mehediTokens[mehediI].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabDto.ShihabLinks.Add(new MehediBatchLinkEntryDto { ShihabUrl = mehediTokens[mehediI], MehediText = "Clipboard URL" });
                    }
                }
                if (shihabDto.ShihabLinks.Count == 0)
                {
                    MessageBox.Show(this, "Cannot find any http:// or https:// web address in clipboard!", "MHS Download Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                HandleBrowserMehediBatchCapture(shihabDto);
            }
            catch { }
        }

        private void OpenShihabSiteGrabberDialog()
        {
            using (ShihabSiteGrabberForm mehediGrab = new ShihabSiteGrabberForm(_mehediConfig.ShihabDarkMode))
            {
                if (mehediGrab.ShowDialog(this) == DialogResult.OK)
                {
                    for (int shihabI = 0; shihabI < mehediGrab.ShihabSelectedGrabbedUrls.Count; shihabI++)
                    {
                        EnqueueSimpleShihabUrl(mehediGrab.ShihabSelectedGrabbedUrls[shihabI], "", "", "", "Site Grabber", mehediGrab.MehediStartImmediately);
                    }
                }
            }
        }

        private void ToggleMehediFloatingDropTarget()
        {
            if (_shihabDropTargetForm == null || _shihabDropTargetForm.IsDisposed)
            {
                _shihabDropTargetForm = new MehediFloatingDropTargetForm(delegate(string shihabUrl)
                {
                    PromptAndAddNewShihabUrl(shihabUrl);
                });
                _shihabDropTargetForm.Show();
                _mehediConfig.MehediShowFloatingDropTarget = true;
            }
            else
            {
                _shihabDropTargetForm.Visible = !_shihabDropTargetForm.Visible;
                _mehediConfig.MehediShowFloatingDropTarget = _shihabDropTargetForm.Visible;
            }
            _shihabStore.SaveMehediConfig(_mehediConfig);
        }

        private void ExportShihabList(bool mehediEf2Format)
        {
            using (SaveFileDialog shihabSfd = new SaveFileDialog())
            {
                shihabSfd.Filter = mehediEf2Format ? "IDM Export Files (*.ef2)|*.ef2" : "Text Files (*.txt)|*.txt";
                shihabSfd.FileName = mehediEf2Format ? "shihab_downloads.ef2" : "shihab_downloads.txt";
                if (shihabSfd.ShowDialog(this) == DialogResult.OK)
                {
                    List<ShihabDownloadItem> mehediItems = _shihabEngine.GetShihabDownloadsSnapshot();
                    if (mehediEf2Format)
                    {
                        ShihabStateStore.ExportToShihabEf2File(shihabSfd.FileName, mehediItems);
                    }
                    else
                    {
                        List<string> shihabLines = new List<string>();
                        for (int mehediI = 0; mehediI < mehediItems.Count; mehediI++) shihabLines.Add(mehediItems[mehediI].MehediUrl);
                        File.WriteAllLines(shihabSfd.FileName, shihabLines.ToArray());
                    }
                }
            }
        }

        private void ImportMehediList()
        {
            using (OpenFileDialog shihabOfd = new OpenFileDialog())
            {
                shihabOfd.Filter = "IDM Export & Text Files (*.ef2;*.txt)|*.ef2;*.txt|All Files (*.*)|*.*";
                if (shihabOfd.ShowDialog(this) == DialogResult.OK)
                {
                    List<ShihabDownloadItem> mehediImported = ShihabStateStore.ImportFromMehediEf2OrText(shihabOfd.FileName);
                    for (int shihabI = 0; shihabI < mehediImported.Count; shihabI++)
                    {
                        ShihabDownloadItem mehediIt = mehediImported[shihabI];
                        ShihabCategoryRule shihabCat = _mehediConfig.ResolveMehediCategoryForFile(mehediIt.ShihabFileName);
                        mehediIt.ShihabCategoryName = shihabCat.ShihabName;
                        mehediIt.MehediSaveDirectory = shihabCat.ShihabDefaultFolder;
                        mehediIt.MehediQueueName = "Main Download Queue";
                        mehediIt.SetMehediStatusEnum(ShihabDownloadStatus.ShihabQueued, "Queued");
                        _shihabEngine.AddShihabDownloadItem(mehediIt, false);
                    }
                }
            }
        }

        private void ShowShihabExtensionInstructions()
        {
            string mehediExtFolder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\extension"));
            MessageBox.Show(
                this,
                "MHS Download Manager Integration Module (Manifest V3 — Chrome / Edge / Brave / Opera):\n\n" +
                "1. Open chrome://extensions or edge://extensions\n" +
                "2. Enable 'Developer mode' in the top-right corner\n" +
                "3. Click 'Load unpacked' (or Reload ↻) on:\n   " + mehediExtFolder + "\n\n" +
                "Features active immediately via 127.0.0.1:" + _mehediConfig.MehediLocalBridgePort + " (+ optional Native Messaging):\n" +
                "• Automatic file download takeover with Cookie + Referer + User-Agent forwarding\n" +
                "• Hold Alt while clicking any link to bypass capture\n" +
                "• Floating MHS Download corner icon on YouTube, Facebook, Instagram & HTML5 video players\n" +
                "• Automatic extraction of exact selected-video titles & post captions\n",
                "MHS Download Manager 1.0.0 — Browser Extension",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowMehediCliHelp()
        {
            MessageBox.Show(
                this,
                "MHS Download Manager Command-Line Switches:\n\n" +
                "mdm.exe /d URL [/p LocalPath] [/f FileName] [/n] [/q] [/a] [/s]\n\n" +
                "  /d <URL>       Download specified file URL\n" +
                "  /p <Path>      Define local folder path to save file\n" +
                "  /f <FileName>  Define local file name to save file\n" +
                "  /n             Silent mode (don't ask any questions)\n" +
                "  /q             Exit MHS Download Manager after download finishes\n" +
                "  /a             Add file to Main Download Queue without starting\n" +
                "  /s             Start Main Download Queue immediately",
                "MHS Download Manager — CLI Switches",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowShihabAboutDialog()
        {
            using (ShihabAboutDeveloperDialog mehediAboutDlg = new ShihabAboutDeveloperDialog(_mehediConfig.ShihabDarkMode))
            {
                mehediAboutDlg.ShowDialog(this);
            }
        }

        private void SafeShihabUiInvoke(Action mehediAction)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(mehediAction);
                else mehediAction();
            }
            catch { }
        }

        private void HandleMehediFormClosing(object shihabSender, FormClosingEventArgs mehediArgs)
        {
            if (!_shihabExitingExplicitly && mehediArgs.CloseReason == CloseReason.UserClosing)
            {
                mehediArgs.Cancel = true;
                Hide();
                return;
            }
            CleanupShihabResources();
        }

        public void ExitShihabApplicationCleanly()
        {
            _shihabExitingExplicitly = true;
            CleanupShihabResources();
            Close();
            Application.Exit();
        }

        private void CleanupShihabResources()
        {
            try { if (_mehediUiTickTimer != null) _mehediUiTickTimer.Stop(); } catch { }
            try { _shihabEngine.StopAllMehediDownloads(); } catch { }
            try { _shihabEngine.PersistShihabState(); } catch { }
            try { _shihabStore.SaveMehediConfig(_mehediConfig); } catch { }
            try { _mehediBridge.Dispose(); } catch { }
            try
            {
                if (_shihabDropTargetForm != null && !_shihabDropTargetForm.IsDisposed) _shihabDropTargetForm.Close();
            }
            catch { }
            try
            {
                if (_mehediTrayIcon != null)
                {
                    _mehediTrayIcon.Visible = false;
                    _mehediTrayIcon.Dispose();
                }
            }
            catch { }
        }
    }
}