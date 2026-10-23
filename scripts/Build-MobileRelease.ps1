param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidateSet('android', 'win-x64')][string]$Target,
    [int]$BuildNumber = 1
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$project = 'mobile/Quantum.Mobile/Quantum.Mobile.csproj'
$publish = "artifacts/mobile-publish/$Target"
$delivery = "artifacts/mobile-delivery/$Target"
New-Item -ItemType Directory -Force $delivery | Out-Null
if ($Target -eq 'android') {
    if (!$env:MOBILE_ANDROID_KEYSTORE_PATH -or !$env:MOBILE_ANDROID_KEYSTORE_PASSWORD) { throw 'Persistent Android signing credentials are required.' }
    # Android emits a universal APK from its Android RuntimeIdentifiers; the build
    # machine's RID must not be added by the SDK's default publish RID inference.
    dotnet publish $project -c Release -f net10.0-android -p:QuantumMobileTargetFramework=net10.0-android `
        -p:UseDefaultPublishRuntimeIdentifier=false -o $publish `
        -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$BuildNumber `
        -p:AndroidPackageFormats=apk -p:AndroidKeyStore=true `
        -p:AndroidSigningKeyStore=$env:MOBILE_ANDROID_KEYSTORE_PATH -p:AndroidSigningKeyAlias=quantum-mobile `
        -p:AndroidSigningStorePass=env:MOBILE_ANDROID_KEYSTORE_PASSWORD -p:AndroidSigningKeyPass=env:MOBILE_ANDROID_KEYSTORE_PASSWORD
    $apk = @(Get-ChildItem $publish -Filter '*-Signed.apk' -File)
    if ($apk.Count -ne 1) { throw 'Expected one signed universal Android APK.' }
    $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
    $signerName = if ($IsWindows) { 'apksigner.bat' } else { 'apksigner' }
    $signer = Get-ChildItem "$sdk/build-tools/*/$signerName" -File | Sort-Object FullName -Descending | Select-Object -First 1
    if (!$signer) { throw 'Android apksigner is required to verify the release.' }
    & $signer.FullName verify --verbose --print-certs $apk[0].FullName
    Copy-Item -LiteralPath $apk[0].FullName -Destination "$delivery/Quantum-Mobile-$Version-android.apk"
} else {
    dotnet publish $project -c Release -f net10.0-windows10.0.19041.0 -p:QuantumMobileTargetFramework=net10.0-windows10.0.19041.0 -r win-x64 --self-contained true `
        -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$BuildNumber `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o $publish
    $metadata = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Resolve-Path "$publish/Quantum.Mobile.dll")))
    if ($metadata.Contains('PreviewFileAsync') -or $metadata.Contains('RenderPreview')) { throw 'Debug preview tools must not be compiled into Release.' }
    Compress-Archive -Path "$publish/*" -DestinationPath "$delivery/Quantum-Mobile-$Version-win-x64.zip" -Force
}
Get-ChildItem $delivery -File | Where-Object Name -NotLike 'SHA256SUMS.*' | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
} | Set-Content "$delivery/SHA256SUMS.$Target.txt"
