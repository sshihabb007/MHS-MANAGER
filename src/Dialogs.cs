using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MHSManager
{
    public sealed class AddDownloadDialog : Form
    {
        private readonly AppSettings _settings;
        private TextBox _txtUrl;
        private Button _btnProbe;
        private Label _lblProbeInfo;
        private TextBox _txtFileName;
        private ComboBox _cmbCategory;
        private ComboBox _cmbQueue;
        private TextBox _txtSaveDir;
        private NumericUpDown _numConnections;
        private TextBox _txtReferer;
        private TextBox _txtCookies;
        private TextBox _txtAuthUser;
        private TextBox _txtAuthPass;
        private TextBox _txtSha256;
        private CancellationTokenSource _probeCts;
        private bool _updatingAutoFields;

        public DownloadItem ResultItem { get; private set; }
        public bool StartImmediately { get; private set; }

        public AddDownloadDialog(AppSettings settings, string initialUrl, string initialReferer, string initialCookies)
        {
            _settings = settings;
            Text = "Add New Download — MHS Manager";
            Size = new Size(650, 520);
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
        }

        private void BuildLayout()
        {
            int leftLabel = 18;
            int leftInput = 135;
            int inputW = 480;
            int y = 18;

            Controls.Add(PlaceLabel("Download URL:", leftLabel, y + 4));
            _txtUrl = MhsTheme.CreateTextBox(false);
            _txtUrl.SetBounds(leftInput, y, inputW - 96, 25);
            _txtUrl.TextChanged += OnUrlTextChanged;
            Controls.Add(_txtUrl);

            _btnProbe = MhsTheme.CreateButton("Probe", MhsTheme.AccentSky, 88, 26);
            _btnProbe.Location = new Point(leftInput + inputW - 88, y - 1);
            _btnProbe.Click += OnProbeClicked;
            Controls.Add(_btnProbe);

            y += 30;
            _lblProbeInfo = MhsTheme.CreateLabel("Enter an HTTP/HTTPS or .m3u8 URL and click Probe to inspect size & Range support.", true);
            _lblProbeInfo.Location = new Point(leftInput, y);
            Controls.Add(_lblProbeInfo);

            y += 28;
            Controls.Add(PlaceLabel("Save File Name:", leftLabel, y + 4));
            _txtFileName = MhsTheme.CreateTextBox(false);
            _txtFileName.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtFileName);

            y += 36;
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
            _cmbQueue.SetBounds(leftInput + 240, y, 130, 25);
            Controls.Add(_cmbQueue);

            Controls.Add(PlaceLabel("Threads:", leftInput + 382, y + 4));
            _numConnections = new NumericUpDown();
            _numConnections.Minimum = 1;
            _numConnections.Maximum = 32;
            _numConnections.Value = Math.Max(1, Math.Min(32, _settings.DefaultConnectionsPerDownload));
            _numConnections.BackColor = MhsTheme.BgInput;
            _numConnections.ForeColor = MhsTheme.TextPrimary;
            _numConnections.SetBounds(leftInput + 436, y + 1, 44, 25);
            Controls.Add(_numConnections);

            y += 36;
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

            y += 40;
            Label hdrAdv = MhsTheme.CreateLabel("Advanced Request Headers & Checksum Verification (Optional)", true);
            hdrAdv.Font = MhsTheme.FontBold;
            hdrAdv.Location = new Point(leftLabel, y);
            Controls.Add(hdrAdv);

            y += 26;
            Controls.Add(PlaceLabel("Referer URL:", leftLabel, y + 4));
            _txtReferer = MhsTheme.CreateTextBox(false);
            _txtReferer.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtReferer);

            y += 34;
            Controls.Add(PlaceLabel("Cookie Header:", leftLabel, y + 4));
            _txtCookies = MhsTheme.CreateTextBox(false);
            _txtCookies.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtCookies);

            y += 34;
            Controls.Add(PlaceLabel("HTTP Auth User:", leftLabel, y + 4));
            _txtAuthUser = MhsTheme.CreateTextBox(false);
            _txtAuthUser.SetBounds(leftInput, y, 185, 25);
            Controls.Add(_txtAuthUser);

            Controls.Add(PlaceLabel("Password:", leftInput + 205, y + 4));
            _txtAuthPass = MhsTheme.CreateTextBox(false);
            _txtAuthPass.UseSystemPasswordChar = true;
            _txtAuthPass.SetBounds(leftInput + 280, y, 200, 25);
            Controls.Add(_txtAuthPass);

            y += 34;
            Controls.Add(PlaceLabel("Expected SHA256:", leftLabel, y + 4));
            _txtSha256 = MhsTheme.CreateTextBox(false);
            _txtSha256.SetBounds(leftInput, y, inputW, 25);
            Controls.Add(_txtSha256);

            y += 48;
            Button btnStart = MhsTheme.CreateButton("Start Download Now", MhsTheme.AccentEmerald, 180, 36);
            btnStart.Location = new Point(leftInput + 60, y);
            btnStart.Click += delegate { ConfirmDialog(true); };
            Controls.Add(btnStart);

            Button btnQueue = MhsTheme.CreateButton("Add to Queue", MhsTheme.AccentSky, 140, 36);
            btnQueue.Location = new Point(leftInput + 250, y);
            btnQueue.Click += delegate { ConfirmDialog(false); };
            Controls.Add(btnQueue);

            Button btnCancel = MhsTheme.CreateButton("Cancel", MhsTheme.BgElevated, 80, 36);
            btnCancel.Location = new Point(leftInput + 400, y);
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
            finally
            {
                _updatingAutoFields = false;
            }
        }

        private void OnCategoryChanged(object sender, EventArgs e)
        {
            if (_updatingAutoFields) return;
            string cat = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : "General";
            _txtSaveDir.Text = CategoryClassifier.ResolveSaveDirectory(_settings, cat);
        }

        private async void OnProbeClicked(object sender, EventArgs e)
        {
            string url = _txtUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (_probeCts != null)
            {
                try { _probeCts.Cancel(); } catch { }
            }
            _probeCts = new CancellationTokenSource();
            _btnProbe.Enabled = false;
            _lblProbeInfo.ForeColor = MhsTheme.AccentCyan;
            _lblProbeInfo.Text = "Probing server headers & byte-range support...";

            UrlProbeResult probe = await DownloadEngine.ProbeUrlAsync(
                url,
                _settings.DefaultUserAgent,
                _txtReferer.Text,
                _txtCookies.Text,
                _txtAuthUser.Text,
                _txtAuthPass.Text,
                _probeCts.Token);

            if (IsDisposed) return;
            _btnProbe.Enabled = true;

            if (!probe.Success)
            {
                _lblProbeInfo.ForeColor = MhsTheme.AccentDanger;
                _lblProbeInfo.Text = "Probe failed: " + (probe.ErrorMessage ?? "Unreachable");
                return;
            }

            if (!string.IsNullOrWhiteSpace(probe.SuggestedFileName))
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
                _lblProbeInfo.Text = "Detected HLS Media Stream (.m3u8) — Multi-segment downloader & TS merger ready.";
            }
            else
            {
                _lblProbeInfo.Text = string.Format(
                    "Size: {0}  •  Multi-Connection Range Support: {1}",
                    probe.TotalBytes >= 0 ? FormattingHelpers.FormatBytes(probe.TotalBytes) : "Unknown",
                    probe.SupportsRanges ? "YES (Dynamic Splitting Ready)" : "No (Single Connection)");
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
            }

            string cat = _cmbCategory.SelectedItem != null ? _cmbCategory.SelectedItem.ToString() : "General";
            string queue = _cmbQueue.SelectedItem != null ? _cmbQueue.SelectedItem.ToString() : "Main Queue";

            ResultItem = new DownloadItem
            {
                Url = url,
                FileName = fileName,
                CategoryName = cat,
                QueueName = queue,
                SaveDirectory = _txtSaveDir.Text.Trim(),
                MaxConnections = (int)_numConnections.Value,
                Referer = _txtReferer.Text.Trim(),
                Cookies = _txtCookies.Text.Trim(),
                AuthUser = _txtAuthUser.Text.Trim(),
                AuthPassword = _txtAuthPass.Text,
                ExpectedSha256 = _txtSha256.Text.Trim()
            };
            StartImmediately = startNow;
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

            Label lblUrl = MhsTheme.CreateLabel(_item.Url, true);
            lblUrl.AutoEllipsis = true;
            lblUrl.AutoSize = false;
            lblUrl.SetBounds(14, 10, 610, 18);
            lblUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            topInfo.Controls.Add(lblUrl);

            _lblStatusVal = AddMetricPair(topInfo, "Status:", 14, 36, 95);
            _lblSizeVal = AddMetricPair(topInfo, "File Size:", 14, 62, 95);
            _lblDownloadedVal = AddMetricPair(topInfo, "Downloaded:", 14, 88, 95);

            _lblSpeedVal = AddMetricPair(topInfo, "Transfer Rate:", 330, 36, 105);
            _lblEtaVal = AddMetricPair(topInfo, "Time Left:", 330, 62, 105);
            _lblResumeVal = AddMetricPair(topInfo, "Resume Support:", 330, 88, 105);

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
            _chkItemLimit.Text = "Speed Cap (KB/s):";
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
            _numItemLimitKb.SetBounds(150, 10, 80, 24);
            _numItemLimitKb.ValueChanged += OnSpeedCapChanged;
            bottomBar.Controls.Add(_numItemLimitKb);

            Label lblWhenDone = MhsTheme.CreateLabel("On Completion:", false);
            lblWhenDone.Location = new Point(252, 13);
            bottomBar.Controls.Add(lblWhenDone);

            _cmbOnComplete = new ComboBox();
            _cmbOnComplete.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbOnComplete.BackColor = MhsTheme.BgInput;
            _cmbOnComplete.ForeColor = MhsTheme.TextPrimary;
            _cmbOnComplete.Items.AddRange(new object[] { "Keep Window / Idle", "Open File", "Open Containing Folder" });
            _cmbOnComplete.SelectedIndex = (int)_item.OnComplete;
            _cmbOnComplete.SetBounds(354, 10, 180, 24);
            _cmbOnComplete.SelectedIndexChanged += delegate
            {
                _item.OnComplete = (CompletionAction)_cmbOnComplete.SelectedIndex;
            };
            bottomBar.Controls.Add(_cmbOnComplete);

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
                : (_item.SupportsRanges ? "Yes (Dynamic Multi-Range)" : "No");
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
            }
            else if (_item.Status == DownloadStatus.Downloading || _item.Status == DownloadStatus.Probing)
            {
                _btnPauseResume.Enabled = true;
                _btnPauseResume.Text = "Pause";
                _btnPauseResume.BackColor = MhsTheme.AccentAmber;
            }
            else
            {
                _btnPauseResume.Enabled = true;
                _btnPauseResume.Text = "Resume";
                _btnPauseResume.BackColor = MhsTheme.AccentEmerald;
            }

            UpdateSegmentListView();
        }

        private void UpdateSegmentListView()
        {
            List<SegmentInfo> copy = new List<SegmentInfo>();
            if (_item.Segments != null)
            {
                lock (_item.Segments)
                {
                    for (int i = 0; i < _item.Segments.Count; i++)
                    {
                        copy.Add(_item.Segments[i]);
                    }
                }
            }

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
            _chkStartNow.Checked = false;
            _chkStartNow.Location = new Point(270, 402);
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
                _allGrabbed = await SiteLinkGrabber.GrabLinksAsync(url, _settings.DefaultUserAgent, null, _cts.Token);
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

        private TextBox _txtBaseDir;
        private CheckBox _chkSubfolders;
        private NumericUpDown _numDefaultConns;
        private CheckBox _chkClipboard;
        private CheckBox _chkProgressPopup;
        private CheckBox _chkTrayOnClose;
        private CheckBox _chkGlobalLimit;
        private NumericUpDown _numGlobalKb;
        private TextBox _txtExtensions;

        private ComboBox _cmbQueueSelect;
        private NumericUpDown _numQueueConcurrent;
        private CheckBox _chkStartAt;
        private NumericUpDown _numStartHour;
        private NumericUpDown _numStartMin;
        private CheckBox _chkStopAt;
        private NumericUpDown _numStopHour;
        private NumericUpDown _numStopMin;
        private Label _lblQueueState;
        private bool _loadingQueueFields;

        public SettingsAndSchedulerDialog(DownloadManagerController controller, BrowserBridgeServer bridge, int initialTab)
        {
            _controller = controller;
            _bridge = bridge;

            Text = "Options, Queues & Browser Integration — MHS Manager";
            Size = new Size(640, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            MhsTheme.StyleForm(this);

            TabControl tabs = new TabControl();
            tabs.SetBounds(14, 14, 598, 372);

            TabPage tabGen = new TabPage("General & Bandwidth");
            tabGen.BackColor = MhsTheme.BgSurface;
            tabGen.ForeColor = MhsTheme.TextPrimary;
            BuildGeneralTab(tabGen);

            TabPage tabSched = new TabPage("Queues & Scheduler");
            tabSched.BackColor = MhsTheme.BgSurface;
            tabSched.ForeColor = MhsTheme.TextPrimary;
            BuildSchedulerTab(tabSched);

            TabPage tabExt = new TabPage("Browser Extension");
            tabExt.BackColor = MhsTheme.BgSurface;
            tabExt.ForeColor = MhsTheme.TextPrimary;
            BuildExtensionTab(tabExt);

            tabs.TabPages.Add(tabGen);
            tabs.TabPages.Add(tabSched);
            tabs.TabPages.Add(tabExt);
            if (initialTab >= 0 && initialTab < tabs.TabCount)
            {
                tabs.SelectedIndex = initialTab;
            }
            Controls.Add(tabs);

            Button btnSave = MhsTheme.CreateButton("Save & Apply", MhsTheme.AccentEmerald, 130, 34);
            btnSave.Location = new Point(390, 396);
            btnSave.Click += OnSaveClicked;
            Controls.Add(btnSave);

            Button btnCancel = MhsTheme.CreateButton("Close", MhsTheme.BgElevated, 82, 34);
            btnCancel.Location = new Point(530, 396);
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);
        }

        private void BuildGeneralTab(TabPage page)
        {
            AppSettings s = _controller.Settings;
            int y = 18;

            Label l1 = MhsTheme.CreateLabel("Default Download Folder:", false);
            l1.Location = new Point(16, y + 4);
            page.Controls.Add(l1);

            _txtBaseDir = MhsTheme.CreateTextBox(false);
            _txtBaseDir.Text = s.BaseDownloadDirectory;
            _txtBaseDir.SetBounds(185, y, 300, 25);
            page.Controls.Add(_txtBaseDir);

            Button btnBrowse = MhsTheme.CreateButton("Browse", MhsTheme.BgElevated, 80, 26);
            btnBrowse.Location = new Point(492, y - 1);
            btnBrowse.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = _txtBaseDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK) _txtBaseDir.Text = fbd.SelectedPath;
                }
            };
            page.Controls.Add(btnBrowse);

            y += 36;
            _chkSubfolders = new CheckBox { Text = "Automatically organize into Category subfolders (Video, Compressed, Programs...)", Checked = s.UseCategorySubfolders, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkSubfolders);

            y += 32;
            Label l2 = MhsTheme.CreateLabel("Default Connections per File (1–32):", false);
            l2.Location = new Point(16, y + 3);
            page.Controls.Add(l2);

            _numDefaultConns = new NumericUpDown { Minimum = 1, Maximum = 32, Value = Math.Max(1, Math.Min(32, s.DefaultConnectionsPerDownload)), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numDefaultConns.SetBounds(250, y, 64, 25);
            page.Controls.Add(_numDefaultConns);

            y += 36;
            _chkClipboard = new CheckBox { Text = "Monitor Windows Clipboard for downloadable URLs", Checked = s.EnableClipboardMonitor, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkClipboard);

            y += 28;
            _chkProgressPopup = new CheckBox { Text = "Show live per-download floating progress window when starting", Checked = s.ShowProgressDialogOnStart, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkProgressPopup);

            y += 28;
            _chkTrayOnClose = new CheckBox { Text = "Minimize MHS Manager to System Tray when closing window", Checked = s.MinimizeToTrayOnClose, AutoSize = true, Location = new Point(16, y) };
            page.Controls.Add(_chkTrayOnClose);

            y += 36;
            _chkGlobalLimit = new CheckBox { Text = "Enable Global Bandwidth Speed Limiter (KB/s):", Checked = s.EnableGlobalSpeedLimit, AutoSize = true, Location = new Point(16, y + 2) };
            page.Controls.Add(_chkGlobalLimit);

            _numGlobalKb = new NumericUpDown { Minimum = 32, Maximum = 2048000, Value = Math.Max(32, s.GlobalSpeedLimitBytesPerSec / 1024), BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numGlobalKb.SetBounds(325, y, 95, 25);
            page.Controls.Add(_numGlobalKb);

            y += 38;
            Label lExt = MhsTheme.CreateLabel("Auto-captured File Extensions (pipe-separated):", true);
            lExt.Location = new Point(16, y);
            page.Controls.Add(lExt);

            y += 20;
            _txtExtensions = MhsTheme.CreateTextBox(false);
            _txtExtensions.Text = s.MonitoredExtensions;
            _txtExtensions.SetBounds(16, y, 556, 25);
            page.Controls.Add(_txtExtensions);
        }

        private void BuildSchedulerTab(TabPage page)
        {
            int y = 20;
            Label lq = MhsTheme.CreateLabel("Select Queue:", false);
            lq.Location = new Point(18, y + 4);
            page.Controls.Add(lq);

            _cmbQueueSelect = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            for (int i = 0; i < _controller.Settings.Queues.Count; i++)
            {
                _cmbQueueSelect.Items.Add(_controller.Settings.Queues[i].Name);
            }
            _cmbQueueSelect.SetBounds(130, y, 180, 25);
            _cmbQueueSelect.SelectedIndexChanged += delegate { LoadSelectedQueue(); };
            page.Controls.Add(_cmbQueueSelect);

            _lblQueueState = MhsTheme.CreateLabel("", false);
            _lblQueueState.Font = MhsTheme.FontBold;
            _lblQueueState.Location = new Point(330, y + 4);
            page.Controls.Add(_lblQueueState);

            y += 44;
            Label lc = MhsTheme.CreateLabel("Max Simultaneous Active Downloads in Queue:", false);
            lc.Location = new Point(18, y + 3);
            page.Controls.Add(lc);

            _numQueueConcurrent = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 4, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numQueueConcurrent.SetBounds(320, y, 64, 25);
            _numQueueConcurrent.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numQueueConcurrent);

            y += 44;
            _chkStartAt = new CheckBox { Text = "Scheduled Auto-Start Queue Daily at (HH : MM):", AutoSize = true, Location = new Point(18, y + 2) };
            _chkStartAt.CheckedChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_chkStartAt);

            _numStartHour = new NumericUpDown { Minimum = 0, Maximum = 23, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStartHour.SetBounds(340, y, 52, 25);
            _numStartHour.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStartHour);

            _numStartMin = new NumericUpDown { Minimum = 0, Maximum = 59, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStartMin.SetBounds(402, y, 52, 25);
            _numStartMin.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStartMin);

            y += 40;
            _chkStopAt = new CheckBox { Text = "Scheduled Auto-Pause Queue Daily at (HH : MM):", AutoSize = true, Location = new Point(18, y + 2) };
            _chkStopAt.CheckedChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_chkStopAt);

            _numStopHour = new NumericUpDown { Minimum = 0, Maximum = 23, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStopHour.SetBounds(340, y, 52, 25);
            _numStopHour.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStopHour);

            _numStopMin = new NumericUpDown { Minimum = 0, Maximum = 59, BackColor = MhsTheme.BgInput, ForeColor = MhsTheme.TextPrimary };
            _numStopMin.SetBounds(402, y, 52, 25);
            _numStopMin.ValueChanged += delegate { SaveCurrentQueueFields(); };
            page.Controls.Add(_numStopMin);

            y += 56;
            Button btnStartQ = MhsTheme.CreateButton("Start Queue Now", MhsTheme.AccentEmerald, 160, 34);
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

            Button btnStopQ = MhsTheme.CreateButton("Stop Queue Now", MhsTheme.AccentAmber, 160, 34);
            btnStopQ.Location = new Point(190, y);
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
            _chkStartAt.Checked = q.EnableScheduleStart;
            _numStartHour.Value = Math.Max(0, Math.Min(23, q.StartHour));
            _numStartMin.Value = Math.Max(0, Math.Min(59, q.StartMinute));
            _chkStopAt.Checked = q.EnableScheduleStop;
            _numStopHour.Value = Math.Max(0, Math.Min(23, q.StopHour));
            _numStopMin.Value = Math.Max(0, Math.Min(59, q.StopMinute));
            _lblQueueState.Text = q.IsRunning ? "Status: RUNNING" : "Status: IDLE";
            _lblQueueState.ForeColor = q.IsRunning ? MhsTheme.AccentEmerald : MhsTheme.TextSecondary;
            _loadingQueueFields = false;
        }

        private void SaveCurrentQueueFields()
        {
            if (_loadingQueueFields) return;
            QueueDefinition q = GetCurrentQueue();
            if (q == null) return;
            q.MaxConcurrentDownloads = (int)_numQueueConcurrent.Value;
            q.EnableScheduleStart = _chkStartAt.Checked;
            q.StartHour = (int)_numStartHour.Value;
            q.StartMinute = (int)_numStartMin.Value;
            q.EnableScheduleStop = _chkStopAt.Checked;
            q.StopHour = (int)_numStopHour.Value;
            q.StopMinute = (int)_numStopMin.Value;
        }

        private void BuildExtensionTab(TabPage page)
        {
            string bridgeStatus = (_bridge != null && _bridge.IsRunning)
                ? string.Format("ONLINE on http://127.0.0.1:{0}", _bridge.Port)
                : "OFFLINE";

            Label lblBridge = MhsTheme.CreateLabel("Localhost Browser RPC Bridge: " + bridgeStatus, false);
            lblBridge.Font = MhsTheme.FontBold;
            lblBridge.ForeColor = (_bridge != null && _bridge.IsRunning) ? MhsTheme.AccentEmerald : MhsTheme.AccentDanger;
            lblBridge.Location = new Point(18, 20);
            page.Controls.Add(lblBridge);

            string extDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "browser-extension"));

            TextBox guide = MhsTheme.CreateTextBox(true);
            guide.Multiline = true;
            guide.SetBounds(18, 52, 550, 215);
            guide.Text =
                "MHS Manager ships with a clean-room Manifest V3 Browser Extension for Chrome, Edge, and Brave:\r\n\r\n" +
                "1. Open chrome://extensions or edge://extensions in your browser.\r\n" +
                "2. Enable 'Developer mode' in the top-right corner.\r\n" +
                "3. Click 'Load unpacked' and select the folder:\r\n   " + extDir + "\r\n\r\n" +
                "Features included:\r\n" +
                " • Automatic download takeover for configured file extensions (with instant toggle in popup)\r\n" +
                " • Right-click 'Download with MHS Manager' & 'Download All Links with MHS Manager'\r\n" +
                " • Floating 'Download Video with MHS' pill above HTML5 video players & .m3u8 streams";
            page.Controls.Add(guide);

            Button btnOpenExt = MhsTheme.CreateButton("Open Extension Folder", MhsTheme.AccentSky, 190, 34);
            btnOpenExt.Location = new Point(18, 282);
            btnOpenExt.Click += delegate
            {
                try
                {
                    if (Directory.Exists(extDir))
                    {
                        Process.Start("explorer.exe", "\"" + extDir + "\"");
                    }
                }
                catch { }
            };
            page.Controls.Add(btnOpenExt);
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            AppSettings s = _controller.Settings;
            s.BaseDownloadDirectory = _txtBaseDir.Text.Trim();
            s.UseCategorySubfolders = _chkSubfolders.Checked;
            s.DefaultConnectionsPerDownload = (int)_numDefaultConns.Value;
            s.EnableClipboardMonitor = _chkClipboard.Checked;
            s.ShowProgressDialogOnStart = _chkProgressPopup.Checked;
            s.MinimizeToTrayOnClose = _chkTrayOnClose.Checked;
            s.EnableGlobalSpeedLimit = _chkGlobalLimit.Checked;
            s.GlobalSpeedLimitBytesPerSec = (long)_numGlobalKb.Value * 1024L;
            s.MonitoredExtensions = _txtExtensions.Text.Trim();

            SaveCurrentQueueFields();
            _controller.ApplySettingsChanged();
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

