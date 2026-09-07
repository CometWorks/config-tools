#!/usr/bin/env python3
"""Linux PTY regression for Terminal.Gui's menu/input queue freeze. Stdlib only."""
import fcntl
import os
from pathlib import Path
import pty
import select
import signal
import struct
import sys
import tempfile
import termios
import time

exe = str(Path(sys.argv[1] if len(sys.argv) > 1 else 'dist/PulsarConfig-linux-x64.bin').resolve())
with tempfile.TemporaryDirectory(prefix='config-tools-input-') as directory:
    root = Path(directory)
    pid, fd = pty.fork()
    if pid == 0:
        fcntl.ioctl(1, termios.TIOCSWINSZ, struct.pack('HHHH', 32, 120, 0, 0))
        os.environ.update(HOME=directory, XDG_CONFIG_HOME=directory + '/config',
                          XDG_STATE_HOME=directory + '/state', XDG_DATA_HOME=directory + '/data',
                          TERM='xterm-256color', HTTPS_PROXY='http://127.0.0.1:9')
        os.execv(exe, [exe, '--target', directory + '/install'])
    os.set_blocking(fd, False)
    output = bytearray()

    def pump(seconds=.1):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            if select.select([fd], [], [], max(0, min(.05, end - time.monotonic())))[0]:
                try:
                    data = os.read(fd, 65536)
                except OSError:
                    return
                if not data:
                    return
                output.extend(data)

    def send(value):
        remaining = memoryview(value.encode())
        while remaining:
            try:
                remaining = remaining[os.write(fd, remaining[:2048]):]
            except BlockingIOError:
                pump()
        pump()

    def expect(text):
        end = time.monotonic() + 10
        while text.encode() not in output and time.monotonic() < end:
            pump()
        assert text.encode() in output, f'Input stopped responding: expected {text!r}'
        output.clear()

    try:
        expect('Start game')
        for iteration in range(12):
            # Click Tools, then rapidly move across open menu headings. No menu action
            # installs, edits configuration, or launches Steam in this test.
            send('\x1b[<0;23;1M\x1b[<0;23;1m')
            # Drain output between bursts, as a real terminal does, rather than
            # overrun the PTY's bounded input buffer with one giant write.
            for batch in range(10):
                send(''.join(f'\x1b[<35;{2 + (batch * 40 + i) % 25};1M' for i in range(40)))
            send('\x1b[<0;100;20M\x1b[<0;100;20m')  # Click passive space to close any open menu
            pump(.15)
            send('\x1b[18~')  # F7: Sources
            expect('Plugin sources')
            send('\x1bOP')    # F1: Home
            expect('Steam launch options:')
        send('\x1b[21~')      # F10: Quit
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            pump()
            ended, status = os.waitpid(pid, os.WNOHANG)
            if ended:
                assert os.waitstatus_to_exitcode(status) == 0
                pid = 0
                break
        assert pid == 0, 'Tool did not exit after menu stress'
        print('PASS: 4,800 mouse events, menu clicks, sources/home navigation, clean exit.')
    finally:
        if pid:
            os.kill(pid, signal.SIGKILL)
            os.waitpid(pid, 0)
        os.close(fd)
