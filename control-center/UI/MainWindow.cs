using System.IO.Ports;
using System.Text.Json;
using MikuOS.Protocol;
using MikuOS.Transport;
using MikuOS.Simulator;
using MikuOS.ControlCenter.Serial;
using MikuOS.ControlCenter.Terminal;
using MikuOS.ControlCenter.Monitoring;
using MikuOS.ControlCenter.Media;

namespace MikuOS.ControlCenter.UI;
public sealed class MainWindow : Form
{
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox port = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 100 };
    private readonly Button connect = Theme.Button("Connect");
    private readonly Label status = new() { AutoSize = true, Padding = new(14, 9, 0, 0), ForeColor = Theme.Accent };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly List<Control> pages = new();
    private readonly List<Button> navigation = new();
    private readonly TerminalView terminal = new();
    private readonly DashboardView dashboard = new();
    private readonly SystemMonitorView monitor = new();
    private readonly MediaView media = new();
    private readonly RichTextBox logs = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Background, ForeColor = Color.Silver, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
    private readonly ListView tasks = new() { Dock = DockStyle.Fill, View = View.Details, BackColor = Theme.Background, ForeColor = Color.Gainsboro, FullRowSelect = true, BorderStyle = BorderStyle.None };
    private readonly Label services = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 13), Padding = new(20) };
    private readonly DeviceSession session = new();
    private readonly HashSet<uint> silentReplies = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private int selected;
    private bool autoReconnect, closing, testing, asciiActive;
    private DateTime nextRetry, lastTaskPoll;
    private int connectionGeneration;
    private readonly string? initialPort;
    public MainWindow(string? serialPort = null, bool smoke = false)
    {
        initialPort = serialPort; testing = smoke;
        Text = "MikuOS / Control Center"; Size = new(1280, 850); MinimumSize = new(1050, 700); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Color.Gainsboro; Font = new Font("Segoe UI", 10);
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Theme.Surface, Padding = new(12, 20, 12, 12) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 100 };
        brand.Controls.Add(new Label { Text = "MIKU / OS", Dock = DockStyle.Top, Height = 42, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        brand.Controls.Add(new Label { Text = "CONTROL CENTER", Dock = DockStyle.Bottom, Height = 50, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 9) });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        sidebar.Controls.Add(nav); sidebar.Controls.Add(brand);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new(20, 15, 0, 0) };
        mode.Items.AddRange(["Simulator", "Real Device"]);
        var saved = UserSettings.Load(); RefreshPorts();
        port.Text = initialPort ?? saved.Port;
        mode.SelectedIndex = initialPort != null ? 1 : smoke ? 0 : saved.Mode == "Real Device" ? 1 : 0;
        mode.SelectedIndexChanged += (_, _) => port.Enabled = mode.SelectedIndex == 1;
        port.Enabled = mode.SelectedIndex == 1;
        var refresh = Theme.Button("↻ Ports"); refresh.Click += (_, _) => RefreshPorts();
        bar.Controls.AddRange([mode, port, refresh, connect, status]);
        void Add(string title, Control page)
        {
            int index = pages.Count; page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); pages.Add(page);
            var button = Theme.Button(title); button.AutoSize = false; button.Width = 164; button.Height = 44; button.Margin = new(0, 3, 0, 3); button.TextAlign = ContentAlignment.MiddleLeft; button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectPage(index); nav.Controls.Add(button); navigation.Add(button);
        }
        tasks.Columns.Add("ID", 65); tasks.Columns.Add("Task", 210); tasks.Columns.Add("State", 150); tasks.Columns.Add("Runs", 100); tasks.Columns.Add("Period ms", 120);
        Add("Dashboard", dashboard); Add("Terminal", terminal); Add("Tasks", tasks);
        var servicePanel = new Panel(); var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new(20, 10, 0, 0) };
        foreach (var name in new[] { "telemetry", "heartbeat", "demo" }) foreach (var verb in new[] { "start", "stop" })
        { var command = verb + " " + name; var b = Theme.Button(command); b.Click += (_, _) => Send(command); buttons.Controls.Add(b); }
        servicePanel.Controls.Add(services); servicePanel.Controls.Add(buttons); Add("Services", servicePanel); Add("System Monitor", monitor);
        var logPanel = new Panel(); var logButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        var exportLogs = Theme.Button("Export logs"); exportLogs.Click += (_, _) => { using var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "miku-logs.txt" }; if (d.ShowDialog(this) == DialogResult.OK) logs.SaveFile(d.FileName, RichTextBoxStreamType.PlainText); };
        var readLogs = Theme.Button("Read device journal"); readLogs.Click += (_, _) => { SelectPage(1); Send("logs"); };
        logButtons.Controls.AddRange([exportLogs, readLogs]); logPanel.Controls.Add(logs); logPanel.Controls.Add(logButtons); Add("Logs", logPanel); Add("Media", media); Add("Settings", Settings());
        Controls.Add(content); Controls.Add(bar); Controls.Add(sidebar); SelectPage(0);
        media.AsciiReady += (frame, color) => { if (!asciiActive) { asciiActive = true; SelectPage(1); } terminal.ShowVideo(frame, color); };
        media.AsciiStopped += () => { asciiActive = false; terminal.HideVideo(); };
        terminal.VideoStopRequested += media.StopPlayback;
        connect.Click += (_, _) => { if (session.Connected || autoReconnect) { autoReconnect = false; Disconnect(); } else Connect(); };
        terminal.Command += Send;
        session.FrameReceived += frame => { int receivedGeneration = connectionGeneration; Post(() => { if (receivedGeneration == connectionGeneration) HandleFrame(frame); }); };
        session.Error += message => Post(() => terminal.Write(message, Color.Salmon));
        timer.Tick += (_, _) => Tick(); timer.Start();
        Shown += (_, _) => Connect();
        FormClosing += (_, _) => { closing = true; timer.Stop(); autoReconnect = false; session.Dispose(); timer.Dispose(); };
    }
    private Control Settings()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Padding = new(30), WrapContents = false };
        panel.Controls.Add(Theme.Label("DEVICE SETTINGS", 18));
        panel.Controls.Add(new Label { AutoSize = true, Text = "USB-UART · 115200 baud · Protocol v1\nESP32-S3 N16R8 · Firmware 0.2\nDevice configuration is stored in NVS.\nCPU load will appear after a validated measurement provider is added.", Padding = new(0, 15, 0, 15) });
        var interval = new NumericUpDown { Minimum = 200, Maximum = 10000, Increment = 100, Value = 1000, Width = 160 };
        panel.Controls.Add(Theme.Label("Telemetry interval, milliseconds")); panel.Controls.Add(interval);
        var apply = Theme.Button("Save interval on device"); apply.Click += (_, _) => Send($"config telemetry_ms {interval.Value}"); panel.Controls.Add(apply);
        var config = Theme.Button("Read device configuration"); config.Click += (_, _) => { SelectPage(1); Send("config"); }; panel.Controls.Add(config);
        var reboot = Theme.Button("Restart MikuOS"); reboot.Click += (_, _) => Send("reboot"); panel.Controls.Add(reboot);
        return panel;
    }
    private void RefreshPorts() { var current = port.Text; port.Items.Clear(); port.Items.AddRange(SerialPort.GetPortNames().Order().Cast<object>().ToArray()); if (current.Length > 0) port.Text = current; else if (port.Items.Count > 0) port.SelectedIndex = 0; }
    private void SelectPage(int index) { pages[selected].Visible = false; selected = index; pages[index].Visible = true; pages[index].BringToFront(); for (int i = 0; i < navigation.Count; i++) { navigation[i].BackColor = i == index ? Color.FromArgb(27, 65, 72) : Theme.Surface; navigation[i].ForeColor = i == index ? Theme.Accent : Color.Gainsboro; } }
    private void Connect()
    {
        connectionGeneration++;
        try
        {
            session.Connect(mode.SelectedIndex == 0 ? new SimulatorTransport() : new SerialTransport(port.Text.Trim()));
            autoReconnect = mode.SelectedIndex == 1; connect.Text = "Disconnect"; mode.Enabled = port.Enabled = false;
            dashboard.Reset(); monitor.Reset(); tasks.Items.Clear(); services.Text = "Waiting for service snapshot…";
            silentReplies.Clear(); Send("ping", true); Send("info", true); Send("tasks", true);
            if (!testing) try { new UserSettings(mode.Text, port.Text.Trim()).Save(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { terminal.Write("Could not save connection preferences: " + e.Message, Color.Salmon); }
        }
        catch (Exception e) { terminal.Write("Connection failed: " + e.Message, Color.Salmon); session.Disconnect(); if (!autoReconnect) { mode.Enabled = true; port.Enabled = mode.SelectedIndex == 1; } nextRetry = DateTime.UtcNow.AddSeconds(3); }
        Tick();
    }
    private void Disconnect() { connectionGeneration++; session.Disconnect(); connect.Text = "Connect"; mode.Enabled = true; port.Enabled = mode.SelectedIndex == 1; status.Text = "OFFLINE"; }
    private void Post(Action action) { if (closing || IsDisposed || !IsHandleCreated) return; try { BeginInvoke((Action)(() => { if (!closing) action(); })); } catch (InvalidOperationException) { } }
    private void Send(string command) => Send(command, false);
    private void Send(string command, bool quiet)
    {
        try { var id = session.Send(command); if (quiet) silentReplies.Add(id); }
        catch (Exception e) { terminal.Write(e.Message, Color.Salmon); }
    }
    private void HandleFrame(Frame frame)
    {
        try
        {
            switch (frame.Type)
            {
                case "RES": case "ERR": bool silent = silentReplies.Remove(frame.Id); if (frame.Payload.Length > 0 && (!silent || frame.Type == "ERR")) terminal.Write(frame.Payload, frame.Type == "ERR" ? Color.Salmon : null); break;
                case "STAT": using (var doc = JsonDocument.Parse(frame.Payload)) { dashboard.UpdateStats(doc.RootElement); monitor.UpdateStats(doc.RootElement); services.Text = string.Join('\n', doc.RootElement.GetProperty("services").EnumerateObject().Select(s => $"{s.Name,-16} {(s.Value.GetBoolean() ? "Enabled" : "Stopped / Faulted")}")); } break;
                case "TASKS": using (var doc = JsonDocument.Parse(frame.Payload)) { tasks.Items.Clear(); foreach (var t in doc.RootElement.EnumerateArray()) tasks.Items.Add(new ListViewItem([t.GetProperty("id").ToString(), t.GetProperty("name").GetString()!, t.GetProperty("state").GetString()!, t.TryGetProperty("runs", out var runs) ? runs.ToString() : "—", t.TryGetProperty("periodMs", out var period) ? period.ToString() : "—"])); } break;
                case "LOG": if (logs.TextLength > 100000) { logs.Select(0, 20000); logs.SelectedText = ""; } logs.AppendText($"[{DateTime.Now:HH:mm:ss}] {frame.Payload}\n"); logs.ScrollToCaret(); break;
                case "EVT": if (frame.Payload == "terminal.clear") terminal.ClearOutput(); else if (frame.Payload == "media.ascii.open") SelectPage(6); else { terminal.Write(frame.Payload, Theme.Accent); if (frame.Payload is "services.changed" or "system.rebooted" or "system.connected") { Send("info", true); Send("tasks", true); } } break;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { terminal.Write("Invalid device payload: " + e.Message, Color.Salmon); }
    }
    private void Tick()
    {
        if (closing) return; session.ExpireRequests(); if (silentReplies.Count > 128) silentReplies.Clear(); var now = DateTime.UtcNow;
        if (autoReconnect && session.Connected && !SerialPort.GetPortNames().Contains(port.Text, StringComparer.OrdinalIgnoreCase)) { session.Disconnect(); nextRetry = now.AddSeconds(2); }
        if (autoReconnect && !session.Connected && now >= nextRetry) { nextRetry = now.AddSeconds(3); Connect(); return; }
        if (session.Connected)
        {
            var age = now - session.LastFrame;
            status.Text = $"{(age < TimeSpan.FromSeconds(5) ? "ONLINE" : session.LastFrame == DateTime.MinValue ? "CONNECTING" : "STALE")}  /  RX {session.Rx:N0}  TX {session.Tx:N0}  /  rejected {session.Rejected}";
            if (now - lastTaskPoll > TimeSpan.FromSeconds(3)) { lastTaskPoll = now; Send("tasks", true); if (age > TimeSpan.FromSeconds(5)) Send("info", true); }
        }
        else status.Text = autoReconnect ? "RECONNECTING…" : "OFFLINE";
    }
    internal async Task SmokeTest(string? videoFile = null)
    {
        try
        {
            async Task AwaitReady(Func<bool> ready) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(100); }
            await AwaitReady(() => tasks.Items.Count >= 3 && services.Text.Contains("demo") && session.Pending == 0);
            Send("ping"); Send("start demo"); Send("video"); await AwaitReady(() => session.Pending == 0 && selected == 6);
            if (session.Pending != 0 || tasks.Items.Count < 3 || selected != 6 || session.LastFrame == DateTime.MinValue || !services.Text.Contains("demo")) throw new InvalidOperationException($"UI integration check failed: pending={session.Pending}, tasks={tasks.Items.Count}, page={selected}, lastFrame={session.LastFrame:O}, connected={session.Connected}, rx={session.Rx}, rejected={session.Rejected}, services={services.Text}");
            if (videoFile != null)
            {
                await media.VerifyPlayback(videoFile);
                using var asciiImage = new Bitmap(Width, Height); DrawToBitmap(asciiImage, new Rectangle(0, 0, Width, Height)); asciiImage.Save(Path.Combine(AppContext.BaseDirectory, "ascii-terminal.png")); terminal.HideVideo();
            }
            Send("stop demo"); await AwaitReady(() => session.Pending == 0 && services.Text.Split('\n').Any(line => line.StartsWith("demo") && line.Contains("Stopped"))); SelectPage(0);
            using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(Path.Combine(AppContext.BaseDirectory, "dashboard.png"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), $"PASS: {(initialPort ?? "Simulator")}, telemetry, tasks, commands, service state, media event, rendering" + (videoFile != null ? ", video frame rendering, ASCII decoding and cancellation" : ""));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), "FAIL: " + e); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
