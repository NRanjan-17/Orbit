using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Orbit.Services;

/// <summary>
/// Draws the battery number as a multi-size .ico (16-48 px, one image per tray DPI) with the digits scaled to
/// fill each size, and caches it under %LOCALAPPDATA%\Orbit\tray. H.NotifyIcon's GeneratedIconSource rendered
/// the text far too small and offers no control over its bitmap size.
/// </summary>
public static class TrayIconRenderer
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48];

    /// <returns>Path of an .ico file for this text and colour (created once, then reused).</returns>
    public static string Render(string text, Color color)
    {
        var dir = Path.Combine(AppPaths.DataDirectory, "tray");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"battery-v2-{text}-{color.ToArgb():X8}.ico");
        if (File.Exists(path))
        {
            return path;
        }

        // Classic 32-bit DIB entries (not PNG-compressed) so every icon consumer can read every size.
        var images = Sizes.Select(size => ToDib(RenderBitmap(text, color, size))).ToList();
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            w.Write((byte)Sizes[i]);
            w.Write((byte)Sizes[i]);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }

        foreach (var png in images)
        {
            w.Write(png);
        }

        return path;
    }

    /// <summary>ICO image entry: BITMAPINFOHEADER (height doubled), bottom-up BGRA pixels, 1-bpp AND mask.</summary>
    private static byte[] ToDib(Bitmap bmp)
    {
        using (bmp)
        {
            var size = bmp.Width;
            var maskStride = ((size + 31) / 32) * 4;
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(40);                  // biSize
            w.Write(size);                // biWidth
            w.Write(size * 2);            // biHeight (XOR + AND)
            w.Write((short)1);            // biPlanes
            w.Write((short)32);           // biBitCount
            w.Write(0);                   // BI_RGB
            w.Write(size * size * 4 + maskStride * size);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            for (var y = size - 1; y >= 0; y--)
            {
                for (var x = 0; x < size; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    w.Write(c.B);
                    w.Write(c.G);
                    w.Write(c.R);
                    w.Write(c.A);
                }
            }

            w.Write(new byte[maskStride * size]); // AND mask all 0: alpha channel decides transparency
            w.Flush();
            return ms.ToArray();
        }
    }

    private static Bitmap RenderBitmap(string text, Color color, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Condensed font so two digits can be tall; fall back to Segoe UI.
            using var family = FontFamily.Families.FirstOrDefault(f => f.Name == "Bahnschrift") is { } b ? new FontFamily(b.Name) : new FontFamily("Segoe UI");
            // Fit the glyph outline to the icon (both directions), leaving a hair of margin.
            using var path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
            var bounds = path.GetBounds();
            var scale = Math.Min((size - 1f) / bounds.Width, (size - 1f) / bounds.Height);
            using var m = new Matrix();
            m.Translate(size / 2f, size / 2f);
            m.Scale(scale, scale);
            m.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
            path.Transform(m);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }

        return bmp;
    }
}
