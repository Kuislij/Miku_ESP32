using System.Drawing.Drawing2D;
using System.Reflection;

namespace MikuOS.ControlCenter.UI;
internal static class MikuArt
{
    public static readonly Image Hero = Load("miku-hero.png");
    public static readonly Image Sprites = Load("miku-boot.png");
    private static Image Load(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(name, StringComparison.Ordinal)))!;
        using var source = Image.FromStream(stream); return new Bitmap(source);
    }
    public static void DrawSprite(Graphics graphics, int frame, Rectangle destination)
    {
        var state = graphics.Save(); graphics.InterpolationMode = InterpolationMode.NearestNeighbor; graphics.PixelOffsetMode = PixelOffsetMode.Half;
        int w = Sprites.Width / 4, h = Sprites.Height / 2;
        graphics.DrawImage(Sprites, destination, new Rectangle(frame % 4 * w, frame / 4 % 2 * h, w, h), GraphicsUnit.Pixel);
        graphics.Restore(state);
    }
}
