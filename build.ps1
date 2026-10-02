param([switch]$BuildOnly,[switch]$Deploy)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$refs = @('/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll',"/r:$wpf\UIAutomationClient.dll","/r:$wpf\UIAutomationTypes.dll","/r:$wpf\WindowsBase.dll")
Push-Location -LiteralPath $PSScriptRoot
try {
    New-Item -ItemType Directory -Path 'build' -Force | Out-Null
    & $compiler /nologo /target:winexe /optimize+ /out:build\CodexPeek.candidate.exe /win32manifest:app.manifest @refs App.cs
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
    if (-not $BuildOnly) {
        node --no-warnings --test test.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed' }
        & $compiler /nologo /target:exe /out:build\LayoutTests.exe /main:LayoutTests @refs App.cs LayoutTests.cs
        if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed' }
        & .\build\LayoutTests.exe
        if ($LASTEXITCODE -ne 0) { throw 'Native layout tests failed' }
    }
    if ($Deploy) { & .\deploy.ps1 }
    else { Write-Output "Build complete. Daily entry stays at $PSScriptRoot\CodexPeek.exe; use -Deploy to update and restart it." }
} finally {
    Pop-Location
}
