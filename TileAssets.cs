using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SimpleCalendar
{
    internal static class TileAssets
    {
        public static void CreateAll(string folder)
        {
            Directory.CreateDirectory(folder);
            Save(folder, "Square44x44Logo.png", 44, 44);
            Save(folder, "Square150x150Logo.png", 150, 150);
            Save(folder, "Square310x310Logo.png", 310, 310);
            Save(folder, "Wide310x150Logo.png", 310, 150);
            Save(folder, "StoreLogo.png", 50, 50);
        }

        private static void Save(string folder, string name, int width, int height)
        {
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                int unit = Math.Min(width, height);
                int box = (int)(unit * .58f);
                int x = (width - box) / 2;
                int y = (height - box) / 2;
                float stroke = Math.Max(2F, unit * .045f);
                using (Pen pen = new Pen(Color.White, stroke))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawRectangle(pen, x, y + box / 8, box, box * 7 / 8);
                    g.DrawLine(pen, x, y + box / 3, x + box, y + box / 3);
                    g.DrawLine(pen, x + box / 4, y, x + box / 4, y + box / 4);
                    g.DrawLine(pen, x + box * 3 / 4, y, x + box * 3 / 4, y + box / 4);
                }
                bitmap.Save(Path.Combine(folder, name), ImageFormat.Png);
            }
        }
    }
}
