using System.Text;
using MikuOS.Storage;
using MikuOS.Transport;
using MikuOS.Simulator;

internal static class FileTests
{
    public static async Task Run(Action<bool, string> check)
    {
        check(FileChecksum.Compute(Encoding.ASCII.GetBytes("123456789")) == 0xcbf43926 && FileChecksum.Compute([]) == 0, "standard CRC32 vectors");
        check(FilePaths.Valid("/Заметки/мой файл.txt") && !FilePaths.Valid("/../x") && !FilePaths.Valid("/.MIKU/upload.bin") && !FilePaths.Valid("/a//b") && !FilePaths.Valid("/bad.") && !FilePaths.Valid("/" + new string('я', 65)), "UTF-8 paths and reserved/path traversal rejection");
        using var session = new DeviceSession(); var visible = new List<MikuOS.Protocol.Frame>(); session.FrameReceived += visible.Add; session.Connect(new SimulatorTransport());
        check((await session.RequestAsync("ping")).Payload == "pong" && session.Pending == 0 && !visible.Any(f => f.Type == "RES"), "async synchronous reply correlation and terminal suppression");
        var client = new DeviceFileClient(session); var info = await client.InfoAsync(); check(info.Ready && info.ChunkSize == 2048, "simulator file capabilities");
        string directory = "/dotnet-" + Guid.NewGuid().ToString("N")[..8], path = directory + "/Моя заметка.txt";
        await client.CreateDirectoryAsync(directory); byte[] bytes = Enumerable.Range(0, 8197).Select(i => (byte)(i * 73)).ToArray();
        await client.UploadAsync(path, bytes); var file = await client.DownloadAsync(path);
        check(file.Bytes.SequenceEqual(bytes) && file.Metadata.Crc32 == FileChecksum.Compute(bytes), "binary upload/download over fragmented transport");
        byte[] edited = Encoding.UTF8.GetBytes("Привет, Miku!\r\n" + new string('я', 1700));
        await client.UploadAsync(path, edited, file.Metadata.Version); check((await client.DownloadAsync(path)).Bytes.SequenceEqual(edited), "versioned UTF-8 edit over multiple chunks");
        try { await client.UploadAsync(path, [], file.Metadata.Version); throw new Exception("stale overwrite accepted"); }
        catch (DeviceFileException e) { check(e.Code == "CONFLICT" && (await client.DownloadAsync(path)).Bytes.SequenceEqual(edited), "stale edit preserves current file"); }
        using (var cancel = new CancellationTokenSource())
        {
            var progress = new ImmediateProgress(p => { if (p.Done >= 2048) cancel.Cancel(); });
            try { await client.UploadAsync(path, bytes, progress: progress, token: cancel.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { check((await client.DownloadAsync(path)).Bytes.SequenceEqual(edited) && session.Pending == 0, "cancellation aborts upload and preserves original"); }
        }
        var before = await client.StatAsync(path);
        await client.CopyAsync(path, directory + "/copy.txt"); await client.RenameAsync(directory + "/copy.txt", directory + "/renamed.txt");
        check((await client.DownloadAsync(directory + "/renamed.txt")).Bytes.SequenceEqual(edited), "simulator device-side copy and rename");
        await client.UploadAsync(directory + "/large.bin", new byte[65539]);
        using (var cancel = new CancellationTokenSource())
        {
            try { await client.CopyAsync(directory + "/large.bin", directory + "/cancelled.bin", cancel.Token, new ImmediateProgress(p => { if (p.Done >= 2048) cancel.Cancel(); })); throw new Exception("copy cancellation ignored"); }
            catch (OperationCanceledException) { check(!(await client.ListAsync(directory)).Any(e => e.Name == "cancelled.bin"), "copy cancellation aborts on-device background job"); }
        }
        await client.DeleteAsync(directory + "/large.bin");
        var conflict = await session.RequestAsync($"fs read {FilePaths.Encode(path)} 0 100 {before.Version}");
        check(conflict.Type == "ERR" && conflict.Payload == "CONFLICT", "read snapshot changes cannot mix file contents");
        for (int i = 0; i < 17; i++) await client.UploadAsync(directory + $"/item-{i}.txt", []);
        check((await client.ListAsync(directory)).Count == 19, "client consumes all directory pages");
        await session.RequestAsync("reboot"); check((await client.DownloadAsync(path)).Bytes.SequenceEqual(edited), "simulator files survive reboot");
        session.Connect(new SimulatorTransport()); check((await client.DownloadAsync(path)).Bytes.SequenceEqual(edited), "demo volume survives reconnect within application");
        foreach (var entry in await client.ListAsync(directory)) await client.DeleteAsync(FilePaths.Combine(directory, entry.Name));
        await client.DeleteAsync(directory); check(!(await client.ListAsync("/")).Any(e => e.Name == directory[1..]), "client deletes files and empty folder");
        using var silent = new DeviceSession(); silent.Connect(new SilentTransport());
        try { await silent.RequestAsync("ping", timeout: TimeSpan.FromMilliseconds(30)); throw new Exception("timeout ignored"); }
        catch (TimeoutException) { check(silent.Pending == 0, "async timeout releases pending request"); }
        using (var cancel = new CancellationTokenSource())
        {
            var request = silent.RequestAsync("ping", cancel.Token); cancel.Cancel();
            try { await request; throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { check(silent.Pending == 0, "async cancellation releases pending request"); }
        }
        var interrupted = silent.RequestAsync("ping"); silent.Disconnect();
        try { await interrupted; throw new Exception("disconnect ignored"); }
        catch (IOException) { check(silent.Pending == 0, "disconnect promptly fails asynchronous file requests"); }
    }
    private sealed class ImmediateProgress(Action<FileProgress> action) : IProgress<FileProgress> { public void Report(FileProgress value) => action(value); }
    private sealed class SilentTransport : ITransport
    {
        public event Action<string>? Received { add { } remove { } }
        public event Action<string>? Faulted { add { } remove { } }
        public bool Connected { get; private set; }
        public void Connect() => Connected = true;
        public void Send(string _) { }
        public void Disconnect() => Connected = false;
        public void Dispose() => Disconnect();
    }
}
