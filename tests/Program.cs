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
Console.WriteLine($"{checks} checks passed");
