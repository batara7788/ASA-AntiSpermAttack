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
        Ornaments.DrawOrnateFrame(graphics, ClientRectangle, Theme.Gold);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillEllipse(accentBrush, 22, 20, 9, 9);
        TextRenderer.DrawText(graphics, Caption, Theme.SmallFont, new Point(36, 16), Theme.MutedText);
        TextRenderer.DrawText(graphics, value, Theme.BigNumberFont, new Rectangle(20, 38, Width - 30, 48), Theme.PaleGold, TextFormatFlags.EndEllipsis);
    }
}
