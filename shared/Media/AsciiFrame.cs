using System.Text;

namespace MikuOS.Media;
public enum AsciiColorMode { Mono, Color256, TrueColor }
public sealed record AsciiFrame(int Width, int Height, byte[] Rgb, string Text)
{
    private const string Ramp = " .:-=+*#%@";
    public static AsciiFrame FromRgb(int width, int height, byte[] rgb)
    {
        if (width < 1 || width > 200 || height < 1 || height > 100 || rgb.Length != width * height * 3) throw new ArgumentException("Invalid RGB frame dimensions");
        var text = new StringBuilder((width + 1) * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                int luminance = (rgb[i] * 2126 + rgb[i + 1] * 7152 + rgb[i + 2] * 722) / 10000;
                text.Append(Ramp[luminance * (Ramp.Length - 1) / 255]);
            }
            text.Append('\n');
        }
        return new(width, height, rgb, text.ToString());
    }
    public string ToAnsi(AsciiColorMode mode)
    {
        if (mode == AsciiColorMode.Mono) return Text;
        var output = new StringBuilder();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 3; var r = Rgb[i]; var g = Rgb[i + 1]; var b = Rgb[i + 2];
                output.Append(mode == AsciiColorMode.TrueColor ? $"\u001b[38;2;{r};{g};{b}m" : $"\u001b[38;5;{16 + 36 * (int)Math.Round(r / 51.0) + 6 * (int)Math.Round(g / 51.0) + (int)Math.Round(b / 51.0)}m");
                output.Append(Text[y * (Width + 1) + x]);
            }
            output.Append("\u001b[0m\n");
        }
        return output.ToString();
    }
}
