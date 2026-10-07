using System.Drawing.Drawing2D;

namespace MikuOS.ControlCenter.UI;

internal class SoftPanel : Panel
{
    public int Radius { get; set; } = 24;
    public Color? GradientEnd { get; set; }
    public SoftPanel() { DoubleBuffered = true; BackColor = Theme.Surface; Padding = new(22); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 5), Math.Max(1, Height - 6));
        using var shadowPath = Theme.Round(new Rectangle(2, 4, bounds.Width, bounds.Height), Radius);
        using var shadow = new SolidBrush(Color.FromArgb(15, 57, 83, 87)); e.Graphics.FillPath(shadow, shadowPath);
        using var path = Theme.Round(bounds, Radius);
        using Brush fill = GradientEnd is { } end ? new LinearGradientBrush(bounds, BackColor, end, 25f) : new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
    }
}

internal sealed class SoftButton : Button
{
    public SoftButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Theme.Surface; ForeColor = Theme.Ink;
        AutoSize = true; Height = 36; Padding = new(14, 4, 14, 4); Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10, FontStyle.Bold);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Math.Min(17, Height / 2));
        var hovered = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
        using var fill = new SolidBrush(!Enabled ? Theme.Pale : hovered ? ControlPaint.Light(BackColor, .08f) : BackColor);
        e.Graphics.FillPath(fill, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height), Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Focused && ShowFocusCues) { using var outline = new Pen(Theme.Accent, 1); e.Graphics.DrawPath(outline, path); }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}
