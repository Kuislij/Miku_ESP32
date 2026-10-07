using System.Diagnostics;
using System.Runtime.CompilerServices;
using MikuOS.Media;

namespace MikuOS.ControlCenter.Media;
public sealed record RgbVideoFrame(int Width, int Height, byte[] Pixels);
public sealed class PlaybackClock
{
    private readonly Stopwatch watch = Stopwatch.StartNew();
    public bool Paused { get; private set; }
    public void Toggle() { Paused = !Paused; if (Paused) watch.Stop(); else watch.Start(); }
    public async Task WaitFor(long frame, int fps, CancellationToken token)
    {
        while (Paused || watch.ElapsedMilliseconds < frame * 1000 / fps) await Task.Delay(10, token);
    }
}
public sealed class FfmpegDecoder
{
    public static string Executable => Environment.GetEnvironmentVariable("MIKU_FFMPEG") ?? Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
    public async IAsyncEnumerable<AsciiFrame> Decode(string file, int width, int height, [EnumeratorCancellation] CancellationToken cancellation = default, PlaybackClock? clock = null)
    {
        await foreach (var raw in DecodeRgb(file, width, height, 10, true, clock ?? new(), cancellation)) yield return AsciiFrame.FromRgb(width, height, raw.Pixels);
    }
    public async IAsyncEnumerable<RgbVideoFrame> DecodeRgb(string file, int width, int height, int fps, bool characters, PlaybackClock clock, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Video file was not found", file);
        if (!File.Exists(Executable)) throw new FileNotFoundException("Install Media dependencies with scripts/setup-media.ps1", Executable);
        if (width is < 1 or > 960 || height is < 1 or > 540 || fps is < 1 or > 30) throw new ArgumentException("Invalid video output dimensions or FPS");
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        int pixelHeight = characters ? (int)Math.Round(height * 1.8) : height;
        var filter = $"fps={fps},scale={width}:{pixelHeight}:force_original_aspect_ratio=decrease,pad={width}:{pixelHeight}:(ow-iw)/2:(oh-ih)/2,scale={width}:{height}";
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", file, "-an", "-vf", filter, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start FFmpeg");
        var errors = new Queue<string>();
        var drain = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is { } line) { if (errors.Count == 8) errors.Dequeue(); errors.Enqueue(line[..Math.Min(line.Length, 512)]); } });
        long frameNumber = 0;
        try
        {
            while (true)
            {
                await clock.WaitFor(frameNumber, fps, cancellation);
                var bytes = new byte[width * height * 3]; int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = await process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(offset), cancellation);
                    if (count == 0) break; offset += count;
                }
                if (offset == 0) break;
                if (offset != bytes.Length) throw new IOException("Incomplete video frame");
                yield return new(width, height, bytes); frameNumber++;
            }
            await process.WaitForExitAsync(cancellation); await drain;
            if (process.ExitCode != 0) throw new IOException(string.Join('\n', errors));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(); await drain;
        }
    }
}
