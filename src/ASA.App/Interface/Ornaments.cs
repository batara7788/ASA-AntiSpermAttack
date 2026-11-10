using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public static class Ornaments
{
    private static Bitmap? damaskTile;
    private static Bitmap? sidebarTile;

    public static Bitmap DamaskTile => damaskTile ??= CreateDamaskTile(Theme.Background, Color.FromArgb(26, Theme.Gold));

    public static Bitmap SidebarTile => sidebarTile ??= CreateDamaskTile(Theme.Sidebar, Color.FromArgb(34, Theme.Gold));

    public static void FillDamask(Graphics graphics, Rectangle area, Bitmap tile, Point origin)
    {
        using var brush = new TextureBrush(tile, WrapMode.Tile);
        brush.TranslateTransform(origin.X, origin.Y);
        graphics.FillRectangle(brush, area);
    }

    public static Bitmap CreateDamaskTile(Color background, Color ornament)
    {
        const int size = 120;
        var bitmap = new Bitmap(size, size);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(background);

        using var pen = new Pen(ornament, 1.2f);
        using var brush = new SolidBrush(ornament);

        DrawLatticeDiamond(graphics, pen, new PointF(size / 2f, size / 2f), size / 2f);
        DrawFleurDeLis(graphics, brush, new PointF(size / 2f, size / 2f), 26);

        foreach (PointF corner in new[] { new PointF(0, 0), new PointF(size, 0), new PointF(0, size), new PointF(size, size) })
        {
            DrawRosette(graphics, pen, brush, corner, 11);
        }

        foreach (PointF middle in new[] { new PointF(size / 2f, 0), new PointF(size / 2f, size), new PointF(0, size / 2f), new PointF(size, size / 2f) })
        {
            graphics.FillEllipse(brush, middle.X - 2, middle.Y - 2, 4, 4);
        }

        return bitmap;
    }

    public static void DrawFleurDeLis(Graphics graphics, Brush brush, PointF center, float height)
    {
        float unit = height / 10f;
        using var path = new GraphicsPath();

        path.StartFigure();
        path.AddBezier(center.X, center.Y + unit, center.X - unit * 1.6f, center.Y - unit * 1.5f, center.X - unit * 0.6f, center.Y - unit * 4f, center.X, center.Y - unit * 5f);
        path.AddBezier(center.X, center.Y - unit * 5f, center.X + unit * 0.6f, center.Y - unit * 4f, center.X + unit * 1.6f, center.Y - unit * 1.5f, center.X, center.Y + unit);
        path.CloseFigure();

        path.StartFigure();
        path.AddBezier(center.X - unit * 0.6f, center.Y + unit * 0.4f, center.X - unit * 3.5f, center.Y - unit * 0.2f, center.X - unit * 4.6f, center.Y - unit * 3.2f, center.X - unit * 2.6f, center.Y - unit * 3.6f);
        path.AddBezier(center.X - unit * 2.6f, center.Y - unit * 3.6f, center.X - unit * 3.8f, center.Y - unit * 2f, center.X - unit * 2.2f, center.Y - unit * 0.4f, center.X - unit * 0.6f, center.Y + unit * 0.4f);
        path.CloseFigure();

        path.StartFigure();
        path.AddBezier(center.X + unit * 0.6f, center.Y + unit * 0.4f, center.X + unit * 3.5f, center.Y - unit * 0.2f, center.X + unit * 4.6f, center.Y - unit * 3.2f, center.X + unit * 2.6f, center.Y - unit * 3.6f);
        path.AddBezier(center.X + unit * 2.6f, center.Y - unit * 3.6f, center.X + unit * 3.8f, center.Y - unit * 2f, center.X + unit * 2.2f, center.Y - unit * 0.4f, center.X + unit * 0.6f, center.Y + unit * 0.4f);
        path.CloseFigure();

        path.AddRectangle(new RectangleF(center.X - unit * 2.2f, center.Y + unit * 1.1f, unit * 4.4f, unit * 0.9f));

        path.StartFigure();
        path.AddBezier(center.X - unit * 0.5f, center.Y + unit * 2f, center.X - unit * 0.7f, center.Y + unit * 3.5f, center.X - unit * 1.8f, center.Y + unit * 4.4f, center.X - unit * 2.4f, center.Y + unit * 4.6f);
        path.AddLine(center.X - unit * 2.4f, center.Y + unit * 4.6f, center.X + unit * 2.4f, center.Y + unit * 4.6f);
        path.AddBezier(center.X + unit * 2.4f, center.Y + unit * 4.6f, center.X + unit * 1.8f, center.Y + unit * 4.4f, center.X + unit * 0.7f, center.Y + unit * 3.5f, center.X + unit * 0.5f, center.Y + unit * 2f);
        path.CloseFigure();

        graphics.FillPath(brush, path);
    }

    public static void DrawCrown(Graphics graphics, RectangleF area, Color color)
    {
        using var brush = new SolidBrush(color);
        float width = area.Width;
        float height = area.Height;
        var points = new[]
        {
            new PointF(area.Left, area.Top + height * 0.35f),
            new PointF(area.Left + width * 0.25f, area.Top + height * 0.6f),
            new PointF(area.Left + width * 0.5f, area.Top + height * 0.1f),
            new PointF(area.Left + width * 0.75f, area.Top + height * 0.6f),
            new PointF(area.Right, area.Top + height * 0.35f),
            new PointF(area.Right - width * 0.08f, area.Bottom - height * 0.18f),
            new PointF(area.Left + width * 0.08f, area.Bottom - height * 0.18f)
        };
        graphics.FillPolygon(brush, points);
        graphics.FillRectangle(brush, area.Left + width * 0.08f, area.Bottom - height * 0.12f, width * 0.84f, height * 0.12f);

        float jewel = width * 0.11f;
        foreach (PointF tip in new[] { points[0], points[2], points[4] })
        {
            graphics.FillEllipse(brush, tip.X - jewel / 2, tip.Y - jewel, jewel, jewel);
        }

        using var gemBrush = new SolidBrush(Theme.Danger);
        graphics.FillEllipse(gemBrush, area.Left + width * 0.45f, area.Bottom - height * 0.42f, width * 0.1f, width * 0.1f);
    }

    public static void DrawOrnateFrame(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = Rectangle.Inflate(bounds, -1, -1);
        var inner = Rectangle.Inflate(bounds, -5, -5);
        using var thickPen = new Pen(Color.FromArgb(200, color), 1.4f);
        using var thinPen = new Pen(Color.FromArgb(110, color), 1f);
        graphics.DrawRectangle(thickPen, outer);
        graphics.DrawRectangle(thinPen, inner);

        DrawCornerFlourish(graphics, color, new PointF(outer.Left, outer.Top), 1, 1);
        DrawCornerFlourish(graphics, color, new PointF(outer.Right, outer.Top), -1, 1);
        DrawCornerFlourish(graphics, color, new PointF(outer.Left, outer.Bottom), 1, -1);
        DrawCornerFlourish(graphics, color, new PointF(outer.Right, outer.Bottom), -1, -1);
    }

    public static void DrawCornerFlourish(Graphics graphics, Color color, PointF corner, int directionX, int directionY)
    {
        const float reach = 22;
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);

        PointF Offset(float x, float y) => new(corner.X + x * directionX, corner.Y + y * directionY);

        graphics.DrawBezier(pen, Offset(0, reach), Offset(2, reach * 0.4f), Offset(reach * 0.4f, 2), Offset(reach, 0));
        graphics.DrawBezier(pen, Offset(5, reach * 0.75f), Offset(14, reach * 0.7f), Offset(reach * 0.7f, 14), Offset(reach * 0.75f, 5));

        PointF diamondCenter = Offset(9, 9);
        var diamond = new[]
        {
            new PointF(diamondCenter.X, diamondCenter.Y - 4),
            new PointF(diamondCenter.X + 4, diamondCenter.Y),
            new PointF(diamondCenter.X, diamondCenter.Y + 4),
            new PointF(diamondCenter.X - 4, diamondCenter.Y)
        };
        graphics.FillPolygon(brush, diamond);

        PointF dotHorizontal = Offset(reach + 5, 0);
        PointF dotVertical = Offset(0, reach + 5);
        graphics.FillEllipse(brush, dotHorizontal.X - 1.8f, dotHorizontal.Y - 1.8f, 3.6f, 3.6f);
        graphics.FillEllipse(brush, dotVertical.X - 1.8f, dotVertical.Y - 1.8f, 3.6f, 3.6f);
    }

    public static void DrawDivider(Graphics graphics, Rectangle area, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float middleY = area.Top + area.Height / 2f;
        float centerX = area.Left + area.Width / 2f;

        using (var fadeLeft = new LinearGradientBrush(new PointF(area.Left, middleY), new PointF(centerX - 30, middleY), Color.FromArgb(0, color), color))
        using (var leftPen = new Pen(fadeLeft, 1.2f))
        {
            graphics.DrawLine(leftPen, area.Left, middleY, centerX - 30, middleY);
        }

        using (var fadeRight = new LinearGradientBrush(new PointF(centerX + 30, middleY), new PointF(area.Right, middleY), color, Color.FromArgb(0, color)))
        using (var rightPen = new Pen(fadeRight, 1.2f))
        {
            graphics.DrawLine(rightPen, centerX + 30, middleY, area.Right, middleY);
        }

        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(color);
        graphics.DrawBezier(pen, centerX - 30, middleY, centerX - 22, middleY - 8, centerX - 12, middleY - 8, centerX - 8, middleY);
        graphics.DrawBezier(pen, centerX - 30, middleY, centerX - 22, middleY + 8, centerX - 12, middleY + 8, centerX - 8, middleY);
        graphics.DrawBezier(pen, centerX + 30, middleY, centerX + 22, middleY - 8, centerX + 12, middleY - 8, centerX + 8, middleY);
        graphics.DrawBezier(pen, centerX + 30, middleY, centerX + 22, middleY + 8, centerX + 12, middleY + 8, centerX + 8, middleY);
        graphics.FillPolygon(brush, new[]
        {
            new PointF(centerX, middleY - 6),
            new PointF(centerX + 6, middleY),
            new PointF(centerX, middleY + 6),
            new PointF(centerX - 6, middleY)
        });
    }

    private static void DrawLatticeDiamond(Graphics graphics, Pen pen, PointF center, float radius)
    {
        var points = new[]
        {
            new PointF(center.X, center.Y - radius),
            new PointF(center.X + radius, center.Y),
            new PointF(center.X, center.Y + radius),
            new PointF(center.X - radius, center.Y)
        };

        for (int index = 0; index < points.Length; index++)
        {
            PointF start = points[index];
            PointF end = points[(index + 1) % points.Length];
            var middle = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            var pull = new PointF(middle.X + (center.X - middle.X) * 0.25f, middle.Y + (center.Y - middle.Y) * 0.25f);
            graphics.DrawBezier(pen, start, pull, pull, end);
        }
    }

    private static void DrawRosette(Graphics graphics, Pen pen, Brush brush, PointF center, float radius)
    {
        for (int petal = 0; petal < 8; petal++)
        {
            double angle = Math.PI * 2 * petal / 8;
            var tip = new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius);
            graphics.DrawLine(pen, center, tip);
            graphics.FillEllipse(brush, tip.X - 1.5f, tip.Y - 1.5f, 3, 3);
        }

        graphics.DrawEllipse(pen, center.X - radius * 0.45f, center.Y - radius * 0.45f, radius * 0.9f, radius * 0.9f);
    }
}
