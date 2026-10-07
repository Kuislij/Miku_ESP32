namespace MikuOS.ControlCenter.Terminal;
public sealed class TerminalView : UserControl
{
    private readonly Media.AsciiCanvas video = new();
    private readonly Panel videoArea = new() { Dock = DockStyle.Top, Height = 390, Visible = false };
    private readonly CheckBox follow = new() { Text = "Автопрокрутка", Checked = true, AutoSize = true, ForeColor = UI.Theme.Muted, Padding = new(8, 4, 0, 0) };
    private readonly RichTextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(9, 16, 24), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 11) };
    private readonly TextBox input = new() { Dock = DockStyle.Fill, MaxLength = 1024, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(20, 32, 44), ForeColor = Color.Turquoise, Font = new Font("Consolas", 11) };
    private readonly List<string> history = new();
    private int cursor;
    public event Action<string>? Command;
    public event Action? VideoStopRequested;
    public TerminalView()
    {
        Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(4) };
        bar.Controls.Add(input); bar.Controls.Add(new Label { Text = "miku@esp32:~$", Dock = DockStyle.Left, Width = 150, ForeColor = Color.Turquoise, TextAlign = ContentAlignment.MiddleLeft });
        var videoTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
        var stop = UI.Theme.Button("Остановить ASCII"); stop.Click += (_, _) => VideoStopRequested?.Invoke(); videoTools.Controls.Add(stop);
        videoArea.Controls.Add(video); videoArea.Controls.Add(videoTools);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new(4) };
        var clear = UI.Theme.Button("Очистить"); clear.Click += (_, _) => ClearOutput(); tools.Controls.Add(clear); tools.Controls.Add(follow);
        Controls.Add(output); Controls.Add(videoArea); Controls.Add(tools); Controls.Add(bar);
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var command = input.Text.Trim(); if (command.Length == 0) return; history.Add(command); if (history.Count > 200) history.RemoveAt(0); cursor = history.Count; input.Clear(); Write("miku@esp32:~$ " + command, Color.Turquoise); Command?.Invoke(command); }
            else if (e.KeyCode is Keys.Up or Keys.Down) { e.SuppressKeyPress = true; cursor = Math.Clamp(cursor + (e.KeyCode == Keys.Up ? -1 : 1), 0, history.Count); input.Text = cursor < history.Count ? history[cursor] : ""; input.SelectionStart = input.Text.Length; }
        };
    }
    public void ClearOutput() => output.Clear();
    public void ShowVideo(MikuOS.Media.AsciiFrame frame, MikuOS.Media.AsciiColorMode mode) { video.Frame = frame; video.ColorMode = mode; videoArea.Visible = true; video.Invalidate(); }
    public void HideVideo() => videoArea.Visible = false;
    public void Write(string text, Color? color = null)
    {
        if (output.TextLength > 150000) { output.Select(0, 50000); output.SelectedText = ""; }
        output.SelectionStart = output.TextLength; output.SelectionColor = color ?? Color.Gainsboro;
        output.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n"); if (follow.Checked) output.ScrollToCaret();
    }
}
