using LibVLCSharp.Shared;
using MikuOS.Media;

namespace MikuOS.ControlCenter.Media;
public sealed class MediaView : UserControl
{
    private readonly VideoCanvas video = new();
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new(12), Text = "Open a local video. ASCII playback appears in Terminal." };
    private readonly ComboBox color = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private LibVLC? vlc;
    private MediaPlayer? audio;
    private CancellationTokenSource? cancellation;
    private PlaybackClock? clock;
    private Task? playback;
    private string? file;
    private bool disposed;
    private int generation;
    private AsciiFrame? lastFrame;
    private Exception? playbackError;
    public event Action<AsciiFrame, AsciiColorMode>? AsciiReady;
    public event Action? AsciiStopped;
    private AsciiColorMode Mode => color.SelectedIndex switch { 1 => AsciiColorMode.Color256, 2 => AsciiColorMode.Mono, _ => AsciiColorMode.TrueColor };
    public MediaView()
    {
        Dock = DockStyle.Fill;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new(10) };
        void Button(string text, Action action) { var b = UI.Theme.Button(text); b.Click += (_, _) => action(); bar.Controls.Add(b); }
        Button("Open video", Open); Button("Play", () => Start(false)); Button("Pause / Resume", Pause); Button("Stop", StopPlayback); Button("ASCII → Terminal", () => Start(true));
        color.Items.AddRange(["True color", "256 colors", "Mono cyan"]); color.SelectedIndex = 0; bar.Controls.Add(color); Button("Export ANSI", ExportAnsi);
        Controls.Add(video); Controls.Add(status); Controls.Add(bar);
    }
    private void Open()
    {
        using var dialog = new OpenFileDialog { Filter = "Video|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv|All files|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        StopPlayback(); file = dialog.FileName; status.Text = Path.GetFileName(file); Start(false);
    }
    private async void Start(bool ascii)
    {
        if (file is null) { Open(); return; }
        int request = ++generation;
        CancelPlayback(); if (playback is not null) await playback;
        if (disposed || request != generation) return;
        cancellation?.Dispose(); cancellation = new(); clock = new();
        playback = Run(file, ascii, clock, cancellation.Token);
    }
    private async Task Run(string path, bool ascii, PlaybackClock timing, CancellationToken token)
    {
        playbackError = null;
        try
        {
            if (!ascii)
            {
                // Decode sound separately; picture frames use the WinForms canvas so no GPU/native child window is required.
                try { LibVLCSharp.Shared.Core.Initialize(); vlc ??= new LibVLC("--no-video", "--no-video-title-show"); audio ??= new MediaPlayer(vlc); using var track = new LibVLCSharp.Shared.Media(vlc, new Uri(path)); audio.Play(track); }
                catch (Exception e) { status.Text = "Audio unavailable: " + e.Message; }
                await foreach (var frame in new FfmpegDecoder().DecodeRgb(path, 640, 360, 24, false, timing, token))
                { if (disposed) break; video.SetFrame(frame); status.Text = "Video / 640×360 / 24 FPS / " + Path.GetFileName(path); }
            }
            else
            {
                int count = 0;
                await foreach (var frame in new FfmpegDecoder().Decode(path, 96, 30, token, timing))
                { if (disposed) break; lastFrame = frame; AsciiReady?.Invoke(frame, Mode); status.Text = $"ASCII / 96×30 / 10 FPS / frame {++count} / {Path.GetFileName(path)}"; }
            }
            if (!disposed && !token.IsCancellationRequested) status.Text = "Playback complete";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { playbackError = e; if (!disposed) status.Text = "Playback failed: " + e.Message; }
        finally { if (!disposed) { audio?.Stop(); if (ascii) AsciiStopped?.Invoke(); } }
    }
    private void Pause() { if (clock is null) return; clock.Toggle(); audio?.SetPause(clock.Paused); status.Text = clock.Paused ? "Paused" : "Playing"; }
    public void StopPlayback() { generation++; CancelPlayback(); }
    private void CancelPlayback() { cancellation?.Cancel(); audio?.Stop(); AsciiStopped?.Invoke(); if (!disposed) status.Text = "Stopped"; }
    private void ExportAnsi()
    {
        if (lastFrame is null) { status.Text = "Play ASCII video before exporting a frame"; return; }
        using var dialog = new SaveFileDialog { Filter = "ANSI frame|*.ans", FileName = "miku-frame.ans" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, lastFrame.ToAnsi(Mode));
    }
    internal async Task VerifyPlayback(string path)
    {
        int initialFrames = video.Frames;
        await Run(path, false, new(), CancellationToken.None);
        if (playbackError != null) throw new IOException("Normal video playback failed", playbackError);
        if (video.Frames - initialFrames < 2) throw new IOException("Normal video decoder produced too few frames");
        video.SaveFrame(Path.Combine(AppContext.BaseDirectory, "video-preview.png"));
        int count = 0; await foreach (var frame in new FfmpegDecoder().Decode(path, 96, 30)) { count++; lastFrame = frame; AsciiReady?.Invoke(frame, Mode); }
        if (count < 2) throw new IOException("ASCII decoder produced too few frames");
        using var cancelled = new CancellationTokenSource();
        await using var stream = new FfmpegDecoder().Decode(path, 96, 30, cancelled.Token).GetAsyncEnumerator();
        if (!await stream.MoveNextAsync()) throw new IOException("Cancellation test produced no frame");
        cancelled.Cancel();
        try { await stream.MoveNextAsync(); throw new IOException("Decoder ignored cancellation"); }
        catch (OperationCanceledException) { }
    }
    internal void StartShutdownTest(string path) { file = path; Start(true); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { disposed = true; cancellation?.Cancel(); audio?.Stop(); audio?.Dispose(); vlc?.Dispose(); cancellation?.Dispose(); }
        base.Dispose(disposing);
    }
}
