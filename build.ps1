param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot "publish\$Runtime"
$releaseDirectory = Join-Path $artifactRoot 'releases'
$project = Join-Path $projectRoot 'src\TrayAlwaysOnTop\TrayAlwaysOnTop.csproj'
$vsCodeExtension = Join-Path $projectRoot 'extensions\vscode'
$visualStudioExtension = Join-Path $projectRoot 'extensions\visualstudio\TrayAlwaysOnTop.VisualStudio.csproj'

$resolvedRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
if (-not $resolvedArtifacts.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Artifact directory resolved outside the project root.'
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Push-Location $vsCodeExtension
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'VS Code extension dependency restore failed.' }
    npm run package
    if ($LASTEXITCODE -ne 0) { throw 'VS Code extension packaging failed.' }
}
finally {
    Pop-Location
}

dotnet build $visualStudioExtension --configuration Release -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw 'Visual Studio extension packaging failed.' }
$visualStudioVsix = Join-Path $projectRoot 'extensions\visualstudio\bin\Release\TrayAlwaysOnTop-VisualStudio.vsix'
if (-not (Test-Path -LiteralPath $visualStudioVsix)) { throw 'Visual Studio VSIX output was not created.' }
Copy-Item -LiteralPath $visualStudioVsix -Destination (Join-Path $artifactRoot 'TrayAlwaysOnTop-VisualStudio.vsix') -Force

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

vpk pack `
    --packId TrayAlwaysOnTop `
    --packVersion $Version `
    --packDir $publishDirectory `
    --mainExe TrayAlwaysOnTop.exe `
    --packTitle 'Tray Always On Top' `
    --packAuthors 'Local' `
    --runtime $Runtime `
    --shortcuts StartMenuRoot `
    --outputDir $releaseDirectory `
    --yes
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

Write-Host "Velopack release created at: $releaseDirectory"
