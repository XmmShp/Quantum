"""Verify and publish GitHub Actions client bundles from mirrored release tags."""
import hashlib
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.parse
import urllib.request

REPOSITORY = 'XmmShp/Quantum'


def collect_assets(root, kind, include_macos=True):
    channels = ('win-x64', 'osx-arm64', 'osx-x64') if kind == 'desktop' else ('win-x64', 'android')
    if kind == 'desktop' and not include_macos:
        channels = ('win-x64',)
    assets = {}
    for channel in channels:
        directory = root / channel
        manifest = directory / f'SHA256SUMS.{channel}.txt'
        if not manifest.is_file():
            raise ValueError(f'Missing {manifest}; complete every platform build before publishing')
        listed = set()
        for line in manifest.read_text(encoding='utf-8-sig').splitlines():
            match = re.fullmatch(r'([0-9a-fA-F]{64})  ([^/\\]+)', line)
            if not match:
                raise ValueError(f'Invalid checksum line in {manifest}')
            digest, name = match.groups()
            path = directory / name
            if name in listed or name in assets or path.is_symlink() or not path.is_file():
                raise ValueError(f'Invalid or duplicate asset: {path}')
            with path.open('rb') as stream:
                actual_digest = hashlib.file_digest(stream, 'sha256').hexdigest()
            if actual_digest != digest.lower():
                raise ValueError(f'Checksum mismatch: {path}')
            listed.add(name)
            assets[name] = path
        actual = {path.name for path in directory.iterdir()}
        if not listed or actual != listed | {manifest.name}:
            raise ValueError(f'Unexpected or missing files in {directory}')
        assets[manifest.name] = manifest
    return assets


class GitHub:
    def __init__(self, token):
        self.token = token

    def request(self, method, path, data=None, file=None):
        url = path if path.startswith('https://uploads.github.com/') else f'https://api.github.com/repos/{REPOSITORY}/{path}'
        headers = {'Authorization': f'Bearer {self.token}', 'Accept': 'application/vnd.github+json',
                   'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'Quantum-GitHub-Release'}
        if file:
            headers.update({'Content-Type': 'application/octet-stream', 'Content-Length': str(file.stat().st_size)})
            with file.open('rb') as stream:
                request = urllib.request.Request(url, data=stream, headers=headers, method=method)
                with urllib.request.urlopen(request, timeout=600) as response:
                    return json.load(response)
        headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(url, data=json.dumps(data).encode() if data is not None else None,
                                         headers=headers, method=method)
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.load(response)


def wait_for_mirror(api, tag, sha):
    for attempt in range(20):
        try:
            obj = api.request('GET', 'git/ref/tags/' + urllib.parse.quote(tag, safe=''))['object']
            while obj['type'] == 'tag':
                obj = api.request('GET', 'git/tags/' + obj['sha'])['object']
            if obj['type'] != 'commit' or obj['sha'] != sha:
                raise ValueError('GitHub release tag differs from the build commit')
            return
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
            if attempt == 19:
                raise ValueError('GitHub tag has not arrived; check the GitLab push mirror') from error
            print('Waiting for GitLab to mirror the release tag...', flush=True)
            time.sleep(15)


def publish(api, tag, sha, assets, pipeline_url):
    # Never let the Releases API create a tag from GitHub's default branch.
    wait_for_mirror(api, tag, sha)
    try:
        release = api.request('GET', 'releases/tags/' + urllib.parse.quote(tag, safe=''))
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        mobile = tag.startswith('mobile-')
        release = api.request('POST', 'releases', {
            'tag_name': tag, 'name': ('Quantum Mobile ' if mobile else 'Quantum ') + tag.split('-v', 1)[1],
            'body': f'Built and verified by GitHub Actions from the GitLab mirror.\n\nWorkflow: {pipeline_url}\n\nCommit: `{sha}`\n\nVerify downloads using the accompanying SHA256SUMS files.',
            'draft': True, 'prerelease': mobile, 'make_latest': 'false' if mobile else 'true'})
    existing = {}
    page = 1
    while True:
        batch = api.request('GET', f"releases/{release['id']}/assets?per_page=100&page={page}")
        existing.update({asset['name']: asset for asset in batch})
        if len(batch) < 100:
            break
        page += 1
    pending = []
    for name, path in assets.items():
        with path.open('rb') as stream:
            digest = 'sha256:' + hashlib.file_digest(stream, 'sha256').hexdigest()
        if name in existing:
            if existing[name].get('digest') != digest or existing[name]['size'] != path.stat().st_size:
                raise ValueError(f'Existing release asset differs: {name}; use a new version')
        else:
            pending.append((name, path, digest))
    if not release['draft']:
        if pending or set(existing) != set(assets):
            raise ValueError('Published releases cannot be changed; use a new version')
        print('Release already published with identical assets:', release['html_url'])
        return
    if set(existing) - set(assets):
        raise ValueError('Draft has unexpected assets; inspect it before retrying')
    for name, path, digest in pending:
        upload_url = release['upload_url'].split('{', 1)[0]
        if not upload_url.startswith(f'https://uploads.github.com/repos/{REPOSITORY}/releases/'):
            raise ValueError('Unexpected GitHub asset upload endpoint')
        uploaded = api.request('POST', upload_url + '?name=' + urllib.parse.quote(name, safe=''), file=path)
        if uploaded.get('digest') != digest or uploaded['size'] != path.stat().st_size:
            raise ValueError(f'Uploaded asset verification failed: {name}')
        print('Uploaded', name, flush=True)
    result = api.request('PATCH', f"releases/{release['id']}", {
        'draft': False, 'prerelease': tag.startswith('mobile-'),
        'make_latest': 'false' if tag.startswith('mobile-') else 'true'})
    print('Published', result['html_url'])


def main():
    if os.environ.get('GITHUB_EVENT_NAME') != 'push' or os.environ.get('GITHUB_REF_TYPE') != 'tag':
        raise ValueError('Publication requires a release tag push; manual builds only produce artifacts')
    tag = os.environ['GITHUB_REF_NAME']
    match = re.fullmatch(r'(desktop|mobile)-v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?', tag)
    if not match or (match[1] == 'desktop' and match[2]):
        raise ValueError('Expected a desktop or mobile release tag')
    sha = os.environ['GITHUB_SHA']
    if not re.fullmatch('[0-9a-f]{40}', sha):
        raise ValueError('Invalid build commit SHA')
    kind = match[1]
    root = Path('Quantum/artifacts') / ('delivery' if kind == 'desktop' else 'mobile-delivery')
    assets = collect_assets(root, kind, os.environ.get('ENABLE_MACOS_RELEASE') == 'true')
    workflow_url = f"https://github.com/{REPOSITORY}/actions/runs/{os.environ['GITHUB_RUN_ID']}"
    publish(GitHub(os.environ['GH_TOKEN']), tag, sha, assets, workflow_url)


if __name__ == '__main__':
    main()
