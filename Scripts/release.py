#!/usr/bin/env python3
"""Build-property release planning and one published release per tool. CI only; stdlib + gh/dotnet."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

TOOLS = ('PulsarConfig', 'MagnetarConfig')


def command(*args, data=None):
    return subprocess.run(args, input=data, text=True, capture_output=True, check=True).stdout.strip()


def version(tool):
    value = command('dotnet', 'msbuild', f'{tool}/{tool}.csproj', '-nologo',
                    '-p:Configuration=Release', '-getProperty:Version')
    if not re.fullmatch(r'\d+\.\d+\.\d+', value):
        raise ValueError(f'{tool}: Version must be X.Y.Z in project build properties; got {value!r}')
    return value


def plan(ref, selected, versions):
    tools = list(TOOLS) if selected == 'all' else [selected]
    if any(tool not in TOOLS for tool in tools):
        raise ValueError('Unknown tool')
    if ref.startswith('refs/tags/'):
        tools = [tool for tool in TOOLS if ref == f'refs/tags/{tool.lower()}-v{versions[tool]}']
        if not tools:
            raise ValueError('Release tag must match the selected project build version')
    rows = [{'tool': tool, 'version': versions[tool], 'os': os_name, 'rid': rid}
            for tool in tools for os_name, rid in
            [('ubuntu-latest', 'linux-x64'), ('windows-latest', 'win-x64')]]
    return {'matrix': {'include': rows},
            'releases': {'include': [{'tool': tool, 'version': versions[tool]} for tool in tools]}}


def api(path, method='GET', payload=None):
    args = ['gh', 'api', path, '--method', method]
    if payload is not None:
        args += ['--input', '-']
    output = command(*args, data=None if payload is None else json.dumps(payload))
    return json.loads(output) if output else None


def list_releases(repo):
    pages = json.loads(command('gh', 'api', f'repos/{repo}/releases?per_page=100', '--paginate', '--slurp'))
    return [release for page in pages for release in page]


def promote(candidate, update, refresh, delete):
    """Verify the replacement is public before deleting superseded releases.

    Never turn old releases back into drafts. A failed publication leaves the
    previous release intact; a failed deletion can be retried without rebuilding.
    """
    prefix = candidate['tag_name'].split('-v', 1)[0] + '-v'
    if candidate['draft']:
        try:
            update(candidate['id'], {'draft': False, 'prerelease': False, 'make_latest': 'false'})
        except Exception:
            # The request may have succeeded remotely but lost its response.
            published = next((r for r in refresh() if r['id'] == candidate['id']), None)
            if not published or published['draft'] or published['prerelease']:
                raise
    current = refresh()
    published = next((r for r in current if r['id'] == candidate['id']), None)
    if not published or published['draft'] or published['prerelease']:
        raise ValueError('Replacement is not a published stable release; preserving previous releases')
    for previous in current:
        if previous['id'] != candidate['id'] and previous['tag_name'].startswith(prefix):
            delete(previous['id'])  # Deletes assets too, but leaves the Git tag intact.


def publish(tool, expected, directory):
    if version(tool) != expected:
        raise ValueError('Planned version differs from project build properties')
    repo, head = os.environ['GITHUB_REPOSITORY'], os.environ['GITHUB_SHA']
    base = f'repos/{repo}'

    def current():
        return api(base + '/commits/main')['sha'] == head

    if not current():
        print('Skipping stale/non-main build; releases follow the current main build properties.')
        return
    tag = f'{tool.lower()}-v{expected}'
    files = sorted(Path(directory).glob('*'))
    assets = [f'{tool}-linux-x64.bin', f'{tool}-win-x64.exe']
    if not files or any(not (Path(directory) / name).is_file() for name in assets + ['SHA256SUMS.txt']):
        raise ValueError('Required release artifacts are missing')
    if any(p.suffix in ('.exe', '.bin') and p.name not in assets for p in files):
        raise ValueError('Artifacts include another tool or platform')
    releases = list_releases(repo)
    candidate = next((r for r in releases if r['tag_name'] == tag), None)
    # Complete versions (including completed drafts) are reused, never rebuilt in place.
    remote = {asset['name']: asset for asset in candidate['assets']} if candidate else {}
    complete = all(re.fullmatch(r'sha256:[0-9a-fA-F]{64}', remote.get(path.name, {}).get('digest') or '') for path in files)
    if candidate is None or (candidate['draft'] and not complete):
        if candidate is None:
            refs = api(f'{base}/git/matching-refs/tags/{tag}')
            if any(ref['ref'] == f'refs/tags/{tag}' for ref in refs) and api(f'{base}/commits/{tag}')['sha'] != head:
                raise ValueError('Existing tag points at a different commit; bump the project version')
            command('gh', 'release', 'create', tag, '--repo', repo, '--draft', '--target', head,
                    '--title', f'{tool} {expected}', '--generate-notes')
        command('gh', 'release', 'upload', tag, *map(str, files), '--repo', repo, '--clobber')
        releases = list_releases(repo)
        candidate = next(r for r in releases if r['tag_name'] == tag)
        remote = {asset['name']: asset for asset in candidate['assets']}
        for path in files:
            digest = 'sha256:' + hashlib.sha256(path.read_bytes()).hexdigest()
            if remote.get(path.name, {}).get('digest') != digest:
                raise ValueError(f'Uploaded asset failed checksum verification: {path.name}')
    remote = {asset['name']: asset for asset in candidate['assets']}
    if candidate['prerelease'] or any(name not in remote or not re.fullmatch(r'sha256:[0-9a-fA-F]{64}', remote[name].get('digest') or '')
                                     for name in assets + ['SHA256SUMS.txt']):
        raise ValueError('Existing release is incomplete or is not a stable release; bump the project version')
    if not current():
        print('Main changed while preparing the draft; leaving the current public release in place.')
        return
    promote(candidate, lambda ident, data: api(f'{base}/releases/{ident}', 'PATCH', data),
            lambda: list_releases(repo), lambda ident: api(f'{base}/releases/{ident}', 'DELETE'))
    print(f'{tag} is the sole published release for {tool}. Superseded releases and assets are deleted; Git tags are retained.')


if __name__ == '__main__':
    try:
        if sys.argv[1] == 'plan':
            result = plan(os.environ['GITHUB_REF'], os.environ.get('SELECTED_TOOL') or 'all',
                          {tool: version(tool) for tool in TOOLS})
            with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
                for key, value in result.items():
                    output.write(f'{key}={json.dumps(value)}\n')
        elif sys.argv[1] == 'publish' and os.environ.get('GITHUB_ACTIONS') == 'true':
            publish(sys.argv[2], sys.argv[3], sys.argv[4])
        else:
            raise ValueError('Use plan, or publish TOOL VERSION DIR inside CI')
    except subprocess.CalledProcessError as error:
        raise SystemExit(error.stderr or str(error))
