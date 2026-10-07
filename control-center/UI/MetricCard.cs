namespace MikuOS.ControlCenter.UI;
internal sealed class MetricCard : Panel
{
    private readonly Label value = new() { Dock = DockStyle.Top, Height = 48, ForeColor = Color.White, Font = new Font("Consolas", 24, FontStyle.Bold) };
    private readonly Label detail = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted };
    public MetricCard(string title)
    {
        Dock = DockStyle.Fill; BackColor = Theme.Surface; Padding = new(16); Margin = new(6); MinimumSize = new(100, 100);
        var name = new Label { Text = title, Dock = DockStyle.Top, Height = 26, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        Controls.Add(detail); Controls.Add(value); Controls.Add(name); value.Text = "—";
    }
    public void Set(string text, string hint) { value.Text = text; detail.Text = hint; }
}
