using System.Drawing.Drawing2D;
using System.Text.Json;
using MikuOS.ControlCenter.Monitoring;

namespace MikuOS.ControlCenter.UI;
public sealed class DashboardView : UserControl
{
    private readonly HeroCard hero = new() { Dock = DockStyle.Top, Height = 285 };
    private readonly MetricCard uptime = new("ВРЕМЯ РАБОТЫ"), heap = new("ВНУТРЕННЯЯ ПАМЯТЬ"), psram = new("ПАМЯТЬ PSRAM"), tasks = new("АКТИВНЫЕ ЗАДАЧИ");
    private readonly HistoryChart chart = new() { Dock = DockStyle.Fill };
    public event Action<int>? ActionRequested;
    public DashboardView()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background;
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 153, ColumnCount = 4, RowCount = 1, Padding = new(0, 8, 0, 8) };
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new(SizeType.Percent, 25));
        cards.Controls.Add(uptime, 0, 0); cards.Controls.Add(heap, 1, 0); cards.Controls.Add(psram, 2, 0); cards.Controls.Add(tasks, 3, 0);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        bottom.ColumnStyles.Add(new(SizeType.Percent, 68)); bottom.ColumnStyles.Add(new(SizeType.Percent, 32));
        var history = new SoftPanel { Dock = DockStyle.Fill, Margin = new(5, 0, 12, 0), Padding = new(24, 18, 24, 18) };
        var title = new Label { Dock = DockStyle.Top, Height = 31, Text = "Пульс системы", BackColor = Color.Transparent, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 15, FontStyle.Bold) };
        var hint = new Label { Dock = DockStyle.Top, Height = 28, Text = "Свободная внутренняя память · последние 120 измерений", ForeColor = Theme.Muted, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9) };
        history.Controls.Add(chart); history.Controls.Add(hint); history.Controls.Add(title); bottom.Controls.Add(history, 0, 0);
        var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        var links = new[] { ("01", "Терминал", "Команды и общение с MikuOS", 1), ("02", "Медиа", "Видео и ASCII-кадры", 6), ("03", "Файлы", "Папки и редактор во Flash ESP32", 8) };
        for (int i = 0; i < links.Length; i++)
        {
            shortcuts.RowStyles.Add(new(SizeType.Percent, 100f / 3)); var link = links[i];
            var panel = new SoftPanel { Dock = DockStyle.Fill, Margin = new(0, 0, 0, i == 2 ? 0 : 9), Padding = new(16, 10, 16, 10), Cursor = Cursors.Hand };
            var number = new Label { Dock = DockStyle.Left, Width = 67, Text = link.Item1, ForeColor = i == 1 ? Theme.Pink : Theme.Accent, BackColor = Color.Transparent, Font = new Font("Segoe UI", 22, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            var name = new Label { Dock = DockStyle.Top, Height = 27, Text = link.Item2, ForeColor = Theme.Ink, BackColor = Color.Transparent, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            var caption = new Label { Dock = DockStyle.Fill, Text = link.Item3, BackColor = Color.Transparent, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f), AutoEllipsis = true };
            var text = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }; text.Controls.Add(caption); text.Controls.Add(name);
            panel.Controls.Add(text); panel.Controls.Add(number); shortcuts.Controls.Add(panel, 0, i);
            foreach (var c in new Control[] { panel, number, name, caption, text }) c.Click += (_, _) => ActionRequested?.Invoke(link.Item4);
        }
        bottom.Controls.Add(shortcuts, 1, 0);
        Controls.Add(bottom); Controls.Add(cards); Controls.Add(hero);
        hero.OpenTerminal += () => ActionRequested?.Invoke(1); Reset();
    }
    public void SetConnection(string text) { hero.Connection = text; hero.Invalidate(); }
    public void Reset() { chart.Reset(); hero.Device = "Подключи ESP32 — остальное сделает MikuOS"; hero.Invalidate(); foreach (var card in new[] { uptime, heap, psram, tasks }) card.Set("—", "Ждём данные платы"); }
    public void UpdateStats(JsonElement data)
    {
        long Get(string name) => data.TryGetProperty(name, out var p) ? p.GetInt64() : 0;
        var memory = Get("freeHeap");
        hero.Device = $"{data.GetProperty("model").GetString()}  /  MikuOS {data.GetProperty("firmware").GetString()}"; hero.Invalidate();
        var elapsed = TimeSpan.FromSeconds(Get("uptime"));
        uptime.Set(elapsed.Days > 0 ? $"{elapsed.Days} д {elapsed.Hours:00} ч" : elapsed.ToString(@"hh\:mm\:ss"), "С момента запуска платы");
        heap.Set($"{memory / 1024.0:F0} KiB", $"Минимум {Get("minHeap") / 1024.0:F0} KiB");
        psram.Set($"{Get("freePsram") / 1048576.0:F1} MiB", $"PSRAM {Get("psramSize") / 1048576} / Flash {Get("flashSize") / 1048576} MiB");
        tasks.Set(Get("taskCount").ToString(), $"Потеряно сообщений: {Get("droppedMessages")}"); chart.Add(memory);
    }
    private sealed class HeroCard : SoftPanel
    {
        public string Device { get; set; } = "";
        public string Connection { get; set; } = "Твоя маленькая система. Большие возможности.";
        public event Action? OpenTerminal;
        private readonly Button open = Theme.Button("Открыть терминал   →");
        public HeroCard()
        {
            BackColor = Color.FromArgb(175, 233, 231); GradientEnd = Color.FromArgb(76, 191, 194); Radius = 30;
            open.BackColor = Color.White; open.ForeColor = Theme.Ink; open.Click += (_, _) => OpenTerminal?.Invoke(); Controls.Add(open);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); open.Location = new(34, Height - 67); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics;
            var state = g.Save(); using var clip = Theme.Round(new Rectangle(0, 0, Width - 5, Height - 6), Radius); g.SetClip(clip);
            using var mist = new SolidBrush(Color.FromArgb(45, Color.White)); g.FillEllipse(mist, -130, Height - 100, 530, 410);
            using var dot = new SolidBrush(Color.FromArgb(115, Color.White));
            for (int i = 0; i < 45; i++) { int x = (i * 137 + 51) % Math.Max(1, Width - 20), y = (i * 71 + 18) % Math.Max(1, Height - 15); g.FillEllipse(dot, x, y, i % 3 == 0 ? 4 : 2, i % 3 == 0 ? 4 : 2); }
            int artWidth = Math.Min(390, Width * 36 / 100); var art = MikuArt.Hero;
            float scale = Math.Min(artWidth / (float)art.Width, (Height - 14) / (float)art.Height);
            int aw = (int)(art.Width * scale), ah = (int)(art.Height * scale);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(art, new Rectangle(Width - aw - 25, Height - ah - 5, aw, ah));
            using var tag = new Font("Segoe UI", 10, FontStyle.Bold); using var title = new Font("Segoe UI", 34, FontStyle.Bold);
            int textWidth = Math.Max(280, Width - artWidth - 64);
            TextRenderer.DrawText(g, "MIKU / OS   •   CONTROL CENTER", tag, new Rectangle(34, 28, textWidth, 30), Theme.Ink, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Привет, я Miku.", title, new Rectangle(29, 64, textWidth + 5, 64), Theme.Ink, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Connection, Font, new Rectangle(34, 137, textWidth, 36), Theme.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Device, Font, new Rectangle(34, 177, textWidth, 30), Color.FromArgb(40, 86, 97), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            g.Restore(state);
        }
    }
}
