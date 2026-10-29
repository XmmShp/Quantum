"""Resume an existing release only when its archive matches an operator-supplied SHA-256."""
import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import time
import urllib.parse
import urllib.request
import uuid
import zipfile
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa


def assertion(client, endpoint, jwks):
    key = json.loads(jwks)['keys'][0]
    def number(name):
        value = key[name]
        return int.from_bytes(base64.urlsafe_b64decode(value + '=' * (-len(value) % 4)), 'big')
    private = rsa.RSAPrivateNumbers(number('p'), number('q'), number('d'), number('dp'), number('dq'), number('qi'), rsa.RSAPublicNumbers(number('e'), number('n'))).private_key()
    def encode(value):
        return base64.urlsafe_b64encode(value).rstrip(b'=')
    now = int(time.time())
    data = b'.'.join([encode(json.dumps({'alg':'RS256','typ':'JWT','kid':key['kid']}).encode()), encode(json.dumps({'iss':client,'sub':client,'aud':endpoint,'iat':now,'exp':now+120,'jti':uuid.uuid4().hex}).encode())])
    return (data + b'.' + encode(private.sign(data, padding.PKCS1v15(), hashes.SHA256()))).decode()


def download(plugin, version, expected_sha256, destination):
    if not re.fullmatch(r'[0-9a-f]{64}', expected_sha256):
        raise ValueError('A trusted lowercase SHA-256 of the original submission is required.')
    base = 'https://quantum.io-vii.com/'
    endpoint = base + 'oauth2/token'
    client = os.environ['QUANTUM_CLIENT_ID']
    signed = assertion(client, endpoint, os.environ['QUANTUM_CLIENT_PRIVATE_JWKS'])
    body = urllib.parse.urlencode({'grant_type':'client_credentials','client_id':client,'client_assertion_type':'urn:ietf:params:oauth:client-assertion-type:jwt-bearer','client_assertion':signed,'scope':'plugin:publish'}).encode()
    with urllib.request.urlopen(urllib.request.Request(endpoint, data=body), timeout=60) as response:
        token = json.load(response)['access_token']
    body = json.dumps({'jsonrpc':'2.0','id':uuid.uuid4().hex,'method':'DownloadPluginRelease','params':{'pluginId':plugin,'version':version}}).encode()
    request = urllib.request.Request(base + 'rpc', data=body, headers={'Content-Type':'application/json','Authorization':'Bearer '+token})
    with urllib.request.urlopen(request, timeout=120) as response:
        envelope = json.load(response)
    result = envelope.get('result', {})
    if not result.get('isSuccess'):
        raise ValueError('The original release could not be downloaded. Check its state and the client scope.')
    release = result['value']
    archive = base64.b64decode(release['packageArchiveBase64'], validate=True)
    if hashlib.sha256(archive).hexdigest() != expected_sha256 or release['packageSha256'] != expected_sha256:
        raise ValueError('Original release SHA-256 does not match the supplied hash.')
    with zipfile.ZipFile(io.BytesIO(archive)) as package:
        manifests = [entry for entry in package.namelist() if entry.lower() == 'plugin.json']
        if len(manifests) != 1:
            raise ValueError('Expected one root plugin manifest.')
        manifest = json.loads(package.read(manifests[0]))
        if manifest['id'] != plugin or manifest['version'] != version:
            raise ValueError('Original release manifest does not match the requested plugin/version.')
    path = Path(destination)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as output:
        output.write(archive)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--plugin-id', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--sha256', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    try:
        download(args.plugin_id, args.version, args.sha256, args.output)
        print(f'Original archive verified: {args.plugin_id}@{args.version} ({args.sha256})')
    except Exception as error:
        raise SystemExit(f'Resume download failed: {type(error).__name__}. No archive will be published.') from None
