namespace MikuOS.ControlCenter;
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? Value(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        bool smoke = args.Contains("--smoke");
        var window = new UI.MainWindow(Value("--serial"), smoke, args.Contains("--simulator") || smoke && !args.Contains("--auto") && Value("--serial") == null, args.Contains("--auto"));
        if (args.Contains("--smoke")) window.Shown += async (_, _) => await window.SmokeTest(Value("--video-test"));
        Application.Run(window);
    }
}
