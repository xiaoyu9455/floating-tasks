$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = Join-Path (Split-Path -Parent $PSScriptRoot) 'FloatingTasks.Mcp.exe'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
$start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
$process = [Diagnostics.Process]::Start($start)
try {
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}')
    $read = $process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(5000)) { throw 'initialize timeout' }
    $init = $read.Result | ConvertFrom-Json
    if ($init.result.protocolVersion -ne '2025-11-25') { throw 'Invalid initialize response' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}')
    $read = $process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(5000)) { throw 'tools/list timeout' }
    $list = $read.Result | ConvertFrom-Json
    if ($list.id -ne 2 -or $list.result.tools.Count -ne 12) { throw 'Invalid tools response or stray stdout' }
    if (($list.result.tools | Where-Object name -eq list_tasks).description -notmatch '稳定') { throw 'UTF-8 text was damaged' }
    $process.StandardInput.Close()
    if (-not $process.WaitForExit(5000)) { throw 'EOF did not terminate the bridge' }
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0 -or $stderr) { throw "Bridge error: $stderr" }
    'PASS: compiled MCP stdio initialize, notification, twelve tools, UTF-8, clean stdout, EOF shutdown'
} finally {
    if (-not $process.HasExited) { $process.Kill() }
    $process.Dispose()
}
