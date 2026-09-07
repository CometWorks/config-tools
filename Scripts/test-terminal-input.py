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
        expect('Start game')
        send('\x1b[18~')
        expect('Plugin sources')
        send('\x1bOP')
        expect('Steam launch options:')
        # Theme selection must release an open menu's mouse grab too.
        send('\x1b[20~')
        send('\x1bOQ')
        expect('Choose a theme for both tools')
        send('\x1bOP')
        expect('Steam launch options:')
        # Open Setup through File, then use the global shortcut to leave its modal.
        send('\x1b[20~')  # F9 opens File
        send('\x1b[B\r')
        expect('Linux setup')
        send('\x1bOP')
        expect('Steam launch options:')
        # Repeat using the mouse for both the File dropdown and setup's Home shortcut.
        send('\x1b[<0;2;1M\x1b[<0;2;1m')
        pump(.5)  # Distinct clicks, outside the driver's double-click window.
        send('\x1b[<0;12;4M\x1b[<0;12;4m')
        expect('Linux setup')
        pump(.5)
        send('\x1b[<0;6;32M\x1b[<0;6;32m')
        expect('Steam launch options:')
        # Other advertised shortcuts also work from Setup, including Start game.
        send('\x1b[20~')
        send('\x1b[B\r')
        expect('Linux setup')
        send('\x1bOR')
        expect('Space/Enter toggles')
        send('\x1b[20~')
        send('\x1b[B\r')
        expect('Linux setup')
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
            # Click Tools, then rapidly move across open menu headings. No menu action
            # installs, edits configuration, or launches Steam in this test.
            send('\x1b[<0;23;1M\x1b[<0;23;1m')
            # Drain output between bursts, as a real terminal does, rather than
            # overrun the PTY's bounded input buffer with one giant write.
            for batch in range(20):
                send(''.join(f'\x1b[<35;{2 + (batch * 20 + i) % 25};1M' for i in range(20)))
            pump(1)  # Let queued menu moves settle before targeting the closing click.
            send('\x1b[<0;100;20M\x1b[<0;100;20m')  # Click passive space to close any open menu
            pump(.5)
            send('\x1b[18~')  # F7: Sources
            expect('Plugin sources')
            send('\x1bOP')    # F1: Home
            expect('Steam launch options:')
        send('\x1b[20~')
        send('\x1b[B\r')
        expect('Linux setup')
        send('\x1b[21~')      # F10 must quit from Setup as well.
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            pump()
            ended, status = os.waitpid(pid, os.WNOHANG)
            if ended:
                assert os.waitstatus_to_exitcode(status) == 0
                pid = 0
                break
        assert pid == 0, 'Tool did not exit after menu stress'
        print('PASS: File > Setup > F1/click Home, global shortcuts, 4,800 mouse events, sources/home navigation, clean exit.')
    finally:
        if pid:
            os.kill(pid, signal.SIGKILL)
            os.waitpid(pid, 0)
        os.close(fd)
