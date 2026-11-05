"""Run with python .github/tests/test_publish_security.py (PyYAML required)."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = yaml.safe_load((ROOT / '.github/workflows/ci.yml').read_text())
BASH = r'C:\Program Files\Git\bin\bash.exe' if os.name == 'nt' else 'bash'


class PublishingSecurity(unittest.TestCase):
    def test_push_stops_at_first_failure_and_rejects_empty_artifact(self):
        push = WORKFLOW['jobs']['publish']['steps'][-1]['run']
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            packages = directory / 'nuget-packages'
            packages.mkdir()
            script = directory / 'push.ps1'
            script.write_text("$ErrorActionPreference = 'Stop'\n" +
                "function dotnet { Add-Content calls.txt 'called'; $global:LASTEXITCODE = 1 }\n" +
                push.replace('${{ runner.temp }}', directory.as_posix()))
            for empty in (True, False):
                if not empty:
                    (packages / 'a.1.0.0.nupkg').touch()
                    (packages / 'b.1.0.0.nupkg').touch()
                result = subprocess.run(['pwsh', '-NoProfile', '-File', str(script)], cwd=tmp, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                calls = directory / 'calls.txt'
                self.assertEqual(calls.read_text().splitlines() if calls.exists() else [], [] if empty else ['called'])

    def test_permissions_and_artifact_boundary(self):
        self.assertEqual(WORKFLOW['permissions'], {'contents': 'read'})
        jobs = WORKFLOW['jobs']
        build, publish = jobs['build-test-deploy'], jobs['publish']
        self.assertEqual(build['permissions'], {'contents': 'read'})
        self.assertEqual(publish['environment'], 'nuget-release')
        self.assertEqual(publish['needs'], 'build-test-deploy')
        self.assertIn("github.event_name == 'push'", publish['if'])
        self.assertIn("startsWith(github.ref, 'refs/tags/')", publish['if'])
        self.assertIn("needs.build-test-deploy.outputs.is_stable_tag == 'true'", publish['if'])
        self.assertEqual(publish['permissions'], {'contents': 'read', 'id-token': 'write'})
        self.assertFalse(any('NuGet/login@' in s.get('uses', '') for s in build['steps']))
        self.assertFalse(any('checkout@' in s.get('uses', '') for s in publish['steps']))
        for step in publish['steps']:
            self.assertNotRegex(step.get('run', ''), r'dotnet\s+(build|restore|test|pack)|\.\/')
        download = next(s for s in publish['steps'] if 'download-artifact@' in s.get('uses', ''))
        self.assertEqual(download['with'], {'name': 'nuget-packages', 'path': '${{ runner.temp }}/nuget-packages'})
        push = publish['steps'][-1]['run']
        self.assertIn('if ($LASTEXITCODE -ne 0)', push)
        self.assertIn('$packages.Count -eq 0', push)

    def test_release_gate_with_real_git_history(self):
        gate = next(s['run'] for s in WORKFLOW['jobs']['build-test-deploy']['steps'] if s.get('id') == 'stable_tag')
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            def git(*args):
                return subprocess.check_output(['git', *args], cwd=tmp, stderr=subprocess.STDOUT, text=True).strip()
            git('init', '-b', 'master')
            git('config', 'user.email', 'test@example.invalid')
            git('config', 'user.name', 'Security test')
            git('commit', '--allow-empty', '-m', 'trusted')
            trusted = git('rev-parse', 'HEAD')
            git('update-ref', 'refs/remotes/origin/master', trusted)
            git('tag', '1.2.3')
            git('tag', '-a', '1.2.4', '-m', 'annotated')
            git('checkout', '-b', 'untrusted')
            git('commit', '--allow-empty', '-m', 'not on master')
            untrusted = git('rev-parse', 'HEAD')
            git('tag', '9.9.9')
            (directory / 'gate.sh').write_text(gate, newline='\n')
            cases = [
                ('push', 'refs/tags/1.2.3', trusted, trusted, True, True),
                ('push', 'refs/tags/1.2.4', trusted, trusted, True, True),
                ('workflow_dispatch', 'refs/tags/1.2.3', trusted, trusted, True, False),
                ('workflow_dispatch', 'refs/heads/master', trusted, trusted, True, False),
                ('pull_request', 'refs/pull/1/merge', trusted, trusted, True, False),
                ('push', 'refs/heads/master', trusted, trusted, True, False),
                ('push', 'refs/tags/01.2.3', trusted, trusted, False, False),
                ('push', 'refs/tags/1.2.3-rc.1', trusted, trusted, False, False),
                ('push', 'refs/tags/1.2.3;echo bad', trusted, trusted, False, False),
                ('push', 'refs/tags/7.7.7', trusted, trusted, False, False),
                ('push', 'refs/tags/9.9.9', untrusted, untrusted, False, False),
                ('push', 'refs/tags/1.2.3', untrusted, trusted, False, False),
                ('push', 'refs/tags/1.2.3', trusted, untrusted, False, False),
            ]
            for event, ref, head, sha, success, eligible in cases:
                with self.subTest(event=event, ref=ref, head=head, sha=sha):
                    git('checkout', '--detach', head)
                    output = directory / 'output'
                    output.write_text('')
                    env = dict(os.environ, GITHUB_EVENT_NAME=event, GITHUB_REF=ref,
                               GITHUB_SHA=sha, GITHUB_OUTPUT='output')
                    result = subprocess.run([BASH, 'gate.sh'], cwd=tmp, env=env, capture_output=True, text=True)
                    self.assertEqual(result.returncode == 0, success, result.stderr)
                    self.assertEqual('is_stable_tag=true' in output.read_text(), eligible)


if __name__ == '__main__':
    unittest.main(verbosity=2)
