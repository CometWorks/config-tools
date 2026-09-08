#!/usr/bin/env python3
"""Linux PTY smoke: open unregistered installs from standalone tools. Stdlib only."""
import fcntl
import os
from pathlib import Path
import pty
import re
import select
import signal
import struct
import sys
import tempfile
import termios
import time


def check(executable, product):
    executable = str(Path(executable).resolve())
    with tempfile.TemporaryDirectory(prefix='config-discovery-') as directory:
        root = Path(directory)
        install = root / 'unpacked'
        names = (['Interim.bin', 'Interim.dll', 'Interim.runtimeconfig.json',
                  'Libraries/Interim/Pulsar.Shared.dll'] if product == 'Pulsar' else
                 ['MagnetarInterim.bin', 'MagnetarInterim.dll', 'MagnetarInterim.runtimeconfig.json'])
        for name in names:
            path = install / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('fixture')
        (root / '.config' / 'SpaceEngineersDedicated').mkdir(parents=True)
        pid, fd = pty.fork()
        if pid == 0:
            fcntl.ioctl(1, termios.TIOCSWINSZ, struct.pack('HHHH', 24, 80, 0, 0))
            os.chdir(install)
            os.environ.update(HOME=directory, XDG_CONFIG_HOME=directory + '/.config',
                              XDG_DATA_HOME=directory + '/data', XDG_STATE_HOME=directory + '/state',
                              TERM='xterm-256color', HTTPS_PROXY='http://127.0.0.1:9')
            os.environ.pop('PULSAR_DATA_DIR', None)
            os.execv(executable, [executable])
        os.set_blocking(fd, False)
        output = bytearray()

        def pump(seconds=.1):
            until = time.monotonic() + seconds
            while time.monotonic() < until:
                if select.select([fd], [], [], max(0, min(.05, until - time.monotonic())))[0]:
                    try:
                        data = os.read(fd, 65536)
                    except OSError:
                        return
                    if not data:
                        return
                    output.extend(data)

        def expect(text):
            until = time.monotonic() + 10
            while text.encode() not in output and time.monotonic() < until:
                pump()
            assert text.encode() in output, (text, re.sub(rb'\x1b\[[0-?]*[ -/]*[@-~]', b'', output).decode(errors='replace')[-2500:])
            output.clear()

        try:
            expect('Installations')
            os.write(fd, b'\r')  # Activate the selected discovered installation.
            expect('Changes are saved' if product == 'Pulsar' else 'Worlds')
            history = root / '.config' / 'CometWorks' / 'config-tools' / (product.lower() + '-installs.json')
            assert str(install) in history.read_text()
            assert not list((root / 'state').rglob('*.json')), 'Discovery wrote installer receipts'
            assert all((install / name).read_text() == 'fixture' for name in names)
        finally:
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            os.waitpid(pid, 0)
            os.close(fd)
    print(f'PASS: {product} discovers an unpacked install outside the tool, opens it, and remembers selection.')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('Usage: test-install-discovery.py PULSAR_EXECUTABLE MAGNETAR_EXECUTABLE')
    check(sys.argv[1], 'Pulsar')
    check(sys.argv[2], 'Magnetar')
