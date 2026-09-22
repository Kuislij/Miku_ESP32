using MikuOS.Protocol;
using MikuOS.Transport;

namespace MikuOS.Simulator;

public sealed class SimulatorTransport : ITransport
{
    private readonly object gate = new();
    private readonly SimulatedDevice device = new();
    private FrameDecoder decoder = new();
    private Timer? timer;
    public event Action<string>? Received;
    public event Action<string>? Faulted;
    public bool Connected { get; private set; }
    public void Connect()
    {
        lock (gate) { if (Connected) return; Connected = true; decoder = new(); Emit(new("EVT", 0, "system.connected")); Emit(device.Stats()); timer = new(_ => Tick(), null, 1000, 1000); }
    }
    private void Tick()
    {
        lock (gate) { if (!Connected) return; try { foreach (var frame in device.Tick()) Emit(frame); } catch (Exception e) { Faulted?.Invoke(e.Message); } }
    }
    private void Emit(Frame frame)
    {
        var wire = frame.Encode();
        // Deliberately fragmented to exercise the same stream framing as Serial.
        var split = wire.Length / 2; Received?.Invoke(wire[..split]); Received?.Invoke(wire[split..]);
    }
    public void Send(string data)
    {
        lock (gate) { if (!Connected) throw new IOException("Disconnected"); foreach (var frame in decoder.Feed(data)) foreach (var reply in device.Handle(frame)) Emit(reply); }
    }
    public void Disconnect() { lock (gate) { Connected = false; timer?.Dispose(); timer = null; } }
    public void Dispose() => Disconnect();
}
