#!/usr/bin/env python3
"""Exercise the published helper on its native OS, including Windows executable replacement."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

binary = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix='config-tools-update-test-') as directory:
    root = Path(directory)
    for corrupt in (False, True):
        case = root / ("corrupt" if corrupt else "valid")
        case.mkdir()
        target = case / binary.name
        shutil.copy2(binary, target)
        before = hashlib.sha256(target.read_bytes()).hexdigest()
        work = case / ('.' + target.name + '-update-test')
        work.mkdir()
        package = work / 'download'
        shutil.copy2(binary, package)
        helper = work / ('helper.exe' if os.name == 'nt' else 'helper')
        shutil.copy2(binary, helper)
        parent = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
        started = (int(Path(f'/proc/{parent.pid}/stat').read_text().rsplit(') ', 1)[1].split()[19])
                   if os.name != 'nt' else int(subprocess.check_output([
            'pwsh', '-NoProfile', '-Command',
            f'(Get-Process -Id {parent.pid}).StartTime.ToUniversalTime().Ticks'
        ], text=True, timeout=15).strip()))
        plan = {
            'Parent': parent.pid, 'Started': started, 'Target': str(target),
            'Hash': '0' * 64 if corrupt else before,
        }
        plan_path = work / 'plan.json'
        plan_path.write_text(json.dumps(plan))
        process = None
        try:
            process = subprocess.Popen([str(helper), '--apply-self-update', str(plan_path)], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            for _ in range(100):
                if (work / 'ready').exists():
                    break
                if process.poll() is not None:
                    output, error = process.communicate()
                    raise AssertionError('Helper exited before it was ready: ' + output + error)
                time.sleep(0.1)
            else:
                raise AssertionError('Helper readiness timed out')
            time.sleep(0.2)
            if process.poll() is not None:
                output, error = process.communicate()
                raise AssertionError('Helper must wait for the parent to exit: ' + output + error)
            assert hashlib.sha256(target.read_bytes()).hexdigest() == before
            parent.terminate()
            parent.wait(timeout=10)
            output, error = process.communicate(timeout=30)
            assert process.returncode == (1 if corrupt else 0), output + error
        finally:
            if parent.poll() is None:
                parent.kill()
                parent.wait(timeout=10)
            if process is not None and process.poll() is None:
                process.kill()
                process.wait(timeout=10)
        assert hashlib.sha256(target.read_bytes()).hexdigest() == before
        if not corrupt:
            assert hashlib.sha256(Path(str(target) + '.previous').read_bytes()).hexdigest() == before
        subprocess.run([str(target), '--tool-version'], check=True, timeout=30)
        assert not work.exists(), 'Completed helper staging should be cleaned on the next launch'
print('Published self-update helper: replacement, checksum rejection and cleanup passed.')
