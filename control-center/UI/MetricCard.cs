namespace MikuOS.ControlCenter.UI;
internal sealed class MetricCard : SoftPanel
{
    private readonly Label value = new() { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 22, FontStyle.Bold) };
    private readonly Label detail = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9) };
    public MetricCard(string title)
    {
        Dock = DockStyle.Fill; Padding = new(20, 18, 20, 15); Margin = new(5); MinimumSize = new(100, 115);
        var name = new Label { Text = title, Dock = DockStyle.Top, Height = 26, BackColor = Color.Transparent, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        Controls.Add(detail); Controls.Add(value); Controls.Add(name); value.Text = "—";
    }
    public void Set(string text, string hint) { value.Text = text; detail.Text = hint; }
}
