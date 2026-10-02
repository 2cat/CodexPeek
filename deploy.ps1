$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot 'CodexPeek.exe'
$candidate = Join-Path $PSScriptRoot 'build\CodexPeek.candidate.exe'
$previous = Join-Path $PSScriptRoot 'build\CodexPeek.previous.exe'
$legacy = Join-Path $PSScriptRoot 'dist\CodexPeek.exe'
if (-not (Test-Path -LiteralPath $candidate)) { throw 'Build the application before deploying it.' }
$hadPrevious = Test-Path -LiteralPath $target
if ($hadPrevious) { Copy-Item -LiteralPath $target -Destination $previous -Force }
$started = $null
try {
    Get-Process -Name CodexPeek -ErrorAction SilentlyContinue | Where-Object { $_.Path -in @($target,$legacy) } | ForEach-Object {
        Stop-Process -Id $_.Id -Force
        if (-not $_.WaitForExit(5000)) { throw 'The previous application did not exit.' }
    }
    Copy-Item -LiteralPath $candidate -Destination $target -Force
    $started = Start-Process -FilePath $target -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
    if ($started.WaitForExit(1000)) { throw 'The application exited during startup.' }
    Write-Output "Running fixed entry: $target (PID $($started.Id))"
} catch {
    $failure = $_
    try {
        if ($started -and -not $started.HasExited) { Stop-Process -Id $started.Id -Force; $started.WaitForExit() }
        if ($hadPrevious) {
            Copy-Item -LiteralPath $previous -Destination $target -Force
            $restored = Start-Process -FilePath $target -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
            if ($restored.WaitForExit(1000)) { throw 'The previous file was restored but could not stay running.' }
            Write-Output "Update failed; restored the previous version at $target (PID $($restored.Id))"
        } elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    } catch {
        throw "Update failed: $($failure.Exception.Message); recovery failed: $($_.Exception.Message)"
    }
    throw $failure
}
