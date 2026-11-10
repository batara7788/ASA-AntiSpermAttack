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
        Size = new Size(206, 100);
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
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);
        Ornaments.DrawOrnateFrame(graphics, ClientRectangle, Theme.Gold);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillEllipse(accentBrush, 30, 27, 8, 8);
        TextRenderer.DrawText(graphics, Caption, Theme.SmallFont, new Rectangle(42, 22, Width - 56, 18), Theme.Text, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, value, Theme.BigNumberFont, new Rectangle(26, 42, Width - 50, 44), Theme.PaleGold, TextFormatFlags.EndEllipsis);
    }
}
