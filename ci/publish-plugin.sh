#!/usr/bin/env bash
set -euo pipefail
case "$CI_COMMIT_TAG" in
    calendar-v*) plugin=Calendar; prefix=QUANTUM; version="${CI_COMMIT_TAG#calendar-v}" ;;
    codex-v*) plugin=Codex; prefix=QUANTUM_CODEX; version="${CI_COMMIT_TAG#codex-v}" ;;
    ledger-v*) plugin=Ledger; prefix=QUANTUM_LEDGER; version="${CI_COMMIT_TAG#ledger-v}" ;;
    *) echo 'Unsupported plugin release tag' >&2; exit 1 ;;
esac
key="${prefix}_CLIENT_ID"
jwks="${prefix}_CLIENT_PRIVATE_JWKS"
export QUANTUM_CLIENT_ID="${!key:?Missing plugin publisher client ID}"
export QUANTUM_CLIENT_PRIVATE_JWKS="${!jwks:?Missing plugin publisher private JWKS}"
source="artifacts/plugins/$plugin"
grep -Eq '"version"[[:space:]]*:[[:space:]]*"'"$version"'"' "$source/plugin.json"
git merge-base --is-ancestor HEAD "origin/$CI_DEFAULT_BRANCH"
dotnet tool install Quantum.Platform.Cli --tool-path .tools --version 0.1.1
.tools/quantum-cli plugins publish "$source" --quantum-version-support '>=0.1.16' --release-notes "Official release $CI_COMMIT_TAG" --wait-seconds 900 --output subprocess | tee publish-result.json
grep -Eq '"ok"[[:space:]]*:[[:space:]]*true' publish-result.json
grep -Eq '"status"[[:space:]]*:[[:space:]]*"Published"' publish-result.json
