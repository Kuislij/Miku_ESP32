using MikuOS.Protocol;

namespace MikuOS.Transport;
public sealed class DeviceSession : IDisposable
{
    private readonly object gate = new();
    private ITransport? transport;
    private FrameDecoder decoder = new();
    private readonly Dictionary<uint, DateTime> pending = new();
    private readonly Dictionary<uint, TaskCompletionSource<Frame>> waiters = new();
    private readonly HashSet<uint> rpcIds = new();
    private readonly Queue<uint> recentRpcIds = new();
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
        lock (gate) { transport = current; decoder = new(); Rx = Tx = 0; LastFrame = DateTime.MinValue; rpcIds.Clear(); recentRpcIds.Clear(); }
        current.Received += chunk =>
        {
            Frame[] frames;
            var completed = new List<(TaskCompletionSource<Frame>, Frame)>();
            var visible = new List<Frame>();
            lock (gate)
            {
                if (transport != current) return;
                Rx += chunk.Length; frames = decoder.Feed(chunk).ToArray();
                foreach (var frame in frames)
                {
                    LastFrame = DateTime.UtcNow;
                    if (frame.Type is "RES" or "ERR")
                    {
                        pending.Remove(frame.Id);
                        if (waiters.Remove(frame.Id, out var waiter)) { completed.Add((waiter, frame)); continue; }
                        if (rpcIds.Contains(frame.Id)) continue;
                    }
                    visible.Add(frame);
                }
            }
            foreach (var (waiter, frame) in completed) waiter.TrySetResult(frame);
            foreach (var frame in visible) FrameReceived?.Invoke(frame);
        };
        current.Faulted += error => { if (transport == current) Error?.Invoke(error); };
        try { current.Connect(); }
        catch { Disconnect(); throw; }
    }
    public uint Send(string command) => SendCore(command, null);
    private uint SendCore(string command, TaskCompletionSource<Frame>? waiter)
    {
        ITransport current; uint id; string wire;
        lock (gate)
        {
            current = transport ?? throw new IOException("Not connected");
            if (!current.Connected) throw new IOException("Not connected");
            if (pending.Count >= 64) throw new IOException("Too many pending requests");
            do { nextId = nextId == uint.MaxValue ? 1 : nextId + 1; } while (pending.ContainsKey(nextId));
            id = nextId; wire = new Frame("CMD", id, command).Encode(); pending[id] = DateTime.UtcNow;
            if (waiter != null)
            {
                waiters[id] = waiter; rpcIds.Add(id); recentRpcIds.Enqueue(id);
                if (recentRpcIds.Count > 256) rpcIds.Remove(recentRpcIds.Dequeue());
            }
        }
        try { current.Send(wire); lock (gate) Tx += wire.Length; return id; }
        catch { lock (gate) { pending.Remove(id); waiters.Remove(id); } throw; }
    }
    public async Task<Frame> RequestAsync(string command, CancellationToken cancellation = default, TimeSpan? timeout = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var waiter = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        uint id = SendCore(command, waiter);
        try { return await waiter.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10), cancellation).ConfigureAwait(false); }
        finally { lock (gate) { waiters.Remove(id); pending.Remove(id); } }
    }
    public void ExpireRequests()
    {
        uint[] expired;
        lock (gate) { expired = pending.Where(p => !waiters.ContainsKey(p.Key) && DateTime.UtcNow - p.Value > TimeSpan.FromSeconds(5)).Select(p => p.Key).ToArray(); foreach (var id in expired) pending.Remove(id); }
        foreach (var id in expired) Error?.Invoke($"Request {id} timed out");
    }
    public void Disconnect()
    {
        ITransport? old; TaskCompletionSource<Frame>[] interrupted;
        lock (gate) { old = transport; transport = null; pending.Clear(); interrupted = waiters.Values.ToArray(); waiters.Clear(); }
        foreach (var waiter in interrupted) waiter.TrySetException(new IOException("Connection lost"));
        old?.Dispose();
    }
    public void Dispose() => Disconnect();
}
