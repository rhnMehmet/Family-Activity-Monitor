param(
    [Parameter(Mandatory = $true)][ValidateRange(1,2147483647)][int]$TargetProcessId,
    [ValidateRange(5,86400)][int]$DurationSeconds = 120,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\performance\manual')
)
$ErrorActionPreference = 'Stop'
if (-not ('FamilyMonitorManualPerformanceProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FamilyMonitorManualPerformanceProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Io {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessIoCounters(IntPtr process, out Io counters);
}
'@
}
$target = [Diagnostics.Process]::GetProcessById($TargetProcessId)
$logicalProcessors = [Environment]::ProcessorCount
$rows = [Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$target.Refresh()
$previousCpu = $target.TotalProcessorTime.TotalSeconds
$previousTime = $watch.Elapsed.TotalSeconds
$previousIo = [FamilyMonitorManualPerformanceProbe+Io]::new()
if (-not [FamilyMonitorManualPerformanceProbe]::GetProcessIoCounters($target.Handle, [ref]$previousIo)) { throw 'Process I/O counters are unavailable.' }
try {
    while ($watch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        Start-Sleep -Seconds 1
        $target.Refresh()
        if ($target.HasExited) { break }
        $now = $watch.Elapsed.TotalSeconds
        $cpu = $target.TotalProcessorTime.TotalSeconds
        $currentIo = [FamilyMonitorManualPerformanceProbe+Io]::new()
        if (-not [FamilyMonitorManualPerformanceProbe]::GetProcessIoCounters($target.Handle, [ref]$currentIo)) { break }
        $corePercent = ($cpu - $previousCpu) / ($now - $previousTime) * 100
        $rows.Add([PSCustomObject]@{
            Utc = [DateTimeOffset]::UtcNow.ToString('o')
            ElapsedSeconds = $now
            LogicalProcessors = $logicalProcessors
            CpuTotalPercent = $corePercent / $logicalProcessors
            CpuSingleCorePercent = $corePercent
            WorkingSetMiB = $target.WorkingSet64 / 1MB
            PrivateMiB = $target.PrivateMemorySize64 / 1MB
            Handles = $target.HandleCount
            Threads = $target.Threads.Count
            LogicalReadBytes = $currentIo.ReadBytes - $previousIo.ReadBytes
            LogicalWriteBytes = $currentIo.WriteBytes - $previousIo.WriteBytes
        })
        $previousCpu = $cpu; $previousTime = $now; $previousIo = $currentIo
    }
}
finally {
    $target.Dispose()
    $directory = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $name = 'monitor-{0}-{1}.csv' -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N')
    $path = Join-Path $directory $name
    $rows | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8
    Write-Output $path
}
