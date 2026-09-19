param(
    [switch]$Kill,
    [switch]$IncludeBlender
)

$ErrorActionPreference = "Stop"

function Get-MatchingProcess {
    Get-CimInstance Win32_Process |
        Where-Object {
            ($_.Name -in @("mcp-for-unity.exe", "uvx.exe", "uv.exe", "python.exe") -and
                ($_.CommandLine -match "mcp-for-unity" -or $_.CommandLine -match "mcpforunityserver")) -or
            ($IncludeBlender -and $_.CommandLine -match "blender-mcp-server")
        } |
        Sort-Object CreationDate
}

$unityProcesses = Get-Process Unity -ErrorAction SilentlyContinue |
    Select-Object Id, ProcessName, Responding, StartTime, MainWindowTitle, Path

$mcpProcesses = Get-MatchingProcess |
    Select-Object ProcessId, ParentProcessId, Name, CreationDate, CommandLine

Write-Host "Unity processes:"
$unityProcesses | Format-Table -AutoSize

Write-Host ""
Write-Host "MCP-related processes:"
$mcpProcesses | Format-Table -AutoSize

if (-not $Kill) {
    Write-Host ""
    Write-Host "Dry run only. Re-run with -Kill to stop MCP-related processes before restarting Codex/Unity."
    exit 0
}

foreach ($process in $mcpProcesses) {
    Write-Host "Stopping $($process.Name) PID=$($process.ProcessId)"
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Host "Done. Restart Unity, then start a fresh Codex task or reconnect MCP."
