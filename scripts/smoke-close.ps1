# Closing the app quits it (Tools #78), the Windows twin of smoke-close.sh. Starts the installed app in its demo mode with an
# empty queue, lets it close itself (TOOLS_SMOKE_CLOSE: close = Window.Close(), quit = the request the close button makes,
# terminate = the host's own exit), then fails unless the process ends within -Seconds of the close and no child process it
# started (the FileScan service, ffmpeg) is left behind.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Mode, [int]$Seconds = 20)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ("smoke-close-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$ready = Join-Path $work 'ready'
# A screenshot run would end the app by itself before the close
Remove-Item Env:TOOLS_SCREENSHOT -ErrorAction SilentlyContinue
$env:TOOLS_DEMO = '1'; $env:TOOLS_DEMO_LANG = 'en'; $env:TOOLS_DEMO_IMPORT = 'empty'
$env:TOOLS_SMOKE_CLOSE = $Mode; $env:TOOLS_SMOKE_CLOSE_DELAY_MS = '8000'; $env:TOOLS_SMOKE_READY = $ready
# Windows gives a process the id of one that has ended, and never changes a child's parent id: an older process whose parent
# had this id (a build server's) is none of the app's
$since = Get-Date
$app = Start-Process $Exe -PassThru -RedirectStandardOutput (Join-Path $work 'out.log') -RedirectStandardError (Join-Path $work 'err.log')

function Get-Descendants([int]$RootId) {
  $all = @(Get-CimInstance Win32_Process)
  $found = [System.Collections.Generic.List[object]]::new()
  $queue = [System.Collections.Generic.Queue[int]]::new()
  $queue.Enqueue($RootId)
  while ($queue.Count -gt 0) {
    $id = $queue.Dequeue()
    foreach ($child in $all | Where-Object { $_.ParentProcessId -eq $id -and $_.ProcessId -ne $id -and $_.CreationDate -ge $since }) { $found.Add($child); $queue.Enqueue([int]$child.ProcessId) }
  }
  $found.ToArray()
}

try {
  $until = (Get-Date).AddSeconds(120)
  while (-not (Test-Path $ready) -and -not $app.HasExited -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 500 }
  if (-not (Test-Path $ready)) {
    Get-Content (Join-Path $work 'out.log'), (Join-Path $work 'err.log') -Tail 40 -ErrorAction SilentlyContinue
    if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force }
    throw "smoke-close ($Mode): the app did not start"
  }
  Start-Sleep -Seconds 3
  $children = @(Get-Descendants $app.Id)
  Write-Output "smoke-close ($Mode): app running, children: $(($children | ForEach-Object { "$($_.Name) $($_.ProcessId)" }) -join ', ')"

  $closedAt = $null
  $limit = (Get-Date).AddSeconds(8 + $Seconds + 10)
  while (-not $app.HasExited -and (Get-Date) -lt $limit) {
    if ($null -eq $closedAt -and (Test-Path "$ready.closing")) { $closedAt = Get-Date }
    Start-Sleep -Milliseconds 250
  }
  if (-not $app.HasExited) {
    Get-Content (Join-Path $work 'out.log'), (Join-Path $work 'err.log') -Tail 60 -ErrorAction SilentlyContinue
    Stop-Process -Id $app.Id -Force
    throw "smoke-close ($Mode): the app was still running after the close"
  }
  if ($null -eq $closedAt) { throw "smoke-close ($Mode): the app ended without being closed (exit $($app.ExitCode))" }
  $took = [math]::Round(((Get-Date) - $closedAt).TotalSeconds)
  if ($took -gt $Seconds) { throw "smoke-close ($Mode): the app took ${took}s to end after the close (limit ${Seconds}s)" }
  Start-Sleep -Seconds 1
  # The app's last-resort exit (10 s after a clean stop) is a late end, not a quit: something still held the process
  if (Select-String -Path (Join-Path $work 'out.log'), (Join-Path $work 'err.log') -Pattern 'still running .* after the quit' -Quiet -ErrorAction SilentlyContinue) { throw "smoke-close ($Mode): only the last-resort exit ended the app" }
  $left = @($children | Where-Object { $p = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue; $p -and $p.ProcessName -eq [IO.Path]::GetFileNameWithoutExtension($_.Name) })
  if ($left.Count -gt 0) {
    $left | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    throw "smoke-close ($Mode): child processes left running: $(($left | ForEach-Object { "$($_.Name) $($_.ProcessId)" }) -join ', ')"
  }
  Write-Output "smoke-close ($Mode) passed: the app ended ${took}s after the close (exit $($app.ExitCode)), no child left"
} catch {
  # What the app wrote (the app has no console on Windows): its own log, the last of it
  Get-ChildItem "$env:LOCALAPPDATA\AlUmmahNow\VisualImporterUno\Logs" -Filter '*.txt' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Write-Output "--- $($_.FullName)"; Get-Content $_.FullName -Tail 60 }
  throw
} finally {
  # Whatever happens, nothing of this run outlives it
  foreach ($child in @(Get-Descendants $app.Id)) { Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue }
  if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue }
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
