using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace FolderBadges;

/// <summary>Draws a folder icon with a red count badge and saves it as a .ico file.</summary>
public static class BadgeIcon
{
    // Change this if you change the badge design, so Windows doesn't show old cached icons.
    const string StyleVersion = "v1";

    // Every size Windows might ask for (list view, desktop, large icons, high-DPI screens).
    static readonly int[] Sizes = { 16, 24, 32, 48, 64, 96, 128, 256 };

    static readonly string IconDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderBadges", "icons");

    static readonly Color BadgeRed = Color.FromArgb(232, 17, 35);

    /// <summary>Returns the path to the icon for this count, creating it if needed.</summary>
    public static string GetOrCreate(int count)
    {
        string text = count > 99 ? "99+" : count.ToString();
        string fileName = $"{StyleVersion}-badge-{(count > 99 ? "99plus" : text)}.ico";
        string path = Path.Combine(IconDir, fileName);

        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(IconDir);

        var frames = new List<(int Size, byte[] Png)>();
        foreach (int size in Sizes)
        {
            using var bmp = DrawFrame(size, text);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            frames.Add((size, ms.ToArray()));
        }

        WriteIco(path, frames);
        return path;
    }

    static Bitmap DrawFrame(int size, string text)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // 1. The normal Windows folder
        using (var folder = LoadWindowsFolderImage(size))
        {
            if (folder != null)
                g.DrawImage(folder, 0, 0, size, size);
            else
                DrawFallbackFolder(g, size);
        }

        // 2. At the tiniest size a number is unreadable, so just show a red dot
        if (size <= 16)
        {
            float d = size * 0.5f;
            using var red = new SolidBrush(BadgeRed);
            g.FillEllipse(Brushes.White, size - d, 0, d, d);
            g.FillEllipse(red, size - d + 1, 1, d - 2, d - 2);
            return bmp;
        }

        // 3. The red pill with the number, top-right
        float height = size * 0.46f;
        using var font = new Font("Segoe UI", height * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        SizeF textSize = g.MeasureString(text, font, PointF.Empty, format);
        float width = Math.Max(height, textSize.Width + height * 0.5f);
        var outer = new RectangleF(size - width, 0, width, height);

        float border = Math.Max(1f, size / 32f);
        var inner = RectangleF.Inflate(outer, -border, -border);

        using (var outline = Pill(outer))
            g.FillPath(Brushes.White, outline); // thin white ring so it stands out on the folder

        using (var fill = Pill(inner))
        using (var red = new SolidBrush(BadgeRed))
            g.FillPath(red, fill);

        g.DrawString(text, font, Brushes.White, inner, format);
        return bmp;
    }

    /// <summary>A rounded "pill" shape; a circle when the number is a single digit.</summary>
    static GraphicsPath Pill(RectangleF r)
    {
        var path = new GraphicsPath();
        float d = r.Height;
        if (r.Width <= d)
        {
            path.AddEllipse(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 90, 180);          // left end
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180); // right end
        path.CloseFigure();
        return path;
    }

    /// <summary>Loads the standard Windows folder icon (imageres.dll, resource 3) at a given size.</summary>
    static Bitmap? LoadWindowsFolderImage(int size)
    {
        try
        {
            string imageres = Path.Combine(Environment.SystemDirectory, "imageres.dll");
            using var icon = Icon.ExtractIcon(imageres, -3, size);
            return icon?.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A simple yellow folder, only used if the Windows icon can't be loaded.</summary>
    static void DrawFallbackFolder(Graphics g, int size)
    {
        using var back = new SolidBrush(Color.FromArgb(230, 170, 40));
        using var front = new SolidBrush(Color.FromArgb(255, 200, 70));
        g.FillRectangle(back, size * 0.08f, size * 0.22f, size * 0.38f, size * 0.12f);
        g.FillRectangle(back, size * 0.08f, size * 0.30f, size * 0.84f, size * 0.55f);
        g.FillRectangle(front, size * 0.08f, size * 0.38f, size * 0.84f, size * 0.47f);
    }

    /// <summary>Packs PNG images into a .ico file (supported since Windows Vista).</summary>
    static void WriteIco(string path, List<(int Size, byte[] Png)> frames)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // Header
        w.Write((short)0);            // reserved
        w.Write((short)1);            // type: icon
        w.Write((short)frames.Count); // number of images

        // Directory: one 16-byte entry per image
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); // width (0 means 256)
            w.Write((byte)(size >= 256 ? 0 : size)); // height
            w.Write((byte)0);   // colour palette count
            w.Write((byte)0);   // reserved
            w.Write((short)1);  // colour planes
            w.Write((short)32); // bits per pixel
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        // The images themselves
        foreach (var (_, png) in frames)
            w.Write(png);
    }
}