param(
    [Parameter(Mandatory)][ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string]$Version,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'osx-arm64', 'osx-x64')][string]$Channel
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$project = 'quantum/src/Quantum/Quantum.csproj'
$output = "artifacts/releases/$Channel"
$publish = "artifacts/publish/$Channel"
$buildNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { '1' }
$feedPath = Join-Path $output "releases.$Channel.json"
if (Test-Path $feedPath) {
    $baseline = Get-Content $feedPath -Raw | ConvertFrom-Json
    foreach ($asset in $baseline.Assets | Where-Object { $_.Type -eq 'Full' }) {
        if ([version]$Version -le [version]$asset.Version) {
            throw "Version $Version must be greater than baseline $($asset.Version). Use a new version and a clean artifacts directory for retries."
        }
    }
}
$packageArgs = @('pack', '--packId', 'Quantum', '--packTitle', 'Quantum',
    '--packVersion', $Version, '--channel', $Channel, '--runtime', $Channel,
    '--outputDir', $output, '--delta', 'BestSize')

if ($Channel -eq 'win-x64') {
    dotnet publish $project -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true `
        -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$buildNumber -p:Version=$Version -p:PublishTrimmed=false `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o $publish
    $packageArgs += @('--packDir', $publish, '--mainExe', 'Quantum.exe', '--framework', 'webview2,vcredist143-x64')
    if ($env:WINDOWS_CERTIFICATE_BASE64) {
        if (!$env:WINDOWS_CERTIFICATE_PASSWORD) { throw 'WINDOWS_CERTIFICATE_PASSWORD is required.' }
        $certificatePath = Join-Path $env:RUNNER_TEMP 'quantum-signing.pfx'
        [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($env:WINDOWS_CERTIFICATE_BASE64))
        try {
            $password = ConvertTo-SecureString $env:WINDOWS_CERTIFICATE_PASSWORD -AsPlainText -Force
            $certificate = Import-PfxCertificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\My -Password $password
            $packageArgs += @('--signParams', "/sha1 $($certificate.Thumbprint) /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256")
        }
        finally { Remove-Item -LiteralPath $certificatePath -Force }
    }
    elseif ($env:REQUIRE_WINDOWS_SIGNING -eq 'true') {
        throw 'Windows signing is required, but no certificate was configured.'
    }
    else { Write-Warning 'Building an unsigned Windows installer; SmartScreen may warn users.' }
}
else {
    foreach ($name in @('MACOS_APPLICATION_IDENTITY', 'MACOS_INSTALLER_IDENTITY', 'QUANTUM_SIGNING_KEYCHAIN')) {
        if (![Environment]::GetEnvironmentVariable($name)) { throw "Missing $name" }
    }
    $rid = $Channel.Replace('osx-', 'maccatalyst-')
    dotnet publish $project -c Release -f net10.0-maccatalyst -r $rid `
        -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$buildNumber -p:Version=$Version -p:QuantumDirectDistribution=true `
        -p:CodesignKey="$env:MACOS_APPLICATION_IDENTITY" -p:CodesignKeychain="$env:QUANTUM_SIGNING_KEYCHAIN" `
        -p:EnableCodeSigning=true -p:CreatePackage=false
    $bundle = "quantum/src/Quantum/bin/Release/net10.0-maccatalyst/$rid/Quantum.app"
    if (!(Test-Path $bundle)) { throw "App bundle missing: $bundle" }
    dotnet publish quantum/src/Quantum.UpdateAgent/Quantum.UpdateAgent.csproj -c Release -r $Channel `
        --self-contained true -p:Version=$Version -o "$bundle/Contents/Helpers/Quantum.UpdateAgent"
    $packageArgs += @('--packDir', $bundle, '--mainExe', 'Quantum',
        '--signAppIdentity', $env:MACOS_APPLICATION_IDENTITY,
        '--signInstallIdentity', $env:MACOS_INSTALLER_IDENTITY,
        '--signEntitlements', 'quantum/src/Quantum/Platforms/MacCatalyst/DirectDistribution.entitlements',
        '--notaryProfile', 'quantum-notary', '--keychain', $env:QUANTUM_SIGNING_KEYCHAIN)
}
try {
    dotnet vpk @packageArgs --yes
}
finally {
    if ($Channel -eq 'win-x64' -and $certificate) {
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
    }
}
# RELEASES and assets.*.json are local/legacy metadata. Only channel-specific
# feeds, packages and installers are uploaded to avoid cross-platform collisions.
$files = Get-ChildItem $output -File | Where-Object {
    $_.Name -eq "releases.$Channel.json" -or $_.Extension -in @('.nupkg', '.exe', '.pkg', '.zip')
}
$delivery = "artifacts/delivery/$Channel"
New-Item -ItemType Directory -Force -Path $delivery | Out-Null
$files | Copy-Item -Destination $delivery
$checksumName = "SHA256SUMS.$Channel.txt"
$checksums = Get-ChildItem $delivery -File | Where-Object Name -ne $checksumName | ForEach-Object {
    $hash = Get-FileHash $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
}
$checksums | Set-Content (Join-Path $delivery $checksumName)
