#!/usr/bin/env bash
set -euo pipefail
directory="$(mktemp -d)"
trap 'rm -rf "$directory"' EXIT
cd "$directory"
curl --fail --silent --show-error -o SHASUMS256.txt https://nodejs.org/dist/latest-v24.x/SHASUMS256.txt
grep -E ' node-v24\.[0-9]+\.[0-9]+-linux-x64.tar.xz$' SHASUMS256.txt > node.sha256
archive="$(awk '{print $2}' node.sha256)"
test -n "$archive"
curl --fail --silent --show-error -O "https://nodejs.org/dist/latest-v24.x/$archive"
sha256sum --check node.sha256
tar -xJf "$archive" -C /usr/local --strip-components=1
