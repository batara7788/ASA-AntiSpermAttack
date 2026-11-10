using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public enum PageIcon
{
    Shield,
    Telescope,
    Chest,
    Cannon,
    Globe,
    Broom,
    Scales,
    Scroll,
    Gear
}

public static class PageIcons
{
    public static void Draw(Graphics graphics, PageIcon icon, PointF center, float size)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float unit = size / 20f;
        PointF Point(float x, float y) => new(center.X + x * unit, center.Y + y * unit);
        RectangleF Box(float left, float top, float width, float height) => new(center.X + left * unit, center.Y + top * unit, width * unit, height * unit);

        var area = new RectangleF(center.X - size / 2, center.Y - size / 2, size, size);
        using LinearGradientBrush gold = Ornaments.CreateMetallicGoldBrush(area);
        using var goldPen = new Pen(gold, Math.Max(1.4f, unit * 1.1f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var thinGoldPen = new Pen(gold, Math.Max(1f, unit * 0.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var detailPen = new Pen(Color.FromArgb(150, 40, 24, 0), Math.Max(0.9f, unit * 0.45f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var darkBrush = new SolidBrush(Color.FromArgb(170, 40, 24, 0));

        switch (icon)
        {
            case PageIcon.Shield:
            {
                using var shield = new GraphicsPath();
                shield.AddLine(Point(-7.5f, -8.5f), Point(7.5f, -8.5f));
                shield.AddBezier(Point(7.5f, -8.5f), Point(7.8f, 0f), Point(5f, 5.5f), Point(0f, 9.5f));
                shield.AddBezier(Point(0f, 9.5f), Point(-5f, 5.5f), Point(-7.8f, 0f), Point(-7.5f, -8.5f));
                shield.CloseFigure();
                graphics.FillPath(gold, shield);
                using var star = new SolidBrush(Color.FromArgb(190, 40, 24, 0));
                Ornaments.DrawStar(graphics, star, Point(0, -0.5f), 4.6f * unit, 5);
                using var inner = new GraphicsPath();
                inner.AddLine(Point(-6f, -7.1f), Point(6f, -7.1f));
                inner.AddBezier(Point(6f, -7.1f), Point(6.2f, -0.4f), Point(4f, 4.4f), Point(0f, 7.8f));
                inner.AddBezier(Point(0f, 7.8f), Point(-4f, 4.4f), Point(-6.2f, -0.4f), Point(-6f, -7.1f));
                inner.CloseFigure();
                graphics.DrawPath(detailPen, inner);
                break;
            }

            case PageIcon.Telescope:
            {
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(center.X, center.Y);
                graphics.RotateTransform(-28);
                graphics.TranslateTransform(-center.X, -center.Y);
                graphics.FillRectangle(gold, Box(-9f, -1.6f, 6f, 3.2f));
                graphics.FillRectangle(gold, Box(-3.2f, -2.2f, 6.4f, 4.4f));
                graphics.FillRectangle(gold, Box(3f, -2.9f, 6.4f, 5.8f));
                graphics.DrawLine(detailPen, Point(-3.2f, -2.2f), Point(-3.2f, 2.2f));
                graphics.DrawLine(detailPen, Point(3f, -2.9f), Point(3f, 2.9f));
                graphics.FillRectangle(darkBrush, Box(8.6f, -2.4f, 0.8f, 4.8f));
                graphics.Restore(state);
                graphics.DrawLine(goldPen, Point(0f, 1.5f), Point(-4.5f, 9.5f));
                graphics.DrawLine(goldPen, Point(0f, 1.5f), Point(4.5f, 9.5f));
                graphics.DrawLine(goldPen, Point(0f, 1.5f), Point(0f, 9.5f));
                break;
            }

            case PageIcon.Chest:
            {
                using var lid = new GraphicsPath();
                lid.AddLine(Point(-9f, -1.5f), Point(-9f, -3f));
                lid.AddBezier(Point(-9f, -3f), Point(-9f, -9f), Point(9f, -9f), Point(9f, -3f));
                lid.AddLine(Point(9f, -3f), Point(9f, -1.5f));
                lid.CloseFigure();
                graphics.FillPath(gold, lid);
                graphics.FillRectangle(gold, Box(-9f, -0.8f, 18f, 9.6f));
                graphics.DrawLine(detailPen, Point(-9f, -1.15f), Point(9f, -1.15f));
                graphics.DrawLine(detailPen, Point(-5.5f, -6.4f), Point(-5.5f, 8.8f));
                graphics.DrawLine(detailPen, Point(5.5f, -6.4f), Point(5.5f, 8.8f));
                graphics.FillRectangle(darkBrush, Box(-2f, -2.4f, 4f, 4.6f));
                graphics.FillEllipse(gold, Box(-0.8f, -1.4f, 1.6f, 1.6f));
                graphics.FillRectangle(gold, Box(-0.35f, -0.2f, 0.7f, 1.6f));
                break;
            }

            case PageIcon.Cannon:
            {
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(center.X, center.Y);
                graphics.RotateTransform(-22);
                graphics.TranslateTransform(-center.X, -center.Y);
                using (var barrel = new GraphicsPath())
                {
                    barrel.AddLine(Point(-8f, -2.8f), Point(8f, -1.8f));
                    barrel.AddLine(Point(8f, -1.8f), Point(9.4f, -2.4f));
                    barrel.AddLine(Point(9.4f, -2.4f), Point(9.4f, 2.4f));
                    barrel.AddLine(Point(9.4f, 2.4f), Point(8f, 1.8f));
                    barrel.AddLine(Point(8f, 1.8f), Point(-8f, 2.8f));
                    barrel.CloseFigure();
                    graphics.FillPath(gold, barrel);
                }

                graphics.FillEllipse(gold, Box(-10.4f, -1.6f, 3.2f, 3.2f));
                graphics.DrawLine(detailPen, Point(-4f, -2.6f), Point(-4f, 2.6f));
                graphics.DrawLine(detailPen, Point(2f, -2.2f), Point(2f, 2.2f));
                graphics.FillEllipse(darkBrush, Box(8.8f, -1f, 1.2f, 2f));
                graphics.Restore(state);

                graphics.FillEllipse(gold, Box(-6.5f, 1.5f, 8f, 8f));
                graphics.FillEllipse(darkBrush, Box(-3.5f, 4.5f, 2f, 2f));
                for (int spoke = 0; spoke < 6; spoke++)
                {
                    double angle = spoke * Math.PI / 3;
                    graphics.DrawLine(detailPen, Point(-2.5f, 5.5f), Point(-2.5f + (float)Math.Cos(angle) * 3.5f, 5.5f + (float)Math.Sin(angle) * 3.5f));
                }

                graphics.FillEllipse(gold, Box(4f, 6.8f, 2.8f, 2.8f));
                graphics.FillEllipse(gold, Box(7f, 6.8f, 2.8f, 2.8f));
                graphics.FillEllipse(gold, Box(5.5f, 4.4f, 2.8f, 2.8f));
                break;
            }

            case PageIcon.Globe:
            {
                graphics.FillEllipse(gold, Box(-7.5f, -9f, 15f, 15f));
                graphics.DrawEllipse(detailPen, Box(-3.2f, -9f, 6.4f, 15f));
                graphics.DrawLine(detailPen, Point(0f, -9f), Point(0f, 6f));
                graphics.DrawLine(detailPen, Point(-7.5f, -1.5f), Point(7.5f, -1.5f));
                graphics.DrawBezier(detailPen, Point(-6.6f, -5f), Point(-3f, -4f), Point(3f, -4f), Point(6.6f, -5f));
                graphics.DrawBezier(detailPen, Point(-6.6f, 2f), Point(-3f, 3f), Point(3f, 3f), Point(6.6f, 2f));
                graphics.DrawArc(goldPen, Box(-9.5f, -11f, 19f, 19f), 20, 140);
                graphics.DrawLine(goldPen, Point(0f, 8f), Point(0f, 9.5f));
                graphics.DrawLine(goldPen, Point(-4f, 9.6f), Point(4f, 9.6f));
                break;
            }

            case PageIcon.Broom:
            {
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(center.X, center.Y);
                graphics.RotateTransform(35);
                graphics.TranslateTransform(-center.X, -center.Y);
                graphics.DrawLine(goldPen, Point(0f, -10f), Point(0f, 1f));
                graphics.FillRectangle(gold, Box(-2.2f, 0.4f, 4.4f, 1.8f));
                using (var bristles = new GraphicsPath())
                {
                    bristles.AddLine(Point(-2.2f, 2.2f), Point(2.2f, 2.2f));
                    bristles.AddLine(Point(2.2f, 2.2f), Point(5f, 10f));
                    bristles.AddLine(Point(5f, 10f), Point(-5f, 10f));
                    bristles.CloseFigure();
                    graphics.FillPath(gold, bristles);
                }

                for (float x = -3f; x <= 3f; x += 1.5f)
                {
                    graphics.DrawLine(detailPen, Point(x * 0.4f, 3f), Point(x, 9.6f));
                }

                graphics.DrawLine(detailPen, Point(-3f, 5f), Point(3f, 5f));
                graphics.Restore(state);
                Ornaments.DrawStar(graphics, gold, Point(-7f, -5f), 2.2f * unit, 4);
                Ornaments.DrawStar(graphics, gold, Point(-4f, -9f), 1.5f * unit, 4);
                break;
            }

            case PageIcon.Scales:
            {
                graphics.DrawLine(goldPen, Point(0f, -8f), Point(0f, 8.5f));
                graphics.FillRectangle(gold, Box(-4f, 8f, 8f, 1.6f));
                graphics.FillEllipse(gold, Box(-1.3f, -9.8f, 2.6f, 2.6f));
                graphics.DrawLine(goldPen, Point(-8f, -6f), Point(8f, -6f));
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = 7f * side;
                    graphics.DrawLine(thinGoldPen, Point(x, -6f), Point(x - 3f, 1.5f));
                    graphics.DrawLine(thinGoldPen, Point(x, -6f), Point(x + 3f, 1.5f));
                    graphics.FillPie(gold, Rectangle.Round(Box(x - 3.6f, -1.2f, 7.2f, 5.4f)), 0, 180);
                }

                break;
            }

            case PageIcon.Scroll:
            {
                graphics.FillRectangle(gold, Box(-7f, -6f, 12f, 13f));
                graphics.FillEllipse(gold, Box(-8.5f, -8f, 15f, 3.6f));
                graphics.FillEllipse(gold, Box(-8.5f, 5.4f, 15f, 3.6f));
                graphics.DrawEllipse(detailPen, Box(-8.5f, -8f, 15f, 3.6f));
                graphics.DrawEllipse(detailPen, Box(-8.5f, 5.4f, 15f, 3.6f));
                for (float y = -2.5f; y <= 3.5f; y += 2f)
                {
                    graphics.DrawLine(detailPen, Point(-5f, y), Point(2f, y));
                }

                using var quill = new GraphicsPath();
                quill.AddBezier(Point(9.5f, -10f), Point(5f, -8f), Point(3f, -3f), Point(2.6f, 3f));
                quill.AddBezier(Point(2.6f, 3f), Point(5.5f, -1f), Point(8.5f, -4f), Point(9.5f, -10f));
                quill.CloseFigure();
                graphics.FillPath(gold, quill);
                graphics.DrawPath(detailPen, quill);
                graphics.DrawLine(thinGoldPen, Point(2.6f, 3f), Point(1.8f, 5.6f));
                break;
            }

            case PageIcon.Gear:
            {
                using var gear = new GraphicsPath();
                int teeth = 10;
                var points = new List<PointF>();
                for (int index = 0; index < teeth * 4; index++)
                {
                    double angle = index * Math.PI * 2 / (teeth * 4);
                    float radius = (index % 4 == 1 || index % 4 == 2) ? 9.5f : 7.4f;
                    points.Add(Point((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius));
                }

                gear.AddPolygon(points.ToArray());
                graphics.FillPath(gold, gear);
                graphics.FillEllipse(darkBrush, Box(-3.2f, -3.2f, 6.4f, 6.4f));
                graphics.DrawEllipse(detailPen, Box(-5.6f, -5.6f, 11.2f, 11.2f));
                Ornaments.DrawStar(graphics, gold, center, 2.4f * unit, 5);
                break;
            }
        }
    }
}
