#!/usr/bin/env bash
set -euo pipefail
: "${MOBILE_ANDROID_KEYSTORE_BASE64:?Persistent Android signing key is required}"
: "${MOBILE_ANDROID_KEYSTORE_PASSWORD:?Android signing password is required}"
: "${ANDROID_HOME:?Use a GitLab runner image with Android SDK, Java 21 and PowerShell installed}"
dotnet workload install maui-android --skip-manifest-update
key="$(mktemp --suffix=.p12)"
trap 'rm -f "$key"' EXIT
printf '%s' "$MOBILE_ANDROID_KEYSTORE_BASE64" | base64 --decode > "$key"
export MOBILE_ANDROID_KEYSTORE_PATH="$key"
version="${CI_COMMIT_TAG#mobile-v}"
version="${version%%-*}"
cd Quantum
dotnet build mobile/Quantum.Mobile/Quantum.Mobile.csproj -f net10.0-android -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=true -p:AndroidSdkDirectory="$ANDROID_HOME"
pwsh -File scripts/Build-MobileRelease.ps1 -Version "$version" -Target android -BuildNumber "$CI_PIPELINE_IID"
