namespace MikuOS.ControlCenter.Terminal;
public sealed class TerminalView : UserControl
{
    private readonly RichTextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(9, 16, 24), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 11) };
    private readonly TextBox input = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(20, 32, 44), ForeColor = Color.Turquoise, Font = new Font("Consolas", 11) };
    private readonly List<string> history = new();
    private int cursor;
    public event Action<string>? Command;
    public TerminalView()
    {
        Dock = DockStyle.Fill;
        var bar = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(4) };
        bar.Controls.Add(input); bar.Controls.Add(new Label { Text = "miku@esp32:~$", Dock = DockStyle.Left, Width = 150, ForeColor = Color.Turquoise, TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(output); Controls.Add(bar);
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; var command = input.Text.Trim(); if (command.Length == 0) return; history.Add(command); cursor = history.Count; input.Clear(); Write("miku@esp32:~$ " + command, Color.Turquoise); Command?.Invoke(command); }
            else if (e.KeyCode is Keys.Up or Keys.Down) { e.SuppressKeyPress = true; cursor = Math.Clamp(cursor + (e.KeyCode == Keys.Up ? -1 : 1), 0, history.Count); input.Text = cursor < history.Count ? history[cursor] : ""; input.SelectionStart = input.Text.Length; }
        };
    }
    public void ClearOutput() => output.Clear();
    public void Write(string text, Color? color = null)
    {
        if (output.TextLength > 150000) { output.Select(0, 50000); output.SelectedText = ""; }
        output.SelectionStart = output.TextLength; output.SelectionColor = color ?? Color.Gainsboro;
        output.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n"); output.ScrollToCaret();
    }
}
