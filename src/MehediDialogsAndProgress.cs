using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShihabMehediIDM
{
    public sealed class ShihabDarkMenuColorTable : ProfessionalColorTable
    {
        private readonly bool _shihabDark;

        public ShihabDarkMenuColorTable(bool shihabDark)
        {
            _shihabDark = shihabDark;
            UseSystemColors = false;
        }

        public override Color MenuStripGradientBegin { get { return _shihabDark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 243, 248); } }
        public override Color MenuStripGradientEnd { get { return _shihabDark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 243, 248); } }
        public override Color ToolStripDropDownBackground { get { return _shihabDark ? Color.FromArgb(30, 30, 32) : Color.White; } }
        public override Color ImageMarginGradientBegin { get { return _shihabDark ? Color.FromArgb(30, 30, 32) : Color.FromArgb(245, 247, 250); } }
        public override Color ImageMarginGradientMiddle { get { return _shihabDark ? Color.FromArgb(30, 30, 32) : Color.FromArgb(245, 247, 250); } }
        public override Color ImageMarginGradientEnd { get { return _shihabDark ? Color.FromArgb(30, 30, 32) : Color.FromArgb(245, 247, 250); } }
        public override Color MenuBorder { get { return _shihabDark ? Color.FromArgb(72, 72, 76) : Color.FromArgb(195, 203, 214); } }
        public override Color MenuItemBorder { get { return _shihabDark ? Color.FromArgb(85, 130, 150) : Color.FromArgb(14, 165, 233); } }
        public override Color MenuItemSelected { get { return _shihabDark ? Color.FromArgb(52, 58, 66) : Color.FromArgb(224, 238, 252); } }
        public override Color MenuItemSelectedGradientBegin { get { return _shihabDark ? Color.FromArgb(48, 54, 62) : Color.FromArgb(224, 238, 252); } }
        public override Color MenuItemSelectedGradientEnd { get { return _shihabDark ? Color.FromArgb(48, 54, 62) : Color.FromArgb(224, 238, 252); } }
        public override Color MenuItemPressedGradientBegin { get { return _shihabDark ? Color.FromArgb(36, 40, 46) : Color.FromArgb(210, 228, 248); } }
        public override Color MenuItemPressedGradientEnd { get { return _shihabDark ? Color.FromArgb(36, 40, 46) : Color.FromArgb(210, 228, 248); } }
        public override Color SeparatorDark { get { return _shihabDark ? Color.FromArgb(62, 62, 66) : Color.FromArgb(210, 216, 224); } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
    }

    public sealed class MehediDarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _mehediDark;
        private readonly bool _shihabIsLargeToolbar;

        public MehediDarkToolStripRenderer(bool mehediDark, bool shihabIsLargeToolbar)
            : base(new ShihabDarkMenuColorTable(mehediDark))
        {
            _mehediDark = mehediDark;
            _shihabIsLargeToolbar = shihabIsLargeToolbar;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs shihabArgs)
        {
            Color mehediFill;
            if (shihabArgs.ToolStrip is ToolStripDropDown)
            {
                mehediFill = _mehediDark ? Color.FromArgb(30, 30, 32) : Color.White;
            }
            else if (_shihabIsLargeToolbar)
            {
                mehediFill = _mehediDark ? Color.FromArgb(54, 54, 54) : Color.FromArgb(236, 240, 245);
            }
            else if (shihabArgs.ToolStrip is StatusStrip)
            {
                mehediFill = _mehediDark ? Color.FromArgb(26, 26, 28) : Color.FromArgb(234, 238, 244);
            }
            else
            {
                mehediFill = _mehediDark ? Color.FromArgb(20, 20, 20) : Color.FromArgb(244, 246, 250);
            }
            using (SolidBrush shihabBrush = new SolidBrush(mehediFill))
            {
                shihabArgs.Graphics.FillRectangle(shihabBrush, shihabArgs.AffectedBounds);
            }
            if (_shihabIsLargeToolbar && !(shihabArgs.ToolStrip is ToolStripDropDown))
            {
                Color mehediTopLine = _mehediDark ? Color.FromArgb(115, 115, 115) : Color.FromArgb(205, 212, 220);
                Color shihabBotLine = _mehediDark ? Color.FromArgb(24, 24, 24) : Color.FromArgb(190, 198, 208);
                using (Pen mehediTopPen = new Pen(mehediTopLine))
                using (Pen shihabBotPen = new Pen(shihabBotLine))
                {
                    shihabArgs.Graphics.DrawLine(mehediTopPen, 0, 0, shihabArgs.ToolStrip.Width, 0);
                    shihabArgs.Graphics.DrawLine(shihabBotPen, 0, shihabArgs.ToolStrip.Height - 1, shihabArgs.ToolStrip.Width, shihabArgs.ToolStrip.Height - 1);
                }
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs mehediArgs)
        {
            if (mehediArgs.ToolStrip is ToolStripDropDown)
            {
                Color shihabBorder = _mehediDark ? Color.FromArgb(74, 74, 78) : Color.FromArgb(195, 203, 214);
                using (Pen mehediPen = new Pen(shihabBorder))
                {
                    Rectangle shihabR = new Rectangle(0, 0, mehediArgs.ToolStrip.Width - 1, mehediArgs.ToolStrip.Height - 1);
                    mehediArgs.Graphics.DrawRectangle(mehediPen, shihabR);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs shihabTextArgs)
        {
            if (shihabTextArgs.Item != null)
            {
                shihabTextArgs.TextColor = _mehediDark ? Color.FromArgb(236, 238, 242) : Color.FromArgb(24, 30, 38);
            }
            base.OnRenderItemText(shihabTextArgs);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs mehediArrowArgs)
        {
            mehediArrowArgs.ArrowColor = _mehediDark ? Color.FromArgb(185, 190, 198) : Color.FromArgb(60, 70, 82);
            base.OnRenderArrow(mehediArrowArgs);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs shihabBtnArgs)
        {
            if (shihabBtnArgs.Item.Selected || shihabBtnArgs.Item.Pressed)
            {
                PaintMehediHoverPill(shihabBtnArgs.Graphics, new Rectangle(1, 2, shihabBtnArgs.Item.Width - 3, shihabBtnArgs.Item.Height - 5), shihabBtnArgs.Item.Pressed);
            }
        }

        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs mehediSplitArgs)
        {
            ToolStripSplitButton shihabSplit = mehediSplitArgs.Item as ToolStripSplitButton;
            if (shihabSplit == null) return;
            if (shihabSplit.Selected || shihabSplit.Pressed || shihabSplit.ButtonPressed)
            {
                PaintMehediHoverPill(mehediSplitArgs.Graphics, new Rectangle(1, 2, shihabSplit.Width - 3, shihabSplit.Height - 5), shihabSplit.Pressed || shihabSplit.ButtonPressed);
            }
            DrawArrow(new ToolStripArrowRenderEventArgs(
                mehediSplitArgs.Graphics,
                shihabSplit,
                shihabSplit.DropDownButtonBounds,
                _mehediDark ? Color.FromArgb(175, 180, 188) : Color.FromArgb(70, 80, 92),
                ArrowDirection.Down));
        }

        private void PaintMehediHoverPill(Graphics shihabG, Rectangle mehediRect, bool shihabPressed)
        {
            shihabG.SmoothingMode = SmoothingMode.AntiAlias;
            Color mehediFill = _mehediDark
                ? (shihabPressed ? Color.FromArgb(40, 44, 48) : Color.FromArgb(68, 72, 78))
                : (shihabPressed ? Color.FromArgb(208, 224, 242) : Color.FromArgb(222, 232, 245));
            Color shihabEdge = _mehediDark
                ? Color.FromArgb(98, 104, 112)
                : Color.FromArgb(165, 185, 212);
            using (GraphicsPath mehediPath = ShihabThemePalette.CreateShihabRoundedRect(mehediRect, 6))
            using (SolidBrush shihabBr = new SolidBrush(mehediFill))
            using (Pen mehediPen = new Pen(shihabEdge, 1f))
            {
                shihabG.FillPath(shihabBr, mehediPath);
                shihabG.DrawPath(mehediPen, mehediPath);
            }
        }
    }

    public sealed class MehediModernTreeView : TreeView
    {
        public bool ShihabDarkMode { get; set; }
        private TreeNode _mehediHoveredNode;

        public MehediModernTreeView()
        {
            ShihabDarkMode = true;
            DoubleBuffered = true;
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            FullRowSelect = true;
            HideSelection = false;
            ShowLines = false;
            ShowPlusMinus = false;
            ShowRootLines = false;
            ItemHeight = 23;
            BorderStyle = BorderStyle.None;
        }

        protected override void OnMouseMove(MouseEventArgs shihabE)
        {
            base.OnMouseMove(shihabE);
            TreeNode mehediHit = GetNodeAt(shihabE.Location);
            if (!ReferenceEquals(mehediHit, _mehediHoveredNode))
            {
                _mehediHoveredNode = mehediHit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs mehediE)
        {
            base.OnMouseLeave(mehediE);
            if (_mehediHoveredNode != null)
            {
                _mehediHoveredNode = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs shihabE)
        {
            base.OnMouseDown(shihabE);
            TreeNode mehediNode = GetNodeAt(shihabE.Location);
            if (mehediNode != null)
            {
                int shihabIndentX = 6 + mehediNode.Level * 16;
                if (mehediNode.Nodes.Count > 0 && shihabE.X >= shihabIndentX - 2 && shihabE.X <= shihabIndentX + 14)
                {
                    if (mehediNode.IsExpanded) mehediNode.Collapse();
                    else mehediNode.Expand();
                }
                SelectedNode = mehediNode;
            }
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs mehediArgs)
        {
            TreeNode shihabNode = mehediArgs.Node;
            if (shihabNode == null || mehediArgs.Bounds.Height <= 0) return;

            Graphics shihabG = mehediArgs.Graphics;
            shihabG.SmoothingMode = SmoothingMode.AntiAlias;
            shihabG.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle mehediRowRect = new Rectangle(0, mehediArgs.Bounds.Y, ClientSize.Width, mehediArgs.Bounds.Height);
            bool shihabSelected = ReferenceEquals(shihabNode, SelectedNode);
            bool mehediHovered = ReferenceEquals(shihabNode, _mehediHoveredNode);

            Color shihabBg = ShihabDarkMode ? Color.FromArgb(28, 28, 28) : Color.White;
            if (shihabSelected)
            {
                shihabBg = ShihabDarkMode ? Color.FromArgb(62, 64, 68) : Color.FromArgb(212, 230, 248);
            }
            else if (mehediHovered)
            {
                shihabBg = ShihabDarkMode ? Color.FromArgb(42, 44, 48) : Color.FromArgb(236, 243, 252);
            }

            using (SolidBrush mehediRowBrush = new SolidBrush(shihabBg))
            {
                shihabG.FillRectangle(mehediRowBrush, mehediRowRect);
            }

            int shihabBaseX = 6 + shihabNode.Level * 16;
            if (shihabNode.Nodes.Count > 0)
            {
                using (Pen mehediChevPen = new Pen(ShihabDarkMode ? Color.FromArgb(205, 210, 216) : Color.FromArgb(80, 90, 102), 1.5f))
                {
                    int shihabCy = mehediRowRect.Y + mehediRowRect.Height / 2;
                    if (shihabNode.IsExpanded)
                    {
                        shihabG.DrawLines(mehediChevPen, new Point[]
                        {
                            new Point(shihabBaseX, shihabCy - 2),
                            new Point(shihabBaseX + 4, shihabCy + 2),
                            new Point(shihabBaseX + 8, shihabCy - 2)
                        });
                    }
                    else
                    {
                        shihabG.DrawLines(mehediChevPen, new Point[]
                        {
                            new Point(shihabBaseX + 2, shihabCy - 4),
                            new Point(shihabBaseX + 6, shihabCy),
                            new Point(shihabBaseX + 2, shihabCy + 4)
                        });
                    }
                }
            }

            Rectangle mehediIconRect = new Rectangle(shihabBaseX + 13, mehediRowRect.Y + 3, 16, 16);
            string shihabTagStr = shihabNode.Tag != null ? shihabNode.Tag.ToString() : shihabNode.Text;
            ShihabThemePalette.DrawMehediTreeCategoryIcon(shihabG, mehediIconRect, shihabTagStr, shihabNode.Text);

            Rectangle shihabTextRect = new Rectangle(
                mehediIconRect.Right + 6,
                mehediRowRect.Y,
                Math.Max(20, mehediRowRect.Width - mehediIconRect.Right - 8),
                mehediRowRect.Height);
            Color mehediTextCol = ShihabDarkMode ? Color.FromArgb(238, 240, 244) : Color.FromArgb(24, 30, 38);
            TextRenderer.DrawText(
                shihabG,
                shihabNode.Text,
                Font,
                shihabTextRect,
                mehediTextCol,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public sealed class ShihabModernListView : ListView
    {
        public bool MehediDarkMode { get; set; }
        public bool ShihabShowFileTypeBadges { get; set; }
        public int MehediSortedColumnIndex { get; set; }
        public bool ShihabSortAscending { get; set; }

        public ShihabModernListView()
        {
            MehediDarkMode = true;
            ShihabShowFileTypeBadges = true;
            MehediSortedColumnIndex = -1;
            ShihabSortAscending = false;
            DoubleBuffered = true;
            OwnerDraw = true;
            FullRowSelect = true;
            View = View.Details;
            BorderStyle = BorderStyle.None;
            HideSelection = false;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs shihabHdrArgs)
        {
            Graphics mehediG = shihabHdrArgs.Graphics;
            mehediG.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle shihabBounds = shihabHdrArgs.Bounds;

            Color mehediHdrBg = MehediDarkMode ? Color.FromArgb(30, 30, 30) : Color.FromArgb(236, 240, 246);
            Color shihabLineCol = MehediDarkMode ? Color.FromArgb(72, 72, 74) : Color.FromArgb(204, 212, 222);
            Color mehediFg = MehediDarkMode ? Color.FromArgb(236, 238, 242) : Color.FromArgb(28, 34, 44);

            using (SolidBrush shihabBgBrush = new SolidBrush(mehediHdrBg))
            using (Pen mehediBorderPen = new Pen(shihabLineCol))
            {
                mehediG.FillRectangle(shihabBgBrush, shihabBounds);
                mehediG.DrawLine(mehediBorderPen, shihabBounds.Right - 1, shihabBounds.Top, shihabBounds.Right - 1, shihabBounds.Bottom);
                mehediG.DrawLine(mehediBorderPen, shihabBounds.Left, shihabBounds.Bottom - 1, shihabBounds.Right, shihabBounds.Bottom - 1);
            }

            string shihabHeaderText = shihabHdrArgs.Header != null ? shihabHdrArgs.Header.Text : "";
            if (shihabHdrArgs.ColumnIndex == MehediSortedColumnIndex)
            {
                shihabHeaderText += ShihabSortAscending ? "  ↑" : "  ↓";
            }

            Rectangle mehediTextBounds = new Rectangle(shihabBounds.X + 6, shihabBounds.Y, Math.Max(4, shihabBounds.Width - 10), shihabBounds.Height);
            TextRenderer.DrawText(
                mehediG,
                shihabHeaderText,
                Font,
                mehediTextBounds,
                mehediFg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs mehediItemArgs)
        {
        }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs shihabSubArgs)
        {
            Graphics mehediG = shihabSubArgs.Graphics;
            mehediG.SmoothingMode = SmoothingMode.AntiAlias;
            mehediG.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle shihabCell = shihabSubArgs.Bounds;
            bool mehediSelected = shihabSubArgs.Item != null && shihabSubArgs.Item.Selected;
            bool shihabEven = (shihabSubArgs.ItemIndex % 2) == 0;

            Color mehediRowBg;
            if (mehediSelected)
            {
                mehediRowBg = MehediDarkMode ? Color.FromArgb(44, 62, 80) : Color.FromArgb(208, 228, 248);
            }
            else if (MehediDarkMode)
            {
                mehediRowBg = shihabEven ? Color.FromArgb(47, 47, 47) : Color.FromArgb(51, 51, 51);
            }
            else
            {
                mehediRowBg = shihabEven ? Color.White : Color.FromArgb(248, 250, 252);
            }

            Color shihabGridLine = MehediDarkMode ? Color.FromArgb(68, 68, 70) : Color.FromArgb(226, 232, 240);
            using (SolidBrush mehediFill = new SolidBrush(mehediRowBg))
            using (Pen shihabGridPen = new Pen(shihabGridLine))
            {
                mehediG.FillRectangle(mehediFill, shihabCell);
                mehediG.DrawLine(shihabGridPen, shihabCell.Right - 1, shihabCell.Top, shihabCell.Right - 1, shihabCell.Bottom);
                mehediG.DrawLine(shihabGridPen, shihabCell.Left, shihabCell.Bottom - 1, shihabCell.Right, shihabCell.Bottom - 1);
            }

            string mehediText = shihabSubArgs.SubItem != null ? (shihabSubArgs.SubItem.Text ?? "") : "";
            Color shihabTextColor = MehediDarkMode ? Color.FromArgb(238, 240, 244) : Color.FromArgb(22, 28, 36);
            int mehediLeftPad = 6;

            if (CheckBoxes && shihabSubArgs.ColumnIndex == 0 && shihabSubArgs.Item != null)
            {
                Rectangle mehediChkRect = new Rectangle(shihabCell.X + 4, shihabCell.Y + (shihabCell.Height - 13) / 2, 13, 13);
                bool shihabIsChecked = shihabSubArgs.Item.Checked;
                Color mehediChkFill = shihabIsChecked
                    ? Color.FromArgb(55, 136, 135)
                    : (MehediDarkMode ? Color.FromArgb(34, 36, 40) : Color.White);
                Color shihabChkEdge = shihabIsChecked
                    ? Color.FromArgb(94, 234, 212)
                    : (MehediDarkMode ? Color.FromArgb(120, 126, 136) : Color.FromArgb(148, 163, 184));
                using (SolidBrush mehediBoxBr = new SolidBrush(mehediChkFill))
                using (Pen shihabBoxPen = new Pen(shihabChkEdge, 1.2f))
                {
                    mehediG.FillRectangle(mehediBoxBr, mehediChkRect);
                    mehediG.DrawRectangle(shihabBoxPen, mehediChkRect);
                }
                if (shihabIsChecked)
                {
                    using (Pen mehediTickPen = new Pen(Color.White, 1.8f))
                    {
                        mehediG.DrawLines(mehediTickPen, new Point[]
                        {
                            new Point(mehediChkRect.X + 2, mehediChkRect.Y + 6),
                            new Point(mehediChkRect.X + 5, mehediChkRect.Y + 9),
                            new Point(mehediChkRect.X + 10, mehediChkRect.Y + 3)
                        });
                    }
                }
                mehediLeftPad = 22;
            }

            ShihabDownloadItem shihabDlItem = shihabSubArgs.Item != null ? (shihabSubArgs.Item.Tag as ShihabDownloadItem) : null;

            if (ShihabShowFileTypeBadges && shihabSubArgs.ColumnIndex == 0)
            {
                Rectangle mehediBadgeRect = new Rectangle(shihabCell.X + mehediLeftPad - 2, shihabCell.Y + (shihabCell.Height - 16) / 2, 18, 16);
                ShihabThemePalette.DrawShihabFileTypeBadge(mehediG, mehediBadgeRect, mehediText);
                mehediLeftPad += 21;
            }
            else if (ShihabShowFileTypeBadges && shihabSubArgs.ColumnIndex == 1 && !string.IsNullOrEmpty(mehediText))
            {
                Rectangle shihabQRect = new Rectangle(shihabCell.X + 4, shihabCell.Y + (shihabCell.Height - 14) / 2, 16, 14);
                ShihabThemePalette.DrawMehediMiniQueueFolder(mehediG, shihabQRect);
                if (shihabCell.Width <= 34)
                {
                    return;
                }
                mehediLeftPad = 23;
            }
            else if (shihabDlItem != null && shihabSubArgs.ColumnIndex == 3)
            {
                ShihabDownloadStatus mehediSt = shihabDlItem.GetShihabStatusEnum();
                if (mehediSt == ShihabDownloadStatus.ShihabDownloading && shihabDlItem.ShihabTotalBytes > 0)
                {
                    double shihabPct = Math.Max(0.0, Math.Min(1.0, (double)shihabDlItem.MehediDownloadedBytes / shihabDlItem.ShihabTotalBytes));
                    Rectangle mehediBarOuter = new Rectangle(shihabCell.X + 4, shihabCell.Y + 3, Math.Max(12, shihabCell.Width - 9), Math.Max(8, shihabCell.Height - 7));
                    int shihabFillW = (int)Math.Round(mehediBarOuter.Width * shihabPct);
                    using (SolidBrush mehediTrack = new SolidBrush(Color.FromArgb(32, 36, 42)))
                    {
                        mehediG.FillRectangle(mehediTrack, mehediBarOuter);
                    }
                    if (shihabFillW > 1)
                    {
                        using (LinearGradientBrush shihabProgBr = new LinearGradientBrush(
                            mehediBarOuter,
                            Color.FromArgb(22, 163, 148),
                            Color.FromArgb(56, 189, 248),
                            0f))
                        {
                            mehediG.FillRectangle(shihabProgBr, mehediBarOuter.X, mehediBarOuter.Y, shihabFillW, mehediBarOuter.Height);
                        }
                    }
                }
                else if (mehediSt == ShihabDownloadStatus.ShihabError)
                {
                    shihabTextColor = MehediDarkMode ? Color.FromArgb(252, 165, 165) : Color.FromArgb(185, 28, 28);
                }
            }

            Rectangle shihabTxtRect = new Rectangle(
                shihabCell.X + mehediLeftPad,
                shihabCell.Y,
                Math.Max(4, shihabCell.Width - mehediLeftPad - 4),
                shihabCell.Height);

            TextFormatFlags mehediFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (ShihabShowFileTypeBadges && shihabSubArgs.ColumnIndex == 2)
            {
                mehediFlags |= TextFormatFlags.Right;
            }
            else
            {
                mehediFlags |= TextFormatFlags.Left;
            }

            TextRenderer.DrawText(mehediG, mehediText, Font, shihabTxtRect, shihabTextColor, mehediFlags);
        }
    }

    public static class ShihabThemePalette
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr shihabHwnd, int mehediAttr, ref int shihabAttrValue, int mehediAttrSize);

        public static void ApplyMehediNativeDarkFrame(Form shihabForm, bool mehediDark)
        {
            if (shihabForm == null) return;
            EventHandler shihabApply = delegate
            {
                try
                {
                    if (!shihabForm.IsHandleCreated) return;
                    int mehediUseDark = mehediDark ? 1 : 0;
                    DwmSetWindowAttribute(shihabForm.Handle, 20, ref mehediUseDark, sizeof(int));
                    DwmSetWindowAttribute(shihabForm.Handle, 19, ref mehediUseDark, sizeof(int));
                    if (mehediDark)
                    {
                        int shihabTealCaptionColorRef = (135 << 16) | (136 << 8) | 55;
                        DwmSetWindowAttribute(shihabForm.Handle, 35, ref shihabTealCaptionColorRef, sizeof(int));
                        int mehediCaptionTextColorRef = 0x00F8FAFC;
                        DwmSetWindowAttribute(shihabForm.Handle, 36, ref mehediCaptionTextColorRef, sizeof(int));
                    }
                }
                catch { }
            };
            if (shihabForm.IsHandleCreated) shihabApply(shihabForm, EventArgs.Empty);
            shihabForm.HandleCreated += shihabApply;
        }

        public static GraphicsPath CreateShihabRoundedRect(Rectangle mehediBounds, int shihabRadius)
        {
            GraphicsPath mehediPath = new GraphicsPath();
            int shihabDiam = shihabRadius * 2;
            if (shihabDiam <= 0 || mehediBounds.Width <= shihabDiam || mehediBounds.Height <= shihabDiam)
            {
                mehediPath.AddRectangle(mehediBounds);
                return mehediPath;
            }
            mehediPath.AddArc(mehediBounds.X, mehediBounds.Y, shihabDiam, shihabDiam, 180, 90);
            mehediPath.AddArc(mehediBounds.Right - shihabDiam, mehediBounds.Y, shihabDiam, shihabDiam, 270, 90);
            mehediPath.AddArc(mehediBounds.Right - shihabDiam, mehediBounds.Bottom - shihabDiam, shihabDiam, shihabDiam, 0, 90);
            mehediPath.AddArc(mehediBounds.X, mehediBounds.Bottom - shihabDiam, shihabDiam, shihabDiam, 90, 90);
            mehediPath.CloseFigure();
            return mehediPath;
        }

        public static Color GetMehediWindowBg(bool shihabDark)
        {
            return shihabDark ? Color.FromArgb(34, 34, 34) : Color.FromArgb(245, 248, 252);
        }

        public static Color GetShihabSurfaceBg(bool mehediDark)
        {
            return mehediDark ? Color.FromArgb(47, 47, 47) : Color.White;
        }

        public static Color GetMehediHeaderBg(bool shihabDark)
        {
            return shihabDark ? Color.FromArgb(26, 26, 26) : Color.FromArgb(232, 240, 250);
        }

        public static Color GetShihabPrimaryText(bool mehediDark)
        {
            return mehediDark ? Color.FromArgb(236, 242, 250) : Color.FromArgb(22, 28, 38);
        }

        public static Color GetMehediMutedText(bool shihabDark)
        {
            return shihabDark ? Color.FromArgb(148, 163, 184) : Color.FromArgb(95, 110, 128);
        }

        public static Color GetShihabAccentBlue()
        {
            return Color.FromArgb(14, 165, 233);
        }

        public static Color GetMehediAccentGreen()
        {
            return Color.FromArgb(16, 185, 129);
        }

        public static Color GetShihabAccentAmber()
        {
            return Color.FromArgb(245, 158, 11);
        }

        public static Color GetMehediAccentRed()
        {
            return Color.FromArgb(239, 68, 68);
        }

        public static Color GetShihabBorder(bool mehediDark)
        {
            return mehediDark ? Color.FromArgb(82, 82, 86) : Color.FromArgb(203, 213, 225);
        }

        public static Button CreateMehediStyledButton(string shihabText, Color mehediBg, Color shihabFg, int mehediWidth)
        {
            Button shihabBtn = new Button();
            shihabBtn.Text = shihabText;
            shihabBtn.Width = mehediWidth;
            shihabBtn.Height = 32;
            shihabBtn.FlatStyle = FlatStyle.Flat;
            shihabBtn.FlatAppearance.BorderSize = 1;
            shihabBtn.FlatAppearance.BorderColor = Color.FromArgb(70, 255, 255, 255);
            shihabBtn.BackColor = mehediBg;
            shihabBtn.ForeColor = shihabFg;
            shihabBtn.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Regular);
            shihabBtn.Cursor = Cursors.Hand;
            return shihabBtn;
        }

        private static Icon _shihabCachedOfficialIcon;
        private static Bitmap _mehediCachedOfficialPng;

        public static Icon CreateShihabAppIcon()
        {
            if (_shihabCachedOfficialIcon != null)
            {
                return _shihabCachedOfficialIcon;
            }
            try
            {
                Stream mehediIcoStream = typeof(ShihabThemePalette).Assembly.GetManifestResourceStream("ShihabMehediIDM.mdm_icon.ico");
                if (mehediIcoStream != null)
                {
                    using (mehediIcoStream)
                    {
                        _shihabCachedOfficialIcon = new Icon(mehediIcoStream);
                        return _shihabCachedOfficialIcon;
                    }
                }
            }
            catch { }

            try
            {
                Icon shihabExeIco = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
                if (shihabExeIco != null)
                {
                    _shihabCachedOfficialIcon = shihabExeIco;
                    return _shihabCachedOfficialIcon;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Bitmap CreateMehediLogoBitmap(int shihabPixelSize)
        {
            if (_mehediCachedOfficialPng == null)
            {
                try
                {
                    Stream shihabPngStream = typeof(ShihabThemePalette).Assembly.GetManifestResourceStream("ShihabMehediIDM.mdm_icon.png");
                    if (shihabPngStream != null)
                    {
                        using (shihabPngStream)
                        using (Image mehediLoaded = Image.FromStream(shihabPngStream))
                        {
                            _mehediCachedOfficialPng = new Bitmap(mehediLoaded);
                        }
                    }
                }
                catch { }
            }

            int mehediSz = Math.Max(16, shihabPixelSize);
            Bitmap shihabScaled = new Bitmap(mehediSz, mehediSz);
            using (Graphics mehediG = Graphics.FromImage(shihabScaled))
            {
                mehediG.SmoothingMode = SmoothingMode.HighQuality;
                mehediG.InterpolationMode = InterpolationMode.HighQualityBicubic;
                mehediG.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (_mehediCachedOfficialPng != null)
                {
                    mehediG.DrawImage(_mehediCachedOfficialPng, new Rectangle(0, 0, mehediSz, mehediSz));
                }
                else
                {
                    Icon shihabFallback = CreateShihabAppIcon();
                    mehediG.DrawIcon(shihabFallback, new Rectangle(0, 0, mehediSz, mehediSz));
                }
            }
            return shihabScaled;
        }

        public static Bitmap DrawMehediLargeToolbarIcon(string shihabKind, bool mehediActive)
        {
            Bitmap shihabBmp = new Bitmap(36, 36);
            using (Graphics mehediG = Graphics.FromImage(shihabBmp))
            {
                mehediG.SmoothingMode = SmoothingMode.AntiAlias;
                mehediG.PixelOffsetMode = PixelOffsetMode.HighQuality;

                if (shihabKind == "add")
                {
                    using (Pen shihabMintPen = new Pen(Color.FromArgb(108, 212, 184), 2.2f))
                    {
                        shihabMintPen.LineJoin = LineJoin.Round;
                        PointF[] mehediCross = new PointF[]
                        {
                            new PointF(15, 5), new PointF(21, 5),
                            new PointF(21, 15), new PointF(31, 15),
                            new PointF(31, 21), new PointF(21, 21),
                            new PointF(21, 31), new PointF(15, 31),
                            new PointF(15, 21), new PointF(5, 21),
                            new PointF(5, 15), new PointF(15, 15)
                        };
                        mehediG.DrawPolygon(shihabMintPen, mehediCross);
                    }
                }
                else if (shihabKind == "resume")
                {
                    Color shihabCol = mehediActive ? Color.FromArgb(110, 231, 183) : Color.FromArgb(156, 160, 166);
                    using (Pen mehediPen = new Pen(shihabCol, 2.2f))
                    {
                        mehediPen.StartCap = LineCap.Round;
                        mehediPen.EndCap = LineCap.Round;
                        mehediG.DrawEllipse(mehediPen, 4, 4, 28, 28);
                        mehediG.DrawLine(mehediPen, 18, 10, 18, 24);
                        mehediG.DrawLine(mehediPen, 12, 18, 18, 24);
                        mehediG.DrawLine(mehediPen, 24, 18, 18, 24);
                    }
                }
                else if (shihabKind == "stop")
                {
                    Color mehediCol = mehediActive ? Color.FromArgb(252, 142, 142) : Color.FromArgb(156, 160, 166);
                    using (Pen shihabPen = new Pen(mehediCol, 2.2f))
                    using (GraphicsPath mehediInnerSq = CreateShihabRoundedRect(new Rectangle(13, 13, 10, 10), 2))
                    {
                        mehediG.DrawEllipse(shihabPen, 4, 4, 28, 28);
                        mehediG.DrawPath(shihabPen, mehediInnerSq);
                    }
                }
                else if (shihabKind == "stopall")
                {
                    Color shihabCol = mehediActive ? Color.FromArgb(251, 191, 114) : Color.FromArgb(162, 166, 172);
                    using (Pen mehediPen = new Pen(shihabCol, 2.1f))
                    {
                        mehediPen.StartCap = LineCap.Round;
                        mehediPen.EndCap = LineCap.Round;
                        mehediPen.LineJoin = LineJoin.Round;
                        mehediG.DrawArc(mehediPen, 9, 16, 19, 15, 0, 180);
                        mehediG.DrawLine(mehediPen, 9, 23, 9, 15);
                        mehediG.DrawArc(mehediPen, 9, 13, 4, 5, 180, 180);
                        mehediG.DrawLine(mehediPen, 13, 15, 13, 8);
                        mehediG.DrawArc(mehediPen, 13, 6, 4, 5, 180, 180);
                        mehediG.DrawLine(mehediPen, 17, 8, 17, 6);
                        mehediG.DrawArc(mehediPen, 17, 5, 4, 5, 180, 180);
                        mehediG.DrawLine(mehediPen, 21, 7, 21, 8);
                        mehediG.DrawArc(mehediPen, 21, 6, 4, 5, 180, 180);
                        mehediG.DrawLine(mehediPen, 25, 9, 25, 11);
                        mehediG.DrawArc(mehediPen, 24, 9, 4, 5, 180, 180);
                        mehediG.DrawLine(mehediPen, 28, 11, 28, 23);
                    }
                }
                else if (shihabKind == "delete")
                {
                    Color mehediCol = mehediActive ? Color.FromArgb(244, 143, 177) : Color.FromArgb(162, 166, 172);
                    DrawShihabTrashCanIcon(mehediG, mehediCol, false);
                }
                else if (shihabKind == "clean")
                {
                    DrawShihabTrashCanIcon(mehediG, Color.FromArgb(229, 107, 140), true);
                }
                else if (shihabKind == "options")
                {
                    Color shihabPurple = Color.FromArgb(157, 132, 232);
                    using (Pen mehediGearPen = new Pen(shihabPurple, 2.2f))
                    {
                        mehediGearPen.LineJoin = LineJoin.Round;
                        List<PointF> shihabGearPts = new List<PointF>();
                        int mehediTeeth = 8;
                        for (int shihabI = 0; shihabI < mehediTeeth * 4; shihabI++)
                        {
                            double mehediAng = (Math.PI * 2.0 * shihabI) / (mehediTeeth * 4);
                            float shihabRad = (shihabI % 4 == 0 || shihabI % 4 == 1) ? 14.2f : 10.6f;
                            shihabGearPts.Add(new PointF(18f + (float)(Math.Cos(mehediAng) * shihabRad), 18f + (float)(Math.Sin(mehediAng) * shihabRad)));
                        }
                        mehediG.DrawPolygon(mehediGearPen, shihabGearPts.ToArray());
                        mehediG.DrawEllipse(mehediGearPen, 13, 13, 10, 10);
                    }
                }
                else if (shihabKind == "scheduler")
                {
                    using (LinearGradientBrush mehediClockGrad = new LinearGradientBrush(
                        new Rectangle(4, 4, 28, 28),
                        Color.FromArgb(216, 132, 176),
                        Color.FromArgb(142, 168, 218),
                        45f))
                    using (Pen shihabClockPen = new Pen(mehediClockGrad, 2.2f))
                    {
                        shihabClockPen.StartCap = LineCap.Round;
                        shihabClockPen.EndCap = LineCap.Round;
                        mehediG.DrawEllipse(shihabClockPen, 6, 7, 24, 24);
                        mehediG.DrawArc(shihabClockPen, 5, 4, 9, 8, 195, 150);
                        mehediG.DrawArc(shihabClockPen, 22, 4, 9, 8, 195, 150);
                        mehediG.DrawLine(shihabClockPen, 18, 12, 18, 19);
                        mehediG.DrawLine(shihabClockPen, 18, 19, 23, 22);
                    }
                }
                else if (shihabKind == "startq")
                {
                    Color shihabSky = Color.FromArgb(116, 185, 216);
                    using (Pen mehediPen = new Pen(shihabSky, 2.1f))
                    using (GraphicsPath shihabBackCard = CreateShihabRoundedRect(new Rectangle(6, 5, 16, 18), 3))
                    using (GraphicsPath mehediFrontCard = CreateShihabRoundedRect(new Rectangle(11, 10, 16, 18), 3))
                    {
                        mehediPen.StartCap = LineCap.Round;
                        mehediPen.EndCap = LineCap.Round;
                        mehediG.DrawPath(mehediPen, shihabBackCard);
                        using (SolidBrush shihabMask = new SolidBrush(Color.FromArgb(54, 54, 54)))
                        {
                            mehediG.FillPath(shihabMask, mehediFrontCard);
                        }
                        mehediG.DrawPath(mehediPen, mehediFrontCard);
                        mehediG.DrawLine(mehediPen, 25, 18, 25, 31);
                        mehediG.DrawLine(mehediPen, 21, 27, 25, 31);
                        mehediG.DrawLine(mehediPen, 29, 27, 25, 31);
                    }
                }
                else if (shihabKind == "stopq")
                {
                    using (Pen shihabBluePen = new Pen(Color.FromArgb(140, 166, 219), 2.1f))
                    using (Pen mehediPinkPen = new Pen(Color.FromArgb(216, 124, 168), 2.1f))
                    using (GraphicsPath shihabCard1 = CreateShihabRoundedRect(new Rectangle(6, 5, 15, 18), 3))
                    using (GraphicsPath mehediCard2 = CreateShihabRoundedRect(new Rectangle(11, 10, 15, 18), 3))
                    using (GraphicsPath shihabStopSq = CreateShihabRoundedRect(new Rectangle(21, 21, 9, 9), 2))
                    {
                        mehediG.DrawPath(shihabBluePen, shihabCard1);
                        using (SolidBrush mehediMask = new SolidBrush(Color.FromArgb(54, 54, 54)))
                        {
                            mehediG.FillPath(mehediMask, mehediCard2);
                            mehediG.DrawPath(mehediPinkPen, mehediCard2);
                            mehediG.FillPath(mehediMask, shihabStopSq);
                            mehediG.DrawPath(mehediPinkPen, shihabStopSq);
                        }
                    }
                }
                else if (shihabKind == "grabber")
                {
                    Color mehediIndigo = Color.FromArgb(130, 144, 226);
                    using (Pen shihabPen = new Pen(mehediIndigo, 2.1f))
                    {
                        mehediG.DrawEllipse(shihabPen, 5, 5, 26, 26);
                        mehediG.DrawEllipse(shihabPen, 12, 5, 12, 26);
                        mehediG.DrawLine(shihabPen, 5, 18, 31, 18);
                        mehediG.DrawArc(shihabPen, 8, 9, 20, 8, 10, 160);
                        mehediG.DrawArc(shihabPen, 8, 19, 20, 8, 190, 160);
                    }
                }
            }
            return shihabBmp;
        }

        private static void DrawShihabTrashCanIcon(Graphics mehediG, Color shihabColor, bool mehediWithCheckmark)
        {
            using (Pen shihabPen = new Pen(shihabColor, 2.1f))
            {
                shihabPen.StartCap = LineCap.Round;
                shihabPen.EndCap = LineCap.Round;
                shihabPen.LineJoin = LineJoin.Round;
                mehediG.DrawLine(shihabPen, 7, 10, 29, 10);
                mehediG.DrawArc(shihabPen, 14, 5, 8, 6, 180, 180);
                PointF[] mehediBody = new PointF[]
                {
                    new PointF(9, 10),
                    new PointF(11, 30),
                    new PointF(25, 30),
                    new PointF(27, 10)
                };
                mehediG.DrawLines(shihabPen, mehediBody);
                if (mehediWithCheckmark)
                {
                    mehediG.DrawLines(shihabPen, new PointF[]
                    {
                        new PointF(13.5f, 20f),
                        new PointF(17f, 23.8f),
                        new PointF(23f, 16.5f)
                    });
                }
                else
                {
                    mehediG.DrawLine(shihabPen, 15, 15, 15, 25);
                    mehediG.DrawLine(shihabPen, 18, 15, 18, 25);
                    mehediG.DrawLine(shihabPen, 21, 15, 21, 25);
                }
            }
        }

        public static Bitmap DrawMehediToolbarGlyph(string shihabKind, Color mehediTint)
        {
            return DrawMehediLargeToolbarIcon(shihabKind, true);
        }

        public static void DrawMehediMiniQueueFolder(Graphics shihabG, Rectangle mehediRect)
        {
            using (SolidBrush shihabGold = new SolidBrush(Color.FromArgb(234, 186, 88)))
            using (Pen mehediEdge = new Pen(Color.FromArgb(180, 134, 42), 1f))
            {
                shihabG.FillRectangle(shihabGold, mehediRect.X + 1, mehediRect.Y + 3, mehediRect.Width - 3, mehediRect.Height - 4);
                shihabG.FillRectangle(shihabGold, mehediRect.X + 1, mehediRect.Y + 1, 6, 3);
                shihabG.DrawRectangle(mehediEdge, mehediRect.X + 1, mehediRect.Y + 3, mehediRect.Width - 3, mehediRect.Height - 4);
            }
        }

        public static void DrawShihabFileTypeBadge(Graphics mehediG, Rectangle shihabRect, string mehediFileName)
        {
            string shihabExt = "";
            if (!string.IsNullOrEmpty(mehediFileName))
            {
                try { shihabExt = Path.GetExtension(mehediFileName).TrimStart('.').ToUpperInvariant(); } catch { }
            }

            if (shihabExt == "ZIP" || shihabExt == "RAR" || shihabExt == "7Z" || shihabExt == "GZ" || shihabExt == "TAR")
            {
                using (SolidBrush shihabB1 = new SolidBrush(Color.FromArgb(186, 85, 211)))
                using (SolidBrush mehediB2 = new SolidBrush(Color.FromArgb(59, 130, 246)))
                using (SolidBrush shihabB3 = new SolidBrush(Color.FromArgb(34, 197, 94)))
                using (SolidBrush mehediStrap = new SolidBrush(Color.FromArgb(245, 190, 66)))
                {
                    mehediG.FillRectangle(shihabB1, shihabRect.X + 1, shihabRect.Y + 1, 15, 4);
                    mehediG.FillRectangle(mehediB2, shihabRect.X + 1, shihabRect.Y + 6, 15, 4);
                    mehediG.FillRectangle(shihabB3, shihabRect.X + 1, shihabRect.Y + 11, 15, 4);
                    mehediG.FillRectangle(mehediStrap, shihabRect.X + 7, shihabRect.Y + 1, 3, 14);
                }
                return;
            }

            if (shihabExt == "EXE" || shihabExt == "MSI" || shihabExt == "APK")
            {
                using (SolidBrush shihabBody = new SolidBrush(Color.FromArgb(226, 232, 240)))
                using (SolidBrush mehediHdr = new SolidBrush(Color.FromArgb(37, 99, 235)))
                using (SolidBrush shihabInner = new SolidBrush(Color.FromArgb(59, 130, 246)))
                {
                    mehediG.FillRectangle(shihabBody, shihabRect.X + 1, shihabRect.Y + 2, 15, 12);
                    mehediG.FillRectangle(mehediHdr, shihabRect.X + 1, shihabRect.Y + 2, 15, 4);
                    mehediG.FillRectangle(shihabInner, shihabRect.X + 4, shihabRect.Y + 8, 9, 4);
                }
                return;
            }

            bool mehediIsMkvOrTs = shihabExt == "MKV" || shihabExt == "TS" || shihabExt == "M3U8" || shihabExt == "WEBM";
            bool shihabIsVideo = mehediIsMkvOrTs || shihabExt == "MP4" || shihabExt == "MOV" || shihabExt == "AVI" || shihabExt == "FLV" || shihabExt == "M4V";
            if (shihabIsVideo)
            {
                Color mehediBadgeCol = mehediIsMkvOrTs ? Color.FromArgb(14, 148, 162) : Color.FromArgb(88, 76, 212);
                string shihabMiniTag = shihabExt.Length > 3 ? shihabExt.Substring(0, 3) : shihabExt;
                using (SolidBrush mehediBg = new SolidBrush(mehediBadgeCol))
                using (SolidBrush shihabWhite = new SolidBrush(Color.White))
                using (Font mehediMicroFont = new Font("Segoe UI", 4.8f, FontStyle.Bold))
                {
                    mehediG.FillRectangle(mehediBg, shihabRect.X + 1, shihabRect.Y, 16, 16);
                    Point[] shihabPlayTri = new Point[]
                    {
                        new Point(shihabRect.X + 6, shihabRect.Y + 2),
                        new Point(shihabRect.X + 13, shihabRect.Y + 5),
                        new Point(shihabRect.X + 6, shihabRect.Y + 8)
                    };
                    mehediG.FillPolygon(shihabWhite, shihabPlayTri);
                    mehediG.DrawString(shihabMiniTag, mehediMicroFont, shihabWhite, shihabRect.X + 1, shihabRect.Y + 8.5f);
                }
                return;
            }

            if (shihabExt == "MP3" || shihabExt == "M4A" || shihabExt == "WAV" || shihabExt == "FLAC" || shihabExt == "AAC")
            {
                using (SolidBrush shihabBg = new SolidBrush(Color.FromArgb(14, 165, 233)))
                using (Pen mehediNotePen = new Pen(Color.White, 1.6f))
                {
                    mehediG.FillRectangle(shihabBg, shihabRect.X + 1, shihabRect.Y + 1, 15, 14);
                    mehediG.DrawLine(mehediNotePen, shihabRect.X + 7, shihabRect.Y + 11, shihabRect.X + 7, shihabRect.Y + 4);
                    mehediG.DrawLine(mehediNotePen, shihabRect.X + 7, shihabRect.Y + 4, shihabRect.X + 13, shihabRect.Y + 3);
                    mehediG.DrawLine(mehediNotePen, shihabRect.X + 13, shihabRect.Y + 3, shihabRect.X + 13, shihabRect.Y + 10);
                }
                return;
            }

            using (SolidBrush shihabPage = new SolidBrush(Color.FromArgb(226, 232, 240)))
            using (Pen mehediLinePen = new Pen(Color.FromArgb(100, 116, 139), 1f))
            {
                mehediG.FillRectangle(shihabPage, shihabRect.X + 3, shihabRect.Y + 1, 12, 14);
                mehediG.DrawLine(mehediLinePen, shihabRect.X + 5, shihabRect.Y + 5, shihabRect.X + 12, shihabRect.Y + 5);
                mehediG.DrawLine(mehediLinePen, shihabRect.X + 5, shihabRect.Y + 8, shihabRect.X + 12, shihabRect.Y + 8);
                mehediG.DrawLine(mehediLinePen, shihabRect.X + 5, shihabRect.Y + 11, shihabRect.X + 10, shihabRect.Y + 11);
            }
        }

        public static void DrawMehediTreeCategoryIcon(Graphics shihabG, Rectangle mehediRect, string shihabTag, string mehediLabel)
        {
            string shihabUpper = (shihabTag ?? "").ToUpperInvariant();
            string mehediNameUpper = (mehediLabel ?? "").ToUpperInvariant();

            if (mehediNameUpper.Contains("COMPRESSED"))
            {
                using (SolidBrush shihabGold = new SolidBrush(Color.FromArgb(234, 188, 96)))
                using (Pen mehediZipPen = new Pen(Color.FromArgb(120, 82, 20), 1.3f))
                {
                    shihabG.FillRectangle(shihabGold, mehediRect.X + 2, mehediRect.Y + 1, 12, 14);
                    shihabG.DrawLine(mehediZipPen, mehediRect.X + 8, mehediRect.Y + 2, mehediRect.X + 8, mehediRect.Y + 13);
                }
                return;
            }
            if (mehediNameUpper.Contains("DOCUMENTS"))
            {
                using (SolidBrush shihabPage = new SolidBrush(Color.FromArgb(230, 235, 242)))
                using (Pen mehediEdge = new Pen(Color.FromArgb(148, 163, 184), 1f))
                {
                    shihabG.FillRectangle(shihabPage, mehediRect.X + 3, mehediRect.Y + 1, 10, 14);
                    shihabG.DrawRectangle(mehediEdge, mehediRect.X + 3, mehediRect.Y + 1, 10, 14);
                }
                return;
            }
            if (mehediNameUpper.Contains("MUSIC"))
            {
                using (Pen shihabNote = new Pen(Color.FromArgb(96, 165, 250), 1.8f))
                using (SolidBrush mehediDot = new SolidBrush(Color.FromArgb(96, 165, 250)))
                {
                    shihabG.DrawLine(shihabNote, mehediRect.X + 6, mehediRect.Y + 12, mehediRect.X + 6, mehediRect.Y + 3);
                    shihabG.DrawLine(shihabNote, mehediRect.X + 6, mehediRect.Y + 3, mehediRect.X + 13, mehediRect.Y + 2);
                    shihabG.DrawLine(shihabNote, mehediRect.X + 13, mehediRect.Y + 2, mehediRect.X + 13, mehediRect.Y + 11);
                    shihabG.FillEllipse(mehediDot, mehediRect.X + 3, mehediRect.Y + 10, 5, 4);
                    shihabG.FillEllipse(mehediDot, mehediRect.X + 10, mehediRect.Y + 9, 5, 4);
                }
                return;
            }
            if (mehediNameUpper.Contains("PROGRAMS"))
            {
                DrawShihabFileTypeBadge(shihabG, mehediRect, "app.exe");
                return;
            }
            if (mehediNameUpper.Contains("VIDEO"))
            {
                using (SolidBrush shihabFilm = new SolidBrush(Color.FromArgb(56, 189, 248)))
                using (SolidBrush mehediHole = new SolidBrush(Color.FromArgb(24, 28, 36)))
                {
                    shihabG.FillRectangle(shihabFilm, mehediRect.X + 2, mehediRect.Y + 2, 12, 12);
                    shihabG.FillRectangle(mehediHole, mehediRect.X + 5, mehediRect.Y + 4, 6, 8);
                }
                return;
            }
            if (shihabUpper == "GRABBER_PROJECTS")
            {
                using (SolidBrush shihabPage = new SolidBrush(Color.FromArgb(226, 232, 240)))
                using (SolidBrush mehediGlobe = new SolidBrush(Color.FromArgb(14, 165, 233)))
                {
                    shihabG.FillRectangle(shihabPage, mehediRect.X + 3, mehediRect.Y + 1, 11, 14);
                    shihabG.FillEllipse(mehediGlobe, mehediRect.X + 4, mehediRect.Y + 5, 8, 8);
                }
                return;
            }

            DrawMehediMiniQueueFolder(shihabG, mehediRect);
            if (shihabUpper == "UNFINISHED")
            {
                using (SolidBrush shihabGreen = new SolidBrush(Color.FromArgb(34, 197, 94)))
                {
                    shihabG.FillEllipse(shihabGreen, mehediRect.X + 9, mehediRect.Y + 7, 6, 6);
                }
            }
            else if (shihabUpper == "FINISHED")
            {
                using (SolidBrush mehediBlue = new SolidBrush(Color.FromArgb(59, 130, 246)))
                {
                    shihabG.FillEllipse(mehediBlue, mehediRect.X + 9, mehediRect.Y + 7, 6, 6);
                }
            }
        }
    }

    public class MehediAddUrlAuthDialog : Form
    {
        public string ShihabEnteredUrl { get; private set; }
        public string MehediEnteredUser { get; private set; }
        public string ShihabEnteredPassword { get; private set; }
        public string MehediEnteredReferer { get; private set; }

        private TextBox _shihabUrlBox;
        private TextBox _mehediRefererBox;
        private CheckBox _shihabAuthCheck;
        private TextBox _mehediUserBox;
        private TextBox _shihabPassBox;

        public MehediAddUrlAuthDialog(bool shihabDark, string mehediInitialUrl)
        {
            Text = "Enter new address to download — MHS Download Manager";
            Size = new Size(580, 275);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ShihabThemePalette.GetMehediWindowBg(shihabDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(shihabDark);
            Font = new Font("Segoe UI", 9f);
            Icon = ShihabThemePalette.CreateShihabAppIcon();
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, shihabDark);

            Label shihabAddrLbl = new Label { Text = "Address (URL):", Location = new Point(16, 18), AutoSize = true };
            _shihabUrlBox = new TextBox
            {
                Location = new Point(115, 15),
                Width = 430,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = mehediInitialUrl ?? ""
            };

            if (string.IsNullOrWhiteSpace(_shihabUrlBox.Text))
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string mehediClip = Clipboard.GetText().Trim();
                        if (mehediClip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            mehediClip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            _shihabUrlBox.Text = mehediClip;
                        }
                    }
                }
                catch { }
            }

            Label mehediRefLbl = new Label { Text = "Referer (opt.):", Location = new Point(16, 54), AutoSize = true };
            _mehediRefererBox = new TextBox
            {
                Location = new Point(115, 51),
                Width = 430,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor
            };

            _shihabAuthCheck = new CheckBox
            {
                Text = "Use authorization (HTTP Basic Login)",
                Location = new Point(115, 86),
                AutoSize = true
            };

            Label shihabUserLbl = new Label { Text = "Login:", Location = new Point(115, 118), AutoSize = true };
            _mehediUserBox = new TextBox
            {
                Location = new Point(170, 115),
                Width = 145,
                Enabled = false,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor
            };

            Label mehediPassLbl = new Label { Text = "Password:", Location = new Point(328, 118), AutoSize = true };
            _shihabPassBox = new TextBox
            {
                Location = new Point(398, 115),
                Width = 147,
                Enabled = false,
                UseSystemPasswordChar = true,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor
            };

            _shihabAuthCheck.CheckedChanged += delegate
            {
                _mehediUserBox.Enabled = _shihabAuthCheck.Checked;
                _shihabPassBox.Enabled = _shihabAuthCheck.Checked;
            };

            Button shihabOkBtn = ShihabThemePalette.CreateMehediStyledButton("OK", ShihabThemePalette.GetShihabAccentBlue(), Color.White, 105);
            shihabOkBtn.Location = new Point(328, 178);
            shihabOkBtn.Click += delegate
            {
                string mehediU = _shihabUrlBox.Text.Trim();
                Uri shihabParsed;
                if (!Uri.TryCreate(mehediU, UriKind.Absolute, out shihabParsed) ||
                    (shihabParsed.Scheme != "http" && shihabParsed.Scheme != "https"))
                {
                    MessageBox.Show(this, "Invalid URL entered. Please enter a valid http:// or https:// address.", "MHS Download Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ShihabEnteredUrl = mehediU;
                MehediEnteredReferer = _mehediRefererBox.Text.Trim();
                MehediEnteredUser = _shihabAuthCheck.Checked ? _mehediUserBox.Text.Trim() : "";
                ShihabEnteredPassword = _shihabAuthCheck.Checked ? _shihabPassBox.Text : "";
                DialogResult = DialogResult.OK;
                Close();
            };

            Button mehediCancelBtn = ShihabThemePalette.CreateMehediStyledButton("Cancel", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 105);
            mehediCancelBtn.Location = new Point(440, 178);
            mehediCancelBtn.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            AcceptButton = shihabOkBtn;
            CancelButton = mehediCancelBtn;
            Controls.AddRange(new Control[]
            {
                shihabAddrLbl, _shihabUrlBox, mehediRefLbl, _mehediRefererBox,
                _shihabAuthCheck, shihabUserLbl, _mehediUserBox, mehediPassLbl, _shihabPassBox,
                shihabOkBtn, mehediCancelBtn
            });
        }
    }

    public class MehediDownloadFileInfoForm : Form
    {
        public bool ShihabStartImmediatelySelected { get; private set; }
        public ShihabDownloadItem MehediConfiguredItem { get; private set; }

        private readonly MehediAppConfiguration _shihabConfig;
        private readonly MehediDynamicSegmentEngine _mehediEngine;
        private TextBox _shihabUrlBox;
        private ComboBox _mehediCategoryCombo;
        private TextBox _shihabSaveAsBox;
        private TextBox _mehediDescBox;
        private ComboBox _shihabQueueCombo;
        private CheckBox _mehediRememberPathCheck;
        private Label _shihabSizeBadgeLabel;

        public MehediDownloadFileInfoForm(
            ShihabDownloadItem shihabDraftItem,
            MehediAppConfiguration mehediConfig,
            MehediDynamicSegmentEngine shihabEngine)
        {
            MehediConfiguredItem = shihabDraftItem;
            _shihabConfig = mehediConfig;
            _mehediEngine = shihabEngine;
            bool shihabDark = mehediConfig.ShihabDarkMode;

            Text = "Download File Info — MHS Download Manager";
            Size = new Size(640, 365);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = ShihabThemePalette.GetMehediWindowBg(shihabDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(shihabDark);
            Font = new Font("Segoe UI", 9f);
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, shihabDark);
            Icon = ShihabThemePalette.CreateShihabAppIcon();

            Label mehediUrlLbl = new Label { Text = "URL:", Location = new Point(18, 18), AutoSize = true };
            _shihabUrlBox = new TextBox
            {
                Location = new Point(110, 15),
                Width = 495,
                ReadOnly = true,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = shihabDraftItem.MehediUrl
            };

            Label shihabCatLbl = new Label { Text = "Category:", Location = new Point(18, 54), AutoSize = true };
            _mehediCategoryCombo = new ComboBox
            {
                Location = new Point(110, 51),
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor
            };

            ShihabCategoryRule mehediAutoCat = _shihabConfig.ResolveMehediCategoryForFile(shihabDraftItem.ShihabFileName);
            int shihabSelIdx = 0;
            for (int mehediI = 0; mehediI < _shihabConfig.ShihabCategories.Count; mehediI++)
            {
                string shihabCName = _shihabConfig.ShihabCategories[mehediI].ShihabName;
                _mehediCategoryCombo.Items.Add(shihabCName);
                if (string.Equals(shihabCName, mehediAutoCat.ShihabName, StringComparison.OrdinalIgnoreCase))
                {
                    shihabSelIdx = mehediI;
                }
            }
            if (_mehediCategoryCombo.Items.Count > 0)
            {
                _mehediCategoryCombo.SelectedIndex = shihabSelIdx;
            }

            long mehediInitDisplayBytes = shihabDraftItem.ShihabTotalBytes > 0
                ? (shihabDraftItem.ShihabTotalBytes + (shihabDraftItem.ShihabAudioCompanionBytes > 0 ? shihabDraftItem.ShihabAudioCompanionBytes : 0L))
                : -1L;
            string shihabInitMuxSuffix = !string.IsNullOrWhiteSpace(shihabDraftItem.MehediAudioCompanionUrl) ? "  [Video + Audio]" : "";

            _shihabSizeBadgeLabel = new Label
            {
                Text = mehediInitDisplayBytes > 0
                    ? ("Size: " + ShihabFormatHelpers.FormatMehediBytes(mehediInitDisplayBytes) + shihabInitMuxSuffix)
                    : "Probing server size & resume...",
                Location = new Point(345, 54),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9f),
                ForeColor = ShihabThemePalette.GetShihabAccentBlue()
            };

            Label mehediSaveLbl = new Label { Text = "Save As:", Location = new Point(18, 92), AutoSize = true };
            string shihabInitFolder = string.IsNullOrWhiteSpace(shihabDraftItem.MehediSaveDirectory)
                ? mehediAutoCat.ShihabDefaultFolder
                : shihabDraftItem.MehediSaveDirectory;
            string mehediUniqueFile = ShihabFormatHelpers.EnsureShihabUniqueFilePath(shihabInitFolder, shihabDraftItem.ShihabFileName);

            _shihabSaveAsBox = new TextBox
            {
                Location = new Point(110, 89),
                Width = 405,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = Path.Combine(shihabInitFolder, mehediUniqueFile)
            };

            Button shihabBrowseBtn = ShihabThemePalette.CreateMehediStyledButton("Browse...", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 82);
            shihabBrowseBtn.Height = 26;
            shihabBrowseBtn.Location = new Point(523, 87);
            shihabBrowseBtn.Click += delegate
            {
                using (SaveFileDialog mehediSfd = new SaveFileDialog())
                {
                    mehediSfd.FileName = Path.GetFileName(_shihabSaveAsBox.Text);
                    string shihabCurDir = Path.GetDirectoryName(_shihabSaveAsBox.Text);
                    if (Directory.Exists(shihabCurDir)) mehediSfd.InitialDirectory = shihabCurDir;
                    mehediSfd.Filter = "All Files (*.*)|*.*";
                    if (mehediSfd.ShowDialog(this) == DialogResult.OK)
                    {
                        _shihabSaveAsBox.Text = mehediSfd.FileName;
                    }
                }
            };

            _mehediRememberPathCheck = new CheckBox
            {
                Text = "Change folder for \"" + mehediAutoCat.ShihabName + "\" category on last selected",
                Location = new Point(110, 122),
                AutoSize = true,
                Checked = mehediAutoCat.MehediRememberLastSavePath
            };

            _mehediCategoryCombo.SelectedIndexChanged += delegate
            {
                int mehediIdx = _mehediCategoryCombo.SelectedIndex;
                if (mehediIdx >= 0 && mehediIdx < _shihabConfig.ShihabCategories.Count)
                {
                    ShihabCategoryRule shihabChosenCat = _shihabConfig.ShihabCategories[mehediIdx];
                    string shihabNewDir = shihabChosenCat.ShihabDefaultFolder;
                    string mehediCurLeaf = Path.GetFileName(_shihabSaveAsBox.Text);
                    _shihabSaveAsBox.Text = Path.Combine(shihabNewDir, mehediCurLeaf);
                    _mehediRememberPathCheck.Text = "Change folder for \"" + shihabChosenCat.ShihabName + "\" category on last selected";
                    _mehediRememberPathCheck.Checked = shihabChosenCat.MehediRememberLastSavePath;
                }
            };

            Label shihabDescLbl = new Label { Text = "Description:", Location = new Point(18, 156), AutoSize = true };
            _mehediDescBox = new TextBox
            {
                Location = new Point(110, 153),
                Width = 495,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = shihabDraftItem.ShihabDescription ?? ""
            };

            Label mehediQueueLbl = new Label { Text = "Queue (Later):", Location = new Point(18, 194), AutoSize = true };
            _shihabQueueCombo = new ComboBox
            {
                Location = new Point(110, 191),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor
            };
            for (int shihabQ = 0; shihabQ < _shihabConfig.MehediQueues.Count; shihabQ++)
            {
                _shihabQueueCombo.Items.Add(_shihabConfig.MehediQueues[shihabQ].ShihabQueueName);
            }
            if (_shihabQueueCombo.Items.Count > 0) _shihabQueueCombo.SelectedIndex = 0;

            Button mehediLaterBtn = ShihabThemePalette.CreateMehediStyledButton("Download Later", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 138);
            mehediLaterBtn.Location = new Point(180, 265);
            mehediLaterBtn.Click += delegate { CommitShihabChoicesAndClose(false); };

            Button shihabStartBtn = ShihabThemePalette.CreateMehediStyledButton("Start Download", ShihabThemePalette.GetMehediAccentGreen(), Color.White, 155);
            shihabStartBtn.Location = new Point(328, 265);
            shihabStartBtn.Click += delegate { CommitShihabChoicesAndClose(true); };

            Button mehediCancelBtn = ShihabThemePalette.CreateMehediStyledButton("Cancel", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 110);
            mehediCancelBtn.Location = new Point(495, 265);
            mehediCancelBtn.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            AcceptButton = shihabStartBtn;
            CancelButton = mehediCancelBtn;

            Controls.AddRange(new Control[]
            {
                mehediUrlLbl, _shihabUrlBox, shihabCatLbl, _mehediCategoryCombo, _shihabSizeBadgeLabel,
                mehediSaveLbl, _shihabSaveAsBox, shihabBrowseBtn, _mehediRememberPathCheck,
                shihabDescLbl, _mehediDescBox, mehediQueueLbl, _shihabQueueCombo,
                mehediLaterBtn, shihabStartBtn, mehediCancelBtn
            });

            Shown += delegate
            {
                try
                {
                    TopMost = true;
                    Activate();
                    BringToFront();
                }
                catch { }
                BeginBackgroundShihabProbe();
            };
        }

        private void BeginBackgroundShihabProbe()
        {
            Task.Run(delegate
            {
                ShihabProbeMetadata mehediProbe = _mehediEngine.ProbeMehediUrl(MehediConfiguredItem.MehediUrl, MehediConfiguredItem);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed) return;
                        if (mehediProbe.MehediSuccess)
                        {
                            if (mehediProbe.ShihabContentLength > 0)
                            {
                                MehediConfiguredItem.ShihabTotalBytes = mehediProbe.ShihabContentLength;
                            }
                            MehediConfiguredItem.ShihabResumeSupported = mehediProbe.MehediAcceptsRanges;
                            long mehediCombinedBytes = MehediConfiguredItem.ShihabTotalBytes > 0
                                ? (MehediConfiguredItem.ShihabTotalBytes + (MehediConfiguredItem.ShihabAudioCompanionBytes > 0 ? MehediConfiguredItem.ShihabAudioCompanionBytes : 0L))
                                : -1L;
                            string shihabMuxTag = !string.IsNullOrWhiteSpace(MehediConfiguredItem.MehediAudioCompanionUrl) ? " · Video+Audio" : "";
                            string shihabSizeStr = mehediCombinedBytes > 0
                                ? ShihabFormatHelpers.FormatMehediBytes(mehediCombinedBytes)
                                : (mehediProbe.MehediIsHlsPlaylist ? "HLS Video Stream (.m3u8)" : "Stream / Unknown");
                            string mehediResStr = mehediProbe.MehediAcceptsRanges ? "Resume: Yes" : "Resume: No";
                            _shihabSizeBadgeLabel.Text = string.Format("Size: {0}  [{1}{2}]", shihabSizeStr, mehediResStr, shihabMuxTag);

                            string shihabCurLeaf = Path.GetFileName(_shihabSaveAsBox.Text);
                            if ((shihabCurLeaf == "download.bin" || shihabCurLeaf == "shihab_download.bin") &&
                                !string.IsNullOrWhiteSpace(mehediProbe.MehediSuggestedFileName))
                            {
                                ShihabCategoryRule mehediResolvedCat = _shihabConfig.ResolveMehediCategoryForFile(mehediProbe.MehediSuggestedFileName);
                                for (int shihabI = 0; shihabI < _mehediCategoryCombo.Items.Count; shihabI++)
                                {
                                    if (string.Equals(_mehediCategoryCombo.Items[shihabI].ToString(), mehediResolvedCat.ShihabName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _mehediCategoryCombo.SelectedIndex = shihabI;
                                        break;
                                    }
                                }
                                string mehediDir = mehediResolvedCat.ShihabDefaultFolder;
                                _shihabSaveAsBox.Text = Path.Combine(
                                    mehediDir,
                                    ShihabFormatHelpers.EnsureShihabUniqueFilePath(mehediDir, mehediProbe.MehediSuggestedFileName));
                            }
                        }
                        else if (MehediConfiguredItem.ShihabTotalBytes > 0)
                        {
                            long mehediKnownTotal = MehediConfiguredItem.ShihabTotalBytes +
                                (MehediConfiguredItem.ShihabAudioCompanionBytes > 0 ? MehediConfiguredItem.ShihabAudioCompanionBytes : 0L);
                            string shihabMuxTag = !string.IsNullOrWhiteSpace(MehediConfiguredItem.MehediAudioCompanionUrl) ? " · Video+Audio" : "";
                            _shihabSizeBadgeLabel.Text = string.Format("Size: {0}  [Browser Bridge{1}]", ShihabFormatHelpers.FormatMehediBytes(mehediKnownTotal), shihabMuxTag);
                        }
                        else
                        {
                            _shihabSizeBadgeLabel.Text = "Size: Resolving via Browser Bridge...";
                        }
                    });
                }
                catch { }
            });
        }

        private void CommitShihabChoicesAndClose(bool mehediStartNow)
        {
            string shihabFullSave = _shihabSaveAsBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(shihabFullSave))
            {
                MessageBox.Show(this, "Please specify a valid Save As file path.", "MHS Download Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string mehediFolder = Path.GetDirectoryName(shihabFullSave);
            string shihabLeaf = ShihabFormatHelpers.SanitizeShihabFileName(Path.GetFileName(shihabFullSave));
            if (string.IsNullOrWhiteSpace(mehediFolder))
            {
                mehediFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
            Directory.CreateDirectory(mehediFolder);

            MehediConfiguredItem.MehediSaveDirectory = mehediFolder;
            MehediConfiguredItem.ShihabFileName = shihabLeaf;
            MehediConfiguredItem.ShihabCategoryName = _mehediCategoryCombo.SelectedItem != null
                ? _mehediCategoryCombo.SelectedItem.ToString()
                : "General";
            MehediConfiguredItem.ShihabDescription = _mehediDescBox.Text.Trim();
            MehediConfiguredItem.MehediMaxConnections = _shihabConfig.ShihabDefaultMaxConnections;

            if (!mehediStartNow)
            {
                MehediConfiguredItem.MehediQueueName = _shihabQueueCombo.SelectedItem != null
                    ? _shihabQueueCombo.SelectedItem.ToString()
                    : "Main Download Queue";
                MehediConfiguredItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabQueued, "Queued");
            }

            if (_mehediRememberPathCheck.Checked)
            {
                for (int shihabI = 0; shihabI < _shihabConfig.ShihabCategories.Count; shihabI++)
                {
                    if (string.Equals(_shihabConfig.ShihabCategories[shihabI].ShihabName, MehediConfiguredItem.ShihabCategoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        _shihabConfig.ShihabCategories[shihabI].ShihabDefaultFolder = mehediFolder;
                        break;
                    }
                }
            }

            ShihabStartImmediatelySelected = mehediStartNow;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class ShihabSegmentMapPanel : Panel
    {
        public ShihabDownloadItem MehediBoundItem { get; set; }
        public bool ShihabIsOverallBar { get; set; }

        public ShihabSegmentMapPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs shihabArgs)
        {
            Graphics mehediG = shihabArgs.Graphics;
            mehediG.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle shihabRect = ClientRectangle;
            using (SolidBrush mehediBg = new SolidBrush(Color.FromArgb(20, 27, 40)))
            {
                mehediG.FillRectangle(mehediBg, shihabRect);
            }

            if (MehediBoundItem != null)
            {
                if (ShihabIsOverallBar)
                {
                    double mehediPct = MehediBoundItem.GetMehediPercent();
                    int shihabFillW = (int)Math.Round((shihabRect.Width * mehediPct) / 100.0);
                    if (shihabFillW > 1)
                    {
                        using (LinearGradientBrush mehediGrad = new LinearGradientBrush(
                            new Rectangle(0, 0, Math.Max(2, shihabFillW), Math.Max(2, shihabRect.Height)),
                            Color.FromArgb(14, 165, 233),
                            Color.FromArgb(16, 185, 129),
                            0f))
                        {
                            mehediG.FillRectangle(mehediGrad, 0, 0, shihabFillW, shihabRect.Height);
                        }
                    }
                    string shihabText = string.Format("{0:0.0}%", mehediPct);
                    using (Font mehediFont = new Font("Segoe UI Semibold", 8.5f))
                    using (SolidBrush shihabWhite = new SolidBrush(Color.White))
                    {
                        SizeF mehediSz = mehediG.MeasureString(shihabText, mehediFont);
                        mehediG.DrawString(
                            shihabText,
                            mehediFont,
                            shihabWhite,
                            (shihabRect.Width - mehediSz.Width) / 2f,
                            (shihabRect.Height - mehediSz.Height) / 2f);
                    }
                }
                else if (MehediBoundItem.ShihabTotalBytes > 0 && MehediBoundItem.MehediSegments != null)
                {
                    long shihabTotal = MehediBoundItem.ShihabTotalBytes;
                    List<MehediSegmentChunk> mehediSnap = new List<MehediSegmentChunk>(MehediBoundItem.MehediSegments);
                    using (SolidBrush shihabFillBrush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                    using (Pen mehediMarkerPen = new Pen(Color.FromArgb(245, 158, 11), 1.5f))
                    {
                        for (int shihabI = 0; shihabI < mehediSnap.Count; shihabI++)
                        {
                            MehediSegmentChunk mehediSeg = mehediSnap[shihabI];
                            if (mehediSeg == null) continue;
                            int shihabX0 = (int)((mehediSeg.MehediStartByte * (long)shihabRect.Width) / shihabTotal);
                            int mehediFillW = (int)((mehediSeg.MehediDownloadedBytes * (long)shihabRect.Width) / shihabTotal);
                            if (mehediSeg.MehediDownloadedBytes > 0 && mehediFillW < 2) mehediFillW = 2;
                            if (mehediFillW > 0)
                            {
                                mehediG.FillRectangle(shihabFillBrush, shihabX0, 1, mehediFillW, shihabRect.Height - 2);
                            }
                            mehediG.DrawLine(mehediMarkerPen, shihabX0, 0, shihabX0, shihabRect.Height);
                        }
                    }
                }
            }

            using (Pen shihabBorder = new Pen(Color.FromArgb(51, 65, 85), 1f))
            {
                mehediG.DrawRectangle(shihabBorder, 0, 0, shihabRect.Width - 1, shihabRect.Height - 1);
            }
        }
    }

    public class ShihabDownloadProgressForm : Form
    {
        private readonly ShihabDownloadItem _shihabItem;
        private readonly MehediDynamicSegmentEngine _mehediEngine;
        private readonly MehediAppConfiguration _shihabConfig;

        private Label _mehediUrlTitleLabel;
        private Label _shihabStatusVal;
        private Label _mehediFileSizeVal;
        private Label _shihabDownloadedVal;
        private Label _mehediRateVal;
        private Label _shihabEtaVal;
        private Label _mehediResumeVal;
        private ShihabSegmentMapPanel _shihabOverallBar;
        private ShihabSegmentMapPanel _mehediSegmentsMapBar;
        private ListView _shihabConnectionsList;
        private Button _mehediPauseResumeBtn;
        private Button _shihabHideDetailsBtn;
        private CheckBox _mehediItemSpeedCheck;
        private NumericUpDown _shihabItemSpeedNum;
        private CheckBox _mehediShowCompleteCheck;
        private CheckBox _shihabOpenOnDoneCheck;
        private CheckBox _mehediExitOnDoneCheck;
        private Timer _shihabUiRefreshTimer;
        private bool _mehediDetailsExpanded = true;

        public ShihabDownloadProgressForm(
            ShihabDownloadItem shihabItem,
            MehediDynamicSegmentEngine mehediEngine,
            MehediAppConfiguration shihabConfig)
        {
            _shihabItem = shihabItem;
            _mehediEngine = mehediEngine;
            _shihabConfig = shihabConfig;
            bool mehediDark = shihabConfig.ShihabDarkMode;

            Text = string.Format("{0:0.0}% — {1}", shihabItem.GetMehediPercent(), shihabItem.ShihabFileName);
            Size = new Size(610, 485);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ShihabThemePalette.GetMehediWindowBg(mehediDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(mehediDark);
            Font = new Font("Segoe UI", 9f);
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, mehediDark);
            Icon = ShihabThemePalette.CreateShihabAppIcon();

            TabControl shihabTabs = new TabControl
            {
                Location = new Point(10, 10),
                Size = new Size(574, 426)
            };

            TabPage mehediStatusTab = new TabPage("Download status")
            {
                BackColor = ShihabThemePalette.GetMehediWindowBg(mehediDark),
                ForeColor = ForeColor
            };
            TabPage shihabLimiterTab = new TabPage("Speed Limiter")
            {
                BackColor = ShihabThemePalette.GetMehediWindowBg(mehediDark),
                ForeColor = ForeColor
            };
            TabPage mehediCompletionTab = new TabPage("Options on completion")
            {
                BackColor = ShihabThemePalette.GetMehediWindowBg(mehediDark),
                ForeColor = ForeColor
            };

            BuildMehediStatusTab(mehediStatusTab, mehediDark);
            BuildShihabLimiterTab(shihabLimiterTab);
            BuildMehediCompletionTab(mehediCompletionTab);

            shihabTabs.TabPages.Add(mehediStatusTab);
            shihabTabs.TabPages.Add(shihabLimiterTab);
            shihabTabs.TabPages.Add(mehediCompletionTab);
            Controls.Add(shihabTabs);

            _shihabUiRefreshTimer = new Timer { Interval = 250 };
            _shihabUiRefreshTimer.Tick += delegate { RefreshShihabProgressView(); };
            _shihabUiRefreshTimer.Start();

            FormClosed += delegate
            {
                _shihabUiRefreshTimer.Stop();
                _shihabUiRefreshTimer.Dispose();
            };

            RefreshShihabProgressView();
        }

        private void BuildMehediStatusTab(TabPage shihabPage, bool mehediDark)
        {
            _mehediUrlTitleLabel = new Label
            {
                Text = _shihabItem.MehediUrl,
                Location = new Point(14, 12),
                Size = new Size(535, 20),
                AutoEllipsis = true,
                Font = new Font("Segoe UI Semibold", 9f),
                ForeColor = ShihabThemePalette.GetShihabAccentBlue()
            };

            int shihabLeftX = 14;
            int mehediValX = 155;
            _shihabStatusVal = AddShihabMetricRow(shihabPage, "Status:", shihabLeftX, mehediValX, 38);
            _mehediFileSizeVal = AddShihabMetricRow(shihabPage, "File size:", shihabLeftX, mehediValX, 60);
            _shihabDownloadedVal = AddShihabMetricRow(shihabPage, "Downloaded:", shihabLeftX, mehediValX, 82);
            _mehediRateVal = AddShihabMetricRow(shihabPage, "Transfer rate:", shihabLeftX, mehediValX, 104);
            _shihabEtaVal = AddShihabMetricRow(shihabPage, "Time left:", shihabLeftX, mehediValX, 126);
            _mehediResumeVal = AddShihabMetricRow(shihabPage, "Resume capability:", shihabLeftX, mehediValX, 148);

            _shihabOverallBar = new ShihabSegmentMapPanel
            {
                MehediBoundItem = _shihabItem,
                ShihabIsOverallBar = true,
                Location = new Point(14, 174),
                Size = new Size(536, 22)
            };

            _shihabHideDetailsBtn = ShihabThemePalette.CreateMehediStyledButton("<< Hide details", ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor, 125);
            _shihabHideDetailsBtn.Location = new Point(14, 204);
            _shihabHideDetailsBtn.Click += delegate
            {
                _mehediDetailsExpanded = !_mehediDetailsExpanded;
                _shihabHideDetailsBtn.Text = _mehediDetailsExpanded ? "<< Hide details" : "Show details >>";
                _mehediSegmentsMapBar.Visible = _mehediDetailsExpanded;
                _shihabConnectionsList.Visible = _mehediDetailsExpanded;
                Height = _mehediDetailsExpanded ? 485 : 318;
            };

            _mehediPauseResumeBtn = ShihabThemePalette.CreateMehediStyledButton("Pause", ShihabThemePalette.GetShihabAccentBlue(), Color.White, 110);
            _mehediPauseResumeBtn.Location = new Point(318, 204);
            _mehediPauseResumeBtn.Click += delegate
            {
                if (_mehediEngine.IsMehediItemActive(_shihabItem.ShihabId))
                {
                    _mehediEngine.PauseShihabDownload(_shihabItem);
                }
                else if (_shihabItem.GetShihabStatusEnum() != ShihabDownloadStatus.ShihabCompleted)
                {
                    _mehediEngine.StartOrResumeMehediDownload(_shihabItem);
                }
                RefreshShihabProgressView();
            };

            Button shihabCancelBtn = ShihabThemePalette.CreateMehediStyledButton("Cancel", ShihabThemePalette.GetMehediAccentRed(), Color.White, 110);
            shihabCancelBtn.Location = new Point(440, 204);
            shihabCancelBtn.Click += delegate
            {
                if (_mehediEngine.IsMehediItemActive(_shihabItem.ShihabId))
                {
                    _mehediEngine.PauseShihabDownload(_shihabItem);
                }
                Close();
            };

            Label mehediConnHeader = new Label
            {
                Text = "Start positions and download progress by connections (Dynamic Segmentation):",
                Location = new Point(14, 244),
                AutoSize = true,
                ForeColor = ShihabThemePalette.GetMehediMutedText(mehediDark)
            };

            _mehediSegmentsMapBar = new ShihabSegmentMapPanel
            {
                MehediBoundItem = _shihabItem,
                ShihabIsOverallBar = false,
                Location = new Point(14, 265),
                Size = new Size(536, 18)
            };

            _shihabConnectionsList = new ShihabModernListView
            {
                Location = new Point(14, 289),
                Size = new Size(536, 98),
                MehediDarkMode = mehediDark,
                ShihabShowFileTypeBadges = false,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark),
                ForeColor = ForeColor
            };
            _shihabConnectionsList.Columns.Add("N.", 50);
            _shihabConnectionsList.Columns.Add("Byte Range", 160);
            _shihabConnectionsList.Columns.Add("Downloaded", 115);
            _shihabConnectionsList.Columns.Add("Info", 190);

            shihabPage.Controls.AddRange(new Control[]
            {
                _mehediUrlTitleLabel, _shihabOverallBar, _shihabHideDetailsBtn,
                _mehediPauseResumeBtn, shihabCancelBtn, mehediConnHeader,
                _mehediSegmentsMapBar, _shihabConnectionsList
            });
        }

        private Label AddShihabMetricRow(TabPage mehediPage, string shihabTitle, int mehediX1, int shihabX2, int mehediY)
        {
            Label shihabKey = new Label { Text = shihabTitle, Location = new Point(mehediX1, mehediY), AutoSize = true };
            Label mehediVal = new Label
            {
                Text = "-",
                Location = new Point(shihabX2, mehediY),
                Size = new Size(390, 18),
                AutoEllipsis = true,
                Font = new Font("Segoe UI Semibold", 9f)
            };
            mehediPage.Controls.Add(shihabKey);
            mehediPage.Controls.Add(mehediVal);
            return mehediVal;
        }

        private void BuildShihabLimiterTab(TabPage mehediPage)
        {
            _mehediItemSpeedCheck = new CheckBox
            {
                Text = "Use Speed Limiter for this download",
                Location = new Point(24, 30),
                AutoSize = true,
                Checked = _shihabItem.ShihabUseItemSpeedLimit
            };

            Label shihabMaxLbl = new Label
            {
                Text = "Maximum download speed for this file (KBytes/sec):",
                Location = new Point(24, 68),
                AutoSize = true
            };

            _shihabItemSpeedNum = new NumericUpDown
            {
                Location = new Point(330, 65),
                Width = 110,
                Minimum = 8,
                Maximum = 1000000,
                Value = Math.Max(8, _shihabItem.MehediItemSpeedLimitKb)
            };

            _mehediItemSpeedCheck.CheckedChanged += delegate
            {
                _shihabItem.ShihabUseItemSpeedLimit = _mehediItemSpeedCheck.Checked;
            };
            _shihabItemSpeedNum.ValueChanged += delegate
            {
                _shihabItem.MehediItemSpeedLimitKb = (int)_shihabItemSpeedNum.Value;
            };

            Label mehediNote = new Label
            {
                Text = "Note: Per-download Speed Limiter throttles all active segments of this download smoothly using Shihab Mehedi token-bucket pacing.",
                Location = new Point(24, 112),
                Size = new Size(510, 50)
            };

            mehediPage.Controls.AddRange(new Control[] { _mehediItemSpeedCheck, shihabMaxLbl, _shihabItemSpeedNum, mehediNote });
        }

        private void BuildMehediCompletionTab(TabPage shihabPage)
        {
            _mehediShowCompleteCheck = new CheckBox
            {
                Text = "Show Download Complete dialog",
                Location = new Point(24, 30),
                AutoSize = true,
                Checked = _shihabItem.ShihabShowCompleteDialog
            };
            _shihabOpenOnDoneCheck = new CheckBox
            {
                Text = "Open downloaded file automatically when done",
                Location = new Point(24, 64),
                AutoSize = true,
                Checked = _shihabItem.ShihabOpenWhenComplete
            };
            _mehediExitOnDoneCheck = new CheckBox
            {
                Text = "Exit MHS Download Manager when this download completes",
                Location = new Point(24, 98),
                AutoSize = true,
                Checked = _shihabItem.MehediExitAppOnComplete
            };

            _mehediShowCompleteCheck.CheckedChanged += delegate { _shihabItem.ShihabShowCompleteDialog = _mehediShowCompleteCheck.Checked; };
            _shihabOpenOnDoneCheck.CheckedChanged += delegate { _shihabItem.ShihabOpenWhenComplete = _shihabOpenOnDoneCheck.Checked; };
            _mehediExitOnDoneCheck.CheckedChanged += delegate { _shihabItem.MehediExitAppOnComplete = _mehediExitOnDoneCheck.Checked; };

            shihabPage.Controls.AddRange(new Control[] { _mehediShowCompleteCheck, _shihabOpenOnDoneCheck, _mehediExitOnDoneCheck });
        }

        private void RefreshShihabProgressView()
        {
            if (IsDisposed) return;

            ShihabDownloadStatus mehediSt = _shihabItem.GetShihabStatusEnum();
            Text = string.Format("{0:0.0}% — {1}", _shihabItem.GetMehediPercent(), _shihabItem.ShihabFileName);
            _shihabStatusVal.Text = _shihabItem.ShihabStatusText;
            _mehediFileSizeVal.Text = ShihabFormatHelpers.FormatMehediBytes(_shihabItem.ShihabTotalBytes);
            _shihabDownloadedVal.Text = string.Format("{0} ({1:0.0}%)",
                ShihabFormatHelpers.FormatMehediBytes(_shihabItem.MehediDownloadedBytes),
                _shihabItem.GetMehediPercent());

            string shihabSpd = ShihabFormatHelpers.FormatShihabSpeed(_shihabItem.ShihabCurrentSpeedBps);
            if (!string.IsNullOrEmpty(shihabSpd) && (_shihabItem.ShihabUseItemSpeedLimit || _shihabConfig.MehediGlobalSpeedLimiterEnabled))
            {
                shihabSpd += " (Limited)";
            }
            _mehediRateVal.Text = string.IsNullOrEmpty(shihabSpd) ? "-" : shihabSpd;
            _shihabEtaVal.Text = _shihabItem.MehediEtaSeconds >= 0 ? ShihabFormatHelpers.FormatMehediTimeLeft(_shihabItem.MehediEtaSeconds) : "-";
            _mehediResumeVal.Text = _shihabItem.ShihabResumeSupported ? "Yes" : "No";
            _mehediResumeVal.ForeColor = _shihabItem.ShihabResumeSupported
                ? ShihabThemePalette.GetMehediAccentGreen()
                : ShihabThemePalette.GetShihabAccentAmber();

            bool mehediActive = _mehediEngine.IsMehediItemActive(_shihabItem.ShihabId);
            _mehediPauseResumeBtn.Text = mehediActive ? "Pause" : "Start";
            _mehediPauseResumeBtn.Enabled = mehediSt != ShihabDownloadStatus.ShihabCompleted;

            _shihabOverallBar.Invalidate();
            _mehediSegmentsMapBar.Invalidate();

            List<MehediSegmentChunk> shihabSegs = _shihabItem.MehediSegments != null
                ? new List<MehediSegmentChunk>(_shihabItem.MehediSegments)
                : new List<MehediSegmentChunk>();

            _shihabConnectionsList.BeginUpdate();
            while (_shihabConnectionsList.Items.Count < shihabSegs.Count)
            {
                _shihabConnectionsList.Items.Add(new ListViewItem(new string[] { "", "", "", "" }));
            }
            while (_shihabConnectionsList.Items.Count > shihabSegs.Count)
            {
                _shihabConnectionsList.Items.RemoveAt(_shihabConnectionsList.Items.Count - 1);
            }
            for (int mehediI = 0; mehediI < shihabSegs.Count; mehediI++)
            {
                MehediSegmentChunk shihabSeg = shihabSegs[mehediI];
                ListViewItem mehediLvi = _shihabConnectionsList.Items[mehediI];
                mehediLvi.SubItems[0].Text = shihabSeg.ShihabSegmentIndex.ToString();
                mehediLvi.SubItems[1].Text = shihabSeg.ShihabEndByte >= 0
                    ? string.Format("{0} - {1}", ShihabFormatHelpers.FormatMehediBytes(shihabSeg.MehediStartByte), ShihabFormatHelpers.FormatMehediBytes(shihabSeg.ShihabEndByte))
                    : "Stream";
                mehediLvi.SubItems[2].Text = ShihabFormatHelpers.FormatMehediBytes(shihabSeg.MehediDownloadedBytes);
                mehediLvi.SubItems[3].Text = shihabSeg.ShihabSegmentState ?? "";
            }
            _shihabConnectionsList.EndUpdate();

            if (mehediSt == ShihabDownloadStatus.ShihabCompleted)
            {
                _shihabUiRefreshTimer.Stop();
                Close();
            }
        }
    }

    public class MehediDownloadCompleteForm : Form
    {
        public MehediDownloadCompleteForm(ShihabDownloadItem shihabItem, MehediAppConfiguration mehediConfig)
        {
            bool shihabDark = mehediConfig.ShihabDarkMode;
            Text = "Download complete — MHS Download Manager";
            Size = new Size(590, 285);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = ShihabThemePalette.GetMehediWindowBg(shihabDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(shihabDark);
            Font = new Font("Segoe UI", 9f);
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, shihabDark);
            Icon = ShihabThemePalette.CreateShihabAppIcon();

            string mehediFullFile = shihabItem.GetShihabFullTargetPath();

            Label shihabBanner = new Label
            {
                Text = "Download Complete — " + ShihabFormatHelpers.FormatMehediBytes(shihabItem.MehediDownloadedBytes),
                Location = new Point(18, 16),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = ShihabThemePalette.GetMehediAccentGreen()
            };

            Label mehediAddrLbl = new Label { Text = "Address:", Location = new Point(18, 52), AutoSize = true };
            TextBox shihabAddrBox = new TextBox
            {
                Location = new Point(105, 49),
                Width = 450,
                ReadOnly = true,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = shihabItem.MehediUrl
            };

            Label shihabSavedLbl = new Label { Text = "Saved as:", Location = new Point(18, 88), AutoSize = true };
            TextBox mehediSavedBox = new TextBox
            {
                Location = new Point(105, 85),
                Width = 450,
                ReadOnly = true,
                BackColor = ShihabThemePalette.GetShihabSurfaceBg(shihabDark),
                ForeColor = ForeColor,
                Text = mehediFullFile
            };

            Label shihabHashLbl = new Label
            {
                Text = "SHA-256: " + (string.IsNullOrEmpty(shihabItem.MehediSha256Hex) ? "-" : shihabItem.MehediSha256Hex),
                Location = new Point(18, 124),
                Size = new Size(540, 20),
                AutoEllipsis = true,
                ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark)
            };

            CheckBox mehediDontShow = new CheckBox
            {
                Text = "Don't show this dialog again",
                Location = new Point(18, 155),
                AutoSize = true
            };
            mehediDontShow.CheckedChanged += delegate
            {
                mehediConfig.ShihabShowDownloadCompleteDialog = !mehediDontShow.Checked;
            };

            Button shihabOpenBtn = ShihabThemePalette.CreateMehediStyledButton("Open", ShihabThemePalette.GetMehediAccentGreen(), Color.White, 115);
            shihabOpenBtn.Location = new Point(18, 195);
            shihabOpenBtn.Click += delegate
            {
                try { if (File.Exists(mehediFullFile)) Process.Start(mehediFullFile); } catch { }
                Close();
            };

            Button mehediOpenWithBtn = ShihabThemePalette.CreateMehediStyledButton("Open With...", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 125);
            mehediOpenWithBtn.Location = new Point(145, 195);
            mehediOpenWithBtn.Click += delegate
            {
                try
                {
                    if (File.Exists(mehediFullFile))
                    {
                        Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + mehediFullFile);
                    }
                }
                catch { }
                Close();
            };

            Button shihabFolderBtn = ShihabThemePalette.CreateMehediStyledButton("Open Folder", ShihabThemePalette.GetShihabAccentBlue(), Color.White, 130);
            shihabFolderBtn.Location = new Point(282, 195);
            shihabFolderBtn.Click += delegate
            {
                try
                {
                    if (File.Exists(mehediFullFile))
                    {
                        Process.Start("explorer.exe", "/select,\"" + mehediFullFile + "\"");
                    }
                    else if (Directory.Exists(shihabItem.MehediSaveDirectory))
                    {
                        Process.Start("explorer.exe", "\"" + shihabItem.MehediSaveDirectory + "\"");
                    }
                }
                catch { }
                Close();
            };

            Button mehediCloseBtn = ShihabThemePalette.CreateMehediStyledButton("Close", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 115);
            mehediCloseBtn.Location = new Point(440, 195);
            mehediCloseBtn.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                shihabBanner, mehediAddrLbl, shihabAddrBox, shihabSavedLbl, mehediSavedBox,
                shihabHashLbl, mehediDontShow, shihabOpenBtn, mehediOpenWithBtn, shihabFolderBtn, mehediCloseBtn
            });
        }
    }

    public class ShihabFilePropertiesForm : Form
    {
        public ShihabFilePropertiesForm(ShihabDownloadItem shihabItem, bool mehediDark)
        {
            Text = "File Properties — " + shihabItem.ShihabFileName;
            Size = new Size(600, 395);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ShihabThemePalette.GetMehediWindowBg(mehediDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(mehediDark);
            Font = new Font("Segoe UI", 9f);
            Icon = ShihabThemePalette.CreateShihabAppIcon();
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, mehediDark);

            Label mehediL1 = new Label { Text = "Address (URL):", Location = new Point(16, 18), AutoSize = true };
            TextBox shihabUrlBox = new TextBox { Location = new Point(125, 15), Width = 440, Text = shihabItem.MehediUrl, BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL2 = new Label { Text = "Referer:", Location = new Point(16, 52), AutoSize = true };
            TextBox shihabRefBox = new TextBox { Location = new Point(125, 49), Width = 440, Text = shihabItem.MehediReferer ?? "", BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL3 = new Label { Text = "Save To:", Location = new Point(16, 86), AutoSize = true };
            TextBox shihabSaveBox = new TextBox { Location = new Point(125, 83), Width = 440, ReadOnly = true, Text = shihabItem.GetShihabFullTargetPath(), BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL4 = new Label { Text = "Size / Status:", Location = new Point(16, 120), AutoSize = true };
            Label shihabSizeInfo = new Label
            {
                Location = new Point(125, 120),
                AutoSize = true,
                Text = string.Format("{0}  |  Downloaded: {1}  |  Status: {2}  |  Resume: {3}",
                    ShihabFormatHelpers.FormatMehediBytes(shihabItem.ShihabTotalBytes),
                    ShihabFormatHelpers.FormatMehediBytes(shihabItem.MehediDownloadedBytes),
                    shihabItem.ShihabStatusText,
                    shihabItem.ShihabResumeSupported ? "Yes" : "No")
            };

            Label mehediL5 = new Label { Text = "SHA-256:", Location = new Point(16, 150), AutoSize = true };
            TextBox shihabHashBox = new TextBox { Location = new Point(125, 147), Width = 440, ReadOnly = true, Text = string.IsNullOrEmpty(shihabItem.MehediSha256Hex) ? "(computed on completion)" : shihabItem.MehediSha256Hex, BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL6 = new Label { Text = "Description:", Location = new Point(16, 184), AutoSize = true };
            TextBox shihabDescBox = new TextBox { Location = new Point(125, 181), Width = 440, Text = shihabItem.ShihabDescription ?? "", BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL7 = new Label { Text = "Login / Pass:", Location = new Point(16, 218), AutoSize = true };
            TextBox shihabUserBox = new TextBox { Location = new Point(125, 215), Width = 200, Text = shihabItem.ShihabAuthUser ?? "", BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };
            TextBox mehediPassBox = new TextBox { Location = new Point(340, 215), Width = 225, UseSystemPasswordChar = true, Text = shihabItem.MehediAuthPassword ?? "", BackColor = ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor = ForeColor };

            Label mehediL8 = new Label { Text = "Max Conns:", Location = new Point(16, 254), AutoSize = true };
            NumericUpDown shihabConnNum = new NumericUpDown { Location = new Point(125, 251), Width = 80, Minimum = 1, Maximum = 32, Value = Math.Max(1, Math.Min(32, shihabItem.MehediMaxConnections)) };

            Button shihabSaveBtn = ShihabThemePalette.CreateMehediStyledButton("Save Changes", ShihabThemePalette.GetShihabAccentBlue(), Color.White, 130);
            shihabSaveBtn.Location = new Point(315, 305);
            shihabSaveBtn.Click += delegate
            {
                if (!string.IsNullOrWhiteSpace(shihabUrlBox.Text))
                {
                    shihabItem.MehediUrl = shihabUrlBox.Text.Trim();
                }
                shihabItem.MehediReferer = shihabRefBox.Text.Trim();
                shihabItem.ShihabDescription = shihabDescBox.Text.Trim();
                shihabItem.ShihabAuthUser = shihabUserBox.Text.Trim();
                shihabItem.MehediAuthPassword = mehediPassBox.Text;
                shihabItem.MehediMaxConnections = (int)shihabConnNum.Value;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button mehediCancelBtn = ShihabThemePalette.CreateMehediStyledButton("Close", ShihabThemePalette.GetShihabSurfaceBg(mehediDark), ForeColor, 110);
            mehediCancelBtn.Location = new Point(455, 305);
            mehediCancelBtn.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                mehediL1, shihabUrlBox, mehediL2, shihabRefBox, mehediL3, shihabSaveBox,
                mehediL4, shihabSizeInfo, mehediL5, shihabHashBox, mehediL6, shihabDescBox,
                mehediL7, shihabUserBox, mehediPassBox, mehediL8, shihabConnNum,
                shihabSaveBtn, mehediCancelBtn
            });
        }
    }

    public class ShihabAboutDeveloperDialog : Form
    {
        public ShihabAboutDeveloperDialog(bool shihabDark)
        {
            Text = "About MHS Download Manager";
            Size = new Size(610, 435);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ShihabThemePalette.GetMehediWindowBg(shihabDark);
            ForeColor = ShihabThemePalette.GetShihabPrimaryText(shihabDark);
            Font = new Font("Segoe UI", 9.2f);
            Icon = ShihabThemePalette.CreateShihabAppIcon();
            ShihabThemePalette.ApplyMehediNativeDarkFrame(this, shihabDark);

            PictureBox mehediHeroLogo = new PictureBox
            {
                Location = new Point(22, 20),
                Size = new Size(72, 72),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = ShihabThemePalette.CreateMehediLogoBitmap(72),
                BackColor = Color.Transparent
            };

            Label shihabAppTitle = new Label
            {
                Text = "MHS Download Manager 1.0.0",
                Location = new Point(108, 18),
                AutoSize = true,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = shihabDark ? Color.FromArgb(248, 250, 252) : Color.FromArgb(15, 23, 42)
            };

            Label mehediExeTag = new Label
            {
                Text = "Executable: mdm.exe (x64)  •  Dynamic 32-Segment Engine & MV3 Browser Video Suite",
                Location = new Point(110, 50),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 8.8f),
                ForeColor = shihabDark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(37, 99, 235)
            };

            Label shihabTagline = new Label
            {
                Text = "High-speed HTTP/HTTPS multi-connection accelerator, HLS stream grabber & smart scheduler.",
                Location = new Point(110, 72),
                AutoSize = true,
                ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark)
            };

            Panel mehediDevCard = new Panel
            {
                Location = new Point(20, 108),
                Size = new Size(554, 224),
                BackColor = shihabDark ? Color.FromArgb(28, 30, 36) : Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Label shihabCardHdr = new Label
            {
                Text = "DEVELOPER & AUTHOR INFORMATION",
                Location = new Point(16, 14),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = shihabDark ? Color.FromArgb(236, 72, 153) : Color.FromArgb(190, 24, 93)
            };

            Label mehediDevNameLbl = new Label
            {
                Text = "Developer:",
                Location = new Point(16, 44),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5f),
                ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark)
            };
            Label shihabDevNameVal = new Label
            {
                Text = "Mehedi Hasan Shihab  (Web & Software Developer)",
                Location = new Point(125, 44),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = ForeColor
            };

            Color mehediLinkCol = shihabDark ? Color.FromArgb(96, 165, 250) : Color.FromArgb(29, 78, 216);

            Label shihabPortLbl = new Label { Text = "Portfolio:", Location = new Point(16, 78), AutoSize = true, ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark) };
            LinkLabel mehediPortLink = CreateShihabUrlLink("https://sshihabb007.github.io/", "https://sshihabb007.github.io/", new Point(125, 78), mehediLinkCol);

            Label shihabMailLbl = new Label { Text = "Email Me:", Location = new Point(16, 108), AutoSize = true, ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark) };
            LinkLabel mehediMailLink = CreateShihabUrlLink("sshihabb007@gmail.com", "mailto:sshihabb007@gmail.com?subject=MHS%20Download%20Manager", new Point(125, 108), mehediLinkCol);

            Label shihabGhLbl = new Label { Text = "GitHub:", Location = new Point(16, 138), AutoSize = true, ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark) };
            LinkLabel mehediGhLink = CreateShihabUrlLink("https://github.com/sshihabb007", "https://github.com/sshihabb007", new Point(125, 138), mehediLinkCol);

            Label shihabLiLbl = new Label { Text = "LinkedIn:", Location = new Point(16, 168), AutoSize = true, ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark) };
            LinkLabel mehediLiLink = CreateShihabUrlLink("https://www.linkedin.com/in/mehedi-hasan-shihab", "https://www.linkedin.com/in/mehedi-hasan-shihab", new Point(125, 168), mehediLinkCol);

            Label shihabCopyLbl = new Label
            {
                Text = "Copyright © 2026 Mehedi Hasan Shihab (sshihabb007). All rights reserved.",
                Location = new Point(16, 196),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = ShihabThemePalette.GetMehediMutedText(shihabDark)
            };

            mehediDevCard.Controls.AddRange(new Control[]
            {
                shihabCardHdr, mehediDevNameLbl, shihabDevNameVal,
                shihabPortLbl, mehediPortLink,
                shihabMailLbl, mehediMailLink,
                shihabGhLbl, mehediGhLink,
                shihabLiLbl, mehediLiLink,
                shihabCopyLbl
            });

            Button shihabPortfolioBtn = ShihabThemePalette.CreateMehediStyledButton("Visit Portfolio", Color.FromArgb(37, 99, 235), Color.White, 136);
            shihabPortfolioBtn.Location = new Point(20, 346);
            shihabPortfolioBtn.Click += delegate { OpenMehediExternalUri("https://sshihabb007.github.io/"); };

            Button mehediEmailBtn = ShihabThemePalette.CreateMehediStyledButton("Email Me", Color.FromArgb(190, 24, 93), Color.White, 120);
            mehediEmailBtn.Location = new Point(164, 346);
            mehediEmailBtn.Click += delegate { OpenMehediExternalUri("mailto:sshihabb007@gmail.com?subject=MHS%20Download%20Manager"); };

            Button shihabGithubBtn = ShihabThemePalette.CreateMehediStyledButton("GitHub", Color.FromArgb(51, 65, 85), Color.White, 110);
            shihabGithubBtn.Location = new Point(292, 346);
            shihabGithubBtn.Click += delegate { OpenMehediExternalUri("https://github.com/sshihabb007"); };

            Button mehediOkBtn = ShihabThemePalette.CreateMehediStyledButton("Close", ShihabThemePalette.GetShihabSurfaceBg(shihabDark), ForeColor, 105);
            mehediOkBtn.Location = new Point(469, 346);
            mehediOkBtn.Click += delegate { Close(); };

            AcceptButton = mehediOkBtn;
            CancelButton = mehediOkBtn;

            Controls.AddRange(new Control[]
            {
                mehediHeroLogo, shihabAppTitle, mehediExeTag, shihabTagline,
                mehediDevCard, shihabPortfolioBtn, mehediEmailBtn, shihabGithubBtn, mehediOkBtn
            });
        }

        private static LinkLabel CreateShihabUrlLink(string mehediText, string shihabTargetUri, Point mehediLoc, Color shihabCol)
        {
            LinkLabel mehediLink = new LinkLabel
            {
                Text = mehediText,
                Location = mehediLoc,
                AutoSize = true,
                LinkColor = shihabCol,
                ActiveLinkColor = Color.FromArgb(244, 114, 182),
                VisitedLinkColor = shihabCol,
                Font = new Font("Segoe UI Semibold", 9.2f)
            };
            mehediLink.LinkClicked += delegate
            {
                OpenMehediExternalUri(shihabTargetUri);
            };
            return mehediLink;
        }

        private static void OpenMehediExternalUri(string shihabUri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(shihabUri) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
