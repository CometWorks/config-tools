#!/usr/bin/env python3
"""Offline release lifecycle checks; never contact GitHub or modify real releases."""
import copy
import hashlib
import os
import tempfile
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('release', Path(__file__).with_name('release.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def test_project_versions_control_plan_and_tags_cannot_override(self):
        versions = {'PulsarConfig': '1.2.3', 'MagnetarConfig': '4.5.6'}
        self.assertEqual(len(release.plan('refs/heads/main', 'all', versions)['matrix']['include']), 3)
        for tool in release.TOOLS:
            result = release.plan(f'refs/tags/{tool.lower()}-v{versions[tool]}', 'all', versions)
            self.assertEqual(result['releases']['include'], [{'tool': tool, 'version': versions[tool]}])
            self.assertEqual({row['version'] for row in result['matrix']['include']}, {versions[tool]})
        self.assertEqual(len(release.plan('refs/heads/main', 'MagnetarConfig', versions)['matrix']['include']), 2)
        with self.assertRaises(ValueError):
            release.plan('refs/tags/pulsarconfig-v9.9.9', 'all', versions)

    def lifecycle(self, fail=None):
        releases = [
            {'id': 1, 'tag_name': 'pulsarconfig-v1.0.0', 'draft': False, 'prerelease': False, 'published_at': '2026-01-01'},
            {'id': 2, 'tag_name': 'pulsarconfig-v1.1.0', 'draft': True, 'prerelease': False, 'published_at': None},
            {'id': 3, 'tag_name': 'magnetarconfig-v2.0.0', 'draft': False, 'prerelease': False, 'published_at': '2026-01-02'},
        ]
        candidate = copy.deepcopy(releases[1])
        calls = []
        def update(ident, data):
            calls.append((ident, data['draft']))
            if fail == 'archive' and ident == 1:
                raise RuntimeError('Archive failed')
            if fail == 'publish' and ident == 2:
                raise RuntimeError('Publish failed')
            next(r for r in releases if r['id'] == ident).update(data)
            for tool in release.TOOLS:
                self.assertLessEqual(sum(not r['draft'] and r['tag_name'].startswith(tool.lower() + '-v') for r in releases), 1)
            if fail == 'response' and ident == 2:
                raise RuntimeError('Published but response was lost')
        return releases, candidate, calls, update

    def test_other_tool_untouched_and_no_public_overlap(self):
        releases, candidate, calls, update = self.lifecycle()
        release.promote(candidate, copy.deepcopy(releases), update, lambda: releases)
        self.assertEqual(calls, [(1, True), (2, False)])
        self.assertEqual([r['id'] for r in releases if not r['draft']], [2, 3])
        release.promote(releases[1], copy.deepcopy(releases), update, lambda: releases)
        self.assertEqual(len(calls), 2)  # rerun is a no-op

    def test_publish_failure_restores_previous_release(self):
        releases, candidate, calls, update = self.lifecycle('publish')
        with self.assertRaises(RuntimeError):
            release.promote(candidate, copy.deepcopy(releases), update, lambda: releases)
        self.assertEqual(calls, [(1, True), (2, False), (1, False)])
        self.assertEqual([r['id'] for r in releases if not r['draft']], [1, 3])

    def test_archive_failure_never_publishes_second_release(self):
        releases, candidate, calls, update = self.lifecycle('archive')
        with self.assertRaises(RuntimeError):
            release.promote(candidate, copy.deepcopy(releases), update, lambda: releases)
        self.assertEqual(calls, [(1, True)])
        self.assertEqual([r['id'] for r in releases if not r['draft']], [1, 3])

    def test_lost_publish_response_does_not_restore_old_release(self):
        releases, candidate, calls, update = self.lifecycle('response')
        with self.assertRaises(RuntimeError):
            release.promote(candidate, copy.deepcopy(releases), update, lambda: releases)
        self.assertEqual(calls, [(1, True), (2, False)])
        self.assertEqual([r['id'] for r in releases if not r['draft']], [2, 3])

    def test_upload_verified_before_switch_and_stale_builds_do_not_promote(self):
        for outcome in ('success', 'bad-checksum', 'stale'):
            with self.subTest(outcome=outcome), tempfile.TemporaryDirectory() as folder:
                paths = [Path(folder) / name for name in ('PulsarConfig-linux-x64.bin', 'SHA256SUMS.txt')]
                for path in paths:
                    path.write_bytes(b'fixture')
                releases, _, _, _ = self.lifecycle()
                releases.pop(1)  # New candidate must be prepared by publish().
                writes, uploaded = [], []
                def api(path, method='GET', payload=None):
                    if method == 'PATCH':
                        writes.append((path, payload))
                        next(r for r in releases if path.endswith('/' + str(r['id']))).update(payload)
                    elif path.endswith('/commits/main'):
                        return {'sha': 'changed' if outcome == 'stale' and uploaded else 'head'}
                    elif '/git/matching-refs/' in path:
                        return []
                    else:
                        raise AssertionError(path)
                def command(*args, **kwargs):
                    if args[2] == 'create':
                        releases.append({'id': 2, 'tag_name': 'pulsarconfig-v1.1.0', 'draft': True,
                                         'prerelease': False, 'published_at': None, 'assets': []})
                    elif args[2] == 'upload':
                        uploaded.append(True)
                        releases[-1]['assets'] = [{'name': p.name, 'digest': 'sha256:' +
                            ('0' * 64 if outcome == 'bad-checksum' else hashlib.sha256(p.read_bytes()).hexdigest())} for p in paths]
                    else:
                        raise AssertionError(args)
                    return ''
                with patch.dict(os.environ, GITHUB_REPOSITORY='owner/repo', GITHUB_SHA='head'), \
                     patch.object(release, 'version', return_value='1.1.0'), \
                     patch.object(release, 'api', side_effect=api), \
                     patch.object(release, 'command', side_effect=command), \
                     patch.object(release, 'list_releases', side_effect=lambda _: copy.deepcopy(releases)):
                    if outcome == 'bad-checksum':
                        with self.assertRaises(ValueError):
                            release.publish('PulsarConfig', '1.1.0', folder)
                    else:
                        release.publish('PulsarConfig', '1.1.0', folder)
                if outcome == 'success':
                    self.assertEqual(len(writes), 2)
                    self.assertEqual([r['id'] for r in releases if not r['draft']], [3, 2])
                else:
                    self.assertFalse(writes)
                    self.assertEqual([r['id'] for r in releases if not r['draft']], [1, 3])


if __name__ == '__main__':
    unittest.main()
