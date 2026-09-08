#!/usr/bin/env python3
"""Linux PTY regression for navigation, resizing and input queue handling. Stdlib only."""
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
    (root / 'install').mkdir()
    (root / 'install' / 'Interim.bin').touch()  # Only a marker; Steam is stubbed below.
    stub = root / 'bin'
    stub.mkdir()
    steam = stub / 'steam'
    steam.write_text('#!/bin/sh\nprintf "%s\\n" "$@" > "$STEAM_CAPTURE"\n')
    steam.chmod(0o755)
    pid, fd = pty.fork()
    if pid == 0:
        fcntl.ioctl(1, termios.TIOCSWINSZ, struct.pack('HHHH', 32, 120, 0, 0))
        os.environ.update(HOME=directory, XDG_CONFIG_HOME=directory + '/config',
                          XDG_STATE_HOME=directory + '/state', XDG_DATA_HOME=directory + '/data',
                          TERM='xterm-256color', HTTPS_PROXY='http://127.0.0.1:9',
                          PATH=str(stub) + ':' + os.environ['PATH'], STEAM_CAPTURE=directory + '/steam-args')
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
        assert text.encode() in output, (f'Input stopped responding: expected {text!r}\n' + repr(bytes(output[-12000:])))
        output.clear()

    try:
        pump(1)
        assert b'\x1b[8;40;128t' in output, 'Missing terminal resize request'
        expect('Start game')
        # Emulate a terminal honoring the request, then a user resizing it later.
        fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack('HHHH', 40, 128, 0, 0))
        os.kill(pid, signal.SIGWINCH)
        pump(.5)
        send('\x1b[18~')
        expect('Plugin sources')
        send('\x1bOP')
        expect('Steam launch options:')
        send('\x1bOQ')
        expect('Choose a theme for both tools')
        send('\x1bOP')
        expect('Steam launch options:')
        # Setup is reached from the home screen, without any menu bar.
        send('\t' * 5 + '\r')
        expect('Manage installation')
        send('\x1bOP')
        expect('Steam launch options:')
        send('\t' * 5 + '\r')
        expect('Manage installation')
        send('\x1b[<0;6;40M\x1b[<0;6;40m')
        expect('Steam launch options:')
        send('\t' * 5 + '\r')
        expect('Manage installation')
        send('\x1bOR')
        expect('Space/Enter toggles')
        send('\x1bOP')
        expect('Steam launch options:')
        send('\t' * 5 + '\r')
        expect('Manage installation')
        send('\x1b[15~')
        expect('Launch requested through Steam')
        pump(.3)
        assert (root / 'steam-args').read_text().splitlines() == ['-applaunch', '244850']
        # F1 cancels an edit dialog too, without saving anything.
        send('\x1b[18~')
        expect('Plugin sources')
        send('\r')
        expect('Edit PluginHub')
        send('\x1bOP')
        expect('Steam launch options:')
        for iteration in range(12):
            for batch in range(20):
                send(''.join(f'\x1b[<35;{2 + (batch * 20 + i) % 100};{40 if i % 2 else 8}M' for i in range(20)))
            pump(1.5)  # Let the terminal drain the burst before sending a key.
            send('\x1b[18~')  # F7: Sources
            expect('Plugin sources')
            send('\x1bOP')    # F1: Home
            expect('Steam launch options:')
        fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack('HHHH', 32, 120, 0, 0))
        os.kill(pid, signal.SIGWINCH)
        pump(.5)
        send('\t' * 5 + '\r')
        expect('Manage installation')
        send('\x1b[21~')      # F10 must quit from Setup as well.
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            pump()
            ended, status = os.waitpid(pid, os.WNOHANG)
            if ended:
                assert os.waitstatus_to_exitcode(status) == 0
                pid = 0
                break
        assert pid == 0, 'Tool did not exit after input stress'
        print('PASS: resize request, home > Setup > F1/click Home, global shortcuts, 4,800 mouse events, sources/home navigation, clean exit.')
    finally:
        if pid:
            os.kill(pid, signal.SIGKILL)
            os.waitpid(pid, 0)
        os.close(fd)
