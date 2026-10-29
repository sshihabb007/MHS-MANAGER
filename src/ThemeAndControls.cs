using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MHSManager
{
    public static class MhsTheme
    {
        public static readonly Color BgDark = Color.FromArgb(15, 23, 42);
        public static readonly Color BgSurface = Color.FromArgb(30, 41, 59);
        public static readonly Color BgElevated = Color.FromArgb(41, 53, 72);
        public static readonly Color BgInput = Color.FromArgb(51, 65, 85);
        public static readonly Color Border = Color.FromArgb(71, 85, 105);
        public static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);
        public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);
        public static readonly Color AccentSky = Color.FromArgb(14, 165, 233);
        public static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);
        public static readonly Color AccentEmerald = Color.FromArgb(16, 185, 129);
        public static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);
        public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);

        public static readonly Font FontRegular = new Font("Segoe UI", 9.25f, FontStyle.Regular);
        public static readonly Font FontBold = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        public static readonly Font FontTitle = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 8.75f, FontStyle.Regular);

        public static void StyleForm(Form form)
        {
            form.BackColor = BgDark;
            form.ForeColor = TextPrimary;
            form.Font = FontRegular;
        }

        public static Button CreateButton(string text, Color backColor, int width, int height)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Width = width;
            btn.Height = height;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 255, 255, 255);
            btn.BackColor = backColor;
            btn.ForeColor = Color.White;
            btn.Font = FontBold;
            btn.Cursor = Cursors.Hand;
            return btn;
        }

        public static Button CreateToolbarButton(string text, Color accentTint, int width)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Width = width;
            btn.Height = 32;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = Border;
            btn.FlatAppearance.MouseOverBackColor = BgInput;
            btn.BackColor = accentTint;
            btn.ForeColor = TextPrimary;
            btn.Font = FontSmall;
            btn.Cursor = Cursors.Hand;
            btn.Margin = new Padding(0, 0, 5, 0);
            return btn;
        }

        public static TextBox CreateTextBox(bool readOnly)
        {
            TextBox tb = new TextBox();
            tb.BackColor = BgInput;
            tb.ForeColor = TextPrimary;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.Font = FontRegular;
            tb.ReadOnly = readOnly;
            return tb;
        }

        public static Label CreateLabel(string text, bool muted)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.AutoSize = true;
            lbl.ForeColor = muted ? TextSecondary : TextPrimary;
            lbl.Font = muted ? FontSmall : FontRegular;
            return lbl;
        }

        public static ToolStripProfessionalRenderer CreateDarkMenuRenderer()
        {
            return new MhsDarkMenuRenderer();
        }
    }

    public sealed class MhsDarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return MhsTheme.BgElevated; } }
        public override Color ImageMarginGradientBegin { get { return MhsTheme.BgElevated; } }
        public override Color ImageMarginGradientMiddle { get { return MhsTheme.BgElevated; } }
        public override Color ImageMarginGradientEnd { get { return MhsTheme.BgElevated; } }
        public override Color MenuBorder { get { return MhsTheme.Border; } }
        public override Color MenuItemBorder { get { return MhsTheme.AccentSky; } }
        public override Color MenuItemSelected { get { return MhsTheme.BgInput; } }
        public override Color MenuStripGradientBegin { get { return MhsTheme.BgDark; } }
        public override Color MenuStripGradientEnd { get { return MhsTheme.BgDark; } }
        public override Color MenuItemSelectedGradientBegin { get { return MhsTheme.BgInput; } }
        public override Color MenuItemSelectedGradientEnd { get { return MhsTheme.BgInput; } }
        public override Color MenuItemPressedGradientBegin { get { return MhsTheme.BgSurface; } }
        public override Color MenuItemPressedGradientEnd { get { return MhsTheme.BgSurface; } }
        public override Color SeparatorDark { get { return MhsTheme.Border; } }
        public override Color SeparatorLight { get { return MhsTheme.Border; } }
    }

    public sealed class MhsDarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public MhsDarkMenuRenderer() : base(new MhsDarkColorTable())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? MhsTheme.TextPrimary : MhsTheme.TextSecondary;
            base.OnRenderItemText(e);
        }
    }

    public sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
        }
    }

    public sealed class SegmentVisualizerBar : Control
    {
        private List<SegmentInfo> _segments = new List<SegmentInfo>();
        private long _totalBytes;
        private double _progressPercent;
        private DownloadStatus _status = DownloadStatus.Queued;

        public SegmentVisualizerBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Height = 28;
            BackColor = MhsTheme.BgSurface;
        }

        public void BindItem(DownloadItem item)
        {
            if (item == null)
            {
                _segments = new List<SegmentInfo>();
                _totalBytes = 0;
                _progressPercent = 0;
                _status = DownloadStatus.Queued;
                Invalidate();
                return;
            }

            _segments = item.GetSegmentsSnapshot();
            _totalBytes = item.TotalBytes;
            _progressPercent = item.ProgressPercent;
            _status = item.Status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            Rectangle rect = ClientRectangle;
            if (rect.Width <= 2 || rect.Height <= 2) return;

            using (SolidBrush bg = new SolidBrush(MhsTheme.BgDark))
            {
                g.FillRectangle(bg, rect);
            }

            Rectangle barRect = new Rectangle(1, 1, rect.Width - 2, rect.Height - 2);
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(30, 41, 59)))
            {
                g.FillRectangle(trackBrush, barRect);
            }

            long effectiveSpan = _totalBytes;
            if (effectiveSpan <= 0 && _segments.Count > 0)
            {
                long maxEnd = 0;
                for (int i = 0; i < _segments.Count; i++)
                {
                    if (_segments[i].EndByte + 1 > maxEnd)
                    {
                        maxEnd = _segments[i].EndByte + 1;
                    }
                }
                effectiveSpan = maxEnd;
            }

            if (_status == DownloadStatus.Completed)
            {
                using (SolidBrush doneBrush = new SolidBrush(MhsTheme.AccentEmerald))
                {
                    g.FillRectangle(doneBrush, barRect);
                }
            }
            else if (effectiveSpan > 0 && _segments.Count > 0)
            {
                using (SolidBrush doneBrush = new SolidBrush(MhsTheme.AccentEmerald))
                using (SolidBrush headBrush = new SolidBrush(MhsTheme.AccentCyan))
                using (Pen splitPen = new Pen(Color.FromArgb(110, 15, 23, 42), 1f))
                {
                    for (int i = 0; i < _segments.Count; i++)
                    {
                        SegmentInfo seg = _segments[i];
                        int xStart = barRect.Left + (int)((seg.StartByte * (long)barRect.Width) / effectiveSpan);
                        int xCur = barRect.Left + (int)((Math.Min(seg.CurrentByte, effectiveSpan) * (long)barRect.Width) / effectiveSpan);
                        int fillW = xCur - xStart;

                        if (fillW > 0)
                        {
                            g.FillRectangle(doneBrush, xStart, barRect.Top, fillW, barRect.Height);
                        }

                        if (seg.State == "Downloading" || seg.State == "Connecting")
                        {
                            int headX = Math.Min(barRect.Right - 3, Math.Max(barRect.Left, xCur));
                            g.FillRectangle(headBrush, headX, barRect.Top, 3, barRect.Height);
                        }

                        if (xStart > barRect.Left)
                        {
                            g.DrawLine(splitPen, xStart, barRect.Top, xStart, barRect.Bottom - 1);
                        }
                    }
                }
            }
            else if (_progressPercent > 0)
            {
                int w = (int)Math.Round((barRect.Width * _progressPercent) / 100.0);
                using (SolidBrush fill = new SolidBrush(MhsTheme.AccentSky))
                {
                    g.FillRectangle(fill, barRect.Left, barRect.Top, Math.Min(barRect.Width, w), barRect.Height);
                }
            }

            using (Pen borderPen = new Pen(MhsTheme.Border, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            string caption = string.Format(
                "{0:0.0}%   ({1} {2})",
                _progressPercent,
                _segments.Count,
                _segments.Count == 1 ? "segment" : "segments");

            TextRenderer.DrawText(
                g,
                caption,
                MhsTheme.FontBold,
                barRect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public sealed class SpeedGraphControl : Control
    {
        private readonly List<double> _samples = new List<double>();
        private const int MaxSamples = 60;
        private double _peakBps;
        private double _currentBps;

        public SpeedGraphControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
            Height = 90;
            BackColor = MhsTheme.BgDark;
            for (int i = 0; i < MaxSamples; i++)
            {
                _samples.Add(0);
            }
        }

        public void PushSample(double bytesPerSec)
        {
            double val = Math.Max(0, bytesPerSec);
            _currentBps = val;
            if (val > _peakBps) _peakBps = val;

            _samples.Add(val);
            while (_samples.Count > MaxSamples)
            {
                _samples.RemoveAt(0);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = ClientRectangle;
            if (r.Width <= 10 || r.Height <= 20) return;

            using (SolidBrush bg = new SolidBrush(MhsTheme.BgDark))
            {
                g.FillRectangle(bg, r);
            }

            using (Pen gridPen = new Pen(Color.FromArgb(45, 71, 85, 105), 1f))
            {
                gridPen.DashStyle = DashStyle.Dot;
                g.DrawLine(gridPen, r.Left, r.Top + r.Height / 3, r.Right, r.Top + r.Height / 3);
                g.DrawLine(gridPen, r.Left, r.Top + (r.Height * 2) / 3, r.Right, r.Top + (r.Height * 2) / 3);
            }

            double maxScale = 1024 * 64;
            for (int i = 0; i < _samples.Count; i++)
            {
                if (_samples[i] * 1.18 > maxScale)
                {
                    maxScale = _samples[i] * 1.18;
                }
            }

            int plotTop = 22;
            int plotBottom = r.Height - 4;
            int plotHeight = Math.Max(8, plotBottom - plotTop);

            if (_samples.Count >= 2)
            {
                PointF[] linePts = new PointF[_samples.Count];
                PointF[] polyPts = new PointF[_samples.Count + 2];

                for (int i = 0; i < _samples.Count; i++)
                {
                    float x = (float)i / (_samples.Count - 1) * (r.Width - 2) + 1f;
                    float ratio = (float)Math.Min(1.0, _samples[i] / maxScale);
                    float y = plotBottom - (ratio * plotHeight);
                    linePts[i] = new PointF(x, y);
                    polyPts[i + 1] = new PointF(x, y);
                }
                polyPts[0] = new PointF(1f, plotBottom);
                polyPts[_samples.Count + 1] = new PointF(r.Width - 1f, plotBottom);

                using (LinearGradientBrush areaBrush = new LinearGradientBrush(
                    new Rectangle(0, plotTop - 2, r.Width, plotHeight + 6),
                    Color.FromArgb(115, 14, 165, 233),
                    Color.FromArgb(10, 16, 185, 129),
                    LinearGradientMode.Vertical))
                {
                    g.FillPolygon(areaBrush, polyPts);
                }

                using (Pen linePen = new Pen(MhsTheme.AccentCyan, 2f))
                {
                    g.DrawLines(linePen, linePts);
                }
            }

            using (Pen borderPen = new Pen(MhsTheme.Border, 1f))
            {
                g.DrawRectangle(borderPen, 0, 0, r.Width - 1, r.Height - 1);
            }

            string leftLabel = "Live Speed: " + FormattingHelpers.FormatSpeed(_currentBps);
            string rightLabel = "Peak: " + FormattingHelpers.FormatSpeed(_peakBps);

            TextRenderer.DrawText(g, leftLabel, MhsTheme.FontBold, new Point(8, 3), MhsTheme.AccentCyan);
            Size rightSz = TextRenderer.MeasureText(rightLabel, MhsTheme.FontSmall);
            TextRenderer.DrawText(g, rightLabel, MhsTheme.FontSmall, new Point(Math.Max(140, r.Width - rightSz.Width - 8), 4), MhsTheme.TextSecondary);
        }
    }
}
