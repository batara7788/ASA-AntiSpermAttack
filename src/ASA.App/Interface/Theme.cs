namespace Asa.App.Interface;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 15, 26);
    public static readonly Color Sidebar = Color.FromArgb(18, 21, 36);
    public static readonly Color Card = Color.FromArgb(25, 29, 48);
    public static readonly Color CardHover = Color.FromArgb(34, 39, 64);
    public static readonly Color Border = Color.FromArgb(45, 51, 82);
    public static readonly Color Text = Color.FromArgb(232, 234, 246);
    public static readonly Color MutedText = Color.FromArgb(140, 147, 184);
    public static readonly Color Accent = Color.FromArgb(124, 58, 237);
    public static readonly Color AccentSecondary = Color.FromArgb(34, 211, 238);
    public static readonly Color Success = Color.FromArgb(34, 197, 94);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 20f);
    public static readonly Font SubtitleFont = new("Segoe UI Semibold", 12f);
    public static readonly Font BodyFont = new("Segoe UI", 10f);
    public static readonly Font SmallFont = new("Segoe UI", 8.5f);
    public static readonly Font BigNumberFont = new("Segoe UI Semibold", 22f);
    public static readonly Font NavigationFont = new("Segoe UI", 10.5f);

    public static Button CreateButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 0, 10, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Card,
            ForeColor = Text,
            Font = BodyFont,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(139, 92, 246) : CardHover;
        return button;
    }

    public static Label CreateLabel(string text, Font? font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = font ?? BodyFont,
            ForeColor = color ?? Text,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(900, 0)
        };
    }

    public static CheckBox CreateCheckBox(string text, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            ForeColor = Text,
            Font = BodyFont,
            Margin = new Padding(0, 0, 0, 8)
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
            TextRenderer.DrawText(eventArgs.Graphics, eventArgs.Header?.Text, SmallFont, Rectangle.Inflate(eventArgs.Bounds, -6, 0), MutedText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
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
