using MikuOS.Protocol;

namespace MikuOS.Transport;
public sealed class DeviceSession : IDisposable
{
    private readonly object gate = new();
    private ITransport? transport;
    private FrameDecoder decoder = new();
    private readonly Dictionary<uint, DateTime> pending = new();
    private uint nextId;
    public event Action<Frame>? FrameReceived;
    public event Action<string>? Error;
    public bool Connected => transport?.Connected == true;
    public long Rx { get; private set; }
    public long Tx { get; private set; }
    public DateTime LastFrame { get; private set; }
    public int Rejected => decoder.Rejected;
    public int Pending { get { lock (gate) return pending.Count; } }
    public void Connect(ITransport current)
    {
        Disconnect();
        lock (gate) { transport = current; decoder = new(); Rx = Tx = 0; LastFrame = DateTime.MinValue; }
        current.Received += chunk =>
        {
            Frame[] frames;
            lock (gate)
            {
                if (transport != current) return;
                Rx += chunk.Length; frames = decoder.Feed(chunk).ToArray();
                foreach (var frame in frames) { LastFrame = DateTime.UtcNow; if (frame.Type is "RES" or "ERR") pending.Remove(frame.Id); }
            }
            foreach (var frame in frames) FrameReceived?.Invoke(frame);
        };
        current.Faulted += error => { if (transport == current) Error?.Invoke(error); };
        try { current.Connect(); }
        catch { Disconnect(); throw; }
    }
    public uint Send(string command)
    {
        ITransport current; uint id; string wire;
        lock (gate)
        {
            current = transport ?? throw new IOException("Not connected");
            if (!current.Connected) throw new IOException("Not connected");
            if (pending.Count >= 64) throw new IOException("Too many pending requests");
            do { nextId = nextId == uint.MaxValue ? 1 : nextId + 1; } while (pending.ContainsKey(nextId));
            id = nextId; wire = new Frame("CMD", id, command).Encode(); pending[id] = DateTime.UtcNow;
        }
        try { current.Send(wire); lock (gate) Tx += wire.Length; return id; }
        catch { lock (gate) pending.Remove(id); throw; }
    }
    public void ExpireRequests()
    {
        uint[] expired;
        lock (gate) { expired = pending.Where(p => DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(5)).Select(p => p.Key).ToArray(); foreach (var id in expired) pending.Remove(id); }
        foreach (var id in expired) Error?.Invoke($"Request {id} timed out");
    }
    public void Disconnect()
    {
        ITransport? old;
        lock (gate) { old = transport; transport = null; pending.Clear(); }
        old?.Dispose();
    }
    public void Dispose() => Disconnect();
}
