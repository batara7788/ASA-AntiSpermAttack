namespace Asa.App.Interface;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(10, 19, 48);
    public static readonly Color Sidebar = Color.FromArgb(6, 12, 34);
    public static readonly Color Card = Color.FromArgb(17, 31, 72);
    public static readonly Color CardHover = Color.FromArgb(28, 45, 98);
    public static readonly Color Border = Color.FromArgb(122, 96, 42);
    public static readonly Color Text = Color.FromArgb(245, 236, 214);
    public static readonly Color MutedText = Color.FromArgb(164, 172, 204);
    public static readonly Color Gold = Color.FromArgb(212, 175, 55);
    public static readonly Color PaleGold = Color.FromArgb(240, 214, 140);
    public static readonly Color Accent = Gold;
    public static readonly Color AccentSecondary = PaleGold;
    public static readonly Color Success = Color.FromArgb(46, 158, 107);
    public static readonly Color Warning = Color.FromArgb(214, 150, 46);
    public static readonly Color Danger = Color.FromArgb(178, 44, 66);

    public static readonly Font TitleFont = new("Georgia", 21f, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Georgia", 12.5f, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 10f);
    public static readonly Font SmallFont = new("Segoe UI", 8.5f);
    public static readonly Font BigNumberFont = new("Georgia", 22f, FontStyle.Bold);
    public static readonly Font NavigationFont = new("Georgia", 11f);
    public static readonly Font LogoFont = new("Georgia", 24f, FontStyle.Bold);
    public static readonly Font ButtonFont = new("Segoe UI", 10f);
    public static readonly Font ButtonFontBold = new("Segoe UI Semibold", 10f);
    public static readonly Font StatusTitleFont = new("Georgia", 19f, FontStyle.Bold);
    public static readonly Font NumeralFont = new("Georgia", 10.5f, FontStyle.Bold);
    public static readonly Font SmallCapsFont = new("Georgia", 8.5f);

    public static Button CreateButton(string text, bool primary = false)
    {
        return new GlossButton(text, primary) { Margin = new Padding(0, 0, 10, 10) };
    }

    public static Label CreateLabel(string text, Font? font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = font ?? BodyFont,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(900, 0),
            UseMnemonic = false
        };
    }

    public static CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new ImperialCheckBox
        {
            Text = text,
            Checked = isChecked,
            Margin = new Padding(0, 0, 0, 6)
        };
    }


    public static ListView CreateListView(params (string Title, int Width)[] columns)
    {
        var listView = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            BackColor = Card,
            ForeColor = Text,
            Font = BodyFont,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            HideSelection = false,
            OwnerDraw = true
        };

        foreach ((string title, int width) in columns)
        {
            listView.Columns.Add(title, width);
        }

        listView.DrawColumnHeader += (_, eventArgs) =>
        {
            using var background = new SolidBrush(Sidebar);
            eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
            using var underline = new Pen(Border);
            eventArgs.Graphics.DrawLine(underline, eventArgs.Bounds.Left, eventArgs.Bounds.Bottom - 1, eventArgs.Bounds.Right, eventArgs.Bounds.Bottom - 1);
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.Header?.Text, SmallFont, Rectangle.Inflate(eventArgs.Bounds, -6, 0), Gold, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        listView.DrawItem += (_, eventArgs) => eventArgs.DrawDefault = false;
        listView.DrawSubItem += (_, eventArgs) =>
        {
            if (eventArgs.Item == null || eventArgs.SubItem == null)
            {
                return;
            }

            Color background = eventArgs.Item.Selected ? CardHover : Card;
            using var brush = new SolidBrush(background);
            eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
            Color foreground = eventArgs.Item.ForeColor == Color.Empty || eventArgs.Item.ForeColor == SystemColors.WindowText ? Text : eventArgs.Item.ForeColor;
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.SubItem.Text, BodyFont, Rectangle.Inflate(eventArgs.Bounds, -6, 0), foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        return listView;
    }

    public static TextBox CreateTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            BackColor = Card,
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = BodyFont,
            Width = 420,
            Margin = new Padding(0, 0, 10, 10)
        };
    }

    public static FlowLayoutPanel CreateButtonRow()
    {
        return new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 4, 0, 8),
            BackColor = Color.Transparent
        };
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    public static Icon LoadApplicationIcon()
    {
        using Stream? stream = typeof(Theme).Assembly.GetManifestResourceStream("asa.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Shield;
    }
}
