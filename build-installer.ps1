$iscc    = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$redist  = "Installer\redist\VC_redist.x64.exe"

if (-not (Test-Path $iscc)) {
    Write-Host "Inno Setup not found at: $iscc" -ForegroundColor Red
    Write-Host "Download from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $redist)) {
    Write-Host "Missing: $redist" -ForegroundColor Red
    Write-Host "Download VC_redist.x64.exe from:" -ForegroundColor Yellow
    Write-Host "  https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Yellow
    Write-Host "Place it in the Installer\redist\ folder." -ForegroundColor Yellow
    exit 1
}

Write-Host "Publishing Go2HDR..." -ForegroundColor Cyan
dotnet publish -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed." -ForegroundColor Red; exit 1 }

Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc "Installer\Go2HDR.iss"
if ($LASTEXITCODE -ne 0) { Write-Host "Inno Setup failed." -ForegroundColor Red; exit 1 }

Write-Host "Done. Installer is in: Installer\Output\" -ForegroundColor Green
