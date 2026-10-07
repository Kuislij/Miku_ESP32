"""Test the real firmware file protocol, either through the host binary or USB."""
import argparse
import base64
import json
from pathlib import Path
import queue
import re
import subprocess
import tempfile
import threading
import time
import uuid
import zlib
import sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

parser = argparse.ArgumentParser()
parser.add_argument('--host')
parser.add_argument('--port')
parser.add_argument('--output', default='artifacts/storage-result.json')
parser.add_argument('--keep-demo', action='store_true')
parser.add_argument('--verify-update-note')
parser.add_argument('--cleanup-test-folder')
args = parser.parse_args()
if args.cleanup_test_folder and not re.fullmatch(r'/test-[0-9a-f]{8}', args.cleanup_test_folder):
    parser.error('Cleanup is restricted to a named integration-test folder')
if bool(args.host) == bool(args.port):
    parser.error('Choose --host or --port')
checks = []
frames = queue.Queue()
temporary = tempfile.TemporaryDirectory(prefix='miku-files-') if args.host else None
process = None
device = None
reader = None
done = threading.Event()
ident = 0
stat_count = 0

def decode(line):
    global stat_count
    try:
        version, kind, number, payload = line.strip().decode('ascii').split('|')
        if version == '1':
            if kind == 'STAT':
                stat_count += 1
            frames.put((kind, int(number), base64.b64decode(payload, validate=True).decode('utf-8')))
    except (ValueError, UnicodeError):
        pass

if args.host:
    process = subprocess.Popen([args.host, '--storage-root', temporary.name], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    def read():
        for line in process.stdout:
            decode(line)
    send = lambda wire: (process.stdin.write(wire), process.stdin.flush())
else:
    import serial
    device = serial.Serial()
    device.port, device.baudrate, device.timeout = args.port, 115200, .1
    device.dtr = device.rts = False
    device.open()
    def read():
        buffer = bytearray()
        while not done.is_set():
            try:
                buffer.extend(device.read(max(1, device.in_waiting)))
            except (serial.SerialException, OSError):
                return
            while b'\n' in buffer:
                line, _, rest = buffer.partition(b'\n')
                buffer[:] = rest
                decode(line)
            if len(buffer) > 8192:
                buffer.clear()
    send = device.write
reader = threading.Thread(target=read, daemon=True)
reader.start()
if args.port:
    # ROM/PSRAM boot follows a flash reset. Sending before UART initialization
    # loses the first command; wait for a firmware frame before testing RPCs.
    deadline = time.monotonic() + 10
    while stat_count == 0 and time.monotonic() < deadline:
        time.sleep(.05)

def command(text, expected='RES'):
    global ident
    ident += 1
    request = ident
    send(f'1|CMD|{request}|{base64.b64encode(text.encode()).decode()}\n'.encode())
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            frame = frames.get(timeout=.2)
        except queue.Empty:
            continue
        if frame[1] == request and frame[0] in ('RES', 'ERR'):
            assert frame[0] == expected, (text[:120], frame)
            return frame[2]
    raise RuntimeError('Timeout: ' + text[:100])

def rpc(text, expected='RES'):
    reply = command('fs ' + text, expected)
    return json.loads(reply) if expected == 'RES' else reply

def path(value):
    return base64.b64encode(value.encode()).decode()

def check(condition, name):
    assert condition, name
    checks.append(name)
    print('PASS ' + name, flush=True)

def upload(name, data, version='*', checksum=None):
    handle = rpc(f'begin {path(name)} {len(data)} {zlib.crc32(data) if checksum is None else checksum} {version}')['token']
    for offset in range(0, len(data), 2048):
        chunk = data[offset:offset+2048]
        reply = rpc(f'chunk {handle} {offset} {base64.b64encode(chunk).decode()}')
        assert reply['received'] == offset + len(chunk)
    return handle

def save(name, data, version='*'):
    return rpc('commit ' + upload(name, data, version))['version']

def download(name):
    meta = rpc('stat ' + path(name))
    data = bytearray()
    for offset in range(0, meta['size'], 2048):
        reply = rpc(f'read {path(name)} {offset} {min(2048, meta["size"]-offset)} {meta["version"]}')
        chunk = base64.b64decode(reply['data'], validate=True)
        assert reply['offset'] == offset and reply['crc32'] == zlib.crc32(chunk) and reply['version'] == meta['version']
        data.extend(chunk)
    assert len(data) == meta['size'] and zlib.crc32(data) == meta['crc32']
    return bytes(data), meta

def listing(name):
    entries, cursor, version = [], 0, '*'
    while True:
        page = rpc(f'list {path(name)} {cursor} {version}')
        entries += page['entries']
        version = page['version']
        if page['next'] is None:
            return entries
        assert page['next'] > cursor
        cursor = page['next']

def copy(source, target):
    handle = rpc(f'copy {path(source)} {path(target)}')['token']
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        status = rpc('status ' + handle)
        assert 0 <= status['received'] <= status['total']
        if status['state'] == 'complete':
            return status['version']
        assert status['state'] == 'running', status
        time.sleep(.03)
    raise RuntimeError('Copy did not finish')

folder = '/test-' + uuid.uuid4().hex[:8]
print('Test directory: ' + folder, flush=True)
try:
    info = rpc('info')
    check(info['ready'] and info['total'] > 10 * 1048576 and info['maxFile'] == 4 * 1048576 and info['chunkSize'] == 2048, 'mounted storage and capacity')
    if args.verify_update_note:
        expected = ('Привет, Miku!\r\nРедактируем файл во Flash.\r\n' * 127).encode()
        check(download(args.verify_update_note)[0] == expected, 'file contents preserved across application-only firmware update')
    rpc('mkdir ' + path(folder))
    rpc('mkdir ' + path(folder + '/Заметки'))
    note = folder + '/Заметки/Моя заметка.txt'
    text = ('Привет, Miku!\r\nРедактируем файл во Flash.\r\n' * 127).encode()
    save(note, text)
    downloaded, metadata = download(note)
    check(downloaded == text, 'multi-chunk UTF-8 file and Cyrillic folder/name')
    rpc(f'rename {path(folder + "/Заметки")} {path(folder + "/Archive")}')
    check(download(folder + '/Archive/Моя заметка.txt')[0] == text, 'folder rename preserves children')
    long_parent = folder + '/' + 'x' * 110
    rpc('mkdir ' + path(long_parent))
    check(rpc(f'rename {path(folder + "/Archive")} {path(long_parent + "/" + "z" * 20)}', 'ERR') == 'INVALID_PATH', 'folder rename cannot strand children beyond path limit')
    rpc(f'rename {path(folder + "/Archive")} {path(folder + "/Заметки")}')
    metadata = rpc('stat ' + path(note))
    edited = b'\xef\xbb\xbf' + text + 'Новая строка!\r\n'.encode()
    save(note, edited, metadata['version'])
    check(download(note)[0] == edited, 'edit preserves full contents and UTF-8 BOM')
    stale = metadata['version']
    check(rpc(f'begin {path(note)} 0 0 {stale}', 'ERR') == 'CONFLICT' and download(note)[0] == edited, 'stale edit rejected without overwriting')
    binary_name = folder + '/bytes.bin'
    binary = bytes(range(256)) * 256 + b'\0\xff\n'
    save(binary_name, binary)
    check(download(binary_name)[0] == binary, '65539-byte binary round trip including zero/FF/newline')
    for size in (0, 1, 2047, 2048, 2049):
        name = folder + '/boundary.bin'
        payload = bytes((i * 73) % 256 for i in range(size))
        save(name, payload)
        check(download(name)[0] == payload, f'chunk boundary {size}')
    handle = upload(note, b'broken', checksum=0)
    check(rpc('commit ' + handle, 'ERR') == 'CHECKSUM_MISMATCH' and download(note)[0] == edited, 'bad checksum keeps original file intact')
    handle = rpc(f'begin {path(note)} 4 {zlib.crc32(b"test")} *')['token']
    check(rpc(f'chunk {handle} 1 dGVzdA==', 'ERR') == 'OFFSET_MISMATCH', 'out-of-order block rejected')
    check(rpc(f'chunk {handle} 0 dGVzdB==', 'ERR') == 'INVALID_CHUNK', 'noncanonical chunk rejected')
    check(rpc('mkdir ' + path(folder + '/busy'), 'ERR') == 'UPLOAD_BUSY', 'mutations excluded during upload')
    rpc('abort ' + handle)
    check(download(note)[0] == edited, 'cancelled upload preserves original')
    copy_name = folder + '/copy.txt'
    copy(note, copy_name)
    renamed = folder + '/renamed.txt'
    rpc(f'rename {path(copy_name)} {path(renamed)}')
    check(download(renamed)[0] == edited, 'on-device streaming copy and rename')
    saved_interval = int(command('config').split('=')[1])
    command('config telemetry_ms 200')
    try:
        initial_stats = stat_count
        background = folder + '/background.bin'
        copy_started = time.monotonic()
        copy(binary_name, background)
        copy_seconds = time.monotonic() - copy_started
        check(stat_count > initial_stats and download(background)[0] == binary, 'background copy preserves autonomous telemetry')
        cancelled = folder + '/cancelled-copy.bin'
        handle = rpc(f'copy {path(binary_name)} {path(cancelled)}')['token']
        check(rpc(f'chunk {handle} 0 AA==', 'ERR') == 'UPLOAD_BUSY', 'background copy cannot be injected with upload chunks')
        rpc('abort ' + handle)
        check(rpc('status ' + handle)['state'] == 'failed' and rpc('stat ' + path(cancelled), 'ERR') == 'NOT_FOUND', 'copy cancellation removes only its temporary file')
        if args.port:
            autonomous = folder + '/without-client.bin'
            handle = rpc(f'copy {path(binary_name)} {path(autonomous)}')['token']
            done.set()
            reader.join(timeout=2)
            device.close()
            time.sleep(min(45, max(10, copy_seconds * 2 + 2)))
            done.clear()
            device.open()
            reader = threading.Thread(target=read, daemon=True)
            reader.start()
            status = rpc('status ' + handle)
            assert status['state'] == 'complete', status
            check(download(autonomous)[0] == binary, 'ESP32 finishes copy while PC serial client is closed')
    finally:
        command(f'config telemetry_ms {saved_interval}')
    check(rpc(f'rename {path(note)} {path(renamed)}', 'ERR') == 'ALREADY_EXISTS', 'rename refuses an existing target')
    check(rpc('remove ' + path(folder), 'ERR') == 'DELETE_FAILED_OR_NOT_EMPTY', 'nonempty directory protected')
    check(rpc('remove ' + path('/'), 'ERR') == 'ROOT_PROTECTED', 'root directory protected')
    for invalid in ('/../x', '/.miku/old.bin', '/.MIKU/x', '/a//b', '/bad.', '/bad\\name', '/a/' + 'я' * 80):
        check(rpc('stat ' + path(invalid), 'ERR') == 'INVALID_PATH', 'invalid/reserved path ' + invalid[:25])
    check(rpc('stat /w==', 'ERR') == 'INVALID_PATH', 'invalid UTF-8 path rejected')
    check(rpc(f'begin {path(folder + "/huge.bin")} 4194305 0 *', 'ERR') == 'FILE_TOO_LARGE', 'file size limit checked before allocation')
    list_folder = folder + '/pages'
    rpc('mkdir ' + path(list_folder))
    for i in range(17):
        save(list_folder + f'/item-{i:02d}.txt', b'')
    check(len(listing(list_folder)) == 17, 'directory pagination retains all 17 entries')
    first_page = rpc(f'list {path(list_folder)} 0 *')
    save(list_folder + '/changed.txt', b'')
    check(rpc(f'list {path(list_folder)} 8 {first_page["version"]}', 'ERR') == 'CONFLICT', 'changed directory cannot mix pagination snapshots')
    command(f'write "{folder}/terminal.txt" "Текст из терминала"')
    check(command(f'cat "{folder}/terminal.txt"') == 'Текст из терминала' and 'terminal.txt' in command('ls ' + folder), 'terminal write/cat/ls with quoted paths')
    handle = rpc(f'begin {path(note)} 4 {zlib.crc32(b"test")} *')['token']
    rpc(f'chunk {handle} 0 dGVzdA==')
    command('reboot')
    time.sleep(1.5 if args.port else .1)
    check(download(note)[0] == edited and download(binary_name)[0] == binary, 'committed text/binary survive reboot; unfinished edit discarded')
    check(rpc('commit ' + handle, 'ERR') == 'INVALID_TOKEN', 'reboot invalidates unfinished upload token')
    before = rpc('info')['free']
    rpc('remove ' + path(binary_name))
    check(rpc('info')['free'] > before, 'deletion returns storage capacity')
    if args.host:
        for i in range(18, 128):
            save(list_folder + f'/item-{i:03d}.txt', b'')
        check(len(listing(list_folder)) == 128 and rpc('mkdir ' + path(list_folder + '/overflow'), 'ERR') == 'DIRECTORY_FULL_OR_MISSING', 'directory limit and bounded pagination')
        rpc(f'rename {path(list_folder + "/item-00.txt")} {path(list_folder + "/renamed-in-full-directory.txt")}')
        check(len(listing(list_folder)) == 128, 'rename within a full directory does not consume another slot')
    def clean(name):
        for entry in listing(name):
            child = name + '/' + entry['name']
            if entry['directory']:
                clean(child)
            else:
                rpc('remove ' + path(child))
        rpc('remove ' + path(name))
    clean(folder)
    if args.cleanup_test_folder:
        clean(args.cleanup_test_folder)
    check(not any(e['name'] == folder[1:] for e in listing('/')), 'test files removed without touching other files')
    if args.keep_demo:
        root = listing('/')
        if not any(e['name'].lower() == 'notes' for e in root):
            rpc('mkdir ' + path('/notes'))
        if not any(e['name'] == 'Первая заметка.txt' for e in listing('/notes')):
            save('/notes/Первая заметка.txt', 'Привет! Я живу во Flash ESP32.\nМеня можно читать и редактировать в MikuOS.\n'.encode())
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text(json.dumps({'passed': len(checks), 'checks': checks, 'storage': rpc('info'), 'device': args.port or 'C++ host'}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'{len(checks)} storage checks passed', flush=True)
finally:
    done.set()
    if process:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
        reader.join(timeout=1)
        if process.returncode not in (0, None):
            print(process.stderr.read().decode(errors='replace'))
    if device:
        reader.join(timeout=1)
        device.close()
    if temporary:
        temporary.cleanup()
