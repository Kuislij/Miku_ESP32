using System.Text.Json;
using MikuOS.ControlCenter.Monitoring;
namespace MikuOS.ControlCenter.UI;
public sealed class DashboardView : UserControl
{
    private readonly Label identity = new() { Dock = DockStyle.Top, Height = 64, ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 16, FontStyle.Bold) };
    private readonly MetricCard uptime = new("UPTIME"), heap = new("INTERNAL HEAP"), psram = new("PSRAM"), tasks = new("SERVICES");
    private readonly HistoryChart chart = new() { Dock = DockStyle.Fill };
    public DashboardView()
    {
        Dock = DockStyle.Fill; Padding = new(20);
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 145, ColumnCount = 4, RowCount = 1 };
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new(SizeType.Percent, 25));
        cards.Controls.Add(uptime, 0, 0); cards.Controls.Add(heap, 1, 0); cards.Controls.Add(psram, 2, 0); cards.Controls.Add(tasks, 3, 0);
        Controls.Add(chart); Controls.Add(new Label { Dock = DockStyle.Top, Height = 46, Padding = new(6, 18, 0, 0), Text = "INTERNAL HEAP  /  LAST 120 SAMPLES", ForeColor = Theme.Accent }); Controls.Add(cards); Controls.Add(identity);
        identity.Text = "Waiting for MikuOS…";
    }
    public void Reset() { chart.Reset(); identity.Text = "Waiting for MikuOS…"; foreach (var card in new[] { uptime, heap, psram, tasks }) card.Set("—", ""); }
    public void UpdateStats(JsonElement data)
    {
        long Get(string name) => data.TryGetProperty(name, out var p) ? p.GetInt64() : 0;
        var memory = Get("freeHeap");
        identity.Text = $"{data.GetProperty("model").GetString()}   /   MikuOS {data.GetProperty("firmware").GetString()}";
        var elapsed = TimeSpan.FromSeconds(Get("uptime"));
        uptime.Set(elapsed.Days > 0 ? $"{elapsed.Days}d {elapsed.Hours:00}h" : elapsed.ToString(@"hh\:mm\:ss"), "Reset: " + (data.TryGetProperty("resetReason", out var r) ? r.GetString() : "unknown"));
        heap.Set($"{memory / 1024.0:F0} KiB", $"Minimum {Get("minHeap") / 1024.0:F0} KiB");
        psram.Set($"{Get("freePsram") / 1048576.0:F1} MiB", $"Total {Get("psramSize") / 1048576} MiB / Flash {Get("flashSize") / 1048576} MiB");
        tasks.Set(Get("taskCount").ToString(), $"Active / IPC drops {Get("droppedMessages")}"); chart.Add(memory);
    }
}
