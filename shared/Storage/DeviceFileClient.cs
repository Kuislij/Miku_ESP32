using System.Text;
using System.Text.Json;
using MikuOS.Transport;

namespace MikuOS.Storage;
public sealed record StorageStatus(bool Ready, string Filesystem, long Total, long Free, int MaxFile, int ChunkSize, int MaxEntries, string Version);
public sealed record FileEntry(string Name, bool Directory, int Size);
public sealed record FileMetadata(int Size, bool Directory, uint Crc32, string Version);
public sealed record DeviceFile(string Path, byte[] Bytes, FileMetadata Metadata);
public sealed record FileProgress(string Operation, int Done, int Total);
public sealed class DeviceFileException(string code) : IOException(Translate(code))
{
    public string Code { get; } = code;
    private static string Translate(string code) => code switch
    {
        "CONFLICT" => "Хранилище изменилось после открытия файла. Откройте актуальную копию; ваши правки оставлены в редакторе.",
        "STORAGE_UNAVAILABLE" => "Хранилище недоступно. Нужна прошивка с настроенным разделом FATFS.",
        "NO_SPACE" => "На плате недостаточно свободного места для временной копии файла.",
        "FILE_TOO_LARGE" => "Один файл может занимать не больше 4 МиБ.",
        "INVALID_PATH" => "Недопустимое имя или путь. Допустимы русские буквы и пробелы; длина пути — до 160 байт UTF-8.",
        "ALREADY_EXISTS" => "Файл или папка с таким именем уже существует.",
        "DELETE_FAILED_OR_NOT_EMPTY" => "Не удалось удалить. Перед удалением папки освободите её.",
        "DIRECTORY_FULL_OR_MISSING" => "Папка не существует или содержит 128 элементов.",
        "UPLOAD_BUSY" => "На плате уже идёт загрузка. Дождитесь завершения или истечения 30 секунд.",
        "CHECKSUM_MISMATCH" => "Контрольная сумма не совпала. Прежний файл сохранён.",
        "NOT_FOUND" => "Файл не найден.",
        _ => "Плата: " + code
    };
}
public static class FilePaths
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static bool Valid(string path)
    {
        if (!path.StartsWith('/')) return false;
        try { if (Utf8.GetByteCount(path) > 160) return false; } catch (EncoderFallbackException) { return false; }
        if (path == "/") return true;
        return path[1..].Split('/').All(n => n.Length != 0 && n != "." && n != ".." && !n.Equals(".miku", StringComparison.OrdinalIgnoreCase)
            && !n.EndsWith('.') && !n.EndsWith(' ') && Utf8.GetByteCount(n) <= 128 && !n.Any(c => c < 32 || c == 127 || "<>:\"\\|?*".Contains(c)));
    }
    public static string Combine(string directory, string name)
    {
        if (name.Contains('/')) throw new DeviceFileException("INVALID_PATH");
        var path = (directory == "/" ? "" : directory) + "/" + name;
        if (!Valid(path)) throw new DeviceFileException("INVALID_PATH"); return path;
    }
    public static string Parent(string path) { var index = path.LastIndexOf('/'); return index <= 0 ? "/" : path[..index]; }
    public static string Encode(string path) { if (!Valid(path)) throw new DeviceFileException("INVALID_PATH"); return Convert.ToBase64String(Utf8.GetBytes(path)); }
}
public static class FileChecksum
{
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint state = uint.MaxValue;
        foreach (var value in bytes) { state ^= value; for (int i = 0; i < 8; i++) state = (state >> 1) ^ (0xedb88320u & (0u - (state & 1))); }
        return state ^ uint.MaxValue;
    }
}
public sealed class DeviceFileClient(DeviceSession session)
{
    public const int MaxFileSize = 4 * 1024 * 1024, EditorLimit = 256 * 1024, ChunkSize = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim operations = new(1, 1);
    private async Task<T> Serialized<T>(Func<Task<T>> action, CancellationToken token)
    { await operations.WaitAsync(token).ConfigureAwait(false); try { return await action().ConfigureAwait(false); } finally { operations.Release(); } }
    private async Task<JsonElement> Rpc(string command, CancellationToken token)
    {
        var frame = await session.RequestAsync("fs " + command, token, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (frame.Type == "ERR") throw new DeviceFileException(frame.Payload);
        if (frame.Type != "RES") throw new IOException("Unexpected file response");
        using var document = JsonDocument.Parse(frame.Payload); return document.RootElement.Clone();
    }
    private static T Read<T>(JsonElement element) => element.Deserialize<T>(JsonOptions) ?? throw new IOException("Invalid file response");
    public Task<StorageStatus> InfoAsync(CancellationToken token = default) => Serialized(async () => Read<StorageStatus>(await Rpc("info", token).ConfigureAwait(false)), token);
    public Task<FileMetadata> StatAsync(string path, CancellationToken token = default) => Serialized(() => Stat(path, token), token);
    private async Task<FileMetadata> Stat(string path, CancellationToken token) => Read<FileMetadata>(await Rpc("stat " + FilePaths.Encode(path), token).ConfigureAwait(false));
    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, CancellationToken token = default) => Serialized<IReadOnlyList<FileEntry>>(async () =>
    {
        var entries = new List<FileEntry>(); int cursor = 0; string version = "*";
        do
        {
            var page = await Rpc($"list {FilePaths.Encode(path)} {cursor} {version}", token).ConfigureAwait(false);
            version = page.GetProperty("version").GetString() ?? throw new IOException("Missing version");
            foreach (var item in page.GetProperty("entries").EnumerateArray()) { var entry = Read<FileEntry>(item); FilePaths.Combine(path, entry.Name); entries.Add(entry); }
            if (entries.Count > 128) throw new IOException("Directory exceeds limit");
            var next = page.GetProperty("next"); if (next.ValueKind == JsonValueKind.Null) break;
            int newCursor = next.GetInt32(); if (newCursor <= cursor) throw new IOException("Invalid directory cursor"); cursor = newCursor;
        } while (true);
        return entries;
    }, token);
    public Task<DeviceFile> DownloadAsync(string path, IProgress<FileProgress>? progress = null, CancellationToken token = default) => Serialized(async () =>
    {
        var meta = await Stat(path, token).ConfigureAwait(false);
        if (meta.Directory || meta.Size < 0 || meta.Size > MaxFileSize) throw new DeviceFileException("FILE_TOO_LARGE");
        var bytes = new byte[meta.Size]; progress?.Report(new("Чтение", 0, meta.Size));
        for (int offset = 0; offset < bytes.Length; offset += ChunkSize)
        {
            int count = Math.Min(ChunkSize, bytes.Length - offset);
            var reply = await Rpc($"read {FilePaths.Encode(path)} {offset} {count} {meta.Version}", token).ConfigureAwait(false);
            var data = Convert.FromBase64String(reply.GetProperty("data").GetString() ?? "");
            if (reply.GetProperty("offset").GetInt32() != offset || data.Length != count || reply.GetProperty("version").GetString() != meta.Version
                || FileChecksum.Compute(data) != reply.GetProperty("crc32").GetUInt32()) throw new IOException("Повреждённый блок файла");
            data.CopyTo(bytes, offset); progress?.Report(new("Чтение", offset + count, meta.Size));
        }
        if (FileChecksum.Compute(bytes) != meta.Crc32) throw new IOException("Контрольная сумма файла не совпала");
        return new DeviceFile(path, bytes, meta);
    }, token);
    public Task<string> UploadAsync(string path, byte[] bytes, string expectedVersion = "*", IProgress<FileProgress>? progress = null, CancellationToken token = default) => Serialized(async () =>
    {
        if (bytes.Length > MaxFileSize) throw new DeviceFileException("FILE_TOO_LARGE");
        token.ThrowIfCancellationRequested();
        bytes = bytes.ToArray();
        var begin = await Rpc($"begin {FilePaths.Encode(path)} {bytes.Length} {FileChecksum.Compute(bytes)} {expectedVersion}", CancellationToken.None).ConfigureAwait(false);
        var handle = begin.GetProperty("token").GetString() ?? throw new IOException("Missing upload token");
        try
        {
            progress?.Report(new("Запись", 0, bytes.Length));
            for (int offset = 0; offset < bytes.Length; offset += ChunkSize)
            {
                int count = Math.Min(ChunkSize, bytes.Length - offset);
                var reply = await Rpc($"chunk {handle} {offset} {Convert.ToBase64String(bytes, offset, count)}", token).ConfigureAwait(false);
                if (reply.GetProperty("received").GetInt32() != offset + count) throw new IOException("Плата не подтвердила запись блока");
                progress?.Report(new("Запись", offset + count, bytes.Length));
            }
            token.ThrowIfCancellationRequested();
            var committed = await Rpc("commit " + handle, CancellationToken.None).ConfigureAwait(false);
            return committed.GetProperty("version").GetString() ?? throw new IOException("Missing version");
        }
        catch
        {
            if (session.Connected) { try { await Rpc("abort " + handle, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { /* Expiry reclaims staging after connection loss. */ } }
            throw;
        }
    }, token);
    public Task<string> CreateDirectoryAsync(string path, CancellationToken token = default) => Mutate("mkdir " + FilePaths.Encode(path), token);
    public Task<string> DeleteAsync(string path, CancellationToken token = default) => Mutate("remove " + FilePaths.Encode(path), token);
    public Task<string> RenameAsync(string from, string to, CancellationToken token = default) => Mutate($"rename {FilePaths.Encode(from)} {FilePaths.Encode(to)}", token);
    public Task<string> CopyAsync(string from, string to, CancellationToken token = default, IProgress<FileProgress>? progress = null) => Serialized(async () =>
    {
        token.ThrowIfCancellationRequested();
        var start = await Rpc($"copy {FilePaths.Encode(from)} {FilePaths.Encode(to)}", CancellationToken.None).ConfigureAwait(false);
        string handle = start.GetProperty("token").GetString() ?? throw new IOException("Missing copy token");
        try
        {
            while (true)
            {
                var status = await Rpc("status " + handle, token).ConfigureAwait(false);
                progress?.Report(new("Копирование на ESP32", status.GetProperty("received").GetInt32(), status.GetProperty("total").GetInt32()));
                string? state = status.GetProperty("state").GetString();
                if (state == "complete") return status.GetProperty("version").GetString() ?? throw new IOException("Missing version");
                if (state == "failed") throw new DeviceFileException(status.GetProperty("error").GetString() ?? "COPY_FAILED");
                if (state != "running") throw new IOException("Invalid copy state");
                await Task.Delay(150, token).ConfigureAwait(false);
            }
        }
        catch
        {
            if (session.Connected) { try { await Rpc("abort " + handle, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch (Exception) { } }
            throw;
        }
    }, token);
    private Task<string> Mutate(string command, CancellationToken token) => Serialized(async () => (await Rpc(command, token).ConfigureAwait(false)).GetProperty("version").GetString() ?? "", token);
}
