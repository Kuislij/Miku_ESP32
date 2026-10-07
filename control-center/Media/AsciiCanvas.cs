using MikuOS.Media;

namespace MikuOS.ControlCenter.Media;
public sealed class AsciiCanvas : Control
{
    public AsciiFrame? Frame { get; set; }
    public AsciiColorMode ColorMode { get; set; } = AsciiColorMode.TrueColor;
    public AsciiCanvas() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = Color.FromArgb(7, 14, 22); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Frame is not { } frame) return;
        float cell = Math.Min((Width - 12f) / frame.Width, (Height - 12f) / (frame.Height * 1.8f));
        if (cell < 2) return;
        using var font = new Font("Consolas", cell * 1.65f, GraphicsUnit.Pixel);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        using var brush = new SolidBrush(Color.Turquoise);
        for (int y = 0; y < frame.Height; y++)
        {
            if (ColorMode == AsciiColorMode.Mono) { e.Graphics.DrawString(frame.Text.Substring(y * (frame.Width + 1), frame.Width), font, brush, 6, 6 + y * cell * 1.8f, format); continue; }
            for (int x = 0; x < frame.Width; x++)
            {
                int i = (y * frame.Width + x) * 3;
                int Quantize(byte v) => ColorMode == AsciiColorMode.Color256 ? (int)Math.Round(v / 51.0) * 51 : v;
                brush.Color = Color.FromArgb(Quantize(frame.Rgb[i]), Quantize(frame.Rgb[i + 1]), Quantize(frame.Rgb[i + 2]));
                e.Graphics.DrawString(frame.Text[y * (frame.Width + 1) + x].ToString(), font, brush, 6 + x * cell, 6 + y * cell * 1.8f, format);
            }
        }
    }
}
