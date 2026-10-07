using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MikuOS.ControlCenter.Media;
public sealed class VideoCanvas : Control
{
    private Bitmap? bitmap;
    public int Frames { get; private set; }
    public VideoCanvas() { Dock = DockStyle.Fill; DoubleBuffered = true; BackColor = Color.Black; }
    public void SetFrame(RgbVideoFrame frame)
    {
        if (bitmap is null || bitmap.Width != frame.Width || bitmap.Height != frame.Height) { bitmap?.Dispose(); bitmap = new(frame.Width, frame.Height, PixelFormat.Format24bppRgb); }
        var locked = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[frame.Width * 3];
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++) { int i = (y * frame.Width + x) * 3; row[x * 3] = frame.Pixels[i + 2]; row[x * 3 + 1] = frame.Pixels[i + 1]; row[x * 3 + 2] = frame.Pixels[i]; }
                Marshal.Copy(row, 0, locked.Scan0 + y * locked.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(locked); }
        Frames++; Invalidate();
    }
    public void SaveFrame(string path) { if (bitmap is null) throw new IOException("No decoded video frame"); bitmap.Save(path, ImageFormat.Png); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (bitmap is null) return;
        double scale = Math.Min(Width / (double)bitmap.Width, Height / (double)bitmap.Height);
        int w = (int)(bitmap.Width * scale), h = (int)(bitmap.Height * scale);
        e.Graphics.DrawImage(bitmap, new Rectangle((Width - w) / 2, (Height - h) / 2, w, h));
    }
    protected override void Dispose(bool disposing) { if (disposing) bitmap?.Dispose(); base.Dispose(disposing); }
}
