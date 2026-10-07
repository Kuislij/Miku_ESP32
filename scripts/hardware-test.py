"""Real-device protocol checks. Does not flash, erase or upload any data externally."""
import argparse
import base64
import json
from pathlib import Path
import time
import serial

parser = argparse.ArgumentParser()
parser.add_argument('--port', default='COM5')
parser.add_argument('--output', default='artifacts/hardware-result.json')
args = parser.parse_args()
checks = []
wire_buffer = bytearray()
events = []
stats = []
next_id = 0

with serial.Serial() as port:
    port.port = args.port
    port.baudrate = 115200
    port.timeout = 0.1
    port.dtr = False
    port.rts = False
    port.open()

    def read_frames(seconds):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            wire_buffer.extend(port.read(max(1, port.in_waiting)))
            while b'\n' in wire_buffer:
                line, _, rest = wire_buffer.partition(b'\n')
                wire_buffer[:] = rest
                try:
                    version, kind, request_id, encoded = line.strip(b'\r').decode('ascii').split('|')
                    if version != '1':
                        continue
                    payload = base64.b64decode(encoded, validate=True).decode('utf-8')
                    frame = (kind, int(request_id), payload)
                    events.append(frame)
                    if kind == 'STAT':
                        stats.append(json.loads(payload))
                    yield frame
                except (ValueError, UnicodeError):
                    continue
            if len(wire_buffer) > 8192:
                wire_buffer.clear()

    def check(value, name):
        if not value:
            raise RuntimeError('FAIL: ' + name)
        checks.append(name)
        print('PASS:', name, flush=True)

    def command(text, expect='RES', fragmented=False):
        global next_id
        next_id += 1
        request_id = next_id
        wire = f'1|CMD|{request_id}|{base64.b64encode(text.encode()).decode()}\n'.encode()
        if fragmented:
            for i in range(0, len(wire), 3):
                port.write(wire[i:i+3])
                time.sleep(0.002)
        else:
            port.write(wire)
        for kind, ident, payload in read_frames(5):
            if ident == request_id and kind in ('RES', 'ERR'):
                check(kind == expect, text + ' response')
                return payload
        raise RuntimeError('Timeout: ' + text)

    list(read_frames(2))
    check(command('ping', fragmented=True) == 'pong', 'fragmented ping')
    command('info')
    check(bool(stats), 'STAT received')
    initial = stats[-1]
    check(initial['firmware'] == '0.2.0', 'firmware version')
    check(initial['flashSize'] == 16 * 1048576, '16 MiB Flash detected')
    check(initial['psramSize'] >= 8_000_000, '8 MiB PSRAM initialized')
    check(initial['freeHeap'] > 100_000, 'internal heap available')
    command('tasks')
    check(any(kind == 'TASKS' and 'Waiting' in data for kind, _, data in events), 'task states snapshot')
    command('start demo')
    command('stop demo')
    check('stopped' in command('services').lower(), 'service stopped')
    command('logs')
    command('start unknown', 'ERR')
    command('ping extra', 'ERR')
    command('config telemetry_ms 1', 'ERR')
    port.write(b'garbage\n' + b'x' * 9000 + b'\n')
    check(command('ping') == 'pong', 'recovery after malformed and oversized frames')
    original_period = initial['telemetryMs']
    command('config telemetry_ms 500')
    command('reboot')
    list(read_frames(3))
    check('telemetry_ms=500' in command('config'), 'configuration survives reboot')
    command(f'config telemetry_ms {original_period}')
    command('video')
    check(any(kind == 'EVT' and data == 'media.ascii.open' for kind, _, data in events), 'media event')
    start_count = len(stats)
    list(read_frames(20))
    check(len(stats) - start_count >= 15, '20-second telemetry stability')
    check(stats[-1]['uptime'] >= 20, 'uptime advances without reboot')
    check(stats[-1]['droppedMessages'] == 0, 'no IPC drops')
    check(initial['freeHeap'] - stats[-1]['freeHeap'] < 16000, 'bounded heap change')
    result = {'status': 'PASS', 'port': args.port, 'checks': checks, 'initial': initial, 'final': stats[-1], 'telemetryFrames': len(stats)}
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
