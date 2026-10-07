using System.Text;
using MikuOS.Storage;
using MikuOS.Transport;

namespace MikuOS.ControlCenter.UI;
public sealed class FileManagerView : UserControl
{
    private readonly DeviceFileClient client;
    private readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, BorderStyle = BorderStyle.None, HideSelection = false };
    private readonly TextBox filter = new() { Dock = DockStyle.Top, PlaceholderText = "Найти в этой папке…", BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox location = new() { Dock = DockStyle.Fill, Text = "/", BorderStyle = BorderStyle.FixedSingle };
    private readonly RichTextBox editor = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new("Consolas", 11), AcceptsTab = true, DetectUrls = false, WordWrap = false, ReadOnly = true, BackColor = Color.White };
    private readonly Label storage = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "Подключите ESP32, чтобы открыть её хранилище", AutoEllipsis = true };
    private readonly Label title = new() { Dock = DockStyle.Top, Height = 36, Text = "Редактор · UTF-8", ForeColor = Theme.Ink, Font = new("Segoe UI", 12, FontStyle.Bold), AutoEllipsis = true };
    private readonly Label message = new() { Dock = DockStyle.Fill, Text = "Файлы хранятся на плате и сохраняются после перезапуска.", AutoEllipsis = true, ForeColor = Theme.Muted };
    private readonly Label details = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Theme.Muted, Text = "Откройте текстовый файл двойным щелчком.", AutoEllipsis = true };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, WrapContents = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 6, Visible = false };
    private readonly Button save = Theme.Button("Сохранить на ESP32"), reload = Theme.Button("Прочитать заново"), cancel = Theme.Button("Отменить передачу"), export = Theme.Button("Копия текста на ПК");
    private readonly Panel browser = new() { Dock = DockStyle.Fill, Padding = new(0, 0, 16, 0) };
    private readonly Panel address = new() { Dock = DockStyle.Fill };
    private IReadOnlyList<FileEntry> entries = [];
    private string directory = "/", savedText = "", lineEnding = "\n";
    private DeviceFile? opened;
    private bool bom, busy, connected, ready, editable, demo;
    private CancellationTokenSource connection = new();
    private CancellationTokenSource? operation;
    public bool HasUnsavedChanges => opened != null && editable && editor.Text != savedText;
    private string? SelectedPath => list.SelectedItems.Count == 0 ? null : FilePaths.Combine(directory, ((FileEntry)list.SelectedItems[0].Tag!).Name);
    public FileManagerView(DeviceSession session)
    {
        client = new(session); BackColor = Theme.Surface; ForeColor = Theme.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 75)); layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Absolute, 90)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        var heading = new Panel { Dock = DockStyle.Fill }; var label = Theme.Label("Файлы на ESP32", 22); label.ForeColor = Theme.Ink; label.Dock = DockStyle.Top; label.Height = 40; label.AutoSize = false;
        heading.Controls.Add(storage); heading.Controls.Add(label); layout.Controls.Add(heading, 0, 0);
        var up = Theme.Button("↑ Выше"); up.Dock = DockStyle.Left; up.Width = 100; up.Click += async (_, _) => await Run(t => Navigate(FilePaths.Parent(directory), t));
        var go = Theme.Button("Открыть путь"); go.Dock = DockStyle.Right; go.Width = 130; go.Click += async (_, _) => await Run(t => Navigate(location.Text.Trim(), t));
        location.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Run(t => Navigate(location.Text.Trim(), t)); } };
        address.Padding = new(0, 0, 0, 8); address.Controls.Add(location); address.Controls.Add(go); address.Controls.Add(up); layout.Controls.Add(address, 0, 1);
        void Action(string text, Func<CancellationToken, Task> handler) { var button = Theme.Button(text); button.Padding = new(12, 5, 12, 5); button.Font = new("Segoe UI", 9); button.Margin = new(0, 0, 7, 7); button.Click += async (_, _) => await Run(handler); actions.Controls.Add(button); }
        Action("Обновить", RefreshFiles); Action("Новая папка", NewDirectory); Action("Новый текст", NewText); Action("Загрузить с ПК", Upload); Action("Скачать на ПК", Download);
        Action("Открыть", OpenSelected); Action("Переименовать", t => RenameOrCopy(false, t)); Action("Копировать", t => RenameOrCopy(true, t)); Action("Удалить", Delete);
        layout.Controls.Add(actions, 0, 2);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; body.ColumnStyles.Add(new(SizeType.Percent, 43)); body.ColumnStyles.Add(new(SizeType.Percent, 57));
        list.Columns.Add("Имя", 235); list.Columns.Add("Тип", 75); list.Columns.Add("Размер", 95); list.DoubleClick += async (_, _) => await Run(OpenSelected);
        list.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Run(OpenSelected); } };
        filter.TextChanged += (_, _) => RenderList(); browser.Controls.Add(list); browser.Controls.Add(filter); body.Controls.Add(browser, 0, 0);
        var editing = new Panel { Dock = DockStyle.Fill, Padding = new(15, 0, 0, 0) }; var editorButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 86, WrapContents = true };
        save.BackColor = Theme.Pale; save.ForeColor = Theme.Accent; save.Click += async (_, _) => await Run(Save);
        reload.Click += async (_, _) => await Run(async t => { if (opened != null && await ConfirmLeaveAsync()) await OpenFile(opened.Path, t); });
        export.Click += (_, _) => ExportText(); editorButtons.Controls.AddRange([save, reload, export]);
        editor.TextChanged += (_, _) => UpdateEditor();
        editor.KeyDown += async (_, e) => { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; await Run(Save); } };
        editing.Controls.Add(editor); editing.Controls.Add(editorButtons); editing.Controls.Add(details); editing.Controls.Add(title); body.Controls.Add(editing, 1, 0); layout.Controls.Add(body, 0, 3);
        var footer = new Panel { Dock = DockStyle.Fill, Padding = new(0, 10, 0, 0) }; cancel.Dock = DockStyle.Right; cancel.Width = 190; cancel.Visible = false; cancel.Click += (_, _) => operation?.Cancel();
        footer.Controls.Add(message); footer.Controls.Add(cancel); footer.Controls.Add(progress); layout.Controls.Add(footer, 0, 4); Controls.Add(layout); UpdateControls();
    }
    private static string SizeText(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:F2} МиБ" : bytes >= 1024 ? $"{bytes / 1024.0:F1} КиБ" : $"{bytes} Б";
    public void ConnectionChanged(bool value, bool demonstration = false)
    {
        connection.Cancel(); connection.Dispose(); connection = new(); connected = value; ready = false; demo = demonstration;
        if (!value) { list.Items.Clear(); storage.Text = "Плата отключена · текст редактора оставлен на экране"; message.Text = "Подключите плату. Несохранённый текст можно экспортировать на ПК."; }
        else { directory = "/"; location.Text = directory; message.Text = demo ? "Демонстрация: файлы живут в памяти приложения до его закрытия." : "Хранилище Flash самой ESP32 · файлы остаются без компьютера."; _ = InitializeWhenIdle(connection.Token); }
        UpdateControls();
    }
    private async Task InitializeWhenIdle(CancellationToken token)
    {
        try { while (busy) await Task.Delay(50, token); if (!token.IsCancellationRequested) await Run(RefreshFiles, true); }
        catch (OperationCanceledException) { }
    }
    private void UpdateControls()
    {
        actions.Enabled = address.Enabled = browser.Enabled = connected && ready && !busy;
        editor.ReadOnly = !editable || !connected || !ready || busy;
        save.Enabled = connected && ready && !busy && HasUnsavedChanges; reload.Enabled = connected && ready && !busy && opened != null;
        export.Enabled = opened != null && editable && !busy; cancel.Visible = progress.Visible = busy;
    }
    private void UpdateEditor()
    {
        title.Text = opened == null ? "Редактор · UTF-8" : Path.GetFileName(opened.Path) + (HasUnsavedChanges ? "  ● Не сохранено" : "  ✓");
        if (opened != null && editable) details.Text = $"UTF-8{(bom ? " с BOM" : "")} · {SizeText(Encoding.UTF8.GetByteCount(editor.Text))} · Ctrl+S сохраняет на плате";
        UpdateControls();
    }
    private async Task Run(Func<CancellationToken, Task> action, bool initializing = false)
    {
        if (busy || !connected || (!ready && !initializing)) return;
        busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(connection.Token); var current = operation; UpdateControls(); progress.Value = 0;
        try { await action(current.Token); }
        catch (OperationCanceledException) { message.Text = "Передача отменена. Проверьте список файлов; прежняя копия сохраняется до подтверждения новой."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or TimeoutException or System.Text.Json.JsonException or DecoderFallbackException or EncoderFallbackException) { if (!IsDisposed) { message.Text = e.Message; message.ForeColor = Color.FromArgb(181, 65, 95); } }
        finally { current.Dispose(); if (operation == current) operation = null; busy = false; if (!IsDisposed) UpdateControls(); }
    }
    private IProgress<FileProgress> Progress() => new Progress<FileProgress>(p => { if (IsDisposed) return; progress.Value = p.Total == 0 ? 100 : (int)Math.Clamp(p.Done * 100L / p.Total, 0, 100); message.Text = $"{p.Operation}: {SizeText(p.Done)} / {SizeText(p.Total)}"; message.ForeColor = Theme.Accent; });
    private async Task RefreshFiles(CancellationToken token)
    {
        var info = await client.InfoAsync(token); token.ThrowIfCancellationRequested(); ready = info.Ready;
        storage.Text = $"{(demo ? "Демонстрация" : "Flash ESP32")} · {info.Filesystem} · свободно {SizeText(info.Free)} из {SizeText(info.Total)} · файл до 4 МиБ";
        if (!ready) throw new DeviceFileException("STORAGE_UNAVAILABLE");
        entries = await client.ListAsync(directory, token); token.ThrowIfCancellationRequested(); location.Text = directory; RenderList(); message.ForeColor = Theme.Muted;
    }
    private void RenderList()
    {
        list.BeginUpdate(); list.Items.Clear();
        foreach (var entry in entries.Where(e => e.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
            list.Items.Add(new ListViewItem([entry.Name, entry.Directory ? "Папка" : "Файл", entry.Directory ? "—" : SizeText(entry.Size)]) { Tag = entry, ForeColor = entry.Directory ? Theme.Accent : Theme.Ink });
        list.EndUpdate();
    }
    private async Task Navigate(string path, CancellationToken token)
    { if (!FilePaths.Valid(path)) throw new DeviceFileException("INVALID_PATH"); var items = await client.ListAsync(path, token); directory = path; entries = items; location.Text = path; filter.Clear(); RenderList(); message.Text = $"{path} · элементов: {items.Count}"; }
    private async Task OpenSelected(CancellationToken token)
    {
        var path = SelectedPath; if (path == null) return;
        var selected = (FileEntry)list.SelectedItems[0].Tag!;
        if (selected.Directory) await Navigate(path, token);
        else if (await ConfirmLeaveAsync()) await OpenFile(path, token);
    }
    private async Task OpenFile(string path, CancellationToken token)
    {
        var meta = await client.StatAsync(path, token);
        if (meta.Size > DeviceFileClient.EditorLimit) { message.Text = "Текстовый редактор открывает до 256 КиБ. Этот файл можно скачать на ПК."; return; }
        var file = await client.DownloadAsync(path, Progress(), token); token.ThrowIfCancellationRequested();
        string text; bool hasBom = file.Bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 });
        try { text = new UTF8Encoding(false, true).GetString(file.Bytes, hasBom ? 3 : 0, file.Bytes.Length - (hasBom ? 3 : 0)); if (text.Any(c => c == '\0' || (c < 32 && c is not '\t' and not '\n' and not '\r'))) throw new DecoderFallbackException(); }
        catch (DecoderFallbackException) { message.Text = "Двоичный файл или другая кодировка. Используйте «Скачать на ПК»."; return; }
        lineEnding = text.Contains("\r\n") ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        bom = hasBom; opened = file; editable = true; editor.Text = text.Replace("\r\n", "\n").Replace('\r', '\n'); savedText = editor.Text; UpdateEditor();
        message.Text = $"Прочитан {path} · CRC32 {file.Metadata.Crc32:X8}";
    }
    private byte[] EditorBytes()
    {
        var text = editor.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", lineEnding);
        var bytes = new UTF8Encoding(false, true).GetBytes(text);
        if (bytes.Length + (bom ? 3 : 0) > DeviceFileClient.EditorLimit) throw new IOException("Текст для редактора ограничен 256 КиБ. Более крупный файл загрузите с ПК.");
        return bom ? new byte[] { 239, 187, 191 }.Concat(bytes).ToArray() : bytes;
    }
    private async Task Save(CancellationToken token)
    {
        if (opened == null || !editable) return; var bytes = EditorBytes();
        string version = await client.UploadAsync(opened.Path, bytes, opened.Metadata.Version, Progress(), token);
        opened = new(opened.Path, bytes, new(bytes.Length, false, FileChecksum.Compute(bytes), version)); savedText = editor.Text; UpdateEditor();
        await RefreshFiles(token); message.Text = $"Сохранено на ESP32: {opened.Path} · CRC32 {opened.Metadata.Crc32:X8}";
    }
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!HasUnsavedChanges) return true;
        var answer = MessageBox.Show(FindForm(), "В редакторе есть несохранённые правки. Сохранить их на ESP32?", "MikuOS · сохранить файл", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false; if (answer == DialogResult.No) return true;
        if (!connected || !ready) { message.Text = "Плата отключена. Экспортируйте текст на ПК или подключите её для сохранения."; return false; }
        try { if (busy) await Save(operation?.Token ?? connection.Token); else await Run(Save); return !HasUnsavedChanges; }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { message.Text = e.Message; return false; }
    }
    private string? AskName(string caption, string initial)
    {
        using var dialog = new Form { Text = caption, Size = new(460, 175), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Background, Font = Font };
        var input = new TextBox { Text = initial, Left = 20, Top = 20, Width = 400 }; var ok = Theme.Button("Готово"); ok.Location = new(210, 70); ok.DialogResult = DialogResult.OK; var back = Theme.Button("Отмена"); back.Location = new(320, 70); back.DialogResult = DialogResult.Cancel;
        dialog.Controls.AddRange([input, ok, back]); dialog.AcceptButton = ok; dialog.CancelButton = back; dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? input.Text.Trim() : null;
    }
    private async Task NewDirectory(CancellationToken token)
    { var name = AskName("Название новой папки", "Новая папка"); if (name == null) return; string version = await client.CreateDirectoryAsync(FilePaths.Combine(directory, name), token); await AdvanceVersion(version, token); await RefreshFiles(token); message.Text = "Папка создана на ESP32."; }
    private async Task NewText(CancellationToken token)
    {
        if (!await ConfirmLeaveAsync()) return; var name = AskName("Имя текстового файла", "note.txt"); if (name == null) return; string path = FilePaths.Combine(directory, name);
        await RefreshFiles(token); if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new DeviceFileException("ALREADY_EXISTS");
        var info = await client.InfoAsync(token); await client.UploadAsync(path, [], info.Version, token: token); await RefreshFiles(token); await OpenFile(path, token); editor.Focus();
    }
    private async Task Upload(CancellationToken token)
    {
        using var dialog = new OpenFileDialog { Filter = "Все файлы|*.*", Title = "Загрузить файл во Flash ESP32" }; if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        var name = Path.GetFileName(dialog.FileName); string path = FilePaths.Combine(directory, name);
        if (opened?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true && !await ConfirmLeaveAsync()) return;
        if (new FileInfo(dialog.FileName).Length > DeviceFileClient.MaxFileSize) throw new DeviceFileException("FILE_TOO_LARGE");
        await RefreshFiles(token);
        if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && MessageBox.Show(FindForm(), $"Заменить {name} на плате?", "MikuOS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var info = await client.InfoAsync(token); byte[] bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName, token);
        string version = await client.UploadAsync(path, bytes, info.Version, Progress(), token); await AdvanceVersion(version, token);
        if (opened?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true) { opened = null; editable = false; editor.Clear(); savedText = ""; UpdateEditor(); }
        await RefreshFiles(token); message.Text = $"Загружено на ESP32: {name}";
    }
    private async Task Download(CancellationToken token)
    {
        var path = SelectedPath; if (path == null || ((FileEntry)list.SelectedItems[0].Tag!).Directory) return;
        using var dialog = new SaveFileDialog { Filter = "Все файлы|*.*", FileName = Path.GetFileName(path), Title = "Скачать файл с ESP32" }; if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        var file = await client.DownloadAsync(path, Progress(), token); string temp = dialog.FileName + ".miku-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { await System.IO.File.WriteAllBytesAsync(temp, file.Bytes, token); token.ThrowIfCancellationRequested(); System.IO.File.Move(temp, dialog.FileName, true); }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
        message.Text = $"Скачано: {Path.GetFileName(path)} · CRC32 {file.Metadata.Crc32:X8}";
    }
    private async Task AdvanceVersion(string version, CancellationToken token)
    {
        if (opened == null) return;
        var current = await client.StatAsync(opened.Path, token);
        if (current.Size == opened.Metadata.Size && current.Crc32 == opened.Metadata.Crc32 && current.Version == version) opened = opened with { Metadata = current };
    }
    private async Task RenameOrCopy(bool copy, CancellationToken token)
    {
        var path = SelectedPath; if (path == null) return; var name = AskName(copy ? "Имя копии" : "Новое имя", Path.GetFileName(path)); if (name == null) return;
        string to = FilePaths.Combine(directory, name); string version = copy ? await client.CopyAsync(path, to, token, Progress()) : await client.RenameAsync(path, to, token);
        if (!copy && opened != null && (opened.Path.Equals(path, StringComparison.OrdinalIgnoreCase) || opened.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))) opened = opened with { Path = to + opened.Path[path.Length..] };
        await AdvanceVersion(version, token); await RefreshFiles(token); UpdateEditor(); message.Text = copy ? "Копия создана самой ESP32." : "Имя изменено на ESP32.";
    }
    private async Task Delete(CancellationToken token)
    {
        var path = SelectedPath; if (path == null) return;
        if (opened?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true && !await ConfirmLeaveAsync()) return;
        if (MessageBox.Show(FindForm(), $"Удалить {path} с ESP32?", "MikuOS", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        string version = await client.DeleteAsync(path, token);
        if (opened?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true) { opened = null; editable = false; editor.Clear(); savedText = ""; }
        await AdvanceVersion(version, token); await RefreshFiles(token); UpdateEditor(); message.Text = "Удалено с ESP32.";
    }
    private void ExportText()
    { if (opened == null) return; using var dialog = new SaveFileDialog { FileName = Path.GetFileName(opened.Path), Filter = "Текст UTF-8|*.txt|Все файлы|*.*" }; if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { try { System.IO.File.WriteAllBytes(dialog.FileName, EditorBytes()); message.Text = "Текст редактора сохранён на ПК."; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { message.Text = e.Message; } } }
    protected override void Dispose(bool disposing) { if (disposing) { connection.Cancel(); operation?.Cancel(); connection.Dispose(); } base.Dispose(disposing); }
    internal async Task VerifyFiles()
    {
        while (busy) await Task.Delay(50); string folder = "/ui-" + Guid.NewGuid().ToString("N")[..8]; string path = folder + "/Проверка.txt";
        await client.CreateDirectoryAsync(folder); await client.UploadAsync(path, new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("Привет, Miku!\r\n")).ToArray());
        await Run(async t => { await Navigate(folder, t); await OpenFile(path, t); editor.AppendText("Правки из редактора.\n"); if (!HasUnsavedChanges) throw new IOException("Editor did not track changes"); await Save(t); });
        var data = await client.DownloadAsync(path); var expected = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("Привет, Miku!\r\nПравки из редактора.\r\n")).ToArray();
        if (!data.Bytes.SequenceEqual(expected) || HasUnsavedChanges) throw new IOException("Editor save/read/BOM/CRLF verification failed");
        await client.CopyAsync(path, folder + "/copy.txt"); await client.RenameAsync(folder + "/copy.txt", folder + "/renamed.txt");
        if (!(await client.DownloadAsync(folder + "/renamed.txt")).Bytes.SequenceEqual(data.Bytes)) throw new IOException("Copy/rename verification failed");
        await client.DeleteAsync(folder + "/renamed.txt"); await client.DeleteAsync(path); await client.DeleteAsync(folder); opened = null; editable = false; editor.Clear(); savedText = "";
        await Run(async t => { await Navigate("/", t); await RefreshFiles(t); await OpenFile("/README.txt", t); });
    }
}
