using System.Text.Json;
using System.Globalization;
using MikuOS.ControlCenter.UI;
namespace MikuOS.ControlCenter.Monitoring;
public sealed class SystemMonitorView : UserControl
{
    private readonly HistoryChart heap = new() { Dock = DockStyle.Fill };
    private readonly HistoryChart psram = new() { Dock = DockStyle.Fill };
    private readonly Label stats = new() { Dock = DockStyle.Top, Height = 140, Font = new Font("Consolas", 12), ForeColor = Color.Gainsboro };
    private readonly Queue<string> samples = new();
    public SystemMonitorView()
    {
        Dock = DockStyle.Fill; Padding = new(20);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        split.RowStyles.Add(new(SizeType.Absolute, 28)); split.RowStyles.Add(new(SizeType.Percent, 50)); split.RowStyles.Add(new(SizeType.Absolute, 28)); split.RowStyles.Add(new(SizeType.Percent, 50));
        split.Controls.Add(Theme.Label("INTERNAL HEAP / LAST 120 SAMPLES"), 0, 0); split.Controls.Add(heap, 0, 1); split.Controls.Add(Theme.Label("PSRAM / LAST 120 SAMPLES"), 0, 2); split.Controls.Add(psram, 0, 3);
        var export = Theme.Button("Export samples · CSV"); export.Dock = DockStyle.Bottom; export.Click += (_, _) => Export();
        Controls.Add(split); Controls.Add(stats); Controls.Add(export);
    }
    public void Reset() { heap.Reset(); psram.Reset(); samples.Clear(); }
    public void UpdateStats(JsonElement data)
    {
        long Get(string name) => data.TryGetProperty(name, out var p) ? p.GetInt64() : 0;
        long free = Get("freeHeap"), external = Get("freePsram"); heap.Add(free); psram.Add(external);
        stats.Text = $"Internal free: {free:N0} B     Minimum: {Get("minHeap"):N0} B\nLargest internal block: {Get("largestBlock"):N0} B\nPSRAM: {external:N0} / {Get("psramSize"):N0} B     Flash: {Get("flashSize") / 1048576} MiB\nIPC drops: {Get("droppedMessages")}     Telemetry: {Get("telemetryMs")} ms\nCPU load: unavailable";
        if (samples.Count == 3600) samples.Dequeue();
        samples.Enqueue(string.Join(',', DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Get("uptime"), free, Get("minHeap"), Get("largestBlock"), external, Get("droppedMessages")));
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "miku-stats.csv" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllLines(dialog.FileName, new[] { "utc,uptime_s,free_internal,min_internal,largest_internal,free_psram,ipc_drops" }.Concat(samples));
    }
}
