namespace Asa.App.Interface.Controls;

public sealed class StatCard : Control
{
    private string value = "0";
    private Color accent;

    public StatCard(string caption, Color accentColor)
    {
        Caption = caption;
        accent = accentColor;
        DoubleBuffered = true;
        Size = new Size(240, 96);
        Margin = new Padding(0, 0, 14, 14);
        BackColor = Theme.Card;
    }

    public string Caption { get; }

    public void SetValue(string newValue, Color? newAccent = null)
    {
        value = newValue;
        if (newAccent.HasValue)
        {
            accent = newAccent.Value;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.Clear(Theme.Card);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, 0, 0, 4, Height);
        TextRenderer.DrawText(graphics, Caption, Theme.SmallFont, new Point(18, 14), Theme.MutedText);
        TextRenderer.DrawText(graphics, value, Theme.BigNumberFont, new Rectangle(14, 36, Width - 20, 50), Theme.Text, TextFormatFlags.EndEllipsis);
    }
}
