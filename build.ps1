param([string]$OutputName = '浮记.exe', [switch]$Test)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object FullName)
$output = Join-Path $PSScriptRoot $OutputName
$entryPoint = '/main:FloatingTasks.Program'
if ($Test) {
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
    $entryPoint = '/main:FloatingTasks.Tests.TestRunner'
    $output = Join-Path $PSScriptRoot 'artifacts\FloatingTasks.Tests.exe'
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'artifacts') -Force | Out-Null
}
$iconPath = Join-Path $PSScriptRoot 'assets\floating-tasks.ico'
$manifestPath = Join-Path $PSScriptRoot 'app.manifest'
& $compiler /win32icon:$iconPath "/resource:$iconPath,FloatingTasks.AppIcon" /win32manifest:$manifestPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $entryPoint /out:$output $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "已生成：$output"
if ($Test) {
    $testProcess = Start-Process -FilePath $output -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts\test-result.txt')
    if ($testProcess.ExitCode -ne 0) { throw '检查未通过，参见 artifacts\test-result.txt' }
}

if (-not $Test) {
    $mcpOutput = Join-Path (Split-Path -Parent $output) 'FloatingTasks.Mcp.exe'
    & $compiler /nologo /target:exe /platform:anycpu /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /main:FloatingTasks.McpProgram /out:$mcpOutput $sources
    if ($LASTEXITCODE -ne 0) { throw 'MCP 编译失败' }
    Write-Output "已生成：$mcpOutput"
}
