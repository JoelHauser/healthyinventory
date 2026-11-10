<#
.SYNOPSIS
    Runs tools/PatchHarness: the plugin's real sources and patches in the game's own mono runtime.

.DESCRIPTION
    Nothing touches a game install or profile. Everything is built under -Work; the game's mono is
    only used as a program to host the run. -SPTPath must hold the SPT-patched Assembly-CSharp
    (a launched install, or a mirror built as CLAUDE.md describes).

.EXAMPLE
    scripts\run-harness.ps1 -SPTPath <mirror> -Mono C:\HUH\MonoBleedingEdge -Work <empty folder>
#>
param(
    [Parameter(Mandatory = $true)][string] $SPTPath,
    [Parameter(Mandatory = $true)][string] $Mono,
    [Parameter(Mandatory = $true)][string] $Work
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hbin = Join-Path $Work 'hbin'
$mhost = Join-Path $Work 'mhost'
$asm = Join-Path $Work 'asm'
New-Item -ItemType Directory -Force -Path $hbin, $mhost, $asm | Out-Null

dotnet build (Join-Path $root 'tools\PatchHarness\PatchHarness.csproj') -c Release "-p:SPTPath=$SPTPath" -o $hbin --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed.' }
dotnet build (Join-Path $root 'tools\MonoHost\MonoHost.csproj') -c Release -o $mhost --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'MonoHost build failed.' }

robocopy (Join-Path $SPTPath 'EscapeFromTarkov_Data\Managed') $asm /E /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy (Join-Path $SPTPath 'BepInEx\core') $asm /E /NFL /NDL /NJH /NJS /NP | Out-Null
Copy-Item (Join-Path $hbin 'PatchHarness.exe') $asm -Force

# Through cmd: the mono runtime writes harmless "out of sync" / "cant resolve internal call" lines
# to stderr, which Windows PowerShell 5.1 would turn into a terminating error here.
$log = Join-Path $Work 'harness-output.txt'
$cmdline = '"{0}" "{1}" "{2}" "{3}" "{4}" "{5}" > "{6}" 2>&1' -f (Join-Path $mhost 'MonoHost.exe'), (Join-Path $Mono 'EmbedRuntime'), (Join-Path $Mono 'etc'), $asm, (Join-Path $asm 'PatchHarness.exe'), $Work, $log
cmd /c "`"$cmdline`""
$code = $LASTEXITCODE
Get-Content $log | Where-Object { $_ -notmatch 'out of sync|cant resolve internal call' }
Write-Host "Harness exit code: $code"
exit $code
