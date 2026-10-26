param(
    [ValidateRange(1024,65535)][int]$Port=9224,
    [ValidateSet('GET','POST')][string]$Method='GET',
    [ValidatePattern('^/[a-z/]+$')][string]$Path='/state',
    [string]$Json='{}'
)
$ErrorActionPreference='Stop'
$token=$env:QUANTUM_MOBILE_DEBUG_TOKEN
if([string]::IsNullOrWhiteSpace($token)){throw 'Set QUANTUM_MOBILE_DEBUG_TOKEN in this shell first.'}
$options=@{
    Uri="http://127.0.0.1:$Port$Path"
    Method=$Method
    Headers=@{Authorization="Bearer $token"}
    NoProxy=$true
    MaximumRedirection=0
    ConnectionTimeoutSeconds=20
    OperationTimeoutSeconds=20
}
if($Method -eq 'POST'){$options.ContentType='application/json; charset=utf-8';$options.Body=[Text.Encoding]::UTF8.GetBytes($Json)}
# Intentionally no automatic retries: a disconnected POST can have an unknown outcome.
Invoke-RestMethod @options
