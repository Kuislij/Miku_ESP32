namespace MikuOS.ControlCenter.Monitoring;
public sealed class HistoryChart : Control
{
    private readonly Queue<float> values = new();
    public HistoryChart() { DoubleBuffered = true; BackColor = Color.White; }
    public void Add(float value) { values.Enqueue(value); while (values.Count > 120) values.Dequeue(); Invalidate(); }
    public void Reset() { values.Clear(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(230, 242, 241));
        for (int y = 30; y < Height; y += 40) e.Graphics.DrawLine(grid, 0, y, Width, y);
        if (values.Count < 2) { TextRenderer.DrawText(e.Graphics, "Здесь появится история измерений", Font, ClientRectangle, UI.Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
        var data = values.ToArray(); var max = Math.Max(1, data.Max() * 1.15f);
        var points = data.Select((v, i) => new PointF(i * (Width - 1f) / Math.Max(1, data.Length - 1), Height - 16 - v / max * Math.Max(1, Height - 32))).ToArray();
        using var area = new System.Drawing.Drawing2D.GraphicsPath(); area.AddLines(points); area.AddLine(points[^1], new PointF(points[^1].X, Height)); area.AddLine(new PointF(points[^1].X, Height), new PointF(0, Height)); area.CloseFigure();
        using var fill = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(85, 89, 197, 195), Color.FromArgb(8, 89, 197, 195), 90f); e.Graphics.FillPath(fill, area);
        using var pen = new Pen(UI.Theme.Accent, 2.5f); e.Graphics.DrawLines(pen, points);
    }
}
