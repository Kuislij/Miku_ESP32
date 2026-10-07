using System.Text.Json;
using MikuOS.Protocol;

namespace MikuOS.Transport;

// A port is accepted only after both a correlated pong and a MikuOS snapshot.
public static class DeviceDiscovery
{
    public static async Task<string?> FindAsync(IEnumerable<string> ports, Func<string, ITransport> factory,
        TimeSpan timeout, CancellationToken cancellation = default)
    {
        foreach (var name in ports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if (await ProbeAsync(name, factory, timeout, cancellation).ConfigureAwait(false)) return name;
        }
        return null;
    }

    private static async Task<bool> ProbeAsync(string name, Func<string, ITransport> factory, TimeSpan timeout, CancellationToken cancellation)
    {
        const uint pingId = 4000000001, infoId = 4000000002;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new FrameDecoder();
        var gate = new object();
        bool pong = false, identity = false;
        try
        {
            using var transport = factory(name);
            transport.Received += chunk =>
            {
                lock (gate)
                {
                    foreach (var frame in decoder.Feed(chunk))
                    {
                        if (frame.Type == "RES" && frame.Id == pingId && frame.Payload == "pong") pong = true;
                        if (frame.Type == "STAT") identity |= IsMikuSnapshot(frame.Payload);
                    }
                    if (pong && identity) ready.TrySetResult(true);
                }
            };
            transport.Faulted += _ => ready.TrySetResult(false);
            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                transport.Connect();
                transport.Send(new Frame("CMD", pingId, "ping").Encode());
                transport.Send(new Frame("CMD", infoId, "info").Encode());
            }, cancellation).ConfigureAwait(false);
            try { return await ready.Task.WaitAsync(timeout, cancellation).ConfigureAwait(false); }
            catch (TimeoutException) { return false; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { return false; }
    }

    private static bool IsMikuSnapshot(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload); var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String && model.GetString()!.StartsWith("ESP32", StringComparison.Ordinal) &&
                root.TryGetProperty("firmware", out var firmware) && firmware.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(firmware.GetString()) &&
                root.TryGetProperty("services", out var services) && services.ValueKind == JsonValueKind.Object && services.TryGetProperty("telemetry", out _);
        }
        catch (JsonException) { return false; }
    }
}
