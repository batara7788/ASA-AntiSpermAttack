using Asa.App.Services;

namespace Asa.App.Interface.Pages;

public abstract class PageBase : UserControl
{
    protected PageBase(ProtectionHost host, string title, string description)
    {
        Host = host;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(28, 24, 28, 20);

        Content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Theme.Background
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            BackColor = Theme.Background,
            Padding = new Padding(0, 0, 0, 12)
        };
        header.Controls.Add(Theme.CreateLabel(title, Theme.TitleFont));
        header.Controls.Add(Theme.CreateLabel(description, Theme.BodyFont, Theme.MutedText));

        Controls.Add(Content);
        Controls.Add(header);
    }

    protected ProtectionHost Host { get; }

    protected FlowLayoutPanel Content { get; }

    public virtual void OnShown()
    {
    }

    protected void RunOnInterface(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    protected Panel CreateFillPanel(int height)
    {
        var panel = new Panel { Height = height, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 12) };
        Content.Resize += (_, _) => panel.Width = Content.ClientSize.Width - 24;
        panel.Width = Math.Max(600, Content.ClientSize.Width - 24);
        return panel;
    }

    protected void StretchToContentWidth(Control control)
    {
        Content.Resize += (_, _) => control.Width = Content.ClientSize.Width - 24;
        control.Width = Math.Max(600, Content.ClientSize.Width - 24);
    }
}
