[CmdletBinding()]
param(
    [string]$GodotPath = '',
    [string]$OutputDirectory = '',
    [switch]$SkipArchive
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$buildsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Builds'))

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $buildsRoot 'Windows'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$buildsPrefix = $buildsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $OutputDirectory.StartsWith($buildsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must remain beneath $buildsRoot"
}

if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $candidates = @(
        $env:GODOT4,
        'C:\Projects\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe',
        'C:\Projects\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
    $GodotPath = $candidates | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath)) {
    throw 'Godot 4.7 .NET was not found. Pass its executable path with -GodotPath.'
}

if (-not $GodotPath.EndsWith('_console.exe', [StringComparison]::OrdinalIgnoreCase)) {
    $consolePath = $GodotPath.Substring(0, $GodotPath.Length - 4) + '_console.exe'
    if (Test-Path -LiteralPath $consolePath) {
        $GodotPath = $consolePath
    }
}

$templateDirectory = Join-Path $env:APPDATA 'Godot\export_templates\4.7.stable.mono'
$requiredTemplates = @(
    (Join-Path $templateDirectory 'windows_debug_x86_64.exe'),
    (Join-Path $templateDirectory 'windows_release_x86_64.exe')
)
$missingTemplates = $requiredTemplates | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missingTemplates.Count -gt 0) {
    $missingTemplateList = $missingTemplates -join [Environment]::NewLine
    throw @"
The Godot 4.7 .NET Windows x86-64 export template is not installed.
Install it from Editor > Manage Export Templates > Windows > x86_64, then rerun this script.
Official templates: https://godotengine.org/download/archive/4.7-stable/
Missing files:
$missingTemplateList
"@
}

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$executablePath = Join-Path $OutputDirectory 'OnlyWar.exe'
& $GodotPath --headless --path $projectRoot --export-release 'Windows Desktop' $executablePath
if ($LASTEXITCODE -ne 0) {
    throw "Godot export failed with exit code $LASTEXITCODE."
}

$databaseOutput = Join-Path $OutputDirectory 'Database'
New-Item -ItemType Directory -Path $databaseOutput -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Database\OnlyWar.s3db') -Destination $databaseOutput
Copy-Item -LiteralPath (Join-Path $projectRoot 'Database\SaveStructure.sql') -Destination $databaseOutput

$requiredFiles = @(
    $executablePath,
    (Join-Path $OutputDirectory 'OnlyWar.pck'),
    (Join-Path $databaseOutput 'OnlyWar.s3db'),
    (Join-Path $databaseOutput 'SaveStructure.sql')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Export is incomplete; missing $requiredFile"
    }
}

$dotnetDataDirectory = Get-ChildItem -LiteralPath $OutputDirectory -Directory -Filter 'data_*' |
    Select-Object -First 1
if ($null -eq $dotnetDataDirectory) {
    throw 'Export is incomplete; the generated .NET data directory is missing.'
}

$sqliteNativeLibrary = Get-ChildItem -LiteralPath $dotnetDataDirectory.FullName -Recurse -File |
    Where-Object { $_.Name -in @('e_sqlite3.dll', 'sqlite3.dll') } |
    Select-Object -First 1
if ($null -eq $sqliteNativeLibrary) {
    throw 'Export is incomplete; no native SQLite library was found in the .NET data directory.'
}

if (-not $SkipArchive) {
    $archivePath = Join-Path $buildsRoot 'OnlyWar-Windows-x86_64.zip'
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Created $archivePath"
}

Write-Host "Deployable game folder: $OutputDirectory"
Write-Host 'Player saves are stored outside this folder under Godot user://saves.'
