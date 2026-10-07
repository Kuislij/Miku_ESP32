using System.Diagnostics;
using System.Text.Json;
using MikuOS.Protocol;

namespace MikuOS.Simulator;

public sealed class SimulatedDevice
{
    private readonly SimulatedFiles files;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<string, bool> services = new() { ["telemetry"] = true, ["heartbeat"] = true, ["demo"] = false };
    private readonly Queue<string> logs = new();
    private int ticks;
    private int telemetryMs = 1000;
    private long lastTelemetry;
    private long lastHeartbeat, lastDemo;
    private readonly Dictionary<string, long> runs = new() { ["telemetry"] = 0, ["heartbeat"] = 0, ["demo"] = 0 };
    public SimulatedDevice(SimulatedFiles? storage = null) { files = storage ?? new(); Log("MikuOS simulator boot complete"); }
    private void Log(string message) { if (logs.Count == 100) logs.Dequeue(); logs.Enqueue(message); }
    public object[] Tasks => services.Select((s, i) => (object)new { id = i + 1, name = s.Key, state = s.Value ? runs[s.Key] == 0 ? "Ready" : "Waiting" : "Stopped", runs = runs[s.Key], periodMs = s.Key == "telemetry" ? telemetryMs : s.Key == "demo" ? 5000 : 1000 }).ToArray();
    public Frame Stats() => new("STAT", 0, JsonSerializer.Serialize(new
    {
        model = "ESP32-S3 (simulated)", firmware = "0.3.0", uptime = (long)clock.Elapsed.TotalSeconds,
        freeHeap = 220000 + (int)(Math.Sin(ticks / 8.0) * 8000), taskCount = services.Count(x => x.Value),
        minHeap = 212000, largestBlock = 170000, psramSize = 8 * 1048576, freePsram = 8 * 1048576, flashSize = 16 * 1048576,
        resetReason = "simulated", droppedMessages = 0, telemetryMs, load = (double?)null, services
    }));
    public IEnumerable<Frame> Tick()
    {
        files.Tick();
        ticks = (int)clock.Elapsed.TotalSeconds;
        long now = clock.ElapsedMilliseconds;
        if (services["telemetry"] && now - lastTelemetry >= telemetryMs) { lastTelemetry = now; runs["telemetry"]++; yield return Stats(); }
        if (services["heartbeat"] && now - lastHeartbeat >= 1000) { lastHeartbeat = now; runs["heartbeat"]++; yield return new("HB", 0, "alive"); }
        if (services["demo"] && now - lastDemo >= 5000) { lastDemo = now; runs["demo"]++; Log("demo service tick"); yield return new("LOG", 0, "demo service tick"); }
    }
    public IEnumerable<Frame> Handle(Frame frame)
    {
        if (frame.Type != "CMD") { yield return new("ERR", frame.Id, "Expected CMD"); yield break; }
        var words = frame.Payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = words.FirstOrDefault() ?? "";
        if (SimulatedFiles.Accepts(cmd)) { yield return files.Handle(frame); yield break; }
        if (cmd != "start" && cmd != "stop" && cmd != "config" && words.Length != 1) { yield return new("ERR", frame.Id, "Command takes no arguments"); yield break; }
        var response = "";
        switch (cmd)
        {
            case "help": response = "help clear uname version uptime mem tasks services start <service> stop <service> reboot logs config [telemetry_ms 200..10000] info neofetch miku video ping"; break;
            case "ping": response = "pong"; break;
            case "uname": response = "MikuOS / miku-kernel / host simulator"; break;
            case "version": response = "MikuOS 0.3.0; protocol 1"; break;
            case "uptime": response = $"{(long)clock.Elapsed.TotalSeconds} seconds"; break;
            case "mem": response = Stats().Payload; break;
            case "info": yield return Stats(); response = "System snapshot emitted"; break;
            case "tasks": yield return new("TASKS", 0, JsonSerializer.Serialize(Tasks)); response = string.Join('\n', services.Select(s => $"{s.Key,-14} {(s.Value ? "Running" : "Stopped")}")); break;
            case "services": response = string.Join('\n', services.Select(s => $"{s.Key,-14} {(s.Value ? "Running" : "Stopped")}")); break;
            case "start": case "stop":
                if (words.Length != 2 || !services.ContainsKey(words[1])) { yield return new("ERR", frame.Id, "Usage: start|stop telemetry|heartbeat|demo"); yield break; }
                services[words[1]] = cmd == "start"; if (words[1] == "telemetry") lastTelemetry = clock.ElapsedMilliseconds; else if (words[1] == "heartbeat") lastHeartbeat = clock.ElapsedMilliseconds; else lastDemo = clock.ElapsedMilliseconds;
                response = $"{words[1]} {(cmd == "start" ? "started" : "stopped")}";
                Log(response); yield return new("EVT", 0, "services.changed"); yield return new("LOG", 0, response); yield return Stats(); break;
            case "config":
                if (words.Length == 1) response = $"telemetry_ms={telemetryMs}";
                else if (words.Length == 3 && words[1] == "telemetry_ms" && int.TryParse(words[2], out var interval) && interval is >= 200 and <= 10000) { telemetryMs = interval; response = $"Saved telemetry_ms={interval}"; }
                else { yield return new("ERR", frame.Id, "Usage: config [telemetry_ms 200..10000]"); yield break; }
                break;
            case "reboot": files.Restart(); clock.Restart(); ticks = 0; lastTelemetry = lastHeartbeat = lastDemo = 0; foreach (var name in runs.Keys) runs[name] = 0; services["telemetry"] = services["heartbeat"] = true; services["demo"] = false; Log("System restarted"); yield return new("EVT", 0, "system.rebooted"); response = "Reboot complete"; break;
            case "logs": response = string.Join('\n', logs); break;
            case "clear": yield return new("EVT", 0, "terminal.clear"); break;
            case "video": yield return new("EVT", 0, "media.ascii.open"); response = "Media requested on host"; break;
            case "miku": case "neofetch": response = "  /\\ /\\  MikuOS\n /  V  \\ miku-kernel\n |  <>  | Original digital companion\n Device: ESP32 (simulated)\n Link: Loopback protocol v1\n Uptime: " + (long)clock.Elapsed.TotalSeconds + "s"; break;
            default: yield return new("ERR", frame.Id, $"Unknown command: {cmd}"); yield break;
        }
        yield return new("RES", frame.Id, response);
    }
}
