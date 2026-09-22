using System.Text.Json;
using MikuOS.ControlCenter.Monitoring;

namespace MikuOS.ControlCenter.UI;
public sealed class DashboardView : UserControl
{
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 175, Font = new Font("Consolas", 14), ForeColor = Color.Gainsboro };
    private readonly HistoryChart chart = new() { Dock = DockStyle.Fill };
    public DashboardView() { Dock = DockStyle.Fill; Padding = new(20); Controls.Add(chart); Controls.Add(new Label { Dock = DockStyle.Top, Height = 35, Text = "FREE HEAP  /  LAST 120 SAMPLES", ForeColor = Color.Turquoise }); Controls.Add(summary); summary.Text = "Connect to a simulator or device to begin."; }
    public void UpdateStats(JsonElement data)
    {
        var heap = data.GetProperty("freeHeap").GetInt32();
        summary.Text = $"{data.GetProperty("model").GetString()}   /   MikuOS {data.GetProperty("firmware").GetString()}\n\nUPTIME  {TimeSpan.FromSeconds(data.GetProperty("uptime").GetInt64()):g}\nHEAP    {heap / 1024.0:F1} KiB\nTASKS   {data.GetProperty("taskCount").GetInt32()} active\nCPU     unavailable (no measured load)";
        chart.Add(heap);
    }
}
