using MikuOS.Protocol;
using MikuOS.Simulator;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var original = new Frame("CMD", 42, "Привет | world\nsecond line");
var wire = original.Encode();
for (int split = 0; split <= wire.Length; split++) { var parser = new FrameDecoder(); Check(parser.Feed(wire[..split]).Concat(parser.Feed(wire[split..])).Single() == original, "fragment boundary " + split); }
var decoder = new FrameDecoder();
Check(decoder.Feed("invalid\n" + new string('x', 9000) + "\n" + wire).Single() == original && decoder.Rejected == 2, "overflow recovery");
Check(new FrameDecoder().Feed(wire + wire).Count() == 2, "coalesced frames");
Check(new FrameDecoder().Feed("2|CMD|1|cGluZw==\n1|CMD|1|!!!\n").Count() == 0, "version and base64 rejection");
Check(new FrameDecoder().Feed("1|CMD|0|cGluZw==\n1|CMD|1|cGluZx==\n1|CMD|1|/w==\n").Count() == 0, "zero ID, noncanonical padding and invalid UTF-8 rejection");
using var transport = new SimulatorTransport();
var received = new List<Frame>(); var stream = new FrameDecoder();
transport.Received += s => received.AddRange(stream.Feed(s)); transport.Connect();
void Send(string cmd, uint id) => transport.Send(new Frame("CMD", id, cmd).Encode());
Send("ping", 10); Check(received.Any(f => f.Type == "RES" && f.Id == 10 && f.Payload == "pong"), "correlated ping over transport");
Send("start demo", 11); Send("services", 12); Check(received.Any(f => f.Id == 12 && f.Payload.Contains("demo") && f.Payload.Contains("Running")), "service start");
Send("stop demo", 13); Send("tasks", 14); Check(received.Any(f => f.Type == "TASKS" && f.Payload.Contains("Stopped")), "task snapshot");
Send("start missing", 15); Check(received.Any(f => f.Type == "ERR" && f.Id == 15), "unknown service error");
Send("video", 16); Check(received.Any(f => f.Type == "EVT" && f.Payload == "media.ascii.open"), "media event");
Send("reboot", 17); Check(received.Any(f => f.Type == "EVT" && f.Payload == "system.rebooted"), "reboot event");
transport.Disconnect(); Check(!transport.Connected, "disconnect");
using (var session = new MikuOS.Transport.DeviceSession())
{
    var replies = new List<Frame>(); session.FrameReceived += replies.Add;
    session.Connect(new SimulatorTransport()); var id = session.Send("ping");
    Check(replies.Any(f => f.Id == id && f.Payload == "pong") && session.Pending == 0, "session resolves synchronous simulator reply");
    session.Send("config telemetry_ms 500"); session.Send("reboot"); var configId = session.Send("config");
    Check(replies.Any(f => f.Id == configId && f.Payload == "telemetry_ms=500"), "simulator configuration survives reboot");
    session.Disconnect(); Check(!session.Connected && session.Pending == 0, "session lifecycle cleanup");
}
var monochrome = MikuOS.Media.AsciiFrame.FromRgb(2, 1, [0, 0, 0, 255, 255, 255]);
Check(monochrome.Text == " @\n", "ASCII luminance extremes");
Check(monochrome.ToAnsi(MikuOS.Media.AsciiColorMode.TrueColor).Contains("\u001b[38;2;255;255;255m@"), "ANSI true color encoding");
Check(monochrome.ToAnsi(MikuOS.Media.AsciiColorMode.Color256).Contains("\u001b[38;5;231m@"), "ANSI 256 palette encoding");
var probes = new List<ProbeTransport>();
MikuOS.Transport.ITransport Probe(string name) { var probe = new ProbeTransport(name); probes.Add(probe); return probe; }
var discoveryTimeout = TimeSpan.FromMilliseconds(25);
var discovered = await MikuOS.Transport.DeviceDiscovery.FindAsync(["busy", "unrelated", "wrong-id", "valid", "never-open"], Probe, discoveryTimeout);
Check(discovered == "valid" && probes.Count == 4 && probes.All(p => p.Disposed), "discovery skips busy/unrelated ports, requires correlated pong, and releases every port");
Check(await MikuOS.Transport.DeviceDiscovery.FindAsync(["silent", "bad-json", "pong-only"], Probe, discoveryTimeout) == null, "discovery times out without accepting noise or pong alone");
Check(await MikuOS.Transport.DeviceDiscovery.FindAsync([], Probe, discoveryTimeout) == null, "discovery with no attached ports");
using (var discoveryCancel = new CancellationTokenSource())
{
    discoveryCancel.CancelAfter(20);
    try { await MikuOS.Transport.DeviceDiscovery.FindAsync(["silent"], Probe, TimeSpan.FromSeconds(3), discoveryCancel.Token); throw new Exception("discovery ignored cancellation"); }
    catch (OperationCanceledException) { Check(probes[^1].Disposed, "discovery cancellation releases transport promptly"); }
}
await FileTests.Run(Check);
Console.WriteLine($"{checks} checks passed");

sealed class ProbeTransport(string behavior) : MikuOS.Transport.ITransport
{
    public event Action<string>? Received;
    public event Action<string>? Faulted { add { } remove { } }
    public bool Connected { get; private set; }
    public bool Disposed { get; private set; }
    public void Connect() { if (behavior == "busy") throw new IOException("busy"); Connected = true; }
    public void Send(string wire)
    {
        var command = Frame.Parse(wire.TrimEnd('\r', '\n'));
        if (behavior == "silent") return;
        string response = command.Payload == "ping" ? new Frame("RES", behavior == "wrong-id" ? command.Id - 1 : command.Id, "pong").Encode() :
            behavior == "pong-only" ? "noise\n" : new Frame("STAT", 0, behavior == "bad-json" ? "[]" :
                "{\"model\":\"" + (behavior == "unrelated" ? "OtherDevice" : "ESP32-S3 N16R8") + "\",\"firmware\":\"0.2.0\",\"services\":{\"telemetry\":true}}").Encode();
        Received?.Invoke(response[..(response.Length / 2)]); Received?.Invoke(response[(response.Length / 2)..]);
    }
    public void Disconnect() => Connected = false;
    public void Dispose() { Disposed = true; Disconnect(); }
}
