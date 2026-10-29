using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MHSManager.Tools
{
    public static class GenerateIcon
    {
        public static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "MHSManager.ico";
            string dir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int[] sizes = new int[] { 16, 32, 48, 256 };
            List<byte[]> pngPayloads = new List<byte[]>();

            foreach (int sz in sizes)
            {
                using (Bitmap bmp = RenderIconBitmap(sz))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngPayloads.Add(ms.ToArray());
                }
            }

            using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // ICONDIR header
                bw.Write((ushort)0);
                bw.Write((ushort)1);
                bw.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    byte[] payload = pngPayloads[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)(sz >= 256 ? 0 : sz));
                    bw.Write((byte)0);
                    bw.Write((byte)0);
                    bw.Write((ushort)1);
                    bw.Write((ushort)32);
                    bw.Write((uint)payload.Length);
                    bw.Write((uint)offset);
                    offset += payload.Length;
                }

                for (int i = 0; i < pngPayloads.Count; i++)
                {
                    bw.Write(pngPayloads[i]);
                }
            }

            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                string extIconDir = args[1];
                if (!Directory.Exists(extIconDir))
                {
                    Directory.CreateDirectory(extIconDir);
                }
                int[] extSizes = new int[] { 16, 48, 128 };
                foreach (int esz in extSizes)
                {
                    using (Bitmap ebmp = RenderIconBitmap(esz))
                    {
                        ebmp.Save(Path.Combine(extIconDir, string.Format("icon{0}.png", esz)), ImageFormat.Png);
                    }
                }
            }

            return 0;
        }

        private static Bitmap RenderIconBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float pad = size * 0.06f;
                float rectSize = size - (pad * 2f);
                float radius = size * 0.24f;

                using (GraphicsPath bgPath = CreateRoundedRect(pad, pad, rectSize, rectSize, radius))
                using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(size, size),
                    Color.FromArgb(255, 14, 165, 233),
                    Color.FromArgb(255, 16, 185, 129)))
                {
                    g.FillPath(bgBrush, bgPath);
                    using (Pen borderPen = new Pen(Color.FromArgb(90, 255, 255, 255), Math.Max(1f, size * 0.03f)))
                    {
                        g.DrawPath(borderPen, bgPath);
                    }
                }

                // Draw bold download arrow + tray bar
                float cx = size * 0.5f;
                float shaftW = size * 0.18f;
                float topY = size * 0.20f;
                float headTopY = size * 0.46f;
                float tipY = size * 0.68f;
                float wingW = size * 0.26f;

                PointF[] arrow = new PointF[]
                {
                    new PointF(cx - shaftW * 0.5f, topY),
                    new PointF(cx + shaftW * 0.5f, topY),
                    new PointF(cx + shaftW * 0.5f, headTopY),
                    new PointF(cx + wingW, headTopY),
                    new PointF(cx, tipY),
                    new PointF(cx - wingW, headTopY),
                    new PointF(cx - shaftW * 0.5f, headTopY)
                };

                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    g.FillPolygon(whiteBrush, arrow);

                    float trayW = size * 0.52f;
                    float trayH = Math.Max(2f, size * 0.085f);
                    float trayY = size * 0.74f;
                    using (GraphicsPath trayPath = CreateRoundedRect(cx - trayW * 0.5f, trayY, trayW, trayH, trayH * 0.45f))
                    {
                        g.FillPath(whiteBrush, trayPath);
                    }
                }
            }
            return bmp;
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
        {
            GraphicsPath path = new GraphicsPath();
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
