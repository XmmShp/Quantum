"""Check the actual payload, rather than assuming Build assets survived Publish."""

import json
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit


class References(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "link" and attrs.get("rel") == "stylesheet":
            self.paths.append(attrs.get("href", ""))
        if tag == "script" and "src" in attrs:
            self.paths.append(attrs["src"])


def verify(directory):
    directory = Path(directory)
    manifest = json.loads((directory / "plugin.json").read_text(encoding="utf-8-sig"))
    parser = References()
    web = manifest.get("web", {})
    for fragment in web.get("head", []) + web.get("postBlazor", []):
        parser.feed(fragment)
    prefix = f"_content/{manifest['id']}/"
    for reference in parser.paths:
        path = unquote(urlsplit(reference).path).lstrip("/")
        if not path.startswith(prefix):
            raise ValueError(f"Unverified asset reference: {reference}")
        asset = (directory / "wwwroot" / path[len(prefix):]).resolve()
        if not asset.is_relative_to((directory / "wwwroot").resolve()):
            raise ValueError(f"Asset is outside wwwroot: {reference}")
        if not asset.is_file() or asset.stat().st_size == 0:
            raise ValueError(f"Missing or empty published asset: {reference}")
    for route in manifest.get("ui", {}).get("routes", []):
        if "iconFile" not in route:
            continue
        relative = Path(route["iconFile"])
        asset = (directory / "wwwroot" / relative).resolve()
        if relative.is_absolute() or ".." in relative.parts or not asset.is_relative_to((directory / "wwwroot").resolve()):
            raise ValueError("Icon is outside wwwroot")
        if relative.suffix.lower() != ".ico" or not asset.is_file():
            raise ValueError("Missing published ICO file")
        content = asset.read_bytes()
        if not 22 <= len(content) <= 1024 * 1024 or content[:4] != b"\x00\x00\x01\x00":
            raise ValueError("Invalid published ICO file")
    print(f"Verified {manifest['id']}: {len(parser.paths)} declared static assets")


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        verify(argument)
