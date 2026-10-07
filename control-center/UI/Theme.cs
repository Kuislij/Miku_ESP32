namespace MikuOS.ControlCenter.UI;
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(9, 15, 24);
    public static readonly Color Surface = Color.FromArgb(16, 26, 39);
    public static readonly Color Accent = Color.FromArgb(61, 223, 207);
    public static readonly Color Muted = Color.FromArgb(132, 153, 174);
    public static Button Button(string text) => new() { Text = text, AutoSize = true, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Surface, ForeColor = Color.Gainsboro, Padding = new(8, 2, 8, 2) };
    public static Label Label(string text, int size = 10) => new() { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", size) };
}
