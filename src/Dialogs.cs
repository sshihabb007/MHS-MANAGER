using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MHSManager
{
    public sealed class AddDownloadDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly List<DownloadItem> _existingDownloads;
        private TextBox _txtUrl;
        private Button _btnProbe;
        private Label _lblProbeInfo;
        private TextBox _txtFileName;
        private ComboBox _cmbCategory;
        private ComboBox _cmbQueue;
        private TextBox _txtSaveDir;
        private NumericUpDown _numConnections;
        private TextBox _txtDescription;
        private TextBox _txtReferer;
        private TextBox _txtCookies;
        private TextBox _txtAuthUser;
        private TextBox _txtAuthPass;
        private TextBox _txtSha256;
        private CheckBox _chkStartQueueOnLater;
        private CancellationTokenSource _probeCts;
        private bool _updatingAutoFields;
        private bool _userEditedFileName;

        public DownloadItem ResultItem { get; private set; }
        public bool StartImmediately { get; private set; }
        public bool HoldPausedInQueue { get; private set; }

        public AddDownloadDialog(AppSettings settings, string initialUrl, string initialReferer, string initialCookies)
            : this(settings, initialUrl, initialReferer, initialCookies, null)
        {
        }

        public AddDownloadDialog(
            AppSettings settings,
            string initialUrl,
            string initialReferer,
            string initialCookies,
            List<DownloadItem> existingDownloads)
        {
            _settings = settings;
            _existingDownloads = existingDownloads;
            Text = "Download File Info — MHS Manager";
            Size = new Size(660, 575);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            BuildLayout();

            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                _txtUrl.Text = initialUrl.Trim();
            }
            if (!string.IsNullOrWhiteSpace(initialReferer))
            {
                _txtReferer.Text = initialReferer.Trim();
            }
            if (!string.IsNullOrWhiteSpace(initialCookies))
            {
                _txtCookies.Text = initialCookies.Trim();
            }

            Shown += OnDialogShown;
        }

        private void OnDialogShown(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_txtUrl.Text))
            {
                BeginProbeAsync();
            }
        }

        private void BuildLayout()
        {
            int leftLabel = 18;
            int leftInput = 138;
            int inputW = 486;
            int y = 16;

            Controls.Add(PlaceLabel("Download URL:", leftLabel, y + 4));
            _txtUrl = MhsTheme.CreateTextBox(false);
            _txtUrl.SetBounds(leftInput, y, inputW - 96, 25);
            _txtUrl.TextChanged += OnUrlTextChanged;
            Controls.Add(_txtUrl);

            _btnProbe = MhsTheme.CreateButton("Probe", MhsTheme.AccentSky, 88, 26);
            _btnProbe.Location = new Point(leftInput + inputW - 88, y - 1);
            _btnProbe.Click += delegate { BeginProbeAsync(); };
            Controls.Add(_btnProbe);

            y += 30;
            _lblProbeInfo = MhsTheme.CreateLabel("Enter an HTTP/HTTPS or .m3u8 URL to inspect file size & resume support.", true);
            _lblProbeInfo.Location = new Point(leftInput, y);
            Controls.Add(_lblProbeInfo);

            y += 28;
            Controls.Add(PlaceLabel("Save File Name:", leftLabel, y + 4));
            _txtFileName = MhsTheme.CreateTextBox(false);
            _txtFileName.SetBounds(leftInput, y, inputW, 25);
            _txtFileName.TextChanged += delegate
            {
                if (!_updatingAutoFields)
                {
                    _userEditedFileName = true;
                }
            };
            Controls.Add(_txtFileName);

            y += 35;
            Controls.Add(PlaceLabel("Category:", leftLabel, y + 4));
            _cmbCategory = new ComboBox();
            _cmbCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbCategory.BackColor = MhsTheme.BgInput;
            _cmbCategory.ForeColor = MhsTheme.TextPrimary;
            _cmbCategory.Items.AddRange(new object[] { "General", "Compressed", "Documents", "Music", "Programs", "Video" });
            _cmbCategory.SelectedIndex = 0;
            _cmbCategory.SetBounds(leftInput, y, 165, 25);
            _cmbCategory.SelectedIndexChanged += OnCategoryChanged;
            Controls.Add(_cmbCategory);

            Controls.Add(PlaceLabel("Queue:", leftInput + 185, y + 4));
            _cmbQueue = new ComboBox();
            _cmbQueue.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbQueue.BackColor = MhsTheme.BgInput;
            _cmbQueue.ForeColor = MhsTheme.TextPrimary;
            for (int i = 0; i < _settings.Queues.Count; i++)
            {
                _cmbQueue.Items.Add(_settings.Queues[i].Name);
            }
            if (_cmbQueue.Items.Count == 0) _cmbQueue.Items.Add("Main Queue");
            _cmbQueue.SelectedIndex = 0;
            _cmbQueue.SetBounds(leftInput + 240, y, 134, 25);
            Controls.Add(_cmbQueue);

            Controls.Add(PlaceLabel("Threads:", leftInput + 386, y + 4));
            _numConnections = new NumericUpDown();
            _numConnections.Minimum = 1;
            _numConnections.Maximum = 32;
            _numConnections.Value = Math.Max(1, Math.Min(32, _settings.DefaultConnectionsPerDownload));
            _numConnections.BackColor = MhsTheme.BgInput;
            _numConnections.ForeColor = MhsTheme.TextPrimary;
            _numConnections.SetBounds(leftInput + 440, y + 1, 46, 25);
            Controls.Add(_numConnections);

            y += 35;
            Controls.Add(PlaceLabel("Save Folder:", leftLabel, y + 4));
            _txtSaveDir = MhsTheme.CreateTextBox(false);
            _txtSaveDir.SetBounds(leftInput, y, inputW - 96, 25);
            _txtSaveDir.Text = CategoryClassifier.ResolveSaveDirectory(_settings, "General");
            Controls.Add(_txtSaveDir);

            Button btnBrowse = MhsTheme.CreateButton("Browse...", MhsTheme.BgElevated, 88, 26);
            btnBrowse.Location = new Point(leftInput + inputW - 88, y - 1);
            btnBrowse.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtSaveDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        _txtSaveDir.Text = fbd.SelectedPath;
                    }
                }
            };
            Controls.Add(btnBrowse);

            y += 35;
            Controls.Add(PlaceLabel("Description:", leftLabel, y + 4));
            _txtDescription = MhsTheme.CreateTextBox(false);
            _txtDescription.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtDescription);

            y += 36;
            Label hdrAdv = MhsTheme.CreateLabel("Authentication, Request Headers & Integrity Check (Optional)", true);
            hdrAdv.Font = MhsTheme.FontBold;
            hdrAdv.Location = new Point(leftLabel, y);
            Controls.Add(hdrAdv);

            y += 24;
            Controls.Add(PlaceLabel("Referer URL:", leftLabel, y + 4));
            _txtReferer = MhsTheme.CreateTextBox(false);
            _txtReferer.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtReferer);

            y += 32;
            Controls.Add(PlaceLabel("Cookie Header:", leftLabel, y + 4));
            _txtCookies = MhsTheme.CreateTextBox(false);
            _txtCookies.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtCookies);

            y += 32;
            Controls.Add(PlaceLabel("HTTP Auth User:", leftLabel, y + 4));
            _txtAuthUser = MhsTheme.CreateTextBox(false);
            _txtAuthUser.SetBounds(leftInput, y, 185, 25);
            Controls.Add(_txtAuthUser);

            Controls.Add(PlaceLabel("Password:", leftInput + 205, y + 4));
            _txtAuthPass = MhsTheme.CreateTextBox(false);
            _txtAuthPass.UseSystemPasswordChar = true;
            _txtAuthPass.SetBounds(leftInput + 280, y, 206, 25);
            Controls.Add(_txtAuthPass);

            y += 32;
            Controls.Add(PlaceLabel("Expected SHA256:", leftLabel, y + 4));
            _txtSha256 = MhsTheme.CreateTextBox(false);
            _txtSha256.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtSha256);

            y += 34;
            _chkStartQueueOnLater = new CheckBox();
            _chkStartQueueOnLater.Text = "Start queue processing when clicking 'Download Later'";
            _chkStartQueueOnLater.AutoSize = true;
            _chkStartQueueOnLater.Checked = false;
            _chkStartQueueOnLater.Location = new Point(leftInput, y);
            Controls.Add(_chkStartQueueOnLater);

            y += 30;
            Button btnStart = MhsTheme.CreateButton("Start Download", MhsTheme.AccentEmerald, 170, 36);
            btnStart.Location = new Point(leftInput + 50, y);
            btnStart.Click += delegate { ConfirmDialog(true); };
            Controls.Add(btnStart);

            Button btnQueue = MhsTheme.CreateButton("Download Later", MhsTheme.AccentSky, 150, 36);
            btnQueue.Location = new Point(leftInput + 230, y);
            btnQueue.Click += delegate { ConfirmDialog(false); };
            Controls.Add(btnQueue);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 90, 36);
            btnCancel.Location = new Point(leftInput + 390, y);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            AcceptButton = btnStart;
            CancelButton = btnCancel;
        }

        private Label PlaceLabel(string text, int x, int y)
        {
            Label lbl = MhsTheme.CreateLabel(text, false);
            lbl.Location = new Point(x, y);
            return lbl;
        }

        private void OnUrlTextChanged(object sender, EventArgs e)
        {
            if (_updatingAutoFields) return;
            string url = _txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            _updatingAutoFields = true;
            try
            {
                if (!_userEditedFileName)
                {
                    string guessed = CategoryClassifier.GuessFileNameFromUrl(url);
                    if (HttpHelpers.IsHlsUrlOrMime(url, null))
                    {
                        guessed = HttpHelpers.EnsureNonM3u8VideoFileName(guessed);
                    }
                    _txtFileName.Text = guessed;
                    string cat = CategoryClassifier.ClassifyByFileName(guessed);
                    SelectComboText(_cmbCategory, cat);
                    _txtSaveDir.Text = CategoryClassifier.ResolveSaveDirectory(_settings, cat);
                }

                if (string.IsNullOrWhiteSpace(_txtAuthUser.Text) && _settings.SiteLogins != null)
                {
                    SiteLoginEntry matched = HttpHelpers.FindMatchingSiteLogin(url, _settings.SiteLogins);
                    if (matched != null)
                    {
                        _txtAuthUser.Text = matched.UserName;
                        _txtAuthPass.Text = matched.Password ?? "";
                    }
                }

                CheckDuplicateNotice(url);
            }
            finally
            {
                _updatingAutoFields = false;
            }
        }

        private void CheckDuplicateNotice(string url)
        {
            if (_existingDownloads == null) return;
            for (int i = 0; i < _existingDownloads.Count; i++)
            {
                if (string.Equals((_existingDownloads[i].Url ?? "").Trim(), url, StringComparison.OrdinalIgnoreCase))
                {
                    _lblProbeInfo.ForeColor = MhsTheme.AccentAmber;
                    _lblProbeInfo.Text = "Note: This URL is already in your download list (" + _existingDownloads[i].Status + ").";
                    return;
                }
            }
        }

        private void OnCategoryChanged(object sender, EventArgs e)
        {
            if (_updatingAutoFields) return;
            string cat = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : "General";
            _txtSaveDir.Text = CategoryClassifier.ResolveSaveDirectory(_settings, cat);
        }

        private async void BeginProbeAsync()
        {
            string url = _txtUrl.Text.Trim();
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return;
            }

            if (_probeCts != null)
            {
                try { _probeCts.Cancel(); } catch { }
            }
            _probeCts = new CancellationTokenSource();
            _btnProbe.Enabled = false;
            _lblProbeInfo.ForeColor = MhsTheme.AccentCyan;
            _lblProbeInfo.Text = "Probing server headers & byte-range resume support...";

            UrlProbeResult probe = await DownloadEngine.ProbeUrlAsync(
                url,
                _settings.DefaultUserAgent,
                _txtReferer.Text,
                _txtCookies.Text,
                _txtAuthUser.Text,
                _txtAuthPass.Text,
                _settings,
                _probeCts.Token);

            if (IsDisposed) return;
            _btnProbe.Enabled = true;

            if (!probe.Success)
            {
                _lblProbeInfo.ForeColor = MhsTheme.AccentDanger;
                _lblProbeInfo.Text = "Probe failed: " + (probe.ErrorMessage ?? "Unreachable");
                return;
            }

            if (!_userEditedFileName && !string.IsNullOrWhiteSpace(probe.SuggestedFileName))
            {
                _updatingAutoFields = true;
                _txtFileName.Text = probe.SuggestedFileName;
                string cat = CategoryClassifier.ClassifyByFileName(probe.SuggestedFileName);
                SelectComboText(_cmbCategory, cat);
                _txtSaveDir.Text = CategoryClassifier.ResolveSaveDirectory(_settings, cat);
                _updatingAutoFields = false;
            }

            _lblProbeInfo.ForeColor = MhsTheme.AccentEmerald;
            if (probe.IsHls)
            {
                _lblProbeInfo.Text = "Detected HLS Media Stream (.m3u8) — Multi-segment downloader & stream merger ready.";
            }
            else
            {
                _lblProbeInfo.Text = string.Format(
                    "Size: {0}  •  Resume Capability: {1}",
                    probe.TotalBytes >= 0 ? FormattingHelpers.FormatBytes(probe.TotalBytes) : "Unknown",
                    probe.SupportsRanges ? "Yes (Dynamic Multi-Range)" : "No (Single Stream)");
            }
        }

        private static void SelectComboText(ComboBox box, string text)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (string.Equals(box.Items[i].ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
        }

        private void ConfirmDialog(bool startNow)
        {
            string url = _txtUrl.Text.Trim();
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "Please enter a valid http:// or https:// URL.", "Invalid URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string fileName = CategoryClassifier.SanitizeFileName(_txtFileName.Text.Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = CategoryClassifier.GuessFileNameFromUrl(url);
                _userEditedFileName = false;
            }

            string cat = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : "General";
            string queue = _cmbQueue.SelectedItem != null ? _cmbQueue.SelectedItem.ToString() : "Main Queue";

            ResultItem = new DownloadItem
            {
                Url = url,
                FileName = fileName,
                HasExplicitFileName = _userEditedFileName,
                CategoryName = cat,
                QueueName = queue,
                Description = _txtDescription.Text.Trim(),
                SaveDirectory = _txtSaveDir.Text.Trim(),
                MaxConnections = (int)_numConnections.Value,
                Referer = _txtReferer.Text.Trim(),
                Cookies = _txtCookies.Text.Trim(),
                AuthUser = _txtAuthUser.Text.Trim(),
                AuthPassword = _txtAuthPass.Text,
                ExpectedSha256 = _txtSha256.Text.Trim()
            };
            StartImmediately = startNow;
            HoldPausedInQueue = !startNow && !_chkStartQueueOnLater.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_probeCts != null)
            {
                try { _probeCts.Cancel(); } catch { }
            }
            base.OnFormClosing(e);
        }
    }

    public sealed class DownloadProgressDialog : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly DownloadItem _item;
        private readonly System.Windows.Forms.Timer _uiTimer;

        private Label _lblUrl;
        private Label _lblStatusVal;
        private Label _lblSizeVal;
        private Label _lblDownloadedVal;
        private Label _lblSpeedVal;
        private Label _lblEtaVal;
        private Label _lblResumeVal;
        private SpeedGraphControl _speedGraph;
        private SegmentVisualizerBar _segBar;
        private BufferedListView _lvSegments;
        private CheckBox _chkItemLimit;
        private NumericUpDown _numItemLimitKb;
        private ComboBox _cmbOnComplete;
        private Button _btnRefreshUrl;
        private Button _btnPauseResume;
        private Button _btnOpenFolder;

        public DownloadProgressDialog(DownloadManagerController controller, DownloadItem item)
        {
            _controller = controller;
            _item = item;

            Text = string.Format("{0:0.0}% — {1}", item.ProgressPercent, item.FileName);
            Size = new Size(660, 560);
            MinimumSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterScreen;
            MhsTheme.StyleForm(this);

            BuildLayout();
            RefreshView(true);

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 400;
            _uiTimer.Tick += delegate { RefreshView(false); };
            _uiTimer.Start();
        }

        private void BuildLayout()
        {
            Panel topInfo = new Panel();
            topInfo.Dock = DockStyle.Top;
            topInfo.Height = 132;
            topInfo.Padding = new Padding(14, 10, 14, 6);
            topInfo.BackColor = MhsTheme.BgSurface;

            _lblUrl = MhsTheme.CreateLabel(_item.Url, true);
            _lblUrl.AutoEllipsis = true;
            _lblUrl.AutoSize = false;
            _lblUrl.SetBounds(14, 10, 610, 18);
            _lblUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            topInfo.Controls.Add(_lblUrl);

            _lblStatusVal = AddMetricPair(topInfo, "Status:", 14, 36, 95);
            _lblSizeVal = AddMetricPair(topInfo, "File Size:", 14, 62, 95);
            _lblDownloadedVal = AddMetricPair(topInfo, "Downloaded:", 14, 88, 95);

            _lblSpeedVal = AddMetricPair(topInfo, "Transfer Rate:", 330, 36, 105);
            _lblEtaVal = AddMetricPair(topInfo, "Time Left:", 330, 62, 105);
            _lblResumeVal = AddMetricPair(topInfo, "Resume Capability:", 330, 88, 118);

            Panel midGraphs = new Panel();
            midGraphs.Dock = DockStyle.Top;
            midGraphs.Height = 148;
            midGraphs.Padding = new Padding(14, 8, 14, 8);
            midGraphs.BackColor = MhsTheme.BgDark;

            _speedGraph = new SpeedGraphControl();
            _speedGraph.Dock = DockStyle.Top;
            _speedGraph.Height = 92;

            Panel spacer = new Panel();
            spacer.Dock = DockStyle.Top;
            spacer.Height = 8;

            _segBar = new SegmentVisualizerBar();
            _segBar.Dock = DockStyle.Top;
            _segBar.Height = 30;

            midGraphs.Controls.Add(_segBar);
            midGraphs.Controls.Add(spacer);
            midGraphs.Controls.Add(_speedGraph);

            Panel bottomBar = new Panel();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 84;
            bottomBar.BackColor = MhsTheme.BgSurface;

            _chkItemLimit = new CheckBox();
            _chkItemLimit.Text = "Speed Limiter (KB/s):";
            _chkItemLimit.AutoSize = true;
            _chkItemLimit.Checked = _item.MaxSpeedBytesPerSec > 0;
            _chkItemLimit.Location = new Point(14, 12);
            _chkItemLimit.CheckedChanged += OnSpeedCapChanged;
            bottomBar.Controls.Add(_chkItemLimit);

            _numItemLimitKb = new NumericUpDown();
            _numItemLimitKb.Minimum = 16;
            _numItemLimitKb.Maximum = 1024000;
            _numItemLimitKb.Value = _item.MaxSpeedBytesPerSec > 0
                ? Math.Max(16, _item.MaxSpeedBytesPerSec / 1024)
                : 512;
            _numItemLimitKb.BackColor = MhsTheme.BgInput;
            _numItemLimitKb.ForeColor = MhsTheme.TextPrimary;
            _numItemLimitKb.SetBounds(166, 10, 80, 24);
            _numItemLimitKb.ValueChanged += OnSpeedCapChanged;
            bottomBar.Controls.Add(_numItemLimitKb);

            Label lblWhenDone = MhsTheme.CreateLabel("On Completion:", false);
            lblWhenDone.Location = new Point(262, 13);
            bottomBar.Controls.Add(lblWhenDone);

            _cmbOnComplete = new ComboBox();
            _cmbOnComplete.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbOnComplete.BackColor = MhsTheme.BgInput;
            _cmbOnComplete.ForeColor = MhsTheme.TextPrimary;
            _cmbOnComplete.Items.AddRange(new object[] { "Do Nothing", "Open File", "Open Containing Folder" });
            _cmbOnComplete.SelectedIndex = (int)_item.OnComplete;
            _cmbOnComplete.SetBounds(364, 10, 180, 24);
            _cmbOnComplete.SelectedIndexChanged += delegate
            {
                _item.OnComplete = (CompletionAction)_cmbOnComplete.SelectedIndex;
            };
            bottomBar.Controls.Add(_cmbOnComplete);

            _btnRefreshUrl = MhsTheme.CreateButton("Refresh URL", MhsTheme.BgElevated, 114, 32);
            _btnRefreshUrl.Location = new Point(188, 44);
            _btnRefreshUrl.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnRefreshUrl.Click += delegate
            {
                using (RefreshAddressDialog rd = new RefreshAddressDialog(_controller, _item))
                {
                    rd.ShowDialog(this);
                }
            };
            bottomBar.Controls.Add(_btnRefreshUrl);

            _btnPauseResume = MhsTheme.CreateButton("Pause", MhsTheme.AccentAmber, 120, 32);
            _btnPauseResume.Location = new Point(310, 44);
            _btnPauseResume.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnPauseResume.Click += OnPauseResumeClicked;
            bottomBar.Controls.Add(_btnPauseResume);

            _btnOpenFolder = MhsTheme.CreateButton("Open Folder", MhsTheme.AccentSky, 120, 32);
            _btnOpenFolder.Location = new Point(438, 44);
            _btnOpenFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnOpenFolder.Click += delegate
            {
                try
                {
                    if (File.Exists(_item.FullTargetPath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + _item.FullTargetPath + "\"");
                    }
                    else if (Directory.Exists(_item.SaveDirectory))
                    {
                        Process.Start("explorer.exe", "\"" + _item.SaveDirectory + "\"");
                    }
                }
                catch { }
            };
            bottomBar.Controls.Add(_btnOpenFolder);

            Button btnClose = MhsTheme.CreateButton("Hide", MhsTheme.BgElevated, 72, 32);
            btnClose.Location = new Point(566, 44);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.Click += delegate { Close(); };
            bottomBar.Controls.Add(btnClose);

            Panel listPanel = new Panel();
            listPanel.Dock = DockStyle.Fill;
            listPanel.Padding = new Padding(14, 0, 14, 10);

            _lvSegments = new BufferedListView();
            _lvSegments.Dock = DockStyle.Fill;
            _lvSegments.View = View.Details;
            _lvSegments.FullRowSelect = true;
            _lvSegments.GridLines = false;
            _lvSegments.BackColor = MhsTheme.BgSurface;
            _lvSegments.ForeColor = MhsTheme.TextPrimary;
            _lvSegments.BorderStyle = BorderStyle.FixedSingle;
            _lvSegments.Columns.Add("#", 48);
            _lvSegments.Columns.Add("Byte / Segment Span", 210);
            _lvSegments.Columns.Add("Downloaded", 120);
            _lvSegments.Columns.Add("Speed", 110);
            _lvSegments.Columns.Add("Worker State", 120);
            listPanel.Controls.Add(_lvSegments);

            Controls.Add(listPanel);
            Controls.Add(bottomBar);
            Controls.Add(midGraphs);
            Controls.Add(topInfo);
        }

        private Label AddMetricPair(Control parent, string caption, int x, int y, int labelW)
        {
            Label cap = MhsTheme.CreateLabel(caption, true);
            cap.Location = new Point(x, y);
            parent.Controls.Add(cap);

            Label val = MhsTheme.CreateLabel("--", false);
            val.Font = MhsTheme.FontBold;
            val.Location = new Point(x + labelW, y - 1);
            parent.Controls.Add(val);
            return val;
        }

        private void OnSpeedCapChanged(object sender, EventArgs e)
        {
            long bps = _chkItemLimit.Checked ? ((long)_numItemLimitKb.Value * 1024L) : 0L;
            _controller.UpdateItemSpeedLimit(_item.Id, bps);
        }

        private void OnPauseResumeClicked(object sender, EventArgs e)
        {
            if (_item.Status == DownloadStatus.Downloading ||
                _item.Status == DownloadStatus.Probing ||
                _item.Status == DownloadStatus.Queued)
            {
                _controller.PauseDownload(_item.Id);
            }
            else if (_item.Status == DownloadStatus.Paused || _item.Status == DownloadStatus.Error)
            {
                _controller.StartDownload(_item.Id);
            }
        }

        private void RefreshView(bool forceSample)
        {
            if (IsDisposed) return;

            if (_lblUrl.Text != _item.Url) _lblUrl.Text = _item.Url;
            Text = string.Format("{0:0.1}% — {1}", _item.ProgressPercent, _item.FileName);
            _lblStatusVal.Text = _item.Status == DownloadStatus.Error
                ? ("Error: " + (_item.ErrorMessage ?? "Failed"))
                : _item.Status.ToString();
            _lblStatusVal.ForeColor = _item.Status == DownloadStatus.Completed
                ? MhsTheme.AccentEmerald
                : (_item.Status == DownloadStatus.Error ? MhsTheme.AccentDanger : MhsTheme.TextPrimary);

            _lblSizeVal.Text = _item.TotalBytes > 0 ? FormattingHelpers.FormatBytes(_item.TotalBytes) : "Dynamic / Unknown";
            _lblDownloadedVal.Text = string.Format("{0} ({1:0.1}%)", FormattingHelpers.FormatBytes(_item.DownloadedBytes), _item.ProgressPercent);
            _lblSpeedVal.Text = FormattingHelpers.FormatSpeed(_item.CurrentSpeedBytesPerSec);
            _lblSpeedVal.ForeColor = MhsTheme.AccentCyan;

            long rem = _item.TotalBytes > _item.DownloadedBytes ? (_item.TotalBytes - _item.DownloadedBytes) : 0;
            _lblEtaVal.Text = _item.Status == DownloadStatus.Completed
                ? "Completed"
                : FormattingHelpers.FormatEta(rem, _item.CurrentSpeedBytesPerSec);

            _lblResumeVal.Text = _item.IsHlsStream
                ? "Yes (HLS Segment Checkpoint)"
                : (_item.SupportsRanges ? "Yes" : "No");
            _lblResumeVal.ForeColor = (_item.SupportsRanges || _item.IsHlsStream) ? MhsTheme.AccentEmerald : MhsTheme.AccentAmber;

            if (_item.Status == DownloadStatus.Downloading || forceSample)
            {
                _speedGraph.PushSample(_item.CurrentSpeedBytesPerSec);
            }
            else
            {
                _speedGraph.PushSample(0);
            }

            _segBar.BindItem(_item);

            if (_item.Status == DownloadStatus.Completed)
            {
                _btnPauseResume.Enabled = false;
                _btnPauseResume.Text = "Completed";
                _btnRefreshUrl.Enabled = false;
            }
            else if (_item.Status == DownloadStatus.Downloading || _item.Status == DownloadStatus.Probing)
            {
                _btnPauseResume.Enabled = true;
                _btnPauseResume.Text = "Pause";
                _btnPauseResume.BackColor = MhsTheme.AccentAmber;
                _btnRefreshUrl.Enabled = true;
            }
            else
            {
                _btnPauseResume.Enabled = true;
                _btnPauseResume.Text = "Start / Resume";
                _btnPauseResume.BackColor = MhsTheme.AccentEmerald;
                _btnRefreshUrl.Enabled = true;
            }

            UpdateSegmentListView();
        }

        private void UpdateSegmentListView()
        {
            List<SegmentInfo> copy = _item.GetSegmentsSnapshot();

            _lvSegments.BeginUpdate();
            try
            {
                while (_lvSegments.Items.Count < copy.Count)
                {
                    ListViewItem lvi = new ListViewItem("");
                    lvi.SubItems.Add("");
                    lvi.SubItems.Add("");
                    lvi.SubItems.Add("");
                    lvi.SubItems.Add("");
                    _lvSegments.Items.Add(lvi);
                }
                while (_lvSegments.Items.Count > copy.Count)
                {
                    _lvSegments.Items.RemoveAt(_lvSegments.Items.Count - 1);
                }

                for (int i = 0; i < copy.Count; i++)
                {
                    SegmentInfo s = copy[i];
                    ListViewItem row = _lvSegments.Items[i];
                    string c0 = "#" + s.Id;
                    string c1 = _item.IsHlsStream
                        ? ("HLS Chunk #" + s.Id)
                        : string.Format("{0} - {1}", FormattingHelpers.FormatBytes(s.StartByte), FormattingHelpers.FormatBytes(s.EndByte));
                    string c2 = _item.IsHlsStream
                        ? (s.State == "Completed" ? "100%" : "0%")
                        : FormattingHelpers.FormatBytes(s.DownloadedBytes);
                    string c3 = s.SpeedBytesPerSec > 1 ? FormattingHelpers.FormatSpeed(s.SpeedBytesPerSec) : "--";
                    string c4 = s.State ?? "Idle";

                    if (row.SubItems[0].Text != c0) row.SubItems[0].Text = c0;
                    if (row.SubItems[1].Text != c1) row.SubItems[1].Text = c1;
                    if (row.SubItems[2].Text != c2) row.SubItems[2].Text = c2;
                    if (row.SubItems[3].Text != c3) row.SubItems[3].Text = c3;
                    if (row.SubItems[4].Text != c4) row.SubItems[4].Text = c4;
                }
            }
            finally
            {
                _lvSegments.EndUpdate();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            _uiTimer.Dispose();
            base.OnFormClosing(e);
        }
    }

    public sealed class DownloadCompleteDialog : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly DownloadItem _item;
        private CheckBox _chkDontShowAgain;

        public DownloadCompleteDialog(DownloadManagerController controller, DownloadItem item)
        {
            _controller = controller;
            _item = item;

            Text = "Download Complete — MHS Manager";
            Size = new Size(560, 295);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            MhsTheme.StyleForm(this);

            Label hdr = MhsTheme.CreateLabel("Download Complete", false);
            hdr.Font = MhsTheme.FontTitle;
            hdr.ForeColor = MhsTheme.AccentEmerald;
            hdr.Location = new Point(18, 16);
            Controls.Add(hdr);

            Label lblFile = MhsTheme.CreateLabel(
                string.Format("{0}  ({1})", item.FileName, FormattingHelpers.FormatBytes(item.TotalBytes)),
                false);
            lblFile.Font = MhsTheme.FontBold;
            lblFile.Location = new Point(18, 46);
            Controls.Add(lblFile);

            TextBox txtSaved = MhsTheme.CreateTextBox(true);
            txtSaved.Text = item.FullTargetPath;
            txtSaved.SetBounds(18, 74, 506, 25);
            Controls.Add(txtSaved);

            Panel dragBox = new Panel();
            dragBox.SetBounds(18, 110, 506, 52);
            dragBox.BackColor = MhsTheme.BgSurface;
            dragBox.BorderStyle = BorderStyle.FixedSingle;
            dragBox.Cursor = Cursors.Hand;

            Label lblDrag = MhsTheme.CreateLabel("📄  Drag & Drop this box to copy the downloaded file into any folder or app", false);
            lblDrag.ForeColor = MhsTheme.AccentCyan;
            lblDrag.Location = new Point(14, 16);
            dragBox.Controls.Add(lblDrag);

            MouseEventHandler startDrag = delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && File.Exists(_item.FullTargetPath))
                {
                    DataObject dobj = new DataObject(DataFormats.FileDrop, new string[] { _item.FullTargetPath });
                    dragBox.DoDragDrop(dobj, DragDropEffects.Copy);
                }
            };
            dragBox.MouseDown += startDrag;
            lblDrag.MouseDown += startDrag;
            Controls.Add(dragBox);

            _chkDontShowAgain = new CheckBox();
            _chkDontShowAgain.Text = "Don't show this dialog again";
            _chkDontShowAgain.AutoSize = true;
            _chkDontShowAgain.Location = new Point(18, 174);
            _chkDontShowAgain.CheckedChanged += delegate
            {
                _controller.Settings.ShowDownloadCompleteDialog = !_chkDontShowAgain.Checked;
                _controller.SaveStateSafe();
            };
            Controls.Add(_chkDontShowAgain);

            Button btnOpen = MhsTheme.CreateButton("Open", MhsTheme.AccentEmerald, 108, 34);
            btnOpen.Location = new Point(18, 206);
            btnOpen.Click += delegate
            {
                try
                {
                    if (File.Exists(_item.FullTargetPath))
                    {
                        Process.Start(new ProcessStartInfo(_item.FullTargetPath) { UseShellExecute = true });
                    }
                }
                catch { }
                Close();
            };
            Controls.Add(btnOpen);

            Button btnOpenWith = MhsTheme.CreateButton("Open With...", MhsTheme.BgElevated, 118, 34);
            btnOpenWith.Location = new Point(136, 206);
            btnOpenWith.Click += delegate
            {
                try
                {
                    if (File.Exists(_item.FullTargetPath))
                    {
                        Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + _item.FullTargetPath);
                    }
                }
                catch { }
                Close();
            };
            Controls.Add(btnOpenWith);

            Button btnFolder = MhsTheme.CreateButton("Open Folder", MhsTheme.AccentSky, 128, 34);
            btnFolder.Location = new Point(264, 206);
            btnFolder.Click += delegate
            {
                try
                {
                    if (File.Exists(_item.FullTargetPath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + _item.FullTargetPath + "\"");
                    }
                }
                catch { }
                Close();
            };
            Controls.Add(btnFolder);

            Button btnClose = MhsTheme.CreateButton("Close", MhsTheme.BgElevated, 122, 34);
            btnClose.Location = new Point(402, 206);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }
    }

    public sealed class DropTargetForm : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly Action<string> _onUrlDropped;
        private readonly Action _onRestoreMain;
        private readonly Action _onHideRequested;
        private bool _draggingWindow;
        private Point _dragStartMouse;
        private Point _dragStartLocation;

        public DropTargetForm(
            DownloadManagerController controller,
            Action<string> onUrlDropped,
            Action onRestoreMain,
            Action onHideRequested)
        {
            _controller = controller;
            _onUrlDropped = onUrlDropped;
            _onRestoreMain = onRestoreMain;
            _onHideRequested = onHideRequested;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AllowDrop = true;
            DoubleBuffered = true;
            Opacity = 0.90;
            Size = new Size(176, 56);
            StartPosition = FormStartPosition.Manual;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 22, wa.Bottom - Height - 22);
            BackColor = MhsTheme.BgDark;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = MhsTheme.CreateDarkMenuRenderer();
            menu.Items.Add("Open MHS Manager", null, delegate { if (_onRestoreMain != null) _onRestoreMain(); });
            menu.Items.Add("Add URL...", null, delegate { if (_onUrlDropped != null) _onUrlDropped(""); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Hide Drop Target", null, delegate { if (_onHideRequested != null) _onHideRequested(); });
            ContextMenuStrip = menu;

            MouseDown += OnBoxMouseDown;
            MouseMove += OnBoxMouseMove;
            MouseUp += delegate { _draggingWindow = false; };
            DoubleClick += delegate { if (_onRestoreMain != null) _onRestoreMain(); };

            DragEnter += OnBoxDragEnter;
            DragDrop += OnBoxDragDrop;
        }

        public void RefreshOverlay()
        {
            if (!IsDisposed) Invalidate();
        }

        private void OnBoxMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _draggingWindow = true;
                _dragStartMouse = Control.MousePosition;
                _dragStartLocation = Location;
            }
        }

        private void OnBoxMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingWindow)
            {
                Point cur = Control.MousePosition;
                Location = new Point(
                    _dragStartLocation.X + (cur.X - _dragStartMouse.X),
                    _dragStartLocation.Y + (cur.Y - _dragStartMouse.Y));
            }
        }

        private static void OnBoxDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.UnicodeText) ||
                e.Data.GetDataPresent(DataFormats.Text) ||
                e.Data.GetDataPresent(DataFormats.Html))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnBoxDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string raw = (e.Data.GetData(DataFormats.UnicodeText) ?? e.Data.GetData(DataFormats.Text) ?? "").ToString().Trim();
                if (string.IsNullOrEmpty(raw) && e.Data.GetDataPresent(DataFormats.Html))
                {
                    string html = (e.Data.GetData(DataFormats.Html) ?? "").ToString();
                    Match m = Regex.Match(html, @"href\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                    if (m.Success) raw = m.Groups[1].Value;
                }
                if (!string.IsNullOrEmpty(raw) && _onUrlDropped != null)
                {
                    _onUrlDropped(raw);
                }
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            using (SolidBrush bg = new SolidBrush(MhsTheme.BgSurface))
            {
                g.FillRectangle(bg, r);
            }
            using (Pen border = new Pen(MhsTheme.AccentSky, 1.5f))
            {
                g.DrawRectangle(border, 1, 1, r.Width - 2, r.Height - 2);
            }

            TextRenderer.DrawText(
                g,
                "🎯 Drop Links Here",
                MhsTheme.FontBold,
                new Point(10, 8),
                MhsTheme.AccentCyan);

            string stats = string.Format(
                "{0} active • {1}",
                _controller.ActiveDownloadCount,
                FormattingHelpers.FormatSpeed(_controller.TotalSpeedBytesPerSec));

            TextRenderer.DrawText(
                g,
                stats,
                MhsTheme.FontSmall,
                new Point(10, 30),
                MhsTheme.TextSecondary);
        }
    }

    public sealed class RefreshAddressDialog : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly DownloadItem _item;
        private TextBox _txtNewUrl;
        private TextBox _txtReferer;
        private TextBox _txtCookies;
        private CheckBox _chkResumeNow;

        public RefreshAddressDialog(DownloadManagerController controller, DownloadItem item)
        {
            _controller = controller;
            _item = item;

            Text = "Refresh Download Address — MHS Manager";
            Size = new Size(620, 310);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            Label lblInfo = MhsTheme.CreateLabel(
                string.Format(
                    "Update expired/signed URL for '{0}' while keeping {1} ({2:0.1}%) downloaded:",
                    item.FileName,
                    FormattingHelpers.FormatBytes(item.DownloadedBytes),
                    item.ProgressPercent),
                false);
            lblInfo.Font = MhsTheme.FontBold;
            lblInfo.Location = new Point(16, 16);
            Controls.Add(lblInfo);

            Label lUrl = MhsTheme.CreateLabel("New URL:", false);
            lUrl.Location = new Point(16, 52);
            Controls.Add(lUrl);

            _txtNewUrl = MhsTheme.CreateTextBox(false);
            _txtNewUrl.Text = item.Url ?? "";
            _txtNewUrl.SetBounds(120, 49, 384, 25);
            Controls.Add(_txtNewUrl);

            Button btnPaste = MhsTheme.CreateButton("Paste", MhsTheme.BgElevated, 76, 26);
            btnPaste.Location = new Point(512, 48);
            btnPaste.Click += delegate
            {
                try
                {
                    if (Clipboard.ContainsText()) _txtNewUrl.Text = Clipboard.GetText().Trim();
                }
                catch { }
            };
            Controls.Add(btnPaste);

            Label lRef = MhsTheme.CreateLabel("Referer:", false);
            lRef.Location = new Point(16, 90);
            Controls.Add(lRef);

            _txtReferer = MhsTheme.CreateTextBox(false);
            _txtReferer.Text = item.Referer ?? "";
            _txtReferer.SetBounds(120, 87, 468, 25);
            Controls.Add(_txtReferer);

            Label lCook = MhsTheme.CreateLabel("Cookies:", false);
            lCook.Location = new Point(16, 128);
            Controls.Add(lCook);

            _txtCookies = MhsTheme.CreateTextBox(false);
            _txtCookies.Text = item.Cookies ?? "";
            _txtCookies.SetBounds(120, 125, 468, 25);
            Controls.Add(_txtCookies);

            _chkResumeNow = new CheckBox();
            _chkResumeNow.Text = "Resume download immediately from saved byte checkpoint";
            _chkResumeNow.AutoSize = true;
            _chkResumeNow.Checked = true;
            _chkResumeNow.Location = new Point(120, 164);
            Controls.Add(_chkResumeNow);

            Button btnApply = MhsTheme.CreateButton("Update & Resume", MhsTheme.AccentEmerald, 160, 34);
            btnApply.Location = new Point(328, 212);
            btnApply.Click += OnApplyClicked;
            Controls.Add(btnApply);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 90, 34);
            btnCancel.Location = new Point(498, 212);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);
        }

        private async void OnApplyClicked(object sender, EventArgs e)
        {
            string url = _txtNewUrl.Text.Trim();
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "Please enter a valid http:// or https:// URL.", "Refresh Download Address", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await _controller.RefreshDownloadAddressAsync(
                _item.Id,
                url,
                _txtReferer.Text.Trim(),
                _txtCookies.Text.Trim(),
                null,
                _chkResumeNow.Checked);

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class FilePropertiesDialog : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly DownloadItem _item;
        private TextBox _txtFileName;
        private TextBox _txtSaveDir;
        private TextBox _txtUrl;
        private ComboBox _cmbCategory;
        private ComboBox _cmbQueue;
        private NumericUpDown _numConns;
        private TextBox _txtDesc;
        private TextBox _txtReferer;
        private TextBox _txtCookies;
        private TextBox _txtAuthUser;
        private TextBox _txtAuthPass;
        private TextBox _txtExpectedSha;
        private CheckBox _chkIgnoreServerMod;

        public FilePropertiesDialog(DownloadManagerController controller, DownloadItem item)
        {
            _controller = controller;
            _item = item;

            Text = "File Properties — " + item.FileName;
            Size = new Size(640, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            int lx = 18;
            int ix = 142;
            int iw = 460;
            int y = 16;

            Controls.Add(MakeLbl("File Name:", lx, y + 4));
            _txtFileName = MhsTheme.CreateTextBox(false);
            _txtFileName.Text = item.FileName ?? "";
            _txtFileName.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtFileName);

            y += 34;
            Controls.Add(MakeLbl("Save To Folder:", lx, y + 4));
            _txtSaveDir = MhsTheme.CreateTextBox(false);
            _txtSaveDir.Text = item.SaveDirectory ?? "";
            _txtSaveDir.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtSaveDir);

            y += 34;
            Controls.Add(MakeLbl("Address (URL):", lx, y + 4));
            _txtUrl = MhsTheme.CreateTextBox(false);
            _txtUrl.Text = item.Url ?? "";
            _txtUrl.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtUrl);

            y += 34;
            Controls.Add(MakeLbl("Category:", lx, y + 4));
            _cmbCategory = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _cmbCategory.Items.AddRange(new object[] { "General", "Compressed", "Documents", "Music", "Programs", "Video" });
            SelectItem(_cmbCategory, item.CategoryName ?? "General");
            _cmbCategory.SetBounds(ix, y, 150, 25);
            Controls.Add(_cmbCategory);

            Controls.Add(MakeLbl("Queue:", ix + 165, y + 4));
            _cmbQueue = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            for (int i = 0; i < _controller.Settings.Queues.Count; i++) _cmbQueue.Items.Add(_controller.Settings.Queues[i].Name);
            SelectItem(_cmbQueue, item.QueueName ?? "Main Queue");
            _cmbQueue.SetBounds(ix + 220, y, 130, 25);
            Controls.Add(_cmbQueue);

            Controls.Add(MakeLbl("Threads:", ix + 360, y + 4));
            _numConns = new NumericUpDown { Minimum = 1, Maximum = 32, Value = Math.Max(1, Math.Min(32, item.MaxConnections)), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numConns.SetBounds(ix + 414, y + 1, 46, 25);
            Controls.Add(_numConns);

            y += 34;
            Controls.Add(MakeLbl("Description:", lx, y + 4));
            _txtDesc = MhsTheme.CreateTextBox(false);
            _txtDesc.Text = item.Description ?? "";
            _txtDesc.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtDesc);

            y += 34;
            Controls.Add(MakeLbl("Referer:", lx, y + 4));
            _txtReferer = MhsTheme.CreateTextBox(false);
            _txtReferer.Text = item.Referer ?? "";
            _txtReferer.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtReferer);

            y += 34;
            Controls.Add(MakeLbl("Cookies:", lx, y + 4));
            _txtCookies = MhsTheme.CreateTextBox(false);
            _txtCookies.Text = item.Cookies ?? "";
            _txtCookies.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtCookies);

            y += 34;
            Controls.Add(MakeLbl("Login User:", lx, y + 4));
            _txtAuthUser = MhsTheme.CreateTextBox(false);
            _txtAuthUser.Text = item.AuthUser ?? "";
            _txtAuthUser.SetBounds(ix, y, 180, 25);
            Controls.Add(_txtAuthUser);

            Controls.Add(MakeLbl("Password:", ix + 195, y + 4));
            _txtAuthPass = MhsTheme.CreateTextBox(false);
            _txtAuthPass.UseSystemPasswordChar = true;
            _txtAuthPass.Text = item.AuthPassword ?? "";
            _txtAuthPass.SetBounds(ix + 268, y, 192, 25);
            Controls.Add(_txtAuthPass);

            y += 34;
            Controls.Add(MakeLbl("Expected SHA256:", lx, y + 4));
            _txtExpectedSha = MhsTheme.CreateTextBox(false);
            _txtExpectedSha.Text = item.ExpectedSha256 ?? "";
            _txtExpectedSha.SetBounds(ix, y, iw, 25);
            Controls.Add(_txtExpectedSha);

            y += 32;
            _chkIgnoreServerMod = new CheckBox
            {
                Text = "Ignore ETag / Last-Modified server change check when resuming",
                Checked = item.IgnoreServerModifiedOnResume,
                AutoSize = true,
                Location = new Point(ix, y)
            };
            Controls.Add(_chkIgnoreServerMod);

            y += 32;
            string metaLine = string.Format(
                "Status: {0}  •  Size: {1}  •  Resume: {2}  •  Last Try: {3}",
                item.Status,
                FormattingHelpers.FormatBytes(item.TotalBytes),
                item.SupportsRanges ? "Yes" : "No",
                item.LastTryUtc.HasValue ? item.LastTryUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Never");
            Label lblMeta = MhsTheme.CreateLabel(metaLine, true);
            lblMeta.Location = new Point(lx, y);
            Controls.Add(lblMeta);

            y += 24;
            string shaLine = "Computed SHA-256: " + (!string.IsNullOrEmpty(item.ComputedSha256) ? item.ComputedSha256 : "(not finalized yet)");
            Label lblSha = MhsTheme.CreateLabel(shaLine, true);
            lblSha.Font = MhsTheme.FontMono;
            lblSha.Location = new Point(lx, y);
            Controls.Add(lblSha);

            Button btnOk = MhsTheme.CreateButton("Save Properties", MhsTheme.AccentEmerald, 145, 34);
            btnOk.Location = new Point(360, 470);
            btnOk.Click += OnSaveProperties;
            Controls.Add(btnOk);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 88, 34);
            btnCancel.Location = new Point(514, 470);
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);
        }

        private static Label MakeLbl(string text, int x, int y)
        {
            Label l = MhsTheme.CreateLabel(text, false);
            l.Location = new Point(x, y);
            return l;
        }

        private static void SelectItem(ComboBox box, string text)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (string.Equals(box.Items[i].ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private async void OnSaveProperties(object sender, EventArgs e)
        {
            string newName = CategoryClassifier.SanitizeFileName(_txtFileName.Text.Trim());
            string newDir = _txtSaveDir.Text.Trim();
            string newUrl = _txtUrl.Text.Trim();

            if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, _item.FileName, StringComparison.Ordinal))
            {
                string oldFull = _item.FullTargetPath;
                if (_item.Status == DownloadStatus.Completed && File.Exists(oldFull))
                {
                    try
                    {
                        string dest = Path.Combine(newDir, newName);
                        if (!Directory.Exists(newDir)) Directory.CreateDirectory(newDir);
                        if (!File.Exists(dest))
                        {
                            File.Move(oldFull, dest);
                        }
                    }
                    catch { }
                }
                _item.FileName = newName;
                _item.HasExplicitFileName = true;
            }

            if (!string.IsNullOrEmpty(newDir)) _item.SaveDirectory = newDir;
            _item.CategoryName = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : "General";
            _item.QueueName = _cmbQueue.SelectedItem != null ? _cmbQueue.SelectedItem.ToString() : "Main Queue";
            _item.MaxConnections = (int)_numConns.Value;
            _item.Description = _txtDesc.Text.Trim();
            _item.Referer = _txtReferer.Text.Trim();
            _item.Cookies = _txtCookies.Text.Trim();
            _item.AuthUser = _txtAuthUser.Text.Trim();
            _item.AuthPassword = _txtAuthPass.Text;
            _item.ExpectedSha256 = _txtExpectedSha.Text.Trim();
            _item.IgnoreServerModifiedOnResume = _chkIgnoreServerMod.Checked;

            if (!string.IsNullOrEmpty(newUrl) && !string.Equals(newUrl, _item.Url, StringComparison.OrdinalIgnoreCase))
            {
                await _controller.RefreshDownloadAddressAsync(_item.Id, newUrl, _item.Referer, _item.Cookies, _item.UserAgent, false);
            }

            _controller.SaveStateSafe();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class BrowserBatchLinksDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly BrowserBatchRequest _batch;
        private CheckBox _chkHideHtml;
        private CheckBox _chkHideImages;
        private TextBox _txtFilter;
        private ComboBox _cmbQueue;
        private CheckBox _chkStartQueue;
        private BufferedListView _lvLinks;
        private Label _lblSummary;

        public List<BrowserDownloadRequest> SelectedRequests { get; private set; }
        public string SelectedQueue { get; private set; }
        public bool StartQueueImmediately { get; private set; }

        public BrowserBatchLinksDialog(AppSettings settings, BrowserBatchRequest batch)
        {
            _settings = settings;
            _batch = batch ?? new BrowserBatchRequest { links = new List<BrowserDownloadRequest>() };
            SelectedRequests = new List<BrowserDownloadRequest>();

            Text = "Download All Links with MHS Manager";
            Size = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            MhsTheme.StyleForm(this);

            _chkHideHtml = new CheckBox { Text = "Hide web pages (.html/.php)", Checked = true, AutoSize = true, Location = new Point(16, 16) };
            _chkHideHtml.CheckedChanged += delegate { PopulateFiltered(); };
            Controls.Add(_chkHideHtml);

            _chkHideImages = new CheckBox { Text = "Hide small images (.png/.jpg/.svg)", Checked = true, AutoSize = true, Location = new Point(220, 16) };
            _chkHideImages.CheckedChanged += delegate { PopulateFiltered(); };
            Controls.Add(_chkHideImages);

            Label lFilter = MhsTheme.CreateLabel("Filter:", true);
            lFilter.Location = new Point(468, 18);
            Controls.Add(lFilter);

            _txtFilter = MhsTheme.CreateTextBox(false);
            _txtFilter.SetBounds(510, 14, 218, 24);
            _txtFilter.TextChanged += delegate { PopulateFiltered(); };
            Controls.Add(_txtFilter);

            _lvLinks = new BufferedListView();
            _lvLinks.SetBounds(16, 48, 712, 360);
            _lvLinks.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lvLinks.View = View.Details;
            _lvLinks.CheckBoxes = true;
            _lvLinks.FullRowSelect = true;
            _lvLinks.BackColor = MhsTheme.BgSurface;
            _lvLinks.ForeColor = MhsTheme.TextPrimary;
            _lvLinks.Columns.Add("File Name", 210);
            _lvLinks.Columns.Add("Category", 95);
            _lvLinks.Columns.Add("URL", 385);
            Controls.Add(_lvLinks);

            _lblSummary = MhsTheme.CreateLabel("", true);
            _lblSummary.Location = new Point(16, 418);
            _lblSummary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_lblSummary);

            Label lQueue = MhsTheme.CreateLabel("Queue:", false);
            lQueue.Location = new Point(16, 444);
            lQueue.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(lQueue);

            _cmbQueue = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            for (int i = 0; i < _settings.Queues.Count; i++) _cmbQueue.Items.Add(_settings.Queues[i].Name);
            if (_cmbQueue.Items.Count == 0) _cmbQueue.Items.Add("Main Queue");
            _cmbQueue.SelectedIndex = 0;
            _cmbQueue.SetBounds(72, 441, 140, 25);
            _cmbQueue.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_cmbQueue);

            _chkStartQueue = new CheckBox { Text = "Start queue processing", Checked = true, AutoSize = true, Location = new Point(225, 443) };
            _chkStartQueue.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_chkStartQueue);

            Button btnAll = MhsTheme.CreateButton("Check All", MhsTheme.BgElevated, 86, 32);
            btnAll.Location = new Point(400, 438);
            btnAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnAll.Click += delegate { SetChecks(true); };
            Controls.Add(btnAll);

            Button btnNone = MhsTheme.CreateButton("Uncheck", MhsTheme.BgElevated, 78, 32);
            btnNone.Location = new Point(492, 438);
            btnNone.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnNone.Click += delegate { SetChecks(false); };
            Controls.Add(btnNone);

            Button btnOk = MhsTheme.CreateButton("Download", MhsTheme.AccentEmerald, 96, 32);
            btnOk.Location = new Point(576, 438);
            btnOk.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnOk.Click += OnConfirm;
            Controls.Add(btnOk);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 52, 32);
            btnCancel.Location = new Point(676, 438);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            PopulateFiltered();
        }

        private void SetChecks(bool v)
        {
            for (int i = 0; i < _lvLinks.Items.Count; i++) _lvLinks.Items[i].Checked = v;
        }

        private void PopulateFiltered()
        {
            string q = (_txtFilter.Text ?? "").Trim();
            _lvLinks.BeginUpdate();
            _lvLinks.Items.Clear();

            List<BrowserDownloadRequest> src = _batch.links ?? new List<BrowserDownloadRequest>();
            for (int i = 0; i < src.Count; i++)
            {
                BrowserDownloadRequest r = src[i];
                if (r == null || string.IsNullOrWhiteSpace(r.url)) continue;

                string fn = !string.IsNullOrWhiteSpace(r.fileName)
                    ? CategoryClassifier.SanitizeFileName(r.fileName)
                    : CategoryClassifier.GuessFileNameFromUrl(r.url);
                if (HttpHelpers.IsHlsUrlOrMime(r.url, null))
                {
                    fn = HttpHelpers.EnsureNonM3u8VideoFileName(fn);
                }

                if (_chkHideHtml.Checked && SiteLinkGrabber.IsHtmlPageLink(r.url, fn)) continue;
                if (_chkHideImages.Checked && SiteLinkGrabber.IsImageLink(fn)) continue;
                if (!string.IsNullOrEmpty(q) &&
                    fn.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    r.url.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string cat = CategoryClassifier.ClassifyByFileName(fn);
                ListViewItem row = new ListViewItem(fn);
                row.Checked = true;
                row.Tag = r;
                row.SubItems.Add(cat);
                row.SubItems.Add(r.url);
                _lvLinks.Items.Add(row);
            }

            _lvLinks.EndUpdate();
            _lblSummary.Text = string.Format("Showing {0} of {1} captured page links.", _lvLinks.Items.Count, src.Count);
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            SelectedRequests.Clear();
            for (int i = 0; i < _lvLinks.Items.Count; i++)
            {
                if (_lvLinks.Items[i].Checked && _lvLinks.Items[i].Tag is BrowserDownloadRequest)
                {
                    BrowserDownloadRequest req = (BrowserDownloadRequest)_lvLinks.Items[i].Tag;
                    if (string.IsNullOrEmpty(req.referer)) req.referer = _batch.referer;
                    if (string.IsNullOrEmpty(req.cookies)) req.cookies = _batch.cookies;
                    if (string.IsNullOrEmpty(req.userAgent)) req.userAgent = _batch.userAgent;
                    SelectedRequests.Add(req);
                }
            }
            if (SelectedRequests.Count == 0) return;
            SelectedQueue = _cmbQueue.SelectedItem != null ? _cmbQueue.SelectedItem.ToString() : "Main Queue";
            StartQueueImmediately = _chkStartQueue.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class BatchDownloadDialog : Form
    {
        private readonly AppSettings _settings;
        private TextBox _txtPattern;
        private BufferedListView _lvPreview;
        private ComboBox _cmbQueue;
        private CheckBox _chkStartNow;
        private Label _lblCount;

        public List<string> SelectedUrls { get; private set; }
        public string SelectedQueue { get; private set; }
        public bool StartImmediately { get; private set; }

        public BatchDownloadDialog(AppSettings settings)
        {
            _settings = settings;
            SelectedUrls = new List<string>();
            Text = "Batch Wildcard Downloader — MHS Manager";
            Size = new Size(680, 490);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            Label lblHint = MhsTheme.CreateLabel("Wildcard URL Pattern — Supports [01-20], [1-100:2], or [a-z]:", false);
            lblHint.Location = new Point(16, 14);
            Controls.Add(lblHint);

            _txtPattern = MhsTheme.CreateTextBox(false);
            _txtPattern.Text = "https://example.com/releases/part_[01-10].zip";
            _txtPattern.SetBounds(16, 38, 632, 26);
            _txtPattern.TextChanged += delegate { UpdatePreview(); };
            Controls.Add(_txtPattern);

            _lblCount = MhsTheme.CreateLabel("0 URLs generated", true);
            _lblCount.Location = new Point(16, 70);
            Controls.Add(_lblCount);

            _lvPreview = new BufferedListView();
            _lvPreview.SetBounds(16, 94, 632, 290);
            _lvPreview.View = View.Details;
            _lvPreview.FullRowSelect = true;
            _lvPreview.CheckBoxes = true;
            _lvPreview.BackColor = MhsTheme.BgSurface;
            _lvPreview.ForeColor = MhsTheme.TextPrimary;
            _lvPreview.Columns.Add("Generated URL", 460);
            _lvPreview.Columns.Add("Target File", 150);
            Controls.Add(_lvPreview);

            Label lblQ = MhsTheme.CreateLabel("Target Queue:", false);
            lblQ.Location = new Point(16, 402);
            Controls.Add(lblQ);

            _cmbQueue = new ComboBox();
            _cmbQueue.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbQueue.BackColor = MhsTheme.BgInput;
            _cmbQueue.ForeColor = MhsTheme.TextPrimary;
            for (int i = 0; i < _settings.Queues.Count; i++) _cmbQueue.Items.Add(_settings.Queues[i].Name);
            if (_cmbQueue.Items.Count == 0) _cmbQueue.Items.Add("Main Queue");
            _cmbQueue.SelectedIndex = 0;
            _cmbQueue.SetBounds(112, 399, 140, 25);
            Controls.Add(_cmbQueue);

            _chkStartNow = new CheckBox();
            _chkStartNow.Text = "Start Queue Immediately";
            _chkStartNow.AutoSize = true;
            _chkStartNow.Checked = true;
            _chkStartNow.Location = new Point(268, 402);
            Controls.Add(_chkStartNow);

            Button btnOk = MhsTheme.CreateButton("Add Checked", MhsTheme.AccentEmerald, 130, 34);
            btnOk.Location = new Point(424, 396);
            btnOk.Click += OnConfirm;
            Controls.Add(btnOk);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 84, 34);
            btnCancel.Location = new Point(564, 396);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            List<string> urls = BatchUrlGenerator.Expand(_txtPattern.Text, 500);
            _lvPreview.BeginUpdate();
            _lvPreview.Items.Clear();
            for (int i = 0; i < urls.Count; i++)
            {
                ListViewItem item = new ListViewItem(urls[i]);
                item.Checked = true;
                item.SubItems.Add(CategoryClassifier.GuessFileNameFromUrl(urls[i]));
                _lvPreview.Items.Add(item);
            }
            _lvPreview.EndUpdate();
            _lblCount.Text = string.Format("{0} URLs generated (max 500 shown)", urls.Count);
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            SelectedUrls.Clear();
            for (int i = 0; i < _lvPreview.Items.Count; i++)
            {
                if (_lvPreview.Items[i].Checked)
                {
                    SelectedUrls.Add(_lvPreview.Items[i].Text);
                }
            }
            if (SelectedUrls.Count == 0)
            {
                MessageBox.Show(this, "No valid URLs checked.", "Batch Download", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SelectedQueue = _cmbQueue.SelectedItem != null ? _cmbQueue.SelectedItem.ToString() : "Main Queue";
            StartImmediately = _chkStartNow.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class SiteGrabberDialog : Form
    {
        private readonly AppSettings _settings;
        private TextBox _txtPageUrl;
        private Button _btnScan;
        private TextBox _txtFilter;
        private CheckBox _chkVideo;
        private CheckBox _chkCompressed;
        private CheckBox _chkPrograms;
        private CheckBox _chkMusic;
        private CheckBox _chkDocs;
        private CheckBox _chkGeneral;
        private BufferedListView _lvLinks;
        private Label _lblStatus;
        private List<GrabbedLink> _allGrabbed = new List<GrabbedLink>();
        private CancellationTokenSource _cts;

        public List<GrabbedLink> ChosenLinks { get; private set; }
        public string RefererPageUrl { get; private set; }

        public SiteGrabberDialog(AppSettings settings)
        {
            _settings = settings;
            ChosenLinks = new List<GrabbedLink>();
            Text = "Site Link Grabber & Media Scanner — MHS Manager";
            Size = new Size(760, 540);
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            Label lblU = MhsTheme.CreateLabel("Webpage URL:", false);
            lblU.Location = new Point(16, 18);
            Controls.Add(lblU);

            _txtPageUrl = MhsTheme.CreateTextBox(false);
            _txtPageUrl.SetBounds(115, 15, 515, 25);
            Controls.Add(_txtPageUrl);

            _btnScan = MhsTheme.CreateButton("Scan Page", MhsTheme.AccentSky, 96, 27);
            _btnScan.Location = new Point(638, 14);
            _btnScan.Click += OnScanClicked;
            Controls.Add(_btnScan);

            _chkVideo = MakeCatCheck("Video", 16, 50, true);
            _chkCompressed = MakeCatCheck("Compressed", 90, 50, true);
            _chkPrograms = MakeCatCheck("Programs", 195, 50, true);
            _chkMusic = MakeCatCheck("Music", 285, 50, true);
            _chkDocs = MakeCatCheck("Documents", 355, 50, true);
            _chkGeneral = MakeCatCheck("General", 455, 50, false);

            Label lblF = MhsTheme.CreateLabel("Filter:", true);
            lblF.Location = new Point(540, 52);
            Controls.Add(lblF);

            _txtFilter = MhsTheme.CreateTextBox(false);
            _txtFilter.SetBounds(580, 48, 154, 24);
            _txtFilter.TextChanged += delegate { ApplyFilters(); };
            Controls.Add(_txtFilter);

            _lvLinks = new BufferedListView();
            _lvLinks.SetBounds(16, 82, 718, 355);
            _lvLinks.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lvLinks.View = View.Details;
            _lvLinks.CheckBoxes = true;
            _lvLinks.FullRowSelect = true;
            _lvLinks.BackColor = MhsTheme.BgSurface;
            _lvLinks.ForeColor = MhsTheme.TextPrimary;
            _lvLinks.Columns.Add("File Name", 210);
            _lvLinks.Columns.Add("Category", 95);
            _lvLinks.Columns.Add("Source", 85);
            _lvLinks.Columns.Add("Resolved URL", 310);
            Controls.Add(_lvLinks);

            _lblStatus = MhsTheme.CreateLabel("Enter a webpage URL and click Scan Page.", true);
            _lblStatus.Location = new Point(16, 452);
            _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_lblStatus);

            Button btnCheckAll = MhsTheme.CreateButton("Check All", MhsTheme.BgElevated, 90, 32);
            btnCheckAll.Location = new Point(340, 446);
            btnCheckAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCheckAll.Click += delegate { SetAllChecked(true); };
            Controls.Add(btnCheckAll);

            Button btnUncheck = MhsTheme.CreateButton("Clear", MhsTheme.BgElevated, 70, 32);
            btnUncheck.Location = new Point(436, 446);
            btnUncheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnUncheck.Click += delegate { SetAllChecked(false); };
            Controls.Add(btnUncheck);

            Button btnAdd = MhsTheme.CreateButton("Queue Checked", MhsTheme.AccentEmerald, 134, 32);
            btnAdd.Location = new Point(512, 446);
            btnAdd.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnAdd.Click += OnQueueChecked;
            Controls.Add(btnAdd);

            Button btnClose = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 80, 32);
            btnClose.Location = new Point(654, 446);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnClose);
        }

        private CheckBox MakeCatCheck(string label, int x, int y, bool initial)
        {
            CheckBox cb = new CheckBox();
            cb.Text = label;
            cb.Checked = initial;
            cb.AutoSize = true;
            cb.Location = new Point(x, y);
            cb.CheckedChanged += delegate { ApplyFilters(); };
            Controls.Add(cb);
            return cb;
        }

        private void SetAllChecked(bool state)
        {
            for (int i = 0; i < _lvLinks.Items.Count; i++)
            {
                _lvLinks.Items[i].Checked = state;
            }
        }

        private async void OnScanClicked(object sender, EventArgs e)
        {
            string url = _txtPageUrl.Text.Trim();
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
            {
                MessageBox.Show(this, "Enter a valid http:// or https:// webpage URL.", "Site Grabber", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_cts != null) { try { _cts.Cancel(); } catch { } }
            _cts = new CancellationTokenSource();
            _btnScan.Enabled = false;
            _lblStatus.Text = "Scanning HTML & embedded media links...";

            try
            {
                _allGrabbed = await SiteLinkGrabber.GrabLinksAsync(url, _settings.DefaultUserAgent, null, _settings, _cts.Token);
                RefererPageUrl = url;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Scan error: " + ex.Message;
            }
            finally
            {
                if (!IsDisposed) _btnScan.Enabled = true;
            }
        }

        private void ApplyFilters()
        {
            string q = _txtFilter.Text.Trim();
            _lvLinks.BeginUpdate();
            _lvLinks.Items.Clear();

            for (int i = 0; i < _allGrabbed.Count; i++)
            {
                GrabbedLink g = _allGrabbed[i];
                if (!IsCategoryEnabled(g.Category)) continue;
                if (!string.IsNullOrEmpty(q) &&
                    g.FileName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    g.Url.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                ListViewItem row = new ListViewItem(g.FileName);
                row.Checked = true;
                row.Tag = g;
                row.SubItems.Add(g.Category);
                row.SubItems.Add(g.SourceTag);
                row.SubItems.Add(g.Url);
                _lvLinks.Items.Add(row);
            }
            _lvLinks.EndUpdate();
            _lblStatus.Text = string.Format("Showing {0} of {1} discovered links.", _lvLinks.Items.Count, _allGrabbed.Count);
        }

        private bool IsCategoryEnabled(string cat)
        {
            if (string.Equals(cat, "Video", StringComparison.OrdinalIgnoreCase)) return _chkVideo.Checked;
            if (string.Equals(cat, "Compressed", StringComparison.OrdinalIgnoreCase)) return _chkCompressed.Checked;
            if (string.Equals(cat, "Programs", StringComparison.OrdinalIgnoreCase)) return _chkPrograms.Checked;
            if (string.Equals(cat, "Music", StringComparison.OrdinalIgnoreCase)) return _chkMusic.Checked;
            if (string.Equals(cat, "Documents", StringComparison.OrdinalIgnoreCase)) return _chkDocs.Checked;
            return _chkGeneral.Checked;
        }

        private void OnQueueChecked(object sender, EventArgs e)
        {
            ChosenLinks.Clear();
            for (int i = 0; i < _lvLinks.Items.Count; i++)
            {
                if (_lvLinks.Items[i].Checked && _lvLinks.Items[i].Tag is GrabbedLink)
                {
                    ChosenLinks.Add((GrabbedLink)_lvLinks.Items[i].Tag);
                }
            }
            if (ChosenLinks.Count == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class SettingsAndSchedulerDialog : Form
    {
        private readonly DownloadManagerController _controller;
        private readonly BrowserBridgeServer _bridge;

        // Tab 1: General
        private TextBox _txtBaseDir;
        private CheckBox _chkSubfolders;
        private NumericUpDown _numDefaultConns;
        private CheckBox _chkClipboard;
        private CheckBox _chkStartDialog;
        private CheckBox _chkProgressPopup;
        private CheckBox _chkCompleteDialog;
        private CheckBox _chkServerLastMod;
        private CheckBox _chkTrayOnClose;
        private CheckBox _chkGlobalLimit;
        private NumericUpDown _numGlobalKb;
        private TextBox _txtExtensions;

        // Tab 2: Queues & Scheduler
        private ComboBox _cmbQueueSelect;
        private NumericUpDown _numQueueConcurrent;
        private NumericUpDown _numQueueRetries;
        private NumericUpDown _numQueueRetryDelay;
        private CheckBox _chkStartAt;
        private NumericUpDown _numStartHour;
        private NumericUpDown _numStartMin;
        private CheckBox _chkStopAt;
        private NumericUpDown _numStopHour;
        private NumericUpDown _numStopMin;
        private CheckBox[] _chkDays;
        private ComboBox _cmbPostAction;
        private Label _lblQueueState;
        private bool _loadingQueueFields;

        // Tab 3: Site Logins
        private BufferedListView _lvSiteLogins;
        private TextBox _txtLoginHost;
        private TextBox _txtLoginUser;
        private TextBox _txtLoginPass;

        // Tab 4: Proxy & Network
        private ComboBox _cmbProxyMode;
        private TextBox _txtProxyHost;
        private NumericUpDown _numProxyPort;
        private TextBox _txtProxyUser;
        private TextBox _txtProxyPass;
        private TextBox _txtDefaultUa;

        // Tab 5: Browser Integration
        private CheckBox _chkEnableBridge;
        private NumericUpDown _numBridgePort;
        private TextBox _txtBridgeToken;
        private TextBox _txtExcludedHosts;
        private TextBox _txtExtId;
        private Label _lblNativeHostStatus;

        public SettingsAndSchedulerDialog(DownloadManagerController controller, BrowserBridgeServer bridge, int initialTab)
        {
            _controller = controller;
            _bridge = bridge;

            Text = "Options, Scheduler, Site Logins & Browser Integration — MHS Manager";
            Size = new Size(690, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            TabControl tabs = new TabControl();
            tabs.SetBounds(14, 14, 646, 430);

            TabPage tabGen = MakeTab("General & Files");
            BuildGeneralTab(tabGen);

            TabPage tabSched = MakeTab("Queues & Scheduler");
            BuildSchedulerTab(tabSched);

            TabPage tabLogins = MakeTab("Site Logins");
            BuildSiteLoginsTab(tabLogins);

            TabPage tabProxy = MakeTab("Proxy / Network");
            BuildProxyTab(tabProxy);

            TabPage tabExt = MakeTab("Browser Integration");
            BuildExtensionTab(tabExt);

            tabs.TabPages.Add(tabGen);
            tabs.TabPages.Add(tabSched);
            tabs.TabPages.Add(tabLogins);
            tabs.TabPages.Add(tabProxy);
            tabs.TabPages.Add(tabExt);
            if (initialTab >= 0 && initialTab < tabs.TabCount)
            {
                tabs.SelectedIndex = initialTab;
            }
            Controls.Add(tabs);

            Button btnSave = MhsTheme.CreateButton("Save & Apply", MhsTheme.AccentEmerald, 138, 34);
            btnSave.Location = new Point(430, 454);
            btnSave.Click += OnSaveClicked;
            Controls.Add(btnSave);

            Button btnCancel = MhsTheme.CreateButton("Close", MhsTheme.BgElevated, 84, 34);
            btnCancel.Location = new Point(576, 454);
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);
        }

        private static TabPage MakeTab(string title)
        {
            TabPage p = new TabPage(title);
            p.BackColor = MhsTheme.BgSurface;
            p.ForeColor = MhsTheme.TextPrimary;
            return p;
        }

        private void BuildGeneralTab(TabPage page)
        {
            AppSettings s = _controller.Settings;
            int y = 16;

            Label l1 = MhsTheme.CreateLabel("Default Download Folder:", false);
            l1.Location = new Point(16, y + 4);
            page.Controls.Add(l1);

            _txtBaseDir = MhsTheme.CreateTextBox(false);
            _txtBaseDir.Text = s.BaseDownloadDirectory;
            _txtBaseDir.SetBounds(185, y, 345, 25);
            page.Controls.Add(_txtBaseDir);

            Button btnBrowse = MhsTheme.CreateButton("Browse", MhsTheme.BgElevated, 82, 26);
            btnBrowse.Location = new Point(538, y - 1);
            btnBrowse.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtBaseDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK) _txtBaseDir.Text = fbd.SelectedPath;
                }
            };
            page.Controls.Add(btnBrowse);

            y += 32;
            _chkSubfolders = new CheckBox { Text = "Automatically organize into Category subfolders (Video, Compressed, Programs...)", Checked = s.UseCategorySubfolders, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkSubfolders);

            y += 28;
            Label l2 = MhsTheme.CreateLabel("Default Connections per File (1–32):", false);
            l2.Location = new Point(16, y + 3);
            page.Controls.Add(l2);

            _numDefaultConns = new NumericUpDown { Minimum = 1, Maximum = 32, Value = Math.Max(1, Math.Min(32, s.DefaultConnectionsPerDownload)), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numDefaultConns.SetBounds(250, y, 64, 25);
            page.Controls.Add(_numDefaultConns);

            y += 30;
            _chkClipboard = new CheckBox { Text = "Monitor Windows Clipboard for downloadable URLs", Checked = s.EnableClipboardMonitor, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkClipboard);

            y += 26;
            _chkStartDialog = new CheckBox { Text = "Show 'Download File Info' dialog when browser captures a download", Checked = s.ShowStartDownloadDialog, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkStartDialog);

            y += 26;
            _chkProgressPopup = new CheckBox { Text = "Show live per-download floating progress window when starting", Checked = s.ShowProgressDialogOnStart, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkProgressPopup);

            y += 26;
            _chkCompleteDialog = new CheckBox { Text = "Show 'Download Complete' dialog with Drag & Drop file box", Checked = s.ShowDownloadCompleteDialog, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkCompleteDialog);

            y += 26;
            _chkServerLastMod = new CheckBox { Text = "Set completed file timestamp to server Last-Modified header", Checked = s.SetServerLastModifiedTime, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkServerLastMod);

            y += 26;
            _chkTrayOnClose = new CheckBox { Text = "Minimize MHS Manager to System Tray when closing window", Checked = s.MinimizeToTrayOnClose, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkTrayOnClose);

            y += 30;
            _chkGlobalLimit = new CheckBox { Text = "Enable Global Speed Limiter (KB/s):", Checked = s.EnableGlobalSpeedLimit, AutoSize = true, Location = new Point(16, y + 2) };
            page.Controls.Add(_chkGlobalLimit);

            _numGlobalKb = new NumericUpDown { Minimum = 32, Maximum = 2048000, Value = Math.Max(32, s.GlobalSpeedLimitBytesPerSec / 1024), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numGlobalKb.SetBounds(275, y, 95, 25);
            page.Controls.Add(_numGlobalKb);

            y += 34;
            Label lExt = MhsTheme.CreateLabel("Auto-captured File Extensions (pipe-separated):", true);
            lExt.Location = new Point(16, y);
            page.Controls.Add(lExt);

            y += 20;
            _txtExtensions = MhsTheme.CreateTextBox(false);
            _txtExtensions.Text = s.MonitoredExtensions;
            _txtExtensions.SetBounds(16, y, 604, 25);
            page.Controls.Add(_txtExtensions);
        }

        private void BuildSchedulerTab(TabPage page)
        {
            int y = 16;
            Label lq = MhsTheme.CreateLabel("Queue:", false);
            lq.Location = new Point(18, y + 4);
            page.Controls.Add(lq);

            _cmbQueueSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            RefreshQueueCombo();
            _cmbQueueSelect.SetBounds(75, y, 185, 25);
            _cmbQueueSelect.SelectedIndexChanged += delegate { LoadSelectedQueue(); };
            page.Controls.Add(_cmbQueueSelect);

            Button btnNewQ = MhsTheme.CreateButton("+ New Queue", MhsTheme.BgElevated, 108, 26);
            btnNewQ.Location = new Point(268, y - 1);
            btnNewQ.Click += OnCreateNewQueue;
            page.Controls.Add(btnNewQ);

            _lblQueueState = MhsTheme.CreateLabel("", false);
            _lblQueueState.Font = MhsTheme.FontBold;
            _lblQueueState.Location = new Point(392, y + 4);
            page.Controls.Add(_lblQueueState);

            y += 38;
            Label lc = MhsTheme.CreateLabel("Files in queue at once:", false);
            lc.Location = new Point(18, y + 3);
            page.Controls.Add(lc);

            _numQueueConcurrent = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 4, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numQueueConcurrent.SetBounds(175, y, 54, 25);
            _numQueueConcurrent.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numQueueConcurrent);

            Label lr = MhsTheme.CreateLabel("Auto-Retries on fail:", false);
            lr.Location = new Point(250, y + 3);
            page.Controls.Add(lr);

            _numQueueRetries = new NumericUpDown { Minimum = 0, Maximum = 99, Value = 5, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numQueueRetries.SetBounds(388, y, 52, 25);
            _numQueueRetries.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numQueueRetries);

            Label ld = MhsTheme.CreateLabel("Delay (s):", false);
            ld.Location = new Point(454, y + 3);
            page.Controls.Add(ld);

            _numQueueRetryDelay = new NumericUpDown { Minimum = 5, Maximum = 3600, Value = 30, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numQueueRetryDelay.SetBounds(524, y, 60, 25);
            _numQueueRetryDelay.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numQueueRetryDelay);

            y += 38;
            _chkStartAt = new CheckBox { Text = "Scheduled Start Time (HH : MM):", AutoSize = true, Location = new Point(18, y + 2) };
            _chkStartAt.CheckedChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_chkStartAt);

            _numStartHour = new NumericUpDown { Minimum = 0, Maximum = 23, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStartHour.SetBounds(280, y, 52, 25);
            _numStartHour.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStartHour);

            _numStartMin = new NumericUpDown { Minimum = 0, Maximum = 59, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStartMin.SetBounds(342, y, 52, 25);
            _numStartMin.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStartMin);

            y += 36;
            _chkStopAt = new CheckBox { Text = "Scheduled Stop Time (HH : MM):", AutoSize = true, Location = new Point(18, y + 2) };
            _chkStopAt.CheckedChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_chkStopAt);

            _numStopHour = new NumericUpDown { Minimum = 0, Maximum = 23, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStopHour.SetBounds(280, y, 52, 25);
            _numStopHour.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStopHour);

            _numStopMin = new NumericUpDown { Minimum = 0, Maximum = 59, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStopMin.SetBounds(342, y, 52, 25);
            _numStopMin.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStopMin);

            y += 38;
            Label lDays = MhsTheme.CreateLabel("Active Days:", false);
            lDays.Location = new Point(18, y + 2);
            page.Controls.Add(lDays);

            string[] dayNames = new string[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            _chkDays = new CheckBox[7];
            for (int d = 0; d < 7; d++)
            {
                CheckBox cb = new CheckBox { Text = dayNames[d], Checked = true, AutoSize = true, Location = new Point(112 + (d * 68), y) };
                cb.CheckedChanged += delegate { SaveCurrentQueueFields(); };
                _chkDays[d] = cb;
                page.Controls.Add(cb);
            }

            y += 38;
            Label lPost = MhsTheme.CreateLabel("When Queue Finishes:", false);
            lPost.Location = new Point(18, y + 4);
            page.Controls.Add(lPost);

            _cmbPostAction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _cmbPostAction.Items.AddRange(new object[]
            {
                "Do Nothing",
                "Exit MHS Manager",
                "Put Computer to Sleep",
                "Hibernate Computer",
                "Shut Down Computer"
            });
            _cmbPostAction.SelectedIndex = 0;
            _cmbPostAction.SetBounds(175, y, 220, 25);
            _cmbPostAction.SelectedIndexChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_cmbPostAction);

            y += 50;
            Button btnStartQ = MhsTheme.CreateButton("▶ Start Queue Now", MhsTheme.AccentEmerald, 170, 34);
            btnStartQ.Location = new Point(18, y);
            btnStartQ.Click += delegate
            {
                SaveCurrentQueueFields();
                QueueDefinition q = GetCurrentQueue();
                if (q != null)
                {
                    _controller.StartQueue(q.Name);
                    LoadSelectedQueue();
                }
            };
            page.Controls.Add(btnStartQ);

            Button btnStopQ = MhsTheme.CreateButton("⏹ Stop Queue Now", MhsTheme.AccentAmber, 170, 34);
            btnStopQ.Location = new Point(200, y);
            btnStopQ.Click += delegate
            {
                SaveCurrentQueueFields();
                QueueDefinition q = GetCurrentQueue();
                if (q != null)
                {
                    _controller.StopQueue(q.Name);
                    LoadSelectedQueue();
                }
            };
            page.Controls.Add(btnStopQ);

            if (_cmbQueueSelect.Items.Count > 0) _cmbQueueSelect.SelectedIndex = 0;
        }

        private void RefreshQueueCombo()
        {
            _cmbQueueSelect.Items.Clear();
            for (int i = 0; i < _controller.Settings.Queues.Count; i++)
            {
                _cmbQueueSelect.Items.Add(_controller.Settings.Queues[i].Name);
            }
        }

        private void OnCreateNewQueue(object sender, EventArgs e)
        {
            int idx = _controller.Settings.Queues.Count + 1;
            string qName = "Custom Queue " + idx;
            _controller.Settings.Queues.Add(new QueueDefinition { Name = qName, MaxConcurrentDownloads = 2, IsRunning = false });
            RefreshQueueCombo();
            _cmbQueueSelect.SelectedIndex = _cmbQueueSelect.Items.Count - 1;
        }

        private QueueDefinition GetCurrentQueue()
        {
            if (_cmbQueueSelect.SelectedItem == null) return null;
            string name = _cmbQueueSelect.SelectedItem.ToString();
            for (int i = 0; i < _controller.Settings.Queues.Count; i++)
            {
                if (string.Equals(_controller.Settings.Queues[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return _controller.Settings.Queues[i];
                }
            }
            return null;
        }

        private void LoadSelectedQueue()
        {
            QueueDefinition q = GetCurrentQueue();
            if (q == null) return;
            _loadingQueueFields = true;
            _numQueueConcurrent.Value = Math.Max(1, Math.Min(16, q.MaxConcurrentDownloads));
            _numQueueRetries.Value = Math.Max(0, Math.Min(99, q.MaxRetriesPerItem));
            _numQueueRetryDelay.Value = Math.Max(5, Math.Min(3600, q.RetryDelaySeconds > 0 ? q.RetryDelaySeconds : 30));
            _chkStartAt.Checked = q.EnableScheduleStart;
            _numStartHour.Value = Math.Max(0, Math.Min(23, q.StartHour));
            _numStartMin.Value = Math.Max(0, Math.Min(59, q.StartMinute));
            _chkStopAt.Checked = q.EnableScheduleStop;
            _numStopHour.Value = Math.Max(0, Math.Min(23, q.StopHour));
            _numStopMin.Value = Math.Max(0, Math.Min(59, q.StopMinute));

            int mask = q.DaysOfWeekMask > 0 ? q.DaysOfWeekMask : 127;
            for (int d = 0; d < 7; d++)
            {
                _chkDays[d].Checked = (mask & (1 << d)) != 0;
            }
            _cmbPostAction.SelectedIndex = Math.Max(0, Math.Min(4, (int)q.PostCompletionAction));

            _lblQueueState.Text = q.IsRunning ? "Status: RUNNING" : "Status: STOPPED";
            _lblQueueState.ForeColor = q.IsRunning ? MhsTheme.AccentEmerald : MhsTheme.TextSecondary;
            _loadingQueueFields = false;
        }

        private void SaveCurrentQueueFields()
        {
            if (_loadingQueueFields) return;
            QueueDefinition q = GetCurrentQueue();
            if (q == null) return;
            q.MaxConcurrentDownloads = (int)_numQueueConcurrent.Value;
            q.MaxRetriesPerItem = (int)_numQueueRetries.Value;
            q.RetryDelaySeconds = (int)_numQueueRetryDelay.Value;
            q.EnableScheduleStart = _chkStartAt.Checked;
            q.StartHour = (int)_numStartHour.Value;
            q.StartMinute = (int)_numStartMin.Value;
            q.EnableScheduleStop = _chkStopAt.Checked;
            q.StopHour = (int)_numStopHour.Value;
            q.StopMinute = (int)_numStopMin.Value;

            int mask = 0;
            for (int d = 0; d < 7; d++)
            {
                if (_chkDays[d].Checked) mask |= (1 << d);
            }
            q.DaysOfWeekMask = mask > 0 ? mask : 127;
            q.PostCompletionAction = (QueuePostAction)_cmbPostAction.SelectedIndex;
        }

        private void BuildSiteLoginsTab(TabPage page)
        {
            Label hint = MhsTheme.CreateLabel("Saved HTTP Basic credentials auto-applied by domain or URL prefix (just like IDM Site Logins):", true);
            hint.Location = new Point(16, 14);
            page.Controls.Add(hint);

            _lvSiteLogins = new BufferedListView();
            _lvSiteLogins.SetBounds(16, 40, 604, 240);
            _lvSiteLogins.View = View.Details;
            _lvSiteLogins.FullRowSelect = true;
            _lvSiteLogins.BackColor = MhsTheme.BgDark;
            _lvSiteLogins.ForeColor = MhsTheme.TextPrimary;
            _lvSiteLogins.Columns.Add("Server / URL Prefix", 310);
            _lvSiteLogins.Columns.Add("User Name", 270);
            page.Controls.Add(_lvSiteLogins);

            RefreshSiteLoginsList();

            Label lHost = MhsTheme.CreateLabel("Host / URL:", false);
            lHost.Location = new Point(16, 294);
            page.Controls.Add(lHost);
            _txtLoginHost = MhsTheme.CreateTextBox(false);
            _txtLoginHost.SetBounds(96, 291, 210, 25);
            page.Controls.Add(_txtLoginHost);

            Label lUser = MhsTheme.CreateLabel("User:", false);
            lUser.Location = new Point(316, 294);
            page.Controls.Add(lUser);
            _txtLoginUser = MhsTheme.CreateTextBox(false);
            _txtLoginUser.SetBounds(358, 291, 120, 25);
            page.Controls.Add(_txtLoginUser);

            Label lPass = MhsTheme.CreateLabel("Pass:", false);
            lPass.Location = new Point(486, 294);
            page.Controls.Add(lPass);
            _txtLoginPass = MhsTheme.CreateTextBox(false);
            _txtLoginPass.UseSystemPasswordChar = true;
            _txtLoginPass.SetBounds(524, 291, 96, 25);
            page.Controls.Add(_txtLoginPass);

            Button btnAdd = MhsTheme.CreateButton("+ Add / Update Login", MhsTheme.AccentSky, 175, 32);
            btnAdd.Location = new Point(16, 330);
            btnAdd.Click += delegate
            {
                string h = _txtLoginHost.Text.Trim();
                string u = _txtLoginUser.Text.Trim();
                if (string.IsNullOrEmpty(h) || string.IsNullOrEmpty(u)) return;
                _controller.Settings.SiteLogins.Add(new SiteLoginEntry { HostPrefix = h, UserName = u, Password = _txtLoginPass.Text });
                _txtLoginHost.Text = "";
                _txtLoginUser.Text = "";
                _txtLoginPass.Text = "";
                RefreshSiteLoginsList();
            };
            page.Controls.Add(btnAdd);

            Button btnDel = MhsTheme.CreateButton("Remove Selected", MhsTheme.BgElevated, 145, 32);
            btnDel.Location = new Point(202, 330);
            btnDel.Click += delegate
            {
                if (_lvSiteLogins.SelectedIndices.Count > 0)
                {
                    int idx = _lvSiteLogins.SelectedIndices[0];
                    if (idx >= 0 && idx < _controller.Settings.SiteLogins.Count)
                    {
                        _controller.Settings.SiteLogins.RemoveAt(idx);
                        RefreshSiteLoginsList();
                    }
                }
            };
            page.Controls.Add(btnDel);
        }

        private void RefreshSiteLoginsList()
        {
            _lvSiteLogins.Items.Clear();
            if (_controller.Settings.SiteLogins == null) return;
            for (int i = 0; i < _controller.Settings.SiteLogins.Count; i++)
            {
                SiteLoginEntry e = _controller.Settings.SiteLogins[i];
                ListViewItem row = new ListViewItem(e.HostPrefix ?? "");
                row.SubItems.Add(e.UserName ?? "");
                _lvSiteLogins.Items.Add(row);
            }
        }

        private void BuildProxyTab(TabPage page)
        {
            AppSettings s = _controller.Settings;
            int y = 20;

            Label lMode = MhsTheme.CreateLabel("Proxy Configuration:", false);
            lMode.Location = new Point(18, y + 4);
            page.Controls.Add(lMode);

            _cmbProxyMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _cmbProxyMode.Items.AddRange(new object[]
            {
                "Use Windows System Proxy Settings",
                "No Proxy (Direct Connection)",
                "Manual HTTP Proxy Server"
            });
            _cmbProxyMode.SelectedIndex = Math.Max(0, Math.Min(2, (int)s.ProxyType));
            _cmbProxyMode.SetBounds(180, y, 280, 25);
            page.Controls.Add(_cmbProxyMode);

            y += 42;
            Label lHost = MhsTheme.CreateLabel("Proxy Address:", false);
            lHost.Location = new Point(18, y + 4);
            page.Controls.Add(lHost);
            _txtProxyHost = MhsTheme.CreateTextBox(false);
            _txtProxyHost.Text = s.ProxyAddress ?? "";
            _txtProxyHost.SetBounds(180, y, 270, 25);
            page.Controls.Add(_txtProxyHost);

            Label lPort = MhsTheme.CreateLabel("Port:", false);
            lPort.Location = new Point(465, y + 4);
            page.Controls.Add(lPort);
            _numProxyPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Max(1, Math.Min(65535, s.ProxyPort > 0 ? s.ProxyPort : 8080)), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numProxyPort.SetBounds(508, y, 76, 25);
            page.Controls.Add(_numProxyPort);

            y += 38;
            Label lUser = MhsTheme.CreateLabel("Proxy Username:", false);
            lUser.Location = new Point(18, y + 4);
            page.Controls.Add(lUser);
            _txtProxyUser = MhsTheme.CreateTextBox(false);
            _txtProxyUser.Text = s.ProxyUser ?? "";
            _txtProxyUser.SetBounds(180, y, 180, 25);
            page.Controls.Add(_txtProxyUser);

            Label lPass = MhsTheme.CreateLabel("Password:", false);
            lPass.Location = new Point(374, y + 4);
            page.Controls.Add(lPass);
            _txtProxyPass = MhsTheme.CreateTextBox(false);
            _txtProxyPass.UseSystemPasswordChar = true;
            _txtProxyPass.Text = s.ProxyPassword ?? "";
            _txtProxyPass.SetBounds(446, y, 138, 25);
            page.Controls.Add(_txtProxyPass);

            y += 48;
            Label lUa = MhsTheme.CreateLabel("Default HTTP User-Agent String:", false);
            lUa.Location = new Point(18, y);
            page.Controls.Add(lUa);

            y += 22;
            _txtDefaultUa = MhsTheme.CreateTextBox(false);
            _txtDefaultUa.Text = s.DefaultUserAgent ?? "";
            _txtDefaultUa.SetBounds(18, y, 596, 25);
            page.Controls.Add(_txtDefaultUa);
        }

        private void BuildExtensionTab(TabPage page)
        {
            AppSettings s = _controller.Settings;
            int y = 14;

            _chkEnableBridge = new CheckBox
            {
                Text = "Enable Authenticated Localhost RPC Bridge on Port:",
                Checked = s.EnableBrowserBridge,
                AutoSize = true,
                Location = new Point(16, y + 3)
            };
            page.Controls.Add(_chkEnableBridge);

            _numBridgePort = new NumericUpDown
            {
                Minimum = 1024,
                Maximum = 65535,
                Value = Math.Max(1024, Math.Min(65535, s.BrowserBridgePort > 0 ? s.BrowserBridgePort : 16888)),
                BackColor = MhsTheme.BgInput,
                ForeColor = MhsTheme.TextPrimary
            };
            _numBridgePort.SetBounds(358, y, 76, 25);
            page.Controls.Add(_numBridgePort);

            string liveState = (_bridge != null && _bridge.IsRunning) ? ("ONLINE :" + _bridge.Port) : "OFFLINE";
            Label lblLive = MhsTheme.CreateLabel(liveState, false);
            lblLive.Font = MhsTheme.FontBold;
            lblLive.ForeColor = (_bridge != null && _bridge.IsRunning) ? MhsTheme.AccentEmerald : MhsTheme.AccentDanger;
            lblLive.Location = new Point(448, y + 4);
            page.Controls.Add(lblLive);

            y += 34;
            Label lTok = MhsTheme.CreateLabel("Extension Pairing Token (X-MHS-Token):", false);
            lTok.Location = new Point(16, y + 4);
            page.Controls.Add(lTok);

            _txtBridgeToken = MhsTheme.CreateTextBox(true);
            _txtBridgeToken.Font = MhsTheme.FontMono;
            _txtBridgeToken.Text = s.BridgeAuthToken ?? "";
            _txtBridgeToken.SetBounds(255, y, 260, 25);
            page.Controls.Add(_txtBridgeToken);

            Button btnCopyTok = MhsTheme.CreateButton("Copy Token", MhsTheme.AccentSky, 98, 26);
            btnCopyTok.Location = new Point(522, y - 1);
            btnCopyTok.Click += delegate
            {
                try { Clipboard.SetText(_txtBridgeToken.Text); } catch { }
            };
            page.Controls.Add(btnCopyTok);

            y += 34;
            Label lExcl = MhsTheme.CreateLabel("Don't capture downloads from hosts (wildcards e.g. *.internal.lan|localhost):", true);
            lExcl.Location = new Point(16, y);
            page.Controls.Add(lExcl);

            y += 20;
            _txtExcludedHosts = MhsTheme.CreateTextBox(false);
            _txtExcludedHosts.Text = s.ExcludedHosts ?? "";
            _txtExcludedHosts.SetBounds(16, y, 604, 25);
            page.Controls.Add(_txtExcludedHosts);

            y += 36;
            Label lNatHdr = MhsTheme.CreateLabel("Chromium Native Messaging Host (com.mhsmanager.host — auto-syncs token & wakes MHS):", false);
            lNatHdr.Font = MhsTheme.FontBold;
            lNatHdr.Location = new Point(16, y);
            page.Controls.Add(lNatHdr);

            y += 24;
            Label lExtId = MhsTheme.CreateLabel("Unpacked Extension ID:", false);
            lExtId.Location = new Point(16, y + 4);
            page.Controls.Add(lExtId);

            _txtExtId = MhsTheme.CreateTextBox(false);
            _txtExtId.SetBounds(165, y, 255, 25);
            page.Controls.Add(_txtExtId);

            Button btnRegNative = MhsTheme.CreateButton("Register Native Host", MhsTheme.AccentEmerald, 192, 27);
            btnRegNative.Location = new Point(428, y - 1);
            btnRegNative.Click += OnRegisterNativeHostClicked;
            page.Controls.Add(btnRegNative);

            y += 32;
            _lblNativeHostStatus = MhsTheme.CreateLabel(
                NativeMessagingRegistrar.IsRegistered()
                    ? ("Native Host Registered: " + NativeMessagingRegistrar.GetManifestPath())
                    : "Not registered yet (Extension can also pair manually by pasting Pairing Token in popup).",
                true);
            _lblNativeHostStatus.Location = new Point(16, y);
            page.Controls.Add(_lblNativeHostStatus);

            y += 28;
            string extDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "browser-extension"));
            TextBox guide = MhsTheme.CreateTextBox(true);
            guide.Multiline = true;
            guide.SetBounds(16, y, 604, 96);
            guide.Text =
                "1. Open chrome://extensions or edge://extensions -> enable Developer mode -> Load unpacked:\r\n" +
                "   " + extDir + "\r\n" +
                "2. Copy the 32-character Extension ID above and click 'Register Native Host' (or paste Copy Token into the extension popup).\r\n" +
                "3. Shortcuts: Hold Alt while clicking a link to bypass MHS Manager; right-click for Download Link / Selected / All.";
            page.Controls.Add(guide);

            y += 104;
            Button btnOpenExt = MhsTheme.CreateButton("Open Extension Folder", MhsTheme.BgElevated, 185, 30);
            btnOpenExt.Location = new Point(16, y);
            btnOpenExt.Click += delegate
            {
                try
                {
                    if (Directory.Exists(extDir)) Process.Start("explorer.exe", "\"" + extDir + "\"");
                }
                catch { }
            };
            page.Controls.Add(btnOpenExt);
        }

        private void OnRegisterNativeHostClicked(object sender, EventArgs e)
        {
            string extId = (_txtExtId.Text ?? "").Trim();
            if (string.IsNullOrEmpty(extId))
            {
                MessageBox.Show(
                    this,
                    "Paste your 32-character Extension ID from chrome://extensions or edge://extensions first.",
                    "Register Native Host",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string manifestPath = NativeMessagingRegistrar.GetManifestPath();
            string plan =
                "MHS Manager will write its isolated Native Messaging configuration (existing IDM keys are untouched):\r\n\r\n" +
                "• Manifest File:\r\n  " + manifestPath + "\r\n\r\n" +
                "• Current-User Registry Keys:\r\n" +
                "  HKCU\\Software\\Google\\Chrome\\NativeMessagingHosts\\com.mhsmanager.host\r\n" +
                "  HKCU\\Software\\Microsoft\\Edge\\NativeMessagingHosts\\com.mhsmanager.host\r\n\r\n" +
                "Proceed?";

            if (MessageBox.Show(this, plan, "Confirm Native Host Registration", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                string saved = NativeMessagingRegistrar.Register(extId, Application.ExecutablePath, true, true);
                _lblNativeHostStatus.ForeColor = MhsTheme.AccentEmerald;
                _lblNativeHostStatus.Text = "Registered: " + saved;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Native Host Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            AppSettings s = _controller.Settings;
            bool portChanged = (s.BrowserBridgePort != (int)_numBridgePort.Value) ||
                               (s.EnableBrowserBridge != _chkEnableBridge.Checked);

            s.BaseDownloadDirectory = _txtBaseDir.Text.Trim();
            s.UseCategorySubfolders = _chkSubfolders.Checked;
            s.DefaultConnectionsPerDownload = (int)_numDefaultConns.Value;
            s.EnableClipboardMonitor = _chkClipboard.Checked;
            s.ShowStartDownloadDialog = _chkStartDialog.Checked;
            s.ShowProgressDialogOnStart = _chkProgressPopup.Checked;
            s.ShowDownloadCompleteDialog = _chkCompleteDialog.Checked;
            s.SetServerLastModifiedTime = _chkServerLastMod.Checked;
            s.MinimizeToTrayOnClose = _chkTrayOnClose.Checked;
            s.EnableGlobalSpeedLimit = _chkGlobalLimit.Checked;
            s.GlobalSpeedLimitBytesPerSec = (long)_numGlobalKb.Value * 1024L;
            s.MonitoredExtensions = _txtExtensions.Text.Trim();

            s.ProxyType = (ProxyMode)_cmbProxyMode.SelectedIndex;
            s.ProxyAddress = _txtProxyHost.Text.Trim();
            s.ProxyPort = (int)_numProxyPort.Value;
            s.ProxyUser = _txtProxyUser.Text.Trim();
            s.ProxyPassword = _txtProxyPass.Text;
            if (!string.IsNullOrWhiteSpace(_txtDefaultUa.Text))
            {
                s.DefaultUserAgent = _txtDefaultUa.Text.Trim();
            }

            s.EnableBrowserBridge = _chkEnableBridge.Checked;
            s.BrowserBridgePort = (int)_numBridgePort.Value;
            s.ExcludedHosts = _txtExcludedHosts.Text.Trim();

            SaveCurrentQueueFields();
            _controller.ApplySettingsChanged();

            if (_bridge != null && portChanged)
            {
                if (s.EnableBrowserBridge) _bridge.Start(s.BrowserBridgePort);
                else _bridge.Stop();
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class ChecksumDialog : Form
    {
        private readonly string _filePath;
        private TextBox _txtMd5;
        private TextBox _txtSha256;
        private TextBox _txtExpected;
        private Label _lblResult;
        private CancellationTokenSource _cts;

        public ChecksumDialog(string filePath, string initialExpected)
        {
            _filePath = filePath;
            Text = "File Integrity & Checksum Verifier — MHS Manager";
            Size = new Size(620, 290);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            Label lblFile = MhsTheme.CreateLabel("File: " + Path.GetFileName(filePath), false);
            lblFile.Font = MhsTheme.FontBold;
            lblFile.Location = new Point(16, 16);
            Controls.Add(lblFile);

            Label lMd5 = MhsTheme.CreateLabel("Computed MD5:", true);
            lMd5.Location = new Point(16, 52);
            Controls.Add(lMd5);
            _txtMd5 = MhsTheme.CreateTextBox(true);
            _txtMd5.Font = MhsTheme.FontMono;
            _txtMd5.Text = "Computing...";
            _txtMd5.SetBounds(145, 49, 440, 24);
            Controls.Add(_txtMd5);

            Label lSha = MhsTheme.CreateLabel("Computed SHA-256:", true);
            lSha.Location = new Point(16, 88);
            Controls.Add(lSha);
            _txtSha256 = MhsTheme.CreateTextBox(true);
            _txtSha256.Font = MhsTheme.FontMono;
            _txtSha256.Text = "Computing...";
            _txtSha256.SetBounds(145, 85, 440, 24);
            Controls.Add(_txtSha256);

            Label lExp = MhsTheme.CreateLabel("Compare Hash:", false);
            lExp.Location = new Point(16, 128);
            Controls.Add(lExp);
            _txtExpected = MhsTheme.CreateTextBox(false);
            _txtExpected.Font = MhsTheme.FontMono;
            _txtExpected.Text = initialExpected ?? "";
            _txtExpected.SetBounds(145, 125, 440, 24);
            _txtExpected.TextChanged += delegate { EvaluateMatch(); };
            Controls.Add(_txtExpected);

            _lblResult = MhsTheme.CreateLabel("Paste an MD5 or SHA-256 hash above to verify authenticity.", true);
            _lblResult.Font = MhsTheme.FontBold;
            _lblResult.Location = new Point(145, 162);
            Controls.Add(_lblResult);

            Button btnClose = MhsTheme.CreateButton("Close", MhsTheme.BgElevated, 90, 32);
            btnClose.Location = new Point(495, 202);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);

            Shown += OnDialogShown;
        }

        private async void OnDialogShown(object sender, EventArgs e)
        {
            _cts = new CancellationTokenSource();
            try
            {
                DualHashResult res = await HttpHelpers.ComputeDualHashesAsync(_filePath, _cts.Token);
                if (IsDisposed) return;
                _txtMd5.Text = res.Md5Hex;
                _txtSha256.Text = res.Sha256Hex;
                EvaluateMatch();
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    _lblResult.ForeColor = MhsTheme.AccentDanger;
                    _lblResult.Text = "Error: " + ex.Message;
                }
            }
        }

        private void EvaluateMatch()
        {
            string exp = (_txtExpected.Text ?? "").Trim();
            if (string.IsNullOrEmpty(exp) || _txtSha256.Text == "Computing...")
            {
                _lblResult.ForeColor = MhsTheme.TextSecondary;
                _lblResult.Text = "Hashes computed. Paste an expected MD5 or SHA-256 digest to compare.";
                return;
            }

            if (string.Equals(exp, _txtMd5.Text, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exp, _txtSha256.Text, StringComparison.OrdinalIgnoreCase))
            {
                _lblResult.ForeColor = MhsTheme.AccentEmerald;
                _lblResult.Text = "MATCH VERIFIED — File cryptographic digest matches expected value.";
            }
            else
            {
                _lblResult.ForeColor = MhsTheme.AccentDanger;
                _lblResult.Text = "MISMATCH — File digest does NOT match expected value!";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null) { try { _cts.Cancel(); } catch { } }
            base.OnFormClosing(e);
        }
    }
}
