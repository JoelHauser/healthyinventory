<#
.SYNOPSIS
    Builds Healthy Inventory, runs the tests, checks the output, and zips it.

.DESCRIPTION
    Run through PowerShell, not Bash (Bash mangles 'C:\HUH' into 'C:HUH').

    -SPTPath must hold the SPT-PATCHED Assembly-CSharp: a launched install, or a mirror built as
    CLAUDE.md describes. The plugin compiles against the game's types, so a build against the
    unpatched assembly would bind obfuscated names that do not exist at runtime; the reference
    check below refuses that.

.PARAMETER InstallTo
    An SPT install root to copy the built DLL into (BepInEx\plugins). Never C:\HUH on the
    development box, which stays untouched.

.EXAMPLE
    scripts\pack.ps1 -SPTPath <mirror>
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -InstallTo H:\SPT4.1.X
#>
param(
    [Parameter(Mandatory = $true)][string]$SPTPath,
    [string]$InstallTo
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $root 'src\HealthyInventory\HealthyInventory.csproj'
$tests = Join-Path $root 'tests\HealthyInventory.Tests\HealthyInventory.Tests.csproj'
$managed = Join-Path $SPTPath 'EscapeFromTarkov_Data\Managed'

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' has no BepInEx\core\BepInEx.dll."
}

# ---------------------------------------------------------------- version agreement
$csprojVersion = ([xml](Get-Content $plugin)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path (Join-Path $root 'src\HealthyInventory\Plugin.cs') `
        -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if ($csprojVersion -ne $sourceVersion) {
    throw "Version mismatch: csproj '$csprojVersion', Plugin.PluginVersion '$sourceVersion'."
}
Write-Host "Version $csprojVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------- build and test
dotnet build $plugin -c Release "-p:SPTPath=$SPTPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$env:HI_MANAGED = $managed
dotnet test $tests --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# ---------------------------------------------------------------- reference check
# Every type the plugin takes from Assembly-CSharp must have a readable name. Obfuscated names are
# private-use characters (U+E000..U+F8FF); finding one means the build used the unpatched assembly.
$built = Join-Path $root 'src\HealthyInventory\bin\Release\HealthyInventory.dll'
$cecil = Join-Path $SPTPath 'BepInEx\core\Mono.Cecil.dll'
if (-not (Test-Path $cecil)) { throw "No Mono.Cecil at '$cecil'." }
Add-Type -Path $cecil
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($built)
try {
    $refs = $module.AssemblyReferences | ForEach-Object { $_.Name }
    $bad = $module.GetTypeReferences() | Where-Object { $_.FullName -match '[\uE000-\uF8FF]' }
    $badMembers = $module.GetMemberReferences() | Where-Object { $_.FullName -match '[\uE000-\uF8FF]' }
}
finally { $module.Dispose() }
if ($bad -or $badMembers) {
    throw "Built against an unpatched assembly: obfuscated references found ($(@($bad).Count) types, $(@($badMembers).Count) members)."
}
$unexpected = $refs | Where-Object { $_ -like 'spt-*' }
if ($unexpected) { throw "Unexpected references: $($unexpected -join ', ')" }
Write-Host "References: $($refs -join ', ')" -ForegroundColor Green

# ---------------------------------------------------------------- stage and zip
# Written entry by entry: Compress-Archive in Windows PowerShell 5.1 stores backslash paths,
# which some extractors and mod managers do not treat as folders.
New-Item -ItemType Directory -Force -Path (Join-Path $root 'releases') | Out-Null
$zip = Join-Path $root "releases\HealthyInventory-$csprojVersion.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $built, 'BepInEx/plugins/HealthyInventory.dll')
}
finally { $archive.Dispose() }
Write-Host "Packed $zip" -ForegroundColor Green

if ($InstallTo) {
    $dest = Join-Path $InstallTo 'BepInEx\plugins'
    if (-not (Test-Path $dest)) { throw "No BepInEx\plugins under '$InstallTo'." }
    Copy-Item $built $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
