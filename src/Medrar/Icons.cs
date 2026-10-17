using System.Drawing.Drawing2D;

namespace Medrar.App;

internal enum IconKind { Logo, Add, Extract, Test, View, Delete, Find, Wizard, Info, Repair, Up, Folder, Archive }

/// <summary>
/// Todos los iconos de Medrar se dibujan aquí por código (no hay imágenes copiadas de ningún otro programa).
/// </summary>
internal static class Icons
{
    private static readonly Color Teal = Color.FromArgb(0x13, 0x8D, 0x90);
    private static readonly Color Indigo = Color.FromArgb(0x3B, 0x4C, 0xB8);
    private static readonly Color Amber = Color.FromArgb(0xF2, 0xA1, 0x1F);
    private static readonly Color Coral = Color.FromArgb(0xE0, 0x52, 0x4A);
    private static readonly Color Green = Color.FromArgb(0x2E, 0xA8, 0x6B);
    private static readonly Color Sky = Color.FromArgb(0x35, 0x9B, 0xE0);
    private static readonly Color Slate = Color.FromArgb(0x5A, 0x67, 0x7A);
    private static readonly Dictionary<(IconKind, int), Bitmap> Cache = new();

    public static Bitmap Get(IconKind kind, int size)
    {
        if (!Cache.TryGetValue((kind, size), out var bmp))
            Cache[(kind, size)] = bmp = Draw(kind, size);
        return bmp;
    }

    public static Icon AppIcon(int size = 32)
    {
        using var b = Get(IconKind.Logo, size);
        return Icon.FromHandle(new Bitmap(b).GetHicon());
    }

    private static Bitmap Draw(IconKind kind, int s)
    {
        var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        float u = s / 32f;                                  // unidad: el dibujo está pensado sobre 32x32
        g.ScaleTransform(u, u);
        switch (kind)
        {
            case IconKind.Logo: Logo(g); break;
            case IconKind.Add: Box(g, Teal); Plus(g, Amber); break;
            case IconKind.Extract: Box(g, Indigo, open: true); ArrowOut(g, Green); break;
            case IconKind.Test: Clipboard(g); break;
            case IconKind.View: Book(g); break;
            case IconKind.Delete: Bin(g); break;
            case IconKind.Find: Lens(g); break;
            case IconKind.Wizard: Wand(g); break;
            case IconKind.Info: InfoDot(g); break;
            case IconKind.Repair: Cross(g); break;
            case IconKind.Up: UpArrow(g); break;
            case IconKind.Folder: FolderShape(g); break;
            case IconKind.Archive: Box(g, Teal); break;
        }
        return bmp;
    }

    private static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void Logo(Graphics g)
    {
        using (var path = Round(new RectangleF(1, 1, 30, 30), 7))
        using (var br = new LinearGradientBrush(new RectangleF(0, 0, 32, 32), Teal, Indigo, 55f))
            g.FillPath(br, path);
        // cubo isométrico: tapa clara, cara izquierda media, cara derecha oscura
        PointF top = new(16, 6), r = new(25, 11), br2 = new(25, 22), bot = new(16, 27), bl = new(7, 22), l = new(7, 11), c = new(16, 16);
        FillPoly(g, Color.FromArgb(245, 255, 255, 255), top, r, c, l);
        FillPoly(g, Color.FromArgb(205, 255, 255, 255), l, c, bot, bl);
        FillPoly(g, Color.FromArgb(150, 255, 255, 255), c, r, br2, bot);
        using var zip = new Pen(Color.FromArgb(230, Amber), 1.7f) { DashStyle = DashStyle.Dash };
        g.DrawLine(zip, 16, 16, 16, 27);
    }

    private static void FillPoly(Graphics g, Color c, params PointF[] pts)
    {
        using var b = new SolidBrush(c);
        g.FillPolygon(b, pts);
    }

    private static void Box(Graphics g, Color color, bool open = false)
    {
        PointF top = new(15, 5), r = new(26, 10), br = new(26, 22), bot = new(15, 27), bl = new(4, 22), l = new(4, 10), c = new(15, 15);
        FillPoly(g, Lighten(color, .35f), top, r, c, l);
        FillPoly(g, color, l, c, bot, bl);
        FillPoly(g, Darken(color, .25f), c, r, br, bot);
        if (open) { using var p = new Pen(Color.White, 1.4f); g.DrawLine(p, 4, 10, 15, 15); g.DrawLine(p, 15, 15, 26, 10); }
    }

    private static void Plus(Graphics g, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 17, 1, 14, 14);
        using var p = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p, 24, 4.5f, 24, 11.5f);
        g.DrawLine(p, 20.5f, 8, 27.5f, 8);
    }

    private static void ArrowOut(Graphics g, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, 17, 1, 14, 14);
        using var p = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLine(p, 20.5f, 11.5f, 27.5f, 4.5f);
        g.DrawLines(p, new PointF[] { new(21.5f, 4.5f), new(27.5f, 4.5f), new(27.5f, 10.5f) });
    }

    private static void Clipboard(Graphics g)
    {
        using (var path = Round(new RectangleF(5, 5, 22, 25), 3))
        using (var b = new SolidBrush(Color.FromArgb(240, 240, 244))) { g.FillPath(b, path); using var p = new Pen(Slate, 1.5f); g.DrawPath(p, path); }
        using (var b = new SolidBrush(Slate)) g.FillRectangle(b, 11, 2.5f, 10, 5);
        using var tick = new Pen(Green, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(tick, new PointF[] { new(10, 18), new(14.5f, 22.5f), new(22.5f, 13) });
    }

    private static void Book(Graphics g)
    {
        using var left = new SolidBrush(Color.FromArgb(250, 250, 252));
        using var edge = new Pen(Slate, 1.5f);
        var l = new PointF[] { new(16, 8), new(5, 6), new(5, 25), new(16, 27) };
        var r = new PointF[] { new(16, 8), new(27, 6), new(27, 25), new(16, 27) };
        g.FillPolygon(left, l); g.FillPolygon(left, r);
        g.DrawPolygon(edge, l); g.DrawPolygon(edge, r);
        using var line = new Pen(Sky, 1.4f);
        for (int i = 0; i < 3; i++) { g.DrawLine(line, 8, 11 + i * 4, 13.5f, 12 + i * 4); g.DrawLine(line, 18.5f, 12 + i * 4, 24, 11 + i * 4); }
        using var mark = new SolidBrush(Coral);
        g.FillRectangle(mark, 22, 5.2f, 3, 8);
    }

    private static void Bin(Graphics g)
    {
        using var b = new SolidBrush(Slate);
        using var lid = new Pen(Slate, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(lid, 6, 8, 26, 8);
        g.FillRectangle(b, 12.5f, 4, 7, 3);
        using (var path = Round(new RectangleF(8, 10, 16, 18), 2.2f)) g.FillPath(b, path);
        using var slot = new Pen(Color.FromArgb(235, 235, 240), 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int x = 12; x <= 20; x += 4) g.DrawLine(slot, x, 14, x, 24);
    }

    private static void Lens(Graphics g)
    {
        using var handle = new Pen(Slate, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(handle, 20, 20, 28, 28);
        using var fill = new SolidBrush(Color.FromArgb(160, 190, 230, 255));
        g.FillEllipse(fill, 4, 4, 18, 18);
        using var ring = new Pen(Sky, 3f);
        g.DrawEllipse(ring, 4, 4, 18, 18);
        using var shine = new Pen(Color.FromArgb(200, 255, 255, 255), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(shine, 8, 8, 10, 10, 200, 70);
    }

    private static void Wand(Graphics g)
    {
        using var stick = new Pen(Slate, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(stick, 6, 27, 20, 13);
        using var tip = new Pen(Amber, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(tip, 17, 16, 20, 13);
        Star(g, 24, 8, 6, Amber);
        Star(g, 10, 8, 3, Sky);
        Star(g, 26, 22, 2.6f, Green);
    }

    private static void Star(Graphics g, float cx, float cy, float r, Color c)
    {
        var pts = new List<PointF>();
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI / 4 * i - Math.PI / 2;
            float rr = i % 2 == 0 ? r : r * .35f;
            pts.Add(new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr));
        }
        using var b = new SolidBrush(c);
        g.FillPolygon(b, pts.ToArray());
    }

    private static void InfoDot(Graphics g)
    {
        using var b = new SolidBrush(Sky);
        g.FillEllipse(b, 3, 3, 26, 26);
        using var w = new SolidBrush(Color.White);
        g.FillEllipse(w, 14, 7.5f, 4, 4);
        using var p = new Pen(Color.White, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(p, 16, 14.5f, 16, 24);
    }

    private static void Cross(Graphics g)
    {
        using var b = new SolidBrush(Green);
        g.FillRectangle(b, 12, 4, 8, 24);
        g.FillRectangle(b, 4, 12, 24, 8);
        using var hi = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f);
        g.DrawLine(hi, 13, 5, 13, 27);
        using var strap = new SolidBrush(Color.FromArgb(240, Color.White));
        g.FillEllipse(strap, 13.5f, 13.5f, 5, 5);
    }

    private static void UpArrow(Graphics g)
    {
        using var p = new Pen(Slate, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLine(p, 16, 27, 16, 6);
        g.DrawLines(p, new PointF[] { new(7, 14), new(16, 5), new(25, 14) });
    }

    private static void FolderShape(Graphics g)
    {
        using var back = new SolidBrush(Color.FromArgb(0xD9, 0x9A, 0x1B));
        using var front = new SolidBrush(Amber);
        using (var tab = Round(new RectangleF(3, 6, 12, 8), 2)) g.FillPath(back, tab);
        using (var body = Round(new RectangleF(3, 10, 26, 17), 2.5f)) g.FillPath(front, body);
    }

    private static Color Lighten(Color c, float f) => Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
    private static Color Darken(Color c, float f) => Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));

    /// <summary>Escribe un .ico con varios tamaños (cada uno como PNG).</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int sz in sizes)
        {
            using var bmp = Draw(IconKind.Logo, sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }
}
