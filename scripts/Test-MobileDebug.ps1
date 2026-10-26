param([ValidateRange(1024,65535)][int]$Port=9224)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$invoke=Join-Path $PSScriptRoot 'Invoke-MobileDebug.ps1'
$reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try{$reservation.Start()}finally{$reservation.Stop()}
$token=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$names=@('QUANTUM_MOBILE_DEBUG_PORT','QUANTUM_MOBILE_DEBUG_TOKEN','QUANTUM_MOBILE_DEBUG_PREVIEW')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$process=$null
try{
    $env:QUANTUM_MOBILE_DEBUG_PORT=[string]$port
    $env:QUANTUM_MOBILE_DEBUG_TOKEN=$token
    $env:QUANTUM_MOBILE_DEBUG_PREVIEW='1'
    $directory=Join-Path $repo 'mobile/Quantum.Mobile/bin/Debug/net10.0-windows10.0.19041.0/win-x64'
    if(!(Test-Path (Join-Path $directory 'Quantum.Mobile.exe'))){throw 'Build the Windows Debug target first.'}
    $process=Start-Process -FilePath (Join-Path $directory 'Quantum.Mobile.exe') -WorkingDirectory $directory -PassThru
    function Call([string]$path,[hashtable]$body){
        if($null -eq $body){return & $invoke -Port $Port -Path $path}
        return & $invoke -Port $Port -Method POST -Path $path -Json ($body|ConvertTo-Json -Compress)
    }
    $state=$null
    for($attempt=0;$attempt -lt 20;$attempt++){
        try{$state=Call '/state' $null;if($state.value.preview){break}}catch{}
        Start-Sleep -Milliseconds 300
    }
    if(!$state.value.preview -or $state.value.authenticated){throw 'Expected isolated, unauthenticated Mock startup.'}
    $view=$state.value.page.viewId
    $null=Call '/input' @{viewId=$view;nodeId='content';value='HTTP debug test 中文'}
    $null=Call '/input' @{viewId=$view;nodeId='pinned';value=$true}
    $after=Call '/press' @{viewId=$view;nodeId='save'}
    if($after.value.status -notmatch 'HTTP debug test' -or $after.value.status -notmatch 'true'){throw 'Native button callback did not see the actual bound inputs.'}
    function Reject([string]$path,[hashtable]$body,[int]$status){
        try{$null=Call $path $body;throw 'Expected HTTP rejection.'}
        catch [Microsoft.PowerShell.Commands.HttpResponseException]{if([int]$_.Exception.Response.StatusCode -ne $status){throw}}
    }
    Reject '/input' @{viewId='stale';nodeId='content';value='must not apply'} 409
    Reject '/input' @{viewId=$view;nodeId='content';value=$false} 400
    Reject '/input' @{viewId=$view;nodeId='content';value=('x'*501)} 400
    Reject '/press' @{viewId=$view;nodeId='not-present'} 404
    Reject '/preview' @{} 409
    $json=(Call '/state' $null)|ConvertTo-Json -Depth 24
    if($json.Contains($token) -or $json -match 'accessToken|refreshToken|deviceSecret|password'){throw 'Credential material appeared in the debug state.'}
    [ordered]@{passed=$true;mode='isolated native Mock';readPageTree=$true;nativeTextAndToggleBinding=$true;nativeButtonCallback=$true;staleViewRejected=$true;wrongTypeAndLengthRejected=$true;unknownButtonRejected=$true;dirtyDraftProtected=$true;credentialsExposed=$false;keyboardOrMouseUsed=$false}|ConvertTo-Json
}finally{
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
}
