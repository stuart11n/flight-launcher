# Publishes a self-contained FlightLauncher build (no separate .NET / WASDK install needed).
# Output: artifacts\publish\win-x64\

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$outDir = Join-Path $root "artifacts\publish\win-x64"
if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

Write-Host "Publishing self-contained win-x64 ($Configuration)..."
dotnet publish .\FlightLauncher.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Published to: $outDir"
Write-Host "Run FlightLauncher.exe from that folder, or build installer\FlightLauncher.iss with Inno Setup."
