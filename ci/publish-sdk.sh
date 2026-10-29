#!/usr/bin/env bash
set -euo pipefail
: "${NUGET_API_KEY:?Set a protected masked NuGet API key in GitLab}"
: "${NPM_TOKEN:?Set a protected masked npm publish token in GitLab}"
version="${CI_COMMIT_TAG#sdk/v}"
[[ "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]]
git merge-base --is-ancestor HEAD "origin/$CI_DEFAULT_BRANCH"
mkdir -p artifacts/sdk
curl --fail --silent --show-error https://registry.npmjs.org/@io7%2fquantum-plugin-sdk > artifacts/sdk/npm-metadata.json
node -e 'const fs=require("fs"); const p=JSON.parse(fs.readFileSync("artifacts/sdk/npm-metadata.json")); fs.writeFileSync("artifacts/sdk/npm-versions.json", JSON.stringify(Object.keys(p.versions)));'
curl --fail --silent --show-error https://api.nuget.org/v3-flatcontainer/quantum.plugin.abstraction/index.json > artifacts/sdk/nuget-versions.json
node Quantum.Sdk/scripts/assert-version-increases.mjs "$version" artifacts/sdk/npm-versions.json artifacts/sdk/nuget-versions.json
dotnet pack Quantum.Sdk/dotnet/src/Quantum.Plugin.Abstraction/Quantum.Plugin.Abstraction.csproj -c Release -p:PackageVersion="$version" -p:ContinuousIntegrationBuild=true -o artifacts/sdk
npm ci --prefix Quantum.Sdk/typescript
npm version "$version" --prefix Quantum.Sdk/typescript --no-git-tag-version --allow-same-version
npm run build --prefix Quantum.Sdk/typescript
config="$(mktemp)"
trap 'rm -f "$config"' EXIT
printf '//registry.npmjs.org/:_authToken=%s\n' "$NPM_TOKEN" > "$config"
export NPM_CONFIG_USERCONFIG="$config"
channel=latest
[[ "$version" != *-* ]] || channel=next
dotnet nuget push 'artifacts/sdk/*.nupkg' --api-key "$NUGET_API_KEY" --source https://api.nuget.org/v3/index.json --skip-duplicate
(cd Quantum.Sdk/typescript && npm publish --access public --tag "$channel")
