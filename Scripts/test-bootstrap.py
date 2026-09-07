#!/usr/bin/env python3
"""Offline check that a Magnetar release cannot hide the latest Pulsar bootstrap asset."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile

spec = importlib.util.spec_from_file_location('bootstrap', Path(__file__).with_name('pulsar-linux.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
package = b'checked executable fixture'
asset = {'name': module.ASSET, 'digest': 'sha256:' + hashlib.sha256(package).hexdigest(),
         'browser_download_url': f'https://github.com/{module.REPO}/releases/download/pulsarconfig-v1.2.0/{module.ASSET}'}
releases = [
    {'tag_name': 'magnetarconfig-v9.0.0', 'assets': []},
    {'tag_name': 'pulsarconfig-v1.2.0', 'assets': [asset]},
    {'tag_name': 'pulsarconfig-v1.1.0', 'assets': [{**asset, 'digest': 'invalid'}]},
    {'tag_name': 'pulsarconfig-v2.0.0', 'prerelease': True, 'assets': [{**asset, 'digest': 'invalid'}]},
]
module.request = lambda url: io.BytesIO(json.dumps(releases).encode() if 'api.github.com' in url else package)
module.os.geteuid = lambda: 1000
calls = []
module.os.execv = lambda executable, args: calls.append((executable, args))
sys.argv = ['pulsar-linux.py', '--target', '/example/Pulsar', '--game', 'se2']
with tempfile.TemporaryDirectory(prefix='bootstrap-test-') as directory:
    module.__file__ = str(Path(directory) / 'pulsar-linux.py')
    module.main()
    output = Path(directory) / 'PulsarConfig.bin'
    assert output.read_bytes() == package
    assert calls == [(str(output), [str(output), *sys.argv[1:]])]
print('Legacy bootstrap: independent release selection, verification and argument forwarding passed.')
