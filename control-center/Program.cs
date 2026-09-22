namespace MikuOS.ControlCenter;
internal static class Program
{
    [STAThread] static void Main(string[] args) { ApplicationConfiguration.Initialize(); var window = new UI.MainWindow(); if (args.Contains("--smoke")) window.Shown += async (_, _) => await window.SmokeTest(); Application.Run(window); }
}
