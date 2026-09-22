namespace MikuOS.ControlCenter.Monitoring;
public sealed class HistoryChart : Control
{
    private readonly Queue<float> values = new();
    public HistoryChart() { DoubleBuffered = true; BackColor = Color.FromArgb(14, 22, 32); }
    public void Add(float value) { values.Enqueue(value); while (values.Count > 120) values.Dequeue(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var grid = new Pen(Color.FromArgb(32, 49, 62));
        for (int y = 30; y < Height; y += 40) e.Graphics.DrawLine(grid, 0, y, Width, y);
        if (values.Count < 2) return;
        var data = values.ToArray(); var max = Math.Max(300000, data.Max());
        var points = data.Select((v, i) => new PointF(i * (Width - 1f) / 119, Height - 20 - v / max * (Height - 40))).ToArray();
        using var pen = new Pen(Color.Turquoise, 2); e.Graphics.DrawLines(pen, points);
    }
}
