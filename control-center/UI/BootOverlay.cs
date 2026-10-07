using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MikuOS.ControlCenter.UI;
internal sealed class BootOverlay : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 110 };
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    internal int FramesAdvanced { get; private set; }
    public string ConnectionText { get; set; } = "Ищем твою ESP32…";
    public BootOverlay()
    {
        DoubleBuffered = true; BackColor = Theme.Background; Dock = DockStyle.Fill; Cursor = Cursors.Hand;
        animation.Tick += (_, _) => { FramesAdvanced++; if (elapsed.ElapsedMilliseconds >= 2600) { animation.Stop(); Visible = false; } else Invalidate(); };
        Click += (_, _) => { animation.Stop(); Visible = false; }; animation.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int cx = Width / 2, cy = Height / 2 - 42;
        using var pale = new SolidBrush(Theme.Pale); g.FillEllipse(pale, cx - 190, cy - 175, 380, 350);
        using var pink = new SolidBrush(Color.FromArgb(248, 213, 226)); g.FillEllipse(pink, cx + 125, cy - 126, 36, 36); g.FillEllipse(pink, cx - 156, cy + 101, 22, 22);
        MikuArt.DrawSprite(g, (int)(elapsed.ElapsedMilliseconds / 110) % 8, new Rectangle(cx - 150, cy - 157, 300, 300));
        using var title = new Font("Segoe UI", 32, FontStyle.Bold);
        TextRenderer.DrawText(g, "MikuOS", title, new Rectangle(0, cy + 172, Width, 58), Theme.Ink, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, ConnectionText, Font, new Rectangle(0, cy + 232, Width, 30), Theme.Muted, TextFormatFlags.HorizontalCenter);
        using var track = new SolidBrush(Theme.Pale); g.FillRectangle(track, cx - 100, cy + 279, 200, 4);
        using var accent = new SolidBrush(Theme.Accent); g.FillRectangle(accent, cx - 100, cy + 279, Math.Min(200, (int)(elapsed.ElapsedMilliseconds * 200 / 2600)), 4);
        TextRenderer.DrawText(g, "Нажми, чтобы пропустить", Font, new Rectangle(0, Height - 55, Width, 25), Theme.Muted, TextFormatFlags.HorizontalCenter);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
