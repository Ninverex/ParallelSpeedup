[CmdletBinding()]
param(
    [ValidateSet('Smoke','Full','Segments')][string]$Mode = 'Smoke',
    [int]$PauseSeconds = 3,
    [string]$OutputDir = '',
    [int]$Seed = 42,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (!$OutputDir) { $OutputDir = if ($Mode -eq 'Smoke') { 'results/smoke' } else { 'results/full' } }
    $OutputDir = [IO.Path]::GetFullPath($OutputDir)
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    if (!$SkipBuild) {
        dotnet build -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    $exe = Join-Path $root 'ParallelSpeedup.Bench/bin/Release/net10.0/ParallelSpeedup.Bench.exe'
    if (!(Test-Path $exe)) { throw "Missing executable: $exe" }
    $meta = Join-Path $OutputDir 'machine-info.txt'
    if (!(Test-Path $meta)) {
        @("Started UTC: $([DateTime]::UtcNow.ToString('o'))", "Seed: $Seed", 'CPU:',
          (Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors,L2CacheSize,L3CacheSize | Format-List | Out-String),
          'RAM:', (Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory | Format-List | Out-String),
          'OS:', (Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber | Format-List | Out-String),
          'Power plan:', (powercfg /getactivescheme | Out-String), '.NET:', (dotnet --info | Out-String)) | Set-Content -Encoding utf8 $meta
    }
    function Measure-Case([string]$Algo, [string]$Variant, [int]$P, [long]$N, [int]$Runs, [int]$SegmentSize = 262144, [string]$Experiment = 'scaling') {
        $file = Join-Path $OutputDir "${Experiment}_${Algo}_${Variant}_p${P}_n${N}_s${SegmentSize}_seed${Seed}.csv"
        if (Test-Path $file) {
            $rows = @(Import-Csv $file)
            if ($rows.Count -eq $Runs -and @($rows | Where-Object { $_.correct -ne 'true' }).Count -eq 0 -and @($rows | Select-Object -ExpandProperty run -Unique).Count -eq $Runs) {
                Write-Host "Skipping completed: $file"; return
            }
            # Restart incomplete cases rather than mixing repeated run IDs.
            Remove-Item $file
        }
        & $exe --algo $Algo --variant $Variant --p $P --n $N --runs $Runs --seed $Seed --segment-size $SegmentSize --experiment $Experiment --out $file
        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed: $file (exit $LASTEXITCODE)" }
        $rows = @(Import-Csv $file)
        if ($rows.Count -ne $Runs -or @($rows | Where-Object { $_.correct -ne 'true' }).Count -ne 0 -or @($rows | Select-Object -ExpandProperty run -Unique).Count -ne $Runs) {
            throw "Incomplete or invalid results: $file"
        }
        if ($PauseSeconds -gt 0) { Start-Sleep -Seconds $PauseSeconds }
    }
    if ($Mode -eq 'Smoke') {
        Measure-Case mc sequential 1 1000000 2
        foreach ($p in @(1,2,4,8)) {
            Measure-Case mc thread-local $p 1000000 2
            Measure-Case mc shared $p 1000000 2
            Measure-Case sieve segmented $p 1000000 2
        }
        Measure-Case sieve classic 1 1000000 2
        foreach ($s in @(16384,262144)) { Measure-Case sieve segmented 8 1000000 2 $s 'segment-size' }
    } elseif ($Mode -eq 'Full') {
        $workers = @(1,2,3,4,6,8,12,16)
        foreach ($n in @(10000000,100000000,1000000000)) {
            Measure-Case mc sequential 1 $n 10
            foreach ($p in $workers) { Measure-Case mc thread-local $p $n 10 }
        }
        foreach ($n in @(1000000,10000000,100000000)) {
            foreach ($p in $workers) { Measure-Case mc shared $p $n 10 }
        }
        foreach ($n in @(10000000,100000000,1000000000)) {
            if ($n -le 100000000) { Measure-Case sieve classic 1 $n 10 }
            foreach ($p in $workers) { Measure-Case sieve segmented $p $n 10 }
        }
    } else {
        foreach ($s in @(16384,65536,262144,524288,1048576,2097152,4194304)) {
            Measure-Case sieve segmented 8 100000000 10 $s 'segment-size'
        }
    }
    Write-Host "Finished $Mode. Results: $OutputDir"
} finally { Pop-Location }
