param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $Root 'bin\LCET.dll')
$server = New-Object LCET.LoopbackServer((Join-Path $Root 'web'))
$cookies = New-Object Net.CookieContainer
Add-Type -TypeDefinition @'
public static class ModelDownloadProbe
{
    public static int Count;
    public static bool Fail;
    public static void Open()
    {
        if (Fail) throw new System.InvalidOperationException("synthetic launch failure");
        System.Threading.Interlocked.Increment(ref Count);
    }
}
'@
function Request([string]$path, [bool]$authenticated = $true, [string]$origin = '', [string]$method = 'GET', [string]$body = '{}') {
    $request = [Net.HttpWebRequest]::Create($server.Origin + $path)
    $request.Timeout = 5000
    $request.Proxy = $null
    if ($authenticated) { $request.CookieContainer = $cookies }
    if ($origin) { $request.Headers.Add('Origin', $origin) }
    $request.Method = $method
    if ($method -eq 'POST') {
        $request.ContentType = 'application/json'
        $data = [Text.Encoding]::UTF8.GetBytes($body)
        $request.ContentLength = $data.Length
        $stream = $request.GetRequestStream()
        $stream.Write($data, 0, $data.Length)
        $stream.Dispose()
    }
    try { return $request.GetResponse() }
    catch [Net.WebException] { if ($_.Exception.Response) { return $_.Exception.Response }; throw }
}
function Expect([string]$name, $response, [int]$code) {
    try {
        if ([int]$response.StatusCode -ne $code) { throw "$name expected $code, got $($response.StatusCode)" }
        Write-Output "PASS $name"
    } finally { $response.Dispose() }
}
try {
    Expect 'entry serves page and sets session cookie' (Request '/') 200
    Expect 'same-origin script available' (Request '/app.js') 200
    Expect 'same-origin shared language catalog available' (Request '/languages.json') 200
    Expect 'shared language catalog requires session cookie' (Request '/languages.json' $false) 403
    Expect 'no session cookie rejected' (Request '/app.js' $false) 403
    Expect 'cross-origin entry rejected' (Request '/' $true 'https://example.com') 403
    Expect 'cross-origin result rejected' (Request '/result' $true 'https://example.com' 'POST') 403
    Expect 'POST without Origin rejected' (Request '/result' $true '' 'POST') 403
    Expect 'same-origin heartbeat accepted' (Request '/heartbeat' $true $server.Origin 'POST') 200
    Expect 'same-origin manual translation result accepted' (Request '/manual-result' $true $server.Origin 'POST') 200
    Expect 'model page launch without a native handler reports unavailable' (Request '/open-model-download' $true $server.Origin 'POST') 503
    $downloadHandler = [ModelDownloadProbe].GetMethod('Open').CreateDelegate([Action])
    $server.add_ModelDownloadRequested($downloadHandler)
    Expect 'authenticated model page launch calls native handler' (Request '/open-model-download' $true $server.Origin 'POST') 200
    if ([ModelDownloadProbe]::Count -ne 1) { throw 'Native model launch handler was not called exactly once' }
    Write-Output 'PASS model page launch reaches native handler exactly once'
    Expect 'model page launch requires session cookie' (Request '/open-model-download' $false) 403
    Expect 'model page launch rejects external origins' (Request '/open-model-download' $true 'https://example.com') 403
    if ([ModelDownloadProbe]::Count -ne 1) { throw 'Rejected request invoked native model launcher' }
    Write-Output 'PASS rejected requests do not invoke model launcher'
    [ModelDownloadProbe]::Fail = $true
    Expect 'native model launch failure returns a visible failure status' (Request '/open-model-download' $true $server.Origin 'POST') 503
    [ModelDownloadProbe]::Fail = $false
    $resource = Request '/language-resources' $true $server.Origin 'POST' '{"sourceLanguage":"en","targetLanguage":"zh"}'
    try {
        $resourceReader = New-Object IO.StreamReader($resource.GetResponseStream())
        $resourceData = $resourceReader.ReadToEnd() | ConvertFrom-Json
        if ($resourceData.sourceLanguage -ne 'en' -or $resourceData.targetLanguage -ne 'zh' -or $resourceData.state -notin @('available', 'missing', 'unknown')) { throw 'Language resource response invalid' }
        Write-Output 'PASS Windows speech pack check returns structured language-pair state'
        $directResource = [LCET.LanguageResources]::Check('en', 'zh')
        if ($resourceData.state -ne $directResource['state']) { throw 'HTTP package status differs from direct Windows query' }
        Write-Output 'PASS loopback Windows speech pack state matches direct ordinary-user package query'
    } finally { $resource.Dispose() }
    Expect 'language resource check rejects invalid language tags' (Request '/language-resources' $true $server.Origin 'POST' '{"sourceLanguage":"../en","targetLanguage":"zh"}') 400
    Expect 'language resource check requires session cookie' (Request '/language-resources' $false) 403
    Expect 'language resource check rejects external origins' (Request '/language-resources' $true 'https://example.com') 403
    Expect 'cross-origin manual translation result rejected' (Request '/manual-result' $true 'https://example.com' 'POST') 403
    Expect 'manual result without session rejected' (Request '/manual-result' $false $server.Origin 'POST') 403
    Expect 'malformed JSON rejected' (Request '/result' $true $server.Origin 'POST' '[]') 400
    Expect 'invalid JSON syntax rejected' (Request '/result' $true $server.Origin 'POST' '{oops') 400
    Expect 'oversized body rejected' (Request '/result' $true $server.Origin 'POST' ('x' * 65537)) 400
    Expect 'path traversal not served' (Request '/../../config/defaults.json') 404
    $sse = Request '/events'
    try {
        $reader = New-Object IO.StreamReader($sse.GetResponseStream())
        $line = $reader.ReadLine()
        if ($line -ne 'retry: 2000') { throw 'SSE framing invalid' }
        Write-Output 'PASS SSE framing'
        [void]$reader.ReadLine()
        $server.Send('task', @{segmentId=7; revision=2; text='sample'})
        if ($reader.ReadLine() -ne 'event: task') { throw 'SSE event kind invalid' }
        $dataLine = $reader.ReadLine()
        if (-not $dataLine.StartsWith('data: ')) { throw 'SSE payload missing' }
        $payload = $dataLine.Substring(6) | ConvertFrom-Json
        if ($payload.segmentId -ne 7 -or $payload.revision -ne 2) { throw 'SSE payload mismatch' }
        Write-Output 'PASS SSE task payload round-trip'
    } finally { $sse.Dispose() }
    $address = [Net.IPAddress]::Parse(([uri]$server.Url).Host)
    if (-not [Net.IPAddress]::IsLoopback($address)) { throw 'Not loopback' }
    Write-Output 'PASS loopback-only origin'
} finally { $server.Dispose() }