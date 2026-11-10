using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public static class Ornaments
{
    private static Bitmap? damaskTile;
    private static Bitmap? sidebarTile;

    public static Bitmap DamaskTile => damaskTile ??= CreateEmpireTile(Theme.Background, Color.FromArgb(24, Theme.Gold));

    public static Bitmap SidebarTile => sidebarTile ??= CreateEmpireTile(Theme.Sidebar, Color.FromArgb(30, Theme.Gold));

    public static void FillDamask(Graphics graphics, Rectangle area, Bitmap tile, Point origin)
    {
        using var brush = new TextureBrush(tile, WrapMode.Tile);
        brush.TranslateTransform(origin.X, origin.Y);
        graphics.FillRectangle(brush, area);
    }

    public static void FillBackgroundFor(Control control, Graphics graphics, Rectangle area, Bitmap tile)
    {
        Form? form = control.FindForm();
        Point origin = Point.Empty;
        if (form != null && control.IsHandleCreated && form.IsHandleCreated)
        {
            Point position = form.PointToClient(control.PointToScreen(Point.Empty));
            origin = new Point(-position.X, -position.Y);
        }

        FillDamask(graphics, area, tile, origin);
    }

    public static void DrawSpacedText(Graphics graphics, string text, Font font, PointF origin, float spacing)
    {
        using var format = StringFormat.GenericTypographic;
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        float x = origin.X;
        var widths = new List<float>();
        foreach (char character in text)
        {
            widths.Add(graphics.MeasureString(character.ToString(), font, PointF.Empty, format).Width);
        }

        float totalWidth = widths.Sum() + spacing * Math.Max(0, text.Length - 1);
        var area = new RectangleF(origin.X, origin.Y, Math.Max(1, totalWidth), font.GetHeight(graphics) + 2);
        using LinearGradientBrush gold = CreateMetallicGoldBrush(area);
        using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        for (int index = 0; index < text.Length; index++)
        {
            string character = text[index].ToString();
            graphics.DrawString(character, font, shadow, x + 1.5f, origin.Y + 2f, format);
            graphics.DrawString(character, font, gold, x, origin.Y, format);
            x += widths[index] + spacing;
        }
    }

    public static Bitmap CreateEmpireTile(Color background, Color ornament)
    {
        const int size = 128;
        var bitmap = new Bitmap(size, size);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(background);

        using var brush = new SolidBrush(ornament);
        using var pen = new Pen(ornament, 1f);

        DrawBee(graphics, brush, pen, new PointF(size / 2f, size / 2f), 26);
        foreach (PointF corner in new[] { new PointF(0, 0), new PointF(size, 0), new PointF(0, size), new PointF(size, size) })
        {
            DrawStar(graphics, brush, corner, 7, 5);
        }

        foreach (PointF middle in new[] { new PointF(size / 2f, 0), new PointF(size / 2f, size), new PointF(0, size / 2f), new PointF(size, size / 2f) })
        {
            graphics.FillEllipse(brush, middle.X - 1.6f, middle.Y - 1.6f, 3.2f, 3.2f);
        }

        return bitmap;
    }

    public static LinearGradientBrush CreateMetallicGoldBrush(RectangleF area, bool vertical = true)
    {
        var brush = new LinearGradientBrush(area, Theme.Gold, Theme.Gold, vertical ? LinearGradientMode.Vertical : LinearGradientMode.Horizontal);
        brush.InterpolationColors = new ColorBlend
        {
            Colors = new[]
            {
                Color.FromArgb(255, 248, 214),
                Color.FromArgb(232, 196, 92),
                Color.FromArgb(160, 118, 34),
                Color.FromArgb(222, 182, 76),
                Color.FromArgb(120, 86, 24)
            },
            Positions = new[] { 0f, 0.3f, 0.55f, 0.8f, 1f }
        };
        return brush;
    }

    public static GraphicsPath CreateRoundedRectangle(RectangleF area, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
        path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 90);
        path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(area.Left, area.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawGlassPanel(Graphics graphics, Rectangle bounds, Color baseColor, float radius = 10, bool withLaurels = true)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new RectangleF(bounds.X + 1.5f, bounds.Y + 1.5f, bounds.Width - 3, bounds.Height - 3);
        using GraphicsPath shape = CreateRoundedRectangle(area, radius);

        Color top = Blend(baseColor, Color.White, 0.10f);
        Color bottom = Blend(baseColor, Color.Black, 0.25f);
        using (var bodyBrush = new LinearGradientBrush(area, top, bottom, LinearGradientMode.Vertical))
        {
            graphics.FillPath(bodyBrush, shape);
        }

        var shineArea = new RectangleF(area.X + 2, area.Y + 2, area.Width - 4, Math.Min(46, area.Height * 0.45f));
        using (GraphicsPath shine = CreateRoundedRectangle(shineArea, Math.Max(2, radius - 2)))
        using (var shineBrush = new LinearGradientBrush(shineArea, Color.FromArgb(46, Color.White), Color.FromArgb(4, Color.White), LinearGradientMode.Vertical))
        {
            graphics.FillPath(shineBrush, shine);
        }

        using (var innerPen = new Pen(Color.FromArgb(40, Color.White), 1f))
        using (GraphicsPath inner = CreateRoundedRectangle(RectangleF.Inflate(area, -3, -3), Math.Max(2, radius - 3)))
        {
            graphics.DrawPath(innerPen, inner);
        }

        using (LinearGradientBrush goldBrush = CreateMetallicGoldBrush(area))
        using (var goldPen = new Pen(goldBrush, 1.8f))
        {
            graphics.DrawPath(goldPen, shape);
        }

        if (withLaurels && bounds.Width > 120 && bounds.Height > 70)
        {
            DrawLaurelCorner(graphics, new PointF(area.Left + 8, area.Top + 8), 1, 1);
            DrawLaurelCorner(graphics, new PointF(area.Right - 8, area.Top + 8), -1, 1);
            DrawLaurelCorner(graphics, new PointF(area.Left + 8, area.Bottom - 8), 1, -1);
            DrawLaurelCorner(graphics, new PointF(area.Right - 8, area.Bottom - 8), -1, -1);
        }
    }

    public static void DrawOrnateFrame(Graphics graphics, Rectangle bounds, Color color)
    {
        DrawGlassPanel(graphics, bounds, Theme.Card);
    }

    public static void DrawLaurelCorner(Graphics graphics, PointF corner, int directionX, int directionY)
    {
        using var leafBrush = new SolidBrush(Color.FromArgb(220, Theme.Gold));
        using var stemPen = new Pen(Color.FromArgb(220, Theme.Gold), 1.1f);
        PointF Offset(float x, float y) => new(corner.X + x * directionX, corner.Y + y * directionY);

        DrawSprig(graphics, leafBrush, stemPen, Offset(2, 2), Offset(34, 4), Offset);
        DrawSprig(graphics, leafBrush, stemPen, Offset(2, 2), Offset(4, 34), Offset);
        graphics.FillEllipse(leafBrush, Offset(2, 2).X - 2.6f, Offset(2, 2).Y - 2.6f, 5.2f, 5.2f);
    }

    private static void DrawSprig(Graphics graphics, Brush brush, Pen stemPen, PointF start, PointF end, Func<float, float, PointF> offset)
    {
        graphics.DrawLine(stemPen, start, end);
        float deltaX = end.X - start.X;
        float deltaY = end.Y - start.Y;
        float length = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        float directionX = deltaX / length;
        float directionY = deltaY / length;
        float normalX = -directionY;
        float normalY = directionX;

        for (int index = 0; index < 4; index++)
        {
            float t = 0.22f + index * 0.21f;
            float leafLength = 8.5f - index * 1.2f;
            var basePoint = new PointF(start.X + deltaX * t, start.Y + deltaY * t);
            for (int side = -1; side <= 1; side += 2)
            {
                var tip = new PointF(
                    basePoint.X + (directionX * 0.75f + normalX * side * 0.65f) * leafLength,
                    basePoint.Y + (directionY * 0.75f + normalY * side * 0.65f) * leafLength);
                DrawLeaf(graphics, brush, basePoint, tip, leafLength * 0.26f);
            }
        }

        DrawLeaf(graphics, brush, end, new PointF(end.X + directionX * 6, end.Y + directionY * 6), 1.6f);
    }

    public static void DrawLeaf(Graphics graphics, Brush brush, PointF start, PointF end, float halfWidth)
    {
        float deltaX = end.X - start.X;
        float deltaY = end.Y - start.Y;
        float length = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length < 0.1f)
        {
            return;
        }

        float normalX = -deltaY / length * halfWidth;
        float normalY = deltaX / length * halfWidth;
        var middle = new PointF((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        using var path = new GraphicsPath();
        path.AddBezier(start, new PointF(middle.X + normalX * 1.6f, middle.Y + normalY * 1.6f), new PointF(middle.X + normalX * 1.2f, middle.Y + normalY * 1.2f), end);
        path.AddBezier(end, new PointF(middle.X - normalX * 1.2f, middle.Y - normalY * 1.2f), new PointF(middle.X - normalX * 1.6f, middle.Y - normalY * 1.6f), start);
        graphics.FillPath(brush, path);
    }

    public static void DrawLaurelBranch(Graphics graphics, Brush brush, Pen stemPen, PointF start, PointF end, int leafCount, float leafSize, float curve)
    {
        var control = new PointF((start.X + end.X) / 2 + curve, (start.Y + end.Y) / 2 - Math.Abs(curve) * 0.4f);
        graphics.DrawBezier(stemPen, start, control, control, end);

        for (int index = 1; index <= leafCount; index++)
        {
            float t = index / (float)(leafCount + 1);
            PointF point = QuadraticPoint(start, control, end, t);
            PointF ahead = QuadraticPoint(start, control, end, Math.Min(1, t + 0.05f));
            float directionX = ahead.X - point.X;
            float directionY = ahead.Y - point.Y;
            float length = MathF.Max(0.01f, MathF.Sqrt(directionX * directionX + directionY * directionY));
            directionX /= length;
            directionY /= length;
            float sideX = -directionY;
            float sideY = directionX;
            float size = leafSize * (0.7f + 0.3f * (1 - Math.Abs(t - 0.5f) * 2));

            DrawLeaf(graphics, brush, point, new PointF(point.X + (directionX * 0.6f + sideX) * size, point.Y + (directionY * 0.6f + sideY) * size), size * 0.32f);
            DrawLeaf(graphics, brush, point, new PointF(point.X + (directionX * 0.6f - sideX) * size, point.Y + (directionY * 0.6f - sideY) * size), size * 0.32f);
        }

        DrawLeaf(graphics, brush, end, new PointF(end.X + (end.X - control.X) * 0.15f, end.Y + (end.Y - control.Y) * 0.15f), leafSize * 0.3f);
    }

    public static void DrawLaurelWreath(Graphics graphics, PointF center, float radius, Brush brush)
    {
        using var stemPen = new Pen(brush, Math.Max(1f, radius / 30f));
        int leaves = 11;
        for (int side = -1; side <= 1; side += 2)
        {
            PointF previous = PointOnArc(center, radius, 100);
            for (int index = 1; index <= leaves; index++)
            {
                float angle = 100 + index * (140f / leaves);
                PointF current = PointOnArc(center, radius, angle);
                var mirroredPrevious = new PointF(center.X + (previous.X - center.X) * side, previous.Y);
                var mirroredCurrent = new PointF(center.X + (current.X - center.X) * side, current.Y);
                graphics.DrawLine(stemPen, mirroredPrevious, mirroredCurrent);

                float tangentX = mirroredCurrent.X - mirroredPrevious.X;
                float tangentY = mirroredCurrent.Y - mirroredPrevious.Y;
                float outwardX = mirroredCurrent.X - center.X;
                float outwardY = mirroredCurrent.Y - center.Y;
                float outwardLength = MathF.Max(0.01f, MathF.Sqrt(outwardX * outwardX + outwardY * outwardY));
                outwardX /= outwardLength;
                outwardY /= outwardLength;
                float leafLength = radius * 0.32f;
                DrawLeaf(graphics, brush, mirroredCurrent, new PointF(mirroredCurrent.X + outwardX * leafLength + tangentX * 0.5f, mirroredCurrent.Y + outwardY * leafLength + tangentY * 0.5f), leafLength * 0.3f);
                DrawLeaf(graphics, brush, mirroredCurrent, new PointF(mirroredCurrent.X - outwardX * leafLength * 0.8f + tangentX * 0.5f, mirroredCurrent.Y - outwardY * leafLength * 0.8f + tangentY * 0.5f), leafLength * 0.26f);
                previous = current;
            }
        }

    }

    public static void DrawImperialEagle(Graphics graphics, PointF center, float size, Brush brush)
    {
        float unit = size / 20f;
        PointF Point(float x, float y) => new(center.X + x * unit, center.Y + y * unit);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var detailPen = new Pen(Color.FromArgb(110, 30, 18, 0), Math.Max(0.8f, unit * 0.35f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (int side = -1; side <= 1; side += 2)
        {
            float s = side;
            using var wing = new GraphicsPath();
            wing.AddBezier(Point(1.4f * s, -3.2f), Point(3.2f * s, -6.8f), Point(5.8f * s, -9.6f), Point(9.4f * s, -10.2f));
            wing.AddBezier(Point(9.4f * s, -10.2f), Point(9.9f * s, -9.2f), Point(9.6f * s, -8.4f), Point(8.9f * s, -7.9f));
            wing.AddBezier(Point(8.9f * s, -7.9f), Point(10f * s, -7.2f), Point(9.8f * s, -6f), Point(8.9f * s, -5.6f));
            wing.AddBezier(Point(8.9f * s, -5.6f), Point(9.8f * s, -4.8f), Point(9.4f * s, -3.6f), Point(8.4f * s, -3.3f));
            wing.AddBezier(Point(8.4f * s, -3.3f), Point(9.1f * s, -2.4f), Point(8.6f * s, -1.3f), Point(7.5f * s, -1.1f));
            wing.AddBezier(Point(7.5f * s, -1.1f), Point(8f * s, -0.2f), Point(7.4f * s, 0.8f), Point(6.2f * s, 0.8f));
            wing.AddBezier(Point(6.2f * s, 0.8f), Point(4.8f * s, 1.1f), Point(3f * s, 0.6f), Point(1.6f * s, 1.4f));
            wing.CloseFigure();
            graphics.FillPath(brush, wing);

            graphics.DrawBezier(detailPen, Point(2.4f * s, -3.4f), Point(4f * s, -5.2f), Point(6f * s, -7f), Point(8.6f * s, -8f));
            graphics.DrawBezier(detailPen, Point(2.6f * s, -2.2f), Point(4.4f * s, -3.6f), Point(6.4f * s, -4.8f), Point(8.6f * s, -5.6f));
            graphics.DrawBezier(detailPen, Point(2.6f * s, -1f), Point(4.4f * s, -1.9f), Point(6.2f * s, -2.6f), Point(8.1f * s, -3.3f));
            graphics.DrawBezier(detailPen, Point(2.4f * s, 0.2f), Point(4f * s, -0.3f), Point(5.6f * s, -0.7f), Point(7.2f * s, -1.1f));
        }

        using (var body = new GraphicsPath())
        {
            body.AddBezier(Point(0f, -4.8f), Point(2.4f, -4.6f), Point(2.8f, 1.6f), Point(1.6f, 4.6f));
            body.AddBezier(Point(1.6f, 4.6f), Point(2.6f, 6f), Point(3.4f, 7.6f), Point(3.2f, 8.8f));
            body.AddBezier(Point(3.2f, 8.8f), Point(2.2f, 8f), Point(1.4f, 7.8f), Point(0.9f, 8f));
            body.AddBezier(Point(0.9f, 8f), Point(0.8f, 8.8f), Point(0.4f, 9.6f), Point(0f, 10f));
            body.AddBezier(Point(0f, 10f), Point(-0.4f, 9.6f), Point(-0.8f, 8.8f), Point(-0.9f, 8f));
            body.AddBezier(Point(-0.9f, 8f), Point(-1.4f, 7.8f), Point(-2.2f, 8f), Point(-3.2f, 8.8f));
            body.AddBezier(Point(-3.2f, 8.8f), Point(-3.4f, 7.6f), Point(-2.6f, 6f), Point(-1.6f, 4.6f));
            body.AddBezier(Point(-1.6f, 4.6f), Point(-2.8f, 1.6f), Point(-2.4f, -4.6f), Point(0f, -4.8f));
            body.CloseFigure();
            graphics.FillPath(brush, body);
        }

        graphics.DrawBezier(detailPen, Point(-1f, -1.6f), Point(-0.4f, -1.1f), Point(0.4f, -1.1f), Point(1f, -1.6f));
        graphics.DrawBezier(detailPen, Point(-1.1f, 0.4f), Point(-0.4f, 0.9f), Point(0.4f, 0.9f), Point(1.1f, 0.4f));
        graphics.DrawBezier(detailPen, Point(-1f, 2.4f), Point(-0.4f, 2.9f), Point(0.4f, 2.9f), Point(1f, 2.4f));
        graphics.DrawLine(detailPen, Point(0f, 5.6f), Point(0f, 8.6f));
        graphics.DrawLine(detailPen, Point(-1.2f, 5.8f), Point(-2.2f, 7.8f));
        graphics.DrawLine(detailPen, Point(1.2f, 5.8f), Point(2.2f, 7.8f));

        using (var head = new GraphicsPath())
        {
            head.AddBezier(Point(1.1f, -4.2f), Point(1.3f, -6.4f), Point(0.4f, -8.2f), Point(-1.4f, -8.4f));
            head.AddBezier(Point(-1.4f, -8.4f), Point(-2.6f, -8.5f), Point(-3.5f, -8f), Point(-4.2f, -7.3f));
            head.AddBezier(Point(-4.2f, -7.3f), Point(-4.1f, -6.6f), Point(-3.7f, -6.3f), Point(-3.2f, -6.5f));
            head.AddBezier(Point(-3.2f, -6.5f), Point(-2.6f, -6.4f), Point(-2.2f, -6f), Point(-1.9f, -5.3f));
            head.AddBezier(Point(-1.9f, -5.3f), Point(-1.6f, -4.6f), Point(-1.2f, -4.2f), Point(-0.8f, -3.8f));
            head.CloseFigure();
            graphics.FillPath(brush, head);
        }

        using (var eyeBrush = new SolidBrush(Color.FromArgb(200, 30, 18, 0)))
        {
            PointF eye = Point(-1.8f, -7.3f);
            float eyeSize = Math.Max(1.2f, unit * 0.7f);
            graphics.FillEllipse(eyeBrush, eye.X - eyeSize / 2, eye.Y - eyeSize / 2, eyeSize, eyeSize);
        }

        graphics.DrawBezier(detailPen, Point(-4.1f, -7.2f), Point(-3.4f, -7.1f), Point(-2.9f, -7f), Point(-2.5f, -6.7f));

        using var boltPen = new Pen(brush, Math.Max(1.2f, unit * 0.75f)) { LineJoin = LineJoin.Miter, StartCap = LineCap.Triangle, EndCap = LineCap.Triangle };
        graphics.DrawLines(boltPen, new[] { Point(-6.2f, 7f), Point(-2.2f, 7.8f), Point(-3f, 6.9f), Point(3f, 7.7f), Point(2.2f, 6.8f), Point(6.2f, 7.6f) });
    }

    public static void DrawImperialEmblem(Graphics graphics, PointF center, float size)
    {
        var area = new RectangleF(center.X - size / 2, center.Y - size / 2, size, size);
        using LinearGradientBrush gold = CreateMetallicGoldBrush(area);
        DrawLaurelWreath(graphics, new PointF(center.X, center.Y + size * 0.04f), size * 0.46f, gold);
        DrawImperialEagle(graphics, new PointF(center.X, center.Y + size * 0.02f), size * 0.62f, gold);
    }

    public static void DrawBee(Graphics graphics, Brush brush, Pen pen, PointF center, float size)
    {
        float unit = size / 10f;
        graphics.FillEllipse(brush, center.X - unit * 1.1f, center.Y - unit * 3.6f, unit * 2.2f, unit * 1.8f);
        graphics.FillEllipse(brush, center.X - unit * 1.5f, center.Y - unit * 1.9f, unit * 3f, unit * 2.6f);

        using (var abdomen = new GraphicsPath())
        {
            abdomen.AddBezier(center.X - unit * 1.6f, center.Y + unit * 0.7f, center.X - unit * 1.9f, center.Y + unit * 3.4f, center.X - unit * 0.4f, center.Y + unit * 4.8f, center.X, center.Y + unit * 5.2f);
            abdomen.AddBezier(center.X, center.Y + unit * 5.2f, center.X + unit * 0.4f, center.Y + unit * 4.8f, center.X + unit * 1.9f, center.Y + unit * 3.4f, center.X + unit * 1.6f, center.Y + unit * 0.7f);
            abdomen.CloseFigure();
            graphics.FillPath(brush, abdomen);
        }

        for (int side = -1; side <= 1; side += 2)
        {
            graphics.DrawEllipse(pen, center.X + (side < 0 ? -unit * 4.6f : unit * 1.0f), center.Y - unit * 2.6f, unit * 3.6f, unit * 2f);
            graphics.DrawEllipse(pen, center.X + (side < 0 ? -unit * 3.9f : unit * 1.0f), center.Y - unit * 0.6f, unit * 2.9f, unit * 1.6f);
            graphics.DrawLine(pen, center.X + side * unit * 0.4f, center.Y - unit * 3.4f, center.X + side * unit * 1.3f, center.Y - unit * 4.6f);
        }
    }

    public static void DrawStar(Graphics graphics, Brush brush, PointF center, float radius, int points)
    {
        var vertices = new PointF[points * 2];
        for (int index = 0; index < vertices.Length; index++)
        {
            double angle = -Math.PI / 2 + Math.PI * index / points;
            float distance = index % 2 == 0 ? radius : radius * 0.42f;
            vertices[index] = new PointF(center.X + (float)Math.Cos(angle) * distance, center.Y + (float)Math.Sin(angle) * distance);
        }

        graphics.FillPolygon(brush, vertices);
    }

    public static void DrawMeanderBand(Graphics graphics, RectangleF area, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.None;
        using var pen = new Pen(color, 1.5f);
        float height = area.Height;
        float step = height;
        graphics.DrawLine(pen, area.Left, area.Top, area.Right, area.Top);
        graphics.DrawLine(pen, area.Left, area.Bottom, area.Right, area.Bottom);
        float inset = height * 0.22f;
        float top = area.Top + inset;
        float bottom = area.Bottom - inset;
        float middleHeight = bottom - top;
        for (float x = area.Left + 2; x + step <= area.Right; x += step)
        {
            var points = new[]
            {
                new PointF(x, bottom),
                new PointF(x, top),
                new PointF(x + step * 0.75f, top),
                new PointF(x + step * 0.75f, top + middleHeight * 0.66f),
                new PointF(x + step * 0.3f, top + middleHeight * 0.66f),
                new PointF(x + step * 0.3f, top + middleHeight * 0.33f),
                new PointF(x + step * 0.5f, top + middleHeight * 0.33f)
            };
            graphics.DrawLines(pen, points);
            graphics.DrawLine(pen, x, bottom, x + step, bottom);
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
    }

    public static void DrawPalmette(Graphics graphics, Brush brush, PointF baseCenter, float height)
    {
        int petals = 7;
        for (int index = 0; index < petals; index++)
        {
            float angle = -150 + index * (120f / (petals - 1));
            double radians = angle * Math.PI / 180;
            float length = height * (index == petals / 2 ? 1f : 0.82f - Math.Abs(index - petals / 2) * 0.06f);
            var tip = new PointF(baseCenter.X + (float)Math.Cos(radians) * length, baseCenter.Y + (float)Math.Sin(radians) * length);
            DrawLeaf(graphics, brush, baseCenter, tip, height * 0.1f);
        }

        graphics.FillEllipse(brush, baseCenter.X - height * 0.14f, baseCenter.Y - height * 0.1f, height * 0.28f, height * 0.2f);
    }

    public static void DrawDivider(Graphics graphics, Rectangle area, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float middleY = area.Top + area.Height / 2f;
        float centerX = area.Left + area.Width / 2f;

        using (var fadeLeft = new LinearGradientBrush(new PointF(area.Left - 1, middleY), new PointF(centerX - 44, middleY), Color.FromArgb(0, color), color))
        using (var leftPen = new Pen(fadeLeft, 1.2f))
        {
            graphics.DrawLine(leftPen, area.Left, middleY, centerX - 44, middleY);
        }

        using (var fadeRight = new LinearGradientBrush(new PointF(centerX + 44, middleY), new PointF(area.Right + 1, middleY), color, Color.FromArgb(0, color)))
        using (var rightPen = new Pen(fadeRight, 1.2f))
        {
            graphics.DrawLine(rightPen, centerX + 44, middleY, area.Right, middleY);
        }

        using var brush = new SolidBrush(color);
        using var stemPen = new Pen(color, 1.1f);
        DrawLaurelBranch(graphics, brush, stemPen, new PointF(centerX - 8, middleY), new PointF(centerX - 44, middleY), 4, 7, 0);
        DrawLaurelBranch(graphics, brush, stemPen, new PointF(centerX + 8, middleY), new PointF(centerX + 44, middleY), 4, 7, 0);
        DrawStar(graphics, brush, new PointF(centerX, middleY), 6, 5);
    }

    private static PointF QuadraticPoint(PointF start, PointF control, PointF end, float t)
    {
        float inverse = 1 - t;
        return new PointF(
            inverse * inverse * start.X + 2 * inverse * t * control.X + t * t * end.X,
            inverse * inverse * start.Y + 2 * inverse * t * control.Y + t * t * end.Y);
    }

    private static PointF PointOnArc(PointF center, float radius, float angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new PointF(center.X + (float)Math.Cos(radians) * radius, center.Y + (float)Math.Sin(radians) * radius);
    }

    private static Color Blend(Color first, Color second, float amount)
    {
        return Color.FromArgb(
            first.A,
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));
    }
}
