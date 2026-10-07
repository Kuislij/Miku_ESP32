namespace MikuOS.ControlCenter.UI;
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(243, 249, 248);
    public static readonly Color Surface = Color.White;
    public static readonly Color Accent = Color.FromArgb(39, 165, 170);
    public static readonly Color Pale = Color.FromArgb(220, 242, 240);
    public static readonly Color Pink = Color.FromArgb(238, 135, 174);
    public static readonly Color Ink = Color.FromArgb(33, 50, 62);
    public static readonly Color Muted = Color.FromArgb(108, 130, 141);
    public static Button Button(string text) => new SoftButton { Text = text };
    public static Label Label(string text, int size = 10) => new() { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", size) };
    public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle rectangle, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90); path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}
