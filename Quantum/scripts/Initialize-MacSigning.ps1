$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
foreach ($name in @('MACOS_APPLICATION_CERTIFICATE_BASE64', 'MACOS_INSTALLER_CERTIFICATE_BASE64',
    'MACOS_CERTIFICATE_PASSWORD', 'APPLE_ID', 'APPLE_TEAM_ID', 'APPLE_APP_SPECIFIC_PASSWORD',
    'MACOS_APPLICATION_IDENTITY', 'MACOS_INSTALLER_IDENTITY')) {
    if (![Environment]::GetEnvironmentVariable($name)) { throw "Required signing secret is missing: $name" }
}
$keychain = Join-Path $env:RUNNER_TEMP 'quantum-signing.keychain-db'
$keychainPassword = [Guid]::NewGuid().ToString('N')
Write-Output "::add-mask::$keychainPassword"
"QUANTUM_SIGNING_KEYCHAIN=$keychain" >> $env:GITHUB_ENV
security create-keychain -p $keychainPassword $keychain
security set-keychain-settings -lut 21600 $keychain
security unlock-keychain -p $keychainPassword $keychain
# Keep the login keychain available for Apple's system signing tooling.
security list-keychains -d user -s $keychain "$env:HOME/Library/Keychains/login.keychain-db"
foreach ($kind in @('APPLICATION', 'INSTALLER')) {
    $path = Join-Path $env:RUNNER_TEMP "quantum-$kind.p12"
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String(
        [Environment]::GetEnvironmentVariable("MACOS_${kind}_CERTIFICATE_BASE64")))
    try {
        security import $path -k $keychain -P $env:MACOS_CERTIFICATE_PASSWORD -T /usr/bin/codesign -T /usr/bin/productsign
    }
    finally { Remove-Item -LiteralPath $path -Force }
}
security set-key-partition-list -S 'apple-tool:,apple:,codesign:' -s -k $keychainPassword $keychain
xcrun notarytool store-credentials quantum-notary --apple-id $env:APPLE_ID --team-id $env:APPLE_TEAM_ID `
    --password $env:APPLE_APP_SPECIFIC_PASSWORD --keychain $keychain
