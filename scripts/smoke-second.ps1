# One copy per queue folder (Tools #89), the Windows twin of smoke-second.sh: a second copy on the same queue folder ends by
# itself within -Seconds with exit code 0 and the first one stays; once the first one is killed, another copy starts.
param([Parameter(Mandatory)][string]$Exe, [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ("smoke-second-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
Remove-Item Env:TOOLS_SCREENSHOT -ErrorAction SilentlyContinue
$env:TOOLS_DEMO = '1'; $env:TOOLS_DEMO_LANG = 'en'; $env:TOOLS_DEMO_IMPORT = 'empty'; $env:TOOLS_DEMO_UPDATE = 'none'
$env:TOOLS_DEMO_INSTANCE = Join-Path $work 'queue'
$started = [System.Collections.Generic.List[object]]::new()

function Start-App([string]$Name, [bool]$Wait) {
  if ($Wait) { $env:TOOLS_SMOKE_CLOSE = 'quit'; $env:TOOLS_SMOKE_CLOSE_DELAY_MS = '300000'; $env:TOOLS_SMOKE_READY = Join-Path $work "$Name.ready" }
  else { Remove-Item Env:TOOLS_SMOKE_CLOSE, Env:TOOLS_SMOKE_CLOSE_DELAY_MS, Env:TOOLS_SMOKE_READY -ErrorAction SilentlyContinue }
  $app = Start-Process $Exe -PassThru -RedirectStandardOutput (Join-Path $work "$Name.log") -RedirectStandardError (Join-Path $work "$Name.err")
  # Keeps the exit code readable after the process has ended
  $null = $app.Handle
  $started.Add($app)
  if ($Wait) {
    $until = (Get-Date).AddSeconds(120)
    while (-not (Test-Path (Join-Path $work "$Name.ready")) -and -not $app.HasExited -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path (Join-Path $work "$Name.ready"))) { throw "smoke-second: $Name did not start" }
  }
  $app
}

try {
  $first = Start-App 'first' $true
  Start-Sleep -Seconds 2
  $second = Start-App 'second' $false
  if (-not $second.WaitForExit($Seconds * 1000)) { throw "smoke-second: the second copy was still running after ${Seconds}s" }
  if ($second.ExitCode -ne 0) { throw "smoke-second: the second copy ended with $($second.ExitCode)" }
  if (-not (Select-String -Path (Join-Path $work 'second.err') -Pattern 'already open; its window is in front' -Quiet)) { throw 'smoke-second: the second copy did not hand over' }
  if (-not (Select-String -Path (Join-Path $work 'first.err') -Pattern 'Brought forward for a second copy' -Quiet)) { throw 'smoke-second: the first copy did not come forward' }
  if ($first.HasExited) { throw 'smoke-second: the first copy ended' }
  Stop-Process -Id $first.Id -Force
  $first.WaitForExit(10000) | Out-Null
  $null = Start-App 'third' $true
  Write-Output 'smoke-second passed: the second copy handed over and ended, and after a kill a new copy starts'
} catch {
  Get-ChildItem $work -File | ForEach-Object { Write-Output "--- $($_.Name)"; Get-Content $_.FullName -Tail 40 }
  Get-ChildItem "$env:LOCALAPPDATA\AlUmmahNow\VisualImporterUno\Logs" -Filter '*.txt' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Write-Output "--- $($_.FullName)"; Get-Content $_.FullName -Tail 60 }
  throw
} finally {
  foreach ($app in $started) { if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue } }
  Remove-Item Env:TOOLS_SMOKE_CLOSE, Env:TOOLS_SMOKE_CLOSE_DELAY_MS, Env:TOOLS_SMOKE_READY, Env:TOOLS_DEMO_INSTANCE, Env:TOOLS_DEMO_UPDATE -ErrorAction SilentlyContinue
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
