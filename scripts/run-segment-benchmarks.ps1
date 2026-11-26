[CmdletBinding()]
param([int]$PauseSeconds = 3, [string]$OutputDir = 'results/full', [int]$Seed = 42, [switch]$SkipBuild)
& (Join-Path $PSScriptRoot 'run-benchmarks.ps1') -Mode Segments -PauseSeconds $PauseSeconds -OutputDir $OutputDir -Seed $Seed -SkipBuild:$SkipBuild
