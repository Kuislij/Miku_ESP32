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
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 205 };
    private readonly ComboBox port = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 100 };
    private readonly Button connect = Theme.Button("Подключить");
    private readonly Label status = new() { Dock = DockStyle.Fill, Padding = new(8, 7, 0, 0), ForeColor = Theme.Accent };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly List<Control> pages = new();
    private readonly List<Button> navigation = new();
    private readonly TerminalView terminal = new();
    private readonly DashboardView dashboard = new();
    private readonly SystemMonitorView monitor = new();
    private readonly MediaView media = new();
    private readonly RichTextBox logs = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10) };
    private readonly ListView tasks = new() { Dock = DockStyle.Fill, View = View.Details, BackColor = Theme.Surface, ForeColor = Theme.Ink, FullRowSelect = true, BorderStyle = BorderStyle.None };
    private readonly Label services = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 13), Padding = new(20), ForeColor = Theme.Ink };
    private readonly DeviceSession session = new();
    private readonly HashSet<uint> silentReplies = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private int selected;
    private bool autoReconnect, closing, testing, asciiActive, discovering;
    private DateTime nextRetry, lastTaskPoll, connectedAt;
    private int connectionGeneration;
    private readonly string? initialPort;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? discoveryCancellation;
    private readonly BootOverlay boot = new();
    private readonly CheckBox bootEnabled = new() { Text = "Показывать Miku при запуске", AutoSize = true, Checked = true };
    public MainWindow(string? serialPort = null, bool smoke = false, bool simulator = false, bool automatic = false)
    {
        initialPort = serialPort; testing = smoke;
        Text = "MikuOS"; Size = new(1280, 890); MinimumSize = new(1120, 820); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Ink; Font = new Font("Segoe UI", 10); Padding = new(25, 12, 25, 22); DoubleBuffered = true;
        var header = new Panel { Dock = DockStyle.Top, Height = 66 };
        var brand = new Label { Text = "MikuOS", Dock = DockStyle.Left, Width = 180, ForeColor = Theme.Accent, Font = new Font("Segoe UI", 21, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new(0, 13, 0, 0) };
        header.Controls.Add(nav); header.Controls.Add(brand);
        var bar = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new(0, 4, 3, 10) };
        connect.Dock = DockStyle.Right; connect.Width = 140; connect.BackColor = Theme.Pale; connect.ForeColor = Theme.Accent;
        bar.Controls.Add(status); bar.Controls.Add(connect);
        mode.Items.AddRange(["Автоматически", "Демонстрация", "Порт вручную"]);
        var saved = UserSettings.Load(); RefreshPorts();
        port.Text = initialPort ?? saved.Port;
        mode.SelectedIndex = initialPort != null ? 2 : simulator ? 1 : automatic ? 0 : saved.Mode == "Демонстрация" ? 1 : saved.Mode == "Порт вручную" ? 2 : 0;
        mode.SelectedIndexChanged += (_, _) => port.Enabled = mode.SelectedIndex == 2;
        port.Enabled = mode.SelectedIndex == 2; bootEnabled.Checked = saved.BootAnimation || testing;
        void Add(string title, Control page)
        {
            int index = pages.Count; page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); pages.Add(page);
            var button = Theme.Button(title); button.MinimumSize = new(0, 37); button.Margin = new(2, 0, 2, 0); button.Padding = new(13, 4, 13, 4); button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            button.Click += (_, _) => SelectPage(index); nav.Controls.Add(button); navigation.Add(button);
        }
        tasks.Columns.Add("ID", 65); tasks.Columns.Add("Задача", 210); tasks.Columns.Add("Состояние", 180); tasks.Columns.Add("Запуски", 100); tasks.Columns.Add("Период, мс", 140);
        Add("Главная", dashboard); Add("Терминал", terminal); Add("Задачи", CardPage(tasks));
        var servicePanel = new Panel(); var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new(20, 10, 0, 0) };
        foreach (var name in new[] { "telemetry", "heartbeat", "demo" }) foreach (var verb in new[] { "start", "stop" })
        { var command = verb + " " + name; var b = Theme.Button(command); b.Click += (_, _) => Send(command); buttons.Controls.Add(b); }
        servicePanel.Controls.Add(services); servicePanel.Controls.Add(buttons); Add("Сервисы", CardPage(servicePanel)); Add("Мониторинг", CardPage(monitor));
        var logPanel = new Panel(); var logButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        var exportLogs = Theme.Button("Сохранить журнал"); exportLogs.Click += (_, _) => { using var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "miku-logs.txt" }; if (d.ShowDialog(this) == DialogResult.OK) logs.SaveFile(d.FileName, RichTextBoxStreamType.PlainText); };
        var readLogs = Theme.Button("Прочитать журнал платы"); readLogs.Click += (_, _) => { SelectPage(1); Send("logs"); };
        logButtons.Controls.AddRange([exportLogs, readLogs]); logPanel.Controls.Add(logs); logPanel.Controls.Add(logButtons); Add("Журнал", CardPage(logPanel)); Add("Медиа", CardPage(media)); Add("Настройки", CardPage(Settings()));
        Controls.Add(content); Controls.Add(bar); Controls.Add(header); Controls.Add(boot); boot.BringToFront(); boot.Visible = bootEnabled.Checked; SelectPage(0);
        dashboard.ActionRequested += SelectPage;
        media.AsciiReady += (frame, color) => { if (!asciiActive) { asciiActive = true; SelectPage(1); } terminal.ShowVideo(frame, color); };
        media.AsciiStopped += () => { asciiActive = false; terminal.HideVideo(); };
        terminal.VideoStopRequested += media.StopPlayback;
        connect.Click += (_, _) => { if (session.Connected || autoReconnect || discovering) { autoReconnect = false; Disconnect(); } else BeginConnection(); };
        terminal.Command += Send;
        session.FrameReceived += frame => { int receivedGeneration = connectionGeneration; Post(() => { if (receivedGeneration == connectionGeneration) HandleFrame(frame); }); };
        session.Error += message => Post(() => terminal.Write(message, Color.Salmon));
        timer.Tick += (_, _) => Tick(); timer.Start();
        Shown += (_, _) => BeginConnection();
        FormClosing += (_, _) => { closing = true; lifetime.Cancel(); discoveryCancellation?.Cancel(); timer.Stop(); autoReconnect = false; session.Dispose(); timer.Dispose(); };
    }
    private static Control CardPage(Control page) { var panel = new SoftPanel { Dock = DockStyle.Fill, Padding = new(22) }; panel.Controls.Add(page); page.Dock = DockStyle.Fill; return panel; }
    private Control Settings()
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Padding = new(30), WrapContents = false };
        panel.AutoScroll = true;
        panel.Controls.Add(Theme.Label("Подключение и запуск", 20));
        panel.Controls.Add(new Label { AutoSize = true, Text = "Подключи плату к USB-UART и запусти MikuOS.\nВ автоматическом режиме порт определяется сам.\nДемонстрация работает без платы и показывает тестовые данные.", Padding = new(0, 12, 0, 12) });
        var connectionTools = new FlowLayoutPanel { AutoSize = true, Width = 750, Height = 48 };
        var refresh = Theme.Button("Обновить порты"); refresh.Click += (_, _) => RefreshPorts();
        var applyConnection = Theme.Button("Применить"); applyConnection.BackColor = Theme.Pale; applyConnection.Click += (_, _) => { autoReconnect = false; Disconnect(); BeginConnection(); };
        connectionTools.Controls.AddRange([mode, port, refresh, applyConnection]); panel.Controls.Add(connectionTools);
        panel.Controls.Add(bootEnabled); bootEnabled.CheckedChanged += (_, _) => SavePreferences();
        panel.Controls.Add(new Label { Text = "Настройки устройства", Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Padding = new(0, 22, 0, 12) });
        panel.Controls.Add(new Label { AutoSize = true, Text = "Интервал телеметрии (мс) · сохраняется на плате", Padding = new(0, 0, 0, 8) });
        var interval = new NumericUpDown { Minimum = 200, Maximum = 10000, Increment = 100, Value = 1000, Width = 160 };
        panel.Controls.Add(interval);
        var apply = Theme.Button("Сохранить на плате"); apply.Click += (_, _) => Send($"config telemetry_ms {interval.Value}"); panel.Controls.Add(apply);
        var config = Theme.Button("Прочитать настройки платы"); config.Click += (_, _) => { SelectPage(1); Send("config"); }; panel.Controls.Add(config);
        var reboot = Theme.Button("Перезапустить MikuOS"); reboot.Click += (_, _) => Send("reboot"); panel.Controls.Add(reboot);
        return panel;
    }
    private void RefreshPorts() { var current = port.Text; port.Items.Clear(); port.Items.AddRange(SerialPort.GetPortNames().Order().Cast<object>().ToArray()); if (current.Length > 0) port.Text = current; else if (port.Items.Count > 0) port.SelectedIndex = 0; }
    private void SelectPage(int index) { pages[selected].Visible = false; selected = index; pages[index].Visible = true; pages[index].BringToFront(); for (int i = 0; i < navigation.Count; i++) { navigation[i].BackColor = i == index ? Theme.Pale : Theme.Background; navigation[i].ForeColor = i == index ? Theme.Accent : Theme.Ink; } }
    private void SavePreferences()
    {
        if (testing) return;
        try { new UserSettings(mode.Text, port.Text.Trim(), bootEnabled.Checked).Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { terminal.Write("Не удалось сохранить настройки: " + e.Message, Color.Salmon); }
    }
    private void BeginConnection()
    {
        autoReconnect = mode.SelectedIndex != 1;
        if (mode.SelectedIndex == 0) { mode.Enabled = port.Enabled = false; connect.Text = "Остановить поиск"; _ = Discover(); }
        else Connect();
    }
    private async Task Discover()
    {
        if (discovering || closing || !autoReconnect) return;
        discovering = true; discoveryCancellation?.Dispose(); discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = discoveryCancellation.Token;
        try
        {
            status.Text = "●  Ищем плату MikuOS…"; dashboard.SetConnection("Подключи плату — я найду её автоматически.");
            var available = SerialPort.GetPortNames().OrderBy(p => p.Equals(port.Text, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(p => p).ToArray();
            var found = await DeviceDiscovery.FindAsync(available, p => new SerialTransport(p), TimeSpan.FromMilliseconds(1400), token);
            if (closing || token.IsCancellationRequested || !autoReconnect) return;
            if (found != null) { port.Text = found; Connect(); }
            else { status.Text = "●  Ожидаем ESP32 · подключи USB-UART кабелем с передачей данных"; boot.ConnectionText = "Ждём твою ESP32…"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { terminal.Write("Поиск платы: " + e.Message, Color.Salmon); }
        finally { discovering = false; nextRetry = DateTime.UtcNow.AddSeconds(3); }
    }
    private void Connect()
    {
        connectionGeneration++;
        try
        {
            session.Connect(mode.SelectedIndex == 1 ? new SimulatorTransport() : new SerialTransport(port.Text.Trim())); connectedAt = DateTime.UtcNow;
            autoReconnect = mode.SelectedIndex != 1; connect.Text = "Отключить"; mode.Enabled = port.Enabled = false;
            dashboard.Reset(); monitor.Reset(); tasks.Items.Clear(); services.Text = "Waiting for service snapshot…";
            silentReplies.Clear(); Send("ping", true); Send("info", true); Send("tasks", true);
            boot.ConnectionText = mode.SelectedIndex == 1 ? "Демонстрация готова!" : "Подключаемся к ESP32…";
            SavePreferences();
        }
        catch (Exception e) { terminal.Write("Не удалось подключиться: " + e.Message, Color.Salmon); session.Disconnect(); if (!autoReconnect) { mode.Enabled = true; port.Enabled = mode.SelectedIndex == 2; } nextRetry = DateTime.UtcNow.AddSeconds(3); }
    }
    private void Disconnect() { discoveryCancellation?.Cancel(); connectionGeneration++; session.Disconnect(); connect.Text = "Подключить"; mode.Enabled = true; port.Enabled = mode.SelectedIndex == 2; status.Text = "●  Не подключено"; dashboard.Reset(); monitor.Reset(); tasks.Items.Clear(); services.Text = "Нет подключения"; }
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
        if (autoReconnect && session.Connected && (!SerialPort.GetPortNames().Contains(port.Text, StringComparer.OrdinalIgnoreCase) || now - (session.LastFrame == DateTime.MinValue ? connectedAt : session.LastFrame) > TimeSpan.FromSeconds(15))) { connectionGeneration++; session.Disconnect(); dashboard.Reset(); monitor.Reset(); tasks.Items.Clear(); services.Text = "Ждём повторное подключение"; nextRetry = now.AddSeconds(2); }
        if (autoReconnect && !session.Connected && now >= nextRetry && !discovering) { nextRetry = now.AddSeconds(3); if (mode.SelectedIndex == 0) _ = Discover(); else Connect(); return; }
        if (session.Connected)
        {
            var age = now - session.LastFrame;
            bool live = age < TimeSpan.FromSeconds(5);
            status.Text = $"●  {(mode.SelectedIndex == 1 ? "Демонстрация · тестовые данные" : live ? "Плата подключена · " + port.Text + "   /   Всё готово к работе" : "Ждём ответ платы · " + port.Text)}";
            dashboard.SetConnection(mode.SelectedIndex == 1 ? "Демонстрационный режим · данные симулятора" : live ? "Всё готово. Твоя ESP32 подключена." : "Подключение есть, ожидаем данные платы…");
            boot.ConnectionText = mode.SelectedIndex == 1 ? "Демонстрация готова!" : live ? "ESP32 подключена. Поехали!" : "Подключаемся к ESP32…";
            if (now - lastTaskPoll > TimeSpan.FromSeconds(3)) { lastTaskPoll = now; Send("tasks", true); if (age > TimeSpan.FromSeconds(5)) Send("info", true); }
        }
        else if (!autoReconnect) status.Text = "●  Не подключено";
    }
    internal async Task SmokeTest(string? videoFile = null)
    {
        try
        {
            if (boot.Visible)
            {
                await Task.Delay(450); using var splash = new Bitmap(boot.Width, boot.Height); boot.DrawToBitmap(splash, boot.ClientRectangle); splash.Save(Path.Combine(AppContext.BaseDirectory, "boot-animation.png"));
                await Task.Delay(330); using var nextFrame = new Bitmap(boot.Width, boot.Height); boot.DrawToBitmap(nextFrame, boot.ClientRectangle); nextFrame.Save(Path.Combine(AppContext.BaseDirectory, "boot-animation-next.png"));
                int changedPixels = 0, cx = boot.Width / 2, cy = boot.Height / 2 - 42;
                for (int x = cx - 145; x < cx + 145; x += 6) for (int y = cy - 150; y < cy + 140; y += 6) if (splash.GetPixel(x, y) != nextFrame.GetPixel(x, y)) changedPixels++;
                await Task.Delay(2400); if (boot.Visible || boot.FramesAdvanced < 3 || changedPixels < 30) throw new InvalidOperationException($"Boot animation did not advance and finish: frames={boot.FramesAdvanced}, changed pixels={changedPixels}, visible={boot.Visible}");
            }
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
            foreach (var index in new[] { 2, 3, 4, 5, 6, 7 }) { SelectPage(index); using var page = new Bitmap(Width, Height); DrawToBitmap(page, new Rectangle(0, 0, Width, Height)); page.Save(Path.Combine(AppContext.BaseDirectory, $"page-{index}.png")); } SelectPage(0);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), $"PASS: {(mode.SelectedIndex == 1 ? "Simulator" : mode.SelectedIndex == 0 ? "Auto " + port.Text : initialPort)}, boot animation, telemetry, tasks, commands, service state, media event, rendering" + (videoFile != null ? ", video frame rendering, ASCII decoding and cancellation" : ""));
            if (videoFile != null) { media.StartShutdownTest(videoFile); await Task.Delay(150); }
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-result.txt"), "FAIL: " + e); Environment.ExitCode = 1; }
        finally { Close(); }
    }
}
