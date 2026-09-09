[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "Go2HDR.csproj"
$publishDir = Join-Path $projectRoot "obj\InstallerPublish"
$artifactsDir = Join-Path $projectRoot "obj\InstallerArtifacts"
$installerDir = Join-Path $projectRoot "Installer"
$redistPath = Join-Path $installerDir "redist\VC_redist.x64.exe"
$installerScript = Join-Path $installerDir "Go2HDR.iss"
$projectXml = [xml](Get-Content -LiteralPath $projectFile -Raw)
$appVersion = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "The application version is missing from Go2HDR.csproj."
}

$resolvedRoot = [System.IO.Path]::GetFullPath($projectRoot) + [System.IO.Path]::DirectorySeparatorChar
$resolvedPublish = [System.IO.Path]::GetFullPath($publishDir)
$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsDir)
if (-not $resolvedPublish.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the project: $resolvedPublish"
}
if (-not $resolvedArtifacts.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an artifacts directory outside the project: $resolvedArtifacts"
}

function Assert-MicrosoftSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Microsoft Corporation(,|$)') {
        throw "The Visual C++ redistributable does not have a valid Microsoft signature: $Path"
    }
}

if (-not (Test-Path -LiteralPath $redistPath)) {
    $redistDirectory = Split-Path $redistPath
    $downloadPath = $redistPath + ".download"
    New-Item -ItemType Directory -Path $redistDirectory -Force | Out-Null
    Write-Host "Downloading the Microsoft Visual C++ 2015-2022 x64 redistributable..."
    try {
        Invoke-WebRequest -Uri "https://aka.ms/vs/17/release/VC_redist.x64.exe" -OutFile $downloadPath
        Assert-MicrosoftSignature $downloadPath
        Move-Item -LiteralPath $downloadPath -Destination $redistPath -Force
    }
    finally {
        Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    }
}
Assert-MicrosoftSignature $redistPath

Write-Host "Publishing Go2HDR ($Configuration)..."
if (Test-Path -LiteralPath $resolvedPublish) {
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
if (Test-Path -LiteralPath $resolvedArtifacts) {
    Remove-Item -LiteralPath $resolvedArtifacts -Recurse -Force
}
dotnet publish $projectFile `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --artifacts-path $resolvedArtifacts `
    --output $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$iscc = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $knownPaths = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $isccPath = $knownPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
} else {
    $isccPath = $iscc.Source
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "Inno Setup 6 was not found. Install it and run this script again."
}
Write-Host "Building the installer..."
$outputPath = Join-Path $installerDir "Output\Go2HDR-Setup-$appVersion.exe"
Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
& $isccPath "/DAppVersion=$appVersion" "/DSourceDir=$publishDir" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $outputPath)) {
    throw "The expected installer was not created: $outputPath"
}

Write-Host "Installer ready: $outputPath"
