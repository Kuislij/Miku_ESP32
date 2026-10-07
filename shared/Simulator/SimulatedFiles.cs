using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MikuOS.Protocol;
using MikuOS.Storage;

namespace MikuOS.Simulator;
public sealed class SimulatedFiles
{
    private readonly object gate = new();
    private readonly Dictionary<string, byte[]?> files = new(StringComparer.OrdinalIgnoreCase)
    { ["/"] = null, ["/docs"] = null, ["/README.txt"] = Encoding.UTF8.GetBytes("Привет! Это демонстрационное хранилище MikuOS.\nНа настоящей плате файлы находятся во Flash ESP32.\n"), ["/docs/welcome.txt"] = Encoding.UTF8.GetBytes("Создавайте папки, загружайте файлы и редактируйте текст UTF-8.\n") };
    private string epoch = Guid.NewGuid().ToString("N"), handle = "", target = "", expected = "";
    private int revision, received;
    private byte[] staging = [];
    private uint wanted;
    private DateTime touched;
    private byte[]? copying;
    private string copyToken = "", copyState = "", copyError = "", copyVersion = "";
    private int copyTotal, copyDone;
    private string Version => epoch + "-" + revision;
    private long Free => 12 * 1048576 - files.Values.Sum(b => b == null ? 4096L : Math.Max(4096L, (b.LongLength + 4095) / 4096 * 4096)) - staging.LongLength;
    public void Restart() { lock (gate) { Abort(); copyToken = ""; epoch = Guid.NewGuid().ToString("N"); revision = 0; } }
    private void Abort() { if (copying != null) { copying = null; copyState = "failed"; copyError = "CANCELLED"; } handle = ""; target = ""; staging = []; received = 0; }
    public void Tick()
    {
        lock (gate)
        {
            if (copying == null) return;
            int count = Math.Min(2048, copying.Length - received); Array.Copy(copying, received, staging, received, count); received += count; copyDone = received; touched = DateTime.UtcNow;
            if (received == staging.Length) { copying = null; wanted = FileChecksum.Compute(staging); Rpc(["commit", handle]); copyState = "complete"; copyVersion = Version; }
        }
    }
    public static bool Accepts(string command) => command is "fs" or "ls" or "cat" or "mkdir" or "rm" or "mv" or "cp" or "touch" or "write" or "df";
    public Frame Handle(Frame frame)
    {
        lock (gate)
        {
            try
            {
                if (handle.Length > 0 && DateTime.UtcNow - touched > TimeSpan.FromSeconds(30)) Abort();
                var words = Regex.Matches(frame.Payload, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToArray();
                if (words.Length == 0) throw new DeviceFileException("INVALID_ARGUMENTS");
                var cmd = words[0]; var a = words.Skip(1).ToArray(); object result;
                if (cmd == "fs") result = Rpc(a);
                else if (cmd == "df" && a.Length == 0) result = Rpc(["info"]);
                else if (cmd == "ls" && a.Length <= 1)
                {
                    var list = Entries(a.Length == 0 ? "/" : a[0]);
                    string text = string.Join('\n', list.Select(e => (e.Directory ? "[dir] " : "[file] ") + e.Name + (e.Directory ? "" : $"  {e.Size} B")));
                    if (Encoding.UTF8.GetByteCount(text) > 3900) text = "Use Files for the complete directory";
                    return new("RES", frame.Id, text.Length == 0 ? "Empty directory" : text);
                }
                else if (cmd == "cat" && a.Length == 1)
                {
                    var bytes = File(Validate(a[0])); if (bytes.Length > 3072) throw new DeviceFileException("Use Files to open larger files");
                    string text = new UTF8Encoding(false, true).GetString(bytes); if (text.Contains('\0')) throw new DeviceFileException("Binary file: use download"); return new("RES", frame.Id, text);
                }
                else if ((cmd is "mkdir" or "rm") && a.Length == 1) result = Rpc([cmd == "rm" ? "remove" : "mkdir", FilePaths.Encode(a[0])]);
                else if ((cmd is "mv" or "cp") && a.Length == 2) result = Rpc([cmd == "mv" ? "rename" : "copy", FilePaths.Encode(a[0]), FilePaths.Encode(a[1])]);
                else if ((cmd == "touch" && a.Length == 1) || (cmd == "write" && a.Length == 2))
                {
                    string path = Validate(a[0]); if (cmd == "touch" && files.ContainsKey(path)) return new("RES", frame.Id, "File already exists");
                    byte[] bytes = cmd == "touch" ? [] : Encoding.UTF8.GetBytes(a[1]);
                    Rpc(["begin", FilePaths.Encode(path), bytes.Length.ToString(), FileChecksum.Compute(bytes).ToString(), Version]);
                    if (bytes.Length != 0) Rpc(["chunk", handle, "0", Convert.ToBase64String(bytes)]);
                    result = Rpc(["commit", handle]);
                }
                else throw new DeviceFileException("INVALID_ARGUMENTS");
                return new("RES", frame.Id, JsonSerializer.Serialize(result));
            }
            catch (DeviceFileException e) { return new("ERR", frame.Id, e.Code); }
            catch (Exception e) when (e is FormatException or OverflowException or DecoderFallbackException or ArgumentException) { return new("ERR", frame.Id, "INVALID_ARGUMENTS"); }
        }
    }
    private static string Validate(string path) { if (!FilePaths.Valid(path)) throw new DeviceFileException("INVALID_PATH"); return path; }
    private static string Decode(string b64) { byte[] bytes = Convert.FromBase64String(b64); if (Convert.ToBase64String(bytes) != b64) throw new FormatException(); return Validate(new UTF8Encoding(false, true).GetString(bytes)); }
    private byte[] File(string path) => files.TryGetValue(path, out var bytes) && bytes != null ? bytes : throw new DeviceFileException("NOT_A_FILE");
    private List<FileEntry> Entries(string path)
    {
        Validate(path); if (!files.TryGetValue(path, out var directory) || directory != null) throw new DeviceFileException("NOT_A_DIRECTORY");
        return files.Where(p => p.Key != "/" && FilePaths.Parent(p.Key).Equals(path, StringComparison.OrdinalIgnoreCase))
            .Select(p => new FileEntry(p.Key[(p.Key.LastIndexOf('/') + 1)..], p.Value == null, p.Value?.Length ?? 0)).OrderByDescending(e => e.Directory).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }
    private void Room(string path) { if (!files.ContainsKey(path) && Entries(FilePaths.Parent(path)).Count >= 128) throw new DeviceFileException("DIRECTORY_FULL_OR_MISSING"); }
    private object Changed() { revision++; return new { version = Version }; }
    private object Rpc(string[] a)
    {
        if (a.Length == 0) throw new DeviceFileException("INVALID_ARGUMENTS"); string op = a[0];
        if (op == "info" && a.Length == 1) return new { ready = true, filesystem = "FATFS (simulated)", total = 12 * 1048576, free = Free, maxFile = DeviceFileClient.MaxFileSize, chunkSize = 2048, maxEntries = 128, version = Version };
        if (op == "status" && a.Length == 2) { if (a[1] != copyToken || copyToken.Length == 0) throw new DeviceFileException("INVALID_TOKEN"); return new { state = copyState, received = copyDone, total = copyTotal, version = copyVersion, error = copyError }; }
        if (op is "chunk" or "commit" or "abort")
        {
            if (a.Length < 2 || handle.Length == 0 || handle != a[1]) throw new DeviceFileException("INVALID_TOKEN");
            if (op == "abort" && a.Length == 2) { Abort(); return new { }; }
            if (op == "chunk" && a.Length == 4)
            {
                if (copying != null) throw new DeviceFileException("UPLOAD_BUSY");
                if (uint.Parse(a[2]) != received) throw new DeviceFileException("OFFSET_MISMATCH"); byte[] bytes = Convert.FromBase64String(a[3]);
                if (Convert.ToBase64String(bytes) != a[3] || bytes.Length == 0 || bytes.Length > 2048 || bytes.Length > staging.Length - received) throw new DeviceFileException("INVALID_CHUNK");
                bytes.CopyTo(staging, received); received += bytes.Length; touched = DateTime.UtcNow; return new { received };
            }
            if (op == "commit" && a.Length == 2)
            {
                if (copying != null) throw new DeviceFileException("UPLOAD_BUSY");
                if (received != staging.Length || FileChecksum.Compute(staging) != wanted) { Abort(); throw new DeviceFileException("CHECKSUM_MISMATCH"); }
                if (expected != "*" && expected != Version) { Abort(); throw new DeviceFileException("CONFLICT"); }
                files[target] = staging; staging = []; Abort(); return Changed();
            }
            throw new DeviceFileException("INVALID_ARGUMENTS");
        }
        if (a.Length < 2) throw new DeviceFileException("INVALID_ARGUMENTS"); string path = Decode(a[1]);
        if (op == "list" && a.Length == 4)
        {
            if (a[3] != "*" && a[3] != Version) throw new DeviceFileException("CONFLICT"); var entries = Entries(path); int cursor = int.Parse(a[2]);
            if (cursor < 0 || cursor > entries.Count) throw new DeviceFileException("INVALID_ARGUMENTS"); int end = Math.Min(cursor + 8, entries.Count);
            return new { version = Version, entries = entries.Skip(cursor).Take(8).Select(e => new { name = e.Name, directory = e.Directory, size = e.Size }), next = end == entries.Count ? (int?)null : end };
        }
        if ((op is "stat" or "hash") && a.Length == 2)
        { if (!files.TryGetValue(path, out var bytes)) throw new DeviceFileException("NOT_FOUND"); return new { size = bytes?.Length ?? 0, directory = bytes == null, crc32 = bytes == null ? 0 : FileChecksum.Compute(bytes), version = Version }; }
        if (op == "read" && a.Length == 5)
        {
            if (a[4] != Version) throw new DeviceFileException("CONFLICT"); byte[] bytes = File(path); int offset = int.Parse(a[2]), count = int.Parse(a[3]);
            if (offset < 0 || offset > bytes.Length || count < 1 || count > 2048) throw new DeviceFileException("INVALID_ARGUMENTS"); byte[] chunk = bytes.Skip(offset).Take(count).ToArray();
            return new { offset, data = Convert.ToBase64String(chunk), crc32 = FileChecksum.Compute(chunk), version = Version };
        }
        if (handle.Length > 0) throw new DeviceFileException("UPLOAD_BUSY");
        if (op == "begin" && a.Length == 5)
        {
            int size = int.Parse(a[2]); if (path == "/" || size < 0 || size > DeviceFileClient.MaxFileSize) throw new DeviceFileException("FILE_TOO_LARGE");
            if (a[4] != "*" && a[4] != Version) throw new DeviceFileException("CONFLICT");
            if (files.TryGetValue(path, out var old) && old == null) throw new DeviceFileException("NOT_A_FILE"); Room(path);
            if (Free < size + 65536L) throw new DeviceFileException("NO_SPACE"); wanted = uint.Parse(a[3]); expected = a[4]; staging = new byte[size]; received = 0; target = path; handle = Guid.NewGuid().ToString("N"); touched = DateTime.UtcNow; return new { token = handle };
        }
        if (path == "/") throw new DeviceFileException("ROOT_PROTECTED");
        if (op == "mkdir" && a.Length == 2) { if (files.ContainsKey(path)) throw new DeviceFileException("CREATE_FAILED"); Room(path); files[path] = null; return Changed(); }
        if (op == "remove" && a.Length == 2) { if (!files.ContainsKey(path) || (files[path] == null && Entries(path).Count > 0)) throw new DeviceFileException("DELETE_FAILED_OR_NOT_EMPTY"); files.Remove(path); return Changed(); }
        if ((op is "rename" or "copy") && a.Length == 3)
        {
            string to = Decode(a[2]); if (files.ContainsKey(to)) throw new DeviceFileException("ALREADY_EXISTS");
            if (op != "rename" || !FilePaths.Parent(to).Equals(FilePaths.Parent(path), StringComparison.OrdinalIgnoreCase)) Room(to);
            if (!files.ContainsKey(path)) throw new DeviceFileException("NOT_FOUND");
            if (op == "copy") { byte[] bytes = File(path); object start = Rpc(["begin", FilePaths.Encode(to), bytes.Length.ToString(), "0", Version]); copying = bytes; copyToken = handle; copyState = "running"; copyDone = 0; copyTotal = bytes.Length; copyError = copyVersion = ""; return start; }
            if (to.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) throw new DeviceFileException("RENAME_FAILED");
            var moved = files.Where(p => p.Key.Equals(path, StringComparison.OrdinalIgnoreCase) || p.Key.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (moved.Any(p => !FilePaths.Valid(to + p.Key[path.Length..]))) throw new DeviceFileException("INVALID_PATH");
            foreach (var pair in moved) { files.Remove(pair.Key); files[to + pair.Key[path.Length..]] = pair.Value; } return Changed();
        }
        throw new DeviceFileException("INVALID_ARGUMENTS");
    }
}
