using System.Diagnostics;
using System.Text.Json;
using MikuOS.Protocol;

namespace MikuOS.Simulator;

public sealed class SimulatedDevice
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<string, bool> services = new() { ["telemetry"] = true, ["heartbeat"] = true, ["demo"] = false };
    private readonly Queue<string> logs = new();
    private int ticks;
    public SimulatedDevice() => Log("MikuOS simulator boot complete");
    private void Log(string message) { if (logs.Count == 100) logs.Dequeue(); logs.Enqueue(message); }
    public object[] Tasks => services.Select((s, i) => (object)new { id = i + 1, name = s.Key, state = s.Value ? "Running" : "Stopped" }).ToArray();
    public Frame Stats() => new("STAT", 0, JsonSerializer.Serialize(new
    {
        model = "ESP32 (simulated)", firmware = "0.1.0", uptime = (long)clock.Elapsed.TotalSeconds,
        freeHeap = 220000 + (int)(Math.Sin(ticks / 8.0) * 8000), taskCount = services.Count(x => x.Value),
        load = (double?)null, services
    }));
    public IEnumerable<Frame> Tick()
    {
        ticks++;
        if (services["telemetry"]) yield return Stats();
        if (services["heartbeat"]) yield return new("HB", 0, "alive");
        if (services["demo"] && ticks % 5 == 0) { Log("demo service tick"); yield return new("LOG", 0, "demo service tick"); }
    }
    public IEnumerable<Frame> Handle(Frame frame)
    {
        if (frame.Type != "CMD") { yield return new("ERR", frame.Id, "Expected CMD"); yield break; }
        var words = frame.Payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = words.FirstOrDefault() ?? "";
        var response = "";
        switch (cmd)
        {
            case "help": response = "help clear uname version uptime mem tasks services start <service> stop <service> reboot logs info neofetch miku video ping"; break;
            case "ping": response = "pong"; break;
            case "uname": response = "MikuOS / miku-kernel / host simulator"; break;
            case "version": response = "MikuOS 0.1.0; protocol 1"; break;
            case "uptime": response = $"{(long)clock.Elapsed.TotalSeconds} seconds"; break;
            case "mem": response = Stats().Payload; break;
            case "info": yield return Stats(); response = "System snapshot emitted"; break;
            case "tasks": yield return new("TASKS", 0, JsonSerializer.Serialize(Tasks)); response = string.Join('\n', services.Select(s => $"{s.Key,-14} {(s.Value ? "Running" : "Stopped")}")); break;
            case "services": response = string.Join('\n', services.Select(s => $"{s.Key,-14} {(s.Value ? "Running" : "Stopped")}")); break;
            case "start": case "stop":
                if (words.Length != 2 || !services.ContainsKey(words[1])) { yield return new("ERR", frame.Id, "Usage: start|stop telemetry|heartbeat|demo"); yield break; }
                services[words[1]] = cmd == "start"; response = $"{words[1]} {(cmd == "start" ? "started" : "stopped")}";
                Log(response); yield return new("EVT", 0, "services.changed"); yield return new("LOG", 0, response); yield return Stats(); break;
            case "reboot": clock.Restart(); ticks = 0; services["telemetry"] = services["heartbeat"] = true; services["demo"] = false; Log("System restarted"); yield return new("EVT", 0, "system.rebooted"); response = "Reboot complete"; break;
            case "logs": response = string.Join('\n', logs); break;
            case "clear": yield return new("EVT", 0, "terminal.clear"); break;
            case "video": yield return new("EVT", 0, "media.ascii.open"); response = "Media requested on host"; break;
            case "miku": case "neofetch": response = "  /\\ /\\  MikuOS\n /  V  \\ miku-kernel\n |  <>  | Original digital companion\n Device: ESP32 (simulated)\n Link: Loopback protocol v1\n Uptime: " + (long)clock.Elapsed.TotalSeconds + "s"; break;
            default: yield return new("ERR", frame.Id, $"Unknown command: {cmd}"); yield break;
        }
        yield return new("RES", frame.Id, response);
    }
}
