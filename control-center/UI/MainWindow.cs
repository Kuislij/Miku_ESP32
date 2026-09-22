using System.Text.Json;
using MikuOS.Protocol;
using MikuOS.Transport;
using MikuOS.Simulator;
using MikuOS.ControlCenter.Serial;
using MikuOS.ControlCenter.Terminal;

namespace MikuOS.ControlCenter.UI;
public sealed class MainWindow : Form
{
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly TextBox port = new() { Text = "COM3", Width = 90 };
    private readonly Button connect = new() { Text = "Connect", Width = 110, Height = 32, FlatStyle = FlatStyle.Flat };
    private readonly Label status = new() { AutoSize = true, Padding = new(10, 7, 0, 0), ForeColor = Color.Turquoise };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TerminalView terminal = new();
    private readonly DashboardView dashboard = new();
    private readonly RichTextBox logs = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(12, 20, 30), ForeColor = Color.Silver, BorderStyle = BorderStyle.None };
    private readonly ListView tasks = new() { Dock = DockStyle.Fill, View = View.Details, BackColor = Color.FromArgb(12, 20, 30), ForeColor = Color.Gainsboro, FullRowSelect = true };
    private readonly Label services = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 13), Padding = new(20) };
    private readonly Dictionary<uint, DateTime> pending = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private ITransport? transport;
    private FrameDecoder decoder = new();
    private uint nextId;
    private long rx, tx;
    private DateTime lastReceive;
    public MainWindow()
    {
        Text = "MikuOS  /  Control Center"; Size = new(1120, 760); MinimumSize = new(850, 580); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(12, 20, 30); ForeColor = Color.Gainsboro; Font = new Font("Segoe UI", 10);
        var header = new Label { Text = "MIKU / OS     CONTROL CENTER", Dock = DockStyle.Top, Height = 78, Padding = new(20, 10, 20, 0), Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = Color.Turquoise };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new(16, 5, 0, 0) };
        mode.Items.AddRange(["Simulator", "Real Device"]); mode.SelectedIndex = 0; port.Enabled = false;
        mode.SelectedIndexChanged += (_, _) => port.Enabled = mode.SelectedIndex == 1;
        bar.Controls.AddRange([mode, port, connect, status]);
        AddPage("Dashboard", dashboard); AddPage("Terminal", terminal);
        tasks.Columns.Add("ID", 80); tasks.Columns.Add("Task", 250); tasks.Columns.Add("State", 180);
        AddPage("Tasks", tasks);
        var servicePanel = new Panel { Dock = DockStyle.Fill }; var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        foreach (var name in new[] { "telemetry", "heartbeat", "demo" }) foreach (var verb in new[] { "start", "stop" })
        { var command = verb + " " + name; var button = new Button { Text = command, AutoSize = true }; button.Click += (_, _) => Send(command); buttons.Controls.Add(button); }
        servicePanel.Controls.Add(services); servicePanel.Controls.Add(buttons); AddPage("Services", servicePanel);
        AddPage("System Monitor", new Label { Dock = DockStyle.Fill, Padding = new(25), Text = "Live free-heap graph is on Dashboard.\nCPU load is unavailable until a validated measurement provider is implemented.\nSimulator memory values are synthetic. RX/TX counters show protocol bytes." });
        AddPage("Logs", logs);
        AddPage("Media", new Label { Dock = DockStyle.Fill, Padding = new(25), Text = "MEDIA / HOST SUBSYSTEM\n\nThe video command routes here through a protocol event.\nLocal playback and ASCII frame decoding are planned for the next iteration.\nVideo decoding will run on Windows, never on the ESP32." });
        AddPage("Settings", new Label { Dock = DockStyle.Fill, Padding = new(25), Text = "Serial: 115200 baud / 8N1 / no flow control\nProtocol: MikuOS v1 / LF framing / UTF-8 payload in Base64\nSelect connection mode and enter a COM port in the top bar.\nNo hardware is needed for Simulator mode." });
        Controls.Add(tabs); Controls.Add(bar); Controls.Add(header);
        connect.Click += (_, _) => { if (transport?.Connected == true) Disconnect(); else Connect(); };
        terminal.Command += Send; timer.Tick += (_, _) => RefreshStatus(); timer.Start();
        Shown += (_, _) => Connect();
        FormClosing += (_, _) => { timer.Stop(); Disconnect(); timer.Dispose(); };
    }
    private void AddPage(string title, Control child) { var page = new TabPage(title) { BackColor = BackColor, ForeColor = ForeColor, Padding = new(10) }; page.Controls.Add(child); tabs.TabPages.Add(page); }
    private void Connect()
    {
        Disconnect(); decoder = new(); rx = tx = 0;
        var current = mode.SelectedIndex == 0 ? (ITransport)new SimulatorTransport() : new SerialTransport(port.Text.Trim()); transport = current;
        current.Received += chunk => Post(() => { if (transport != current) return; rx += chunk.Length; lastReceive = DateTime.UtcNow; foreach (var frame in decoder.Feed(chunk)) HandleFrame(frame); });
        current.Faulted += message => Post(() => { if (transport != current) return; terminal.Write(message, Color.Salmon); Disconnect(); });
        try { current.Connect(); connect.Text = "Disconnect"; mode.Enabled = port.Enabled = false; lastReceive = DateTime.UtcNow; Send("info"); Send("tasks"); }
        catch (Exception e) { terminal.Write("Connection failed: " + e.Message, Color.Salmon); Disconnect(); }
        RefreshStatus();
    }
    private void Post(Action action) { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    private void Disconnect() { var previous = transport; transport = null; previous?.Dispose(); pending.Clear(); connect.Text = "Connect"; mode.Enabled = true; port.Enabled = mode.SelectedIndex == 1; status.Text = "OFFLINE"; }
    private void Send(string command)
    {
        if (transport?.Connected != true) { terminal.Write("Not connected", Color.Salmon); return; }
        var id = ++nextId;
        try { var wire = new Frame("CMD", id, command).Encode(); pending[id] = DateTime.UtcNow; transport.Send(wire); tx += wire.Length; }
        catch (Exception e) { pending.Remove(id); terminal.Write(e.Message, Color.Salmon); }
    }
    private void HandleFrame(Frame frame)
    {
        try
        {
            switch (frame.Type)
            {
                case "RES": case "ERR": pending.Remove(frame.Id); if (frame.Payload.Length > 0) terminal.Write(frame.Payload, frame.Type == "ERR" ? Color.Salmon : null); break;
                case "STAT": using (var doc = JsonDocument.Parse(frame.Payload)) { dashboard.UpdateStats(doc.RootElement); services.Text = string.Join('\n', doc.RootElement.GetProperty("services").EnumerateObject().Select(s => $"{s.Name,-16} {(s.Value.GetBoolean() ? "Running" : "Stopped")}")); } break;
                case "TASKS": using (var doc = JsonDocument.Parse(frame.Payload)) { tasks.Items.Clear(); foreach (var t in doc.RootElement.EnumerateArray()) tasks.Items.Add(new ListViewItem([t.GetProperty("id").ToString(), t.GetProperty("name").GetString()!, t.GetProperty("state").GetString()!])); } break;
                case "LOG": if (logs.TextLength > 100000) logs.Clear(); logs.AppendText($"[{DateTime.Now:HH:mm:ss}] {frame.Payload}\n"); break;
                case "EVT": if (frame.Payload == "terminal.clear") terminal.ClearOutput(); else if (frame.Payload == "media.ascii.open") tabs.SelectedIndex = 6; else { terminal.Write(frame.Payload, Color.Turquoise); if (frame.Payload is "services.changed" or "system.rebooted") Send("tasks"); } break;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { terminal.Write("Invalid device payload: " + e.Message, Color.Salmon); }
    }
    private void RefreshStatus()
    {
        foreach (var id in pending.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(5)).Select(p => p.Key).ToArray()) { pending.Remove(id); terminal.Write($"Request {id} timed out", Color.Salmon); }
        if (transport?.Connected == true) status.Text = $"{(DateTime.UtcNow - lastReceive < TimeSpan.FromSeconds(5) ? "ONLINE" : "STALE")}  •  RX {rx:N0} / TX {tx:N0}  •  bad frames {decoder.Rejected}";
    }
    internal async Task SmokeTest()
    {
        try
        {
            await Task.Delay(1500); Send("ping"); Send("start demo"); Send("video");
            await Task.Delay(500);
            if (pending.Count != 0 || tasks.Items.Count != 3 || tabs.SelectedIndex != 6 || rx == 0 || !services.Text.Contains("demo")) throw new InvalidOperationException("UI integration check failed");
            tabs.SelectedIndex = 0;
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, "dashboard.png"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), "PASS: connection, telemetry, task list, correlated replies, service command, media navigation, dashboard rendering");
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), "FAIL: " + e); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
