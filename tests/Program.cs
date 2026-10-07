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
Console.WriteLine($"{checks} checks passed");
