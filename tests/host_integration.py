"""Exercise the actual C++ host binary over stdin/stdout; standard library only."""
import base64
import json
import queue
import subprocess
import sys
import threading
import time

process = subprocess.Popen([sys.argv[1]], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
frames = queue.Queue()

def read():
    for line in process.stdout:
        version, kind, ident, payload = line.decode().strip().split('|')
        assert version == '1'
        frames.put((kind, int(ident), base64.b64decode(payload, validate=True).decode()))

thread = threading.Thread(target=read, daemon=True)
thread.start()

def wait(predicate):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        try:
            frame = frames.get(timeout=0.1)
        except queue.Empty:
            continue
        if predicate(frame):
            return frame
    raise RuntimeError('Timed out waiting for host frame')

def wire(ident, text):
    return f'1|CMD|{ident}|{base64.b64encode(text.encode()).decode()}\n'.encode()

def command(ident, text, expected='RES'):
    process.stdin.write(wire(ident, text))
    process.stdin.flush()
    frame = wait(lambda f: f[1] == ident and f[0] in ('RES', 'ERR'))
    assert frame[0] == expected, frame
    return frame[2]

try:
    assert json.loads(wait(lambda f: f[0] == 'STAT')[2])['taskCount'] == 2
    print('PASS autonomous host telemetry')
    for byte in wire(1, 'ping'):
        process.stdin.write(bytes([byte]))
        process.stdin.flush()
    assert wait(lambda f: f[1] == 1)[2] == 'pong'
    print('PASS fragmented host ping')
    command(2, 'start demo')
    command(3, 'config telemetry_ms 500')
    process.stdin.write(wire(4, 'reboot') + wire(5, 'config') + wire(6, 'services'))
    process.stdin.flush()
    wait(lambda f: f[1] == 4)
    assert wait(lambda f: f[1] == 5)[2] == 'telemetry_ms=500'
    assert 'demo  Stopped' in wait(lambda f: f[1] == 6)[2]
    print('PASS coalesced commands and reboot state/configuration')
    process.stdin.write(b'x' * 9000 + b'\n')
    process.stdin.flush()
    assert command(7, 'ping') == 'pong'
    command(8, 'start missing', 'ERR')
    print('PASS overflow recovery and service error')
finally:
    process.stdin.close()
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        process.kill()
    thread.join(timeout=1)
assert process.returncode == 0, process.stderr.read().decode()
