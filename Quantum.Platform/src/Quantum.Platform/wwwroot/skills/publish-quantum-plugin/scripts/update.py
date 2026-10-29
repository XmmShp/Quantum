#!/usr/bin/env python3
"""Check and install hash-verified publish-quantum-plugin skill updates."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import tempfile
from urllib.parse import urljoin
from urllib.request import Request, urlopen
import zipfile

DEFAULT_MANIFEST_URL = "https://quantum.io-vii.com/skills/publish-quantum-plugin/manifest.json"

def fetch(url):
    with urlopen(Request(url, headers={"User-Agent": "quantum-skill-updater/1"}), timeout=30) as response:
        return response.read()

def digest(value):
    return hashlib.sha256(value).hexdigest()

def safe_path(value):
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or not path.parts:
        raise ValueError(f"Unsafe package path: {value}")
    return path

def read_files(root):
    return {path.relative_to(root).as_posix(): path.read_bytes() for path in root.rglob("*") if path.is_file()}

def content_digest(files):
    value = hashlib.sha256()
    for name in sorted(files):
        encoded = name.encode()
        value.update(len(encoded).to_bytes(8, "big")); value.update(encoded)
        value.update(len(files[name]).to_bytes(8, "big")); value.update(files[name])
    return value.hexdigest()

def load_manifest(url):
    manifest = json.loads(fetch(url))
    required = {"schemaVersion", "name", "contentSha256", "downloadUrl", "archiveSha256", "files"}
    if not isinstance(manifest, dict) or not required.issubset(manifest):
        raise ValueError("Invalid skill manifest.")
    if manifest["schemaVersion"] != 1 or manifest["name"] != "publish-quantum-plugin":
        raise ValueError("Unexpected skill identity or schema.")
    for item in manifest["files"]:
        safe_path(item["path"])
        if len(item["sha256"]) != 64 or item["size"] < 0:
            raise ValueError("Invalid file metadata.")
    return manifest

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--skill-root", type=Path)
    parser.add_argument("--manifest-url", default=DEFAULT_MANIFEST_URL)
    args = parser.parse_args()
    if args.check and args.apply:
        parser.error("choose --check or --apply")
    requested_root = (args.skill_root or Path(__file__).resolve().parents[1]).expanduser()
    if requested_root.name != "publish-quantum-plugin" or requested_root.is_symlink():
        raise ValueError("Unexpected or symlinked skill root.")
    root = requested_root.resolve()
    local = read_files(root) if root.exists() else {}
    manifest = load_manifest(args.manifest_url)
    remote_hash = manifest["contentSha256"]
    if not args.apply:
        print(json.dumps({"status": "up-to-date" if content_digest(local) == remote_hash else "update-available", "remoteContentSha256": remote_hash}, sort_keys=True))
        return
    package = fetch(urljoin(args.manifest_url, manifest["downloadUrl"]))
    if digest(package) != manifest["archiveSha256"]:
        raise ValueError("Archive SHA-256 mismatch.")
    expected = {item["path"]: item for item in manifest["files"]}
    with tempfile.TemporaryDirectory(prefix="quantum-skill-") as temporary:
        stage = Path(temporary)
        with zipfile.ZipFile(io.BytesIO(package)) as archive:
            names = [item.filename for item in archive.infolist() if not item.is_dir()]
            if len(names) != len(set(names)) or set(names) != set(expected):
                raise ValueError("Archive file list mismatch.")
            for name in names:
                target = stage.joinpath(*safe_path(name).parts)
                value = archive.read(name)
                if len(value) != expected[name]["size"] or digest(value) != expected[name]["sha256"]:
                    raise ValueError(f"Integrity mismatch: {name}")
                target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(value)
        if content_digest(read_files(stage)) != remote_hash:
            raise ValueError("Content SHA-256 mismatch.")
        root.mkdir(parents=True, exist_ok=True)
        for existing in sorted(root.rglob("*"), reverse=True):
            if existing.is_file() and existing.relative_to(root).as_posix() not in expected:
                existing.unlink()
        for source in (path for path in stage.rglob("*") if path.is_file()):
            target = root / source.relative_to(stage); target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as temp:
                temp.write(source.read_bytes()); temp.flush(); os.fsync(temp.fileno()); temp_name = temp.name
            os.replace(temp_name, target)
    print(json.dumps({"status": "synchronized", "currentContentSha256": remote_hash}, sort_keys=True))

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"status": "error", "message": str(error)}, sort_keys=True))
        raise SystemExit(1)
