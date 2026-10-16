import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.parse
import zipfile
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding, rsa

spec = importlib.util.spec_from_file_location('resume', Path(__file__).parents[2] / 'scripts/download-release.py')
resume = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resume)


class ResumeTests(unittest.TestCase):
    def test_assertion_is_signed_for_exact_token_endpoint(self):
        private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        p = private.private_numbers()
        def encoded(n):
            return base64.urlsafe_b64encode(n.to_bytes((n.bit_length()+7)//8, 'big')).decode().rstrip('=')
        jwks = json.dumps({'keys':[{'kid':'test', **{k:encoded(v) for k,v in {'n':p.public_numbers.n,'e':p.public_numbers.e,'d':p.d,'p':p.p,'q':p.q,'dp':p.dmp1,'dq':p.dmq1,'qi':p.iqmp}.items()}}]})
        token = resume.assertion('calendar-ci', 'https://quantum.io-vii.com/oauth2/token', jwks)
        header, payload, signature = token.split('.')
        private.public_key().verify(base64.urlsafe_b64decode(signature+'='*(-len(signature)%4)), (header+'.'+payload).encode(), padding.PKCS1v15(), hashes.SHA256())
        claims = json.loads(base64.urlsafe_b64decode(payload+'='*(-len(payload)%4)))
        self.assertEqual('calendar-ci', claims['sub'])
        self.assertEqual('https://quantum.io-vii.com/oauth2/token', claims['aud'])
        self.assertEqual(120, claims['exp']-claims['iat'])

    def check_download(self, expected_hash=None, plugin='quantum.plugin.calendar'):
        package = io.BytesIO()
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr('plugin.json', json.dumps({'id':plugin,'version':'1.1.1'}))
        data = package.getvalue()
        sha = hashlib.sha256(data).hexdigest()
        responses = [io.BytesIO(b'{"access_token":"test-token"}'), io.BytesIO(json.dumps({'result':{'isSuccess':True,'value':{'packageArchiveBase64':base64.b64encode(data).decode(),'packageSha256':sha}}}).encode())]
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as root, patch.dict(os.environ, {'QUANTUM_CLIENT_ID':'calendar-ci','QUANTUM_CLIENT_PRIVATE_JWKS':'not-logged'}), patch.object(resume,'assertion',return_value='signed-assertion'), patch.object(resume.urllib.request,'urlopen',side_effect=responses) as request:
            self.assertTrue(Path(root).resolve().is_relative_to(Path(__file__).parent.resolve()))
            output = Path(root) / 'original.zip'
            if expected_hash or plugin != 'quantum.plugin.calendar':
                with self.assertRaises(ValueError):
                    resume.download('quantum.plugin.calendar','1.1.1',expected_hash or sha,output)
                self.assertFalse(output.exists())
            else:
                resume.download(plugin,'1.1.1',sha,output)
                self.assertEqual(data, output.read_bytes())
                rpc = request.call_args_list[1].args[0]
                self.assertEqual('Bearer test-token', rpc.get_header('Authorization'))
                self.assertEqual('DownloadPluginRelease', json.loads(rpc.data)['method'])

    def test_exact_original_archive_is_preserved(self):
        self.check_download()

    def test_changed_archive_is_not_written(self):
        self.check_download(expected_hash='a'*64)

    def test_wrong_plugin_is_not_written(self):
        self.check_download(plugin='quantum.plugin.other')


if __name__ == '__main__':
    unittest.main()
