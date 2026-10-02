$ErrorActionPreference = 'Stop'
$scratch = Join-Path $PSScriptRoot ('build\deploy-check-' + [Guid]::NewGuid().ToString('N'))
$candidate = Join-Path $scratch 'build\CodexPeek.candidate.exe'
$target = Join-Path $scratch 'CodexPeek.exe'
New-Item -ItemType Directory -Path (Join-Path $scratch 'build') -Force | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'deploy.ps1') -Destination $scratch
    $fixture = Join-Path $scratch 'Fixture.cs'
    'class Fixture { static void Main() { System.Threading.Thread.Sleep(30000); } }' | Set-Content -LiteralPath $fixture
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe "/out:$candidate" $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
    $goodHash = (Get-FileHash -LiteralPath $candidate).Hash
    & (Join-Path $scratch 'deploy.ps1')
    $running = @(Get-Process CodexPeek -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })
    if ($running.Count -ne 1 -or (Get-FileHash -LiteralPath $target).Hash -ne $goodHash) { throw 'Fixed entry was not deployed and started' }
    [IO.File]::WriteAllText($candidate,'Invalid executable for the deployment failure check')
    $failed = $false
    try { & (Join-Path $scratch 'deploy.ps1') } catch { $failed = $true }
    $running = @(Get-Process CodexPeek -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })
    if (-not $failed -or $running.Count -ne 1 -or (Get-FileHash -LiteralPath $target).Hash -ne $goodHash) { throw 'Failed update did not restore the same working entry' }
    Remove-Item -LiteralPath $candidate
    $before = $running[0].Id;$failed = $false
    try { & (Join-Path $scratch 'deploy.ps1') } catch { $failed = $true }
    $running = @(Get-Process CodexPeek -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target })
    if (-not $failed -or $running.Count -ne 1 -or $running[0].Id -ne $before) { throw 'Missing build disrupted the running entry' }
    Write-Output 'PASS: fixed entry, startup failure rollback and missing-build protection'
} finally {
    Get-Process CodexPeek -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target } | ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit() }
    $resolved = (Resolve-Path -LiteralPath $scratch).Path
    $boundary = (Join-Path $PSScriptRoot 'build').TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup escaped the build directory' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
