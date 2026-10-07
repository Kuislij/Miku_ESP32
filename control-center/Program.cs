namespace MikuOS.ControlCenter;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? Value(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var window = new UI.MainWindow(Value("--serial"), args.Contains("--smoke"));
        if (args.Contains("--smoke")) window.Shown += async (_, _) => await window.SmokeTest(Value("--video-test"));
        Application.Run(window);
    }
}
