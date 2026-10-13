# Builds the two downloadable versions of FolderBadges into the "dist" folder.
#
#   dist\FolderBadges.exe        Works on any Windows 10/11 PC (.NET is packed inside). Big file.
#   dist\FolderBadges-small.exe  Needs the .NET 10 Desktop Runtime installed. Tiny file.
#
# Run from the repo folder:  .\build-release.ps1
# The "dist" folder is ignored by Git; upload these files to a GitHub Release instead.

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\FolderBadges"
$dist = Join-Path $PSScriptRoot "dist"
$work = Join-Path $PSScriptRoot "src\FolderBadges\bin\publish-work"

# Start from an empty dist folder so old builds never get mixed in
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

Write-Host "Building FolderBadges.exe (.NET included)..."
dotnet publish $project -c Release -p:SelfContained=true -o "$work\full"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Copy-Item "$work\full\FolderBadges.exe" "$dist\FolderBadges.exe"

Write-Host "Building FolderBadges-small.exe (needs .NET 10 Desktop Runtime)..."
dotnet publish $project -c Release -p:SelfContained=false -o "$work\small"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Copy-Item "$work\small\FolderBadges.exe" "$dist\FolderBadges-small.exe"

Remove-Item $work -Recurse -Force

Write-Host ""
Write-Host "Done. Files in $dist :"
Get-ChildItem $dist | ForEach-Object { "  {0,-24} {1,6:N1} MB" -f $_.Name, ($_.Length / 1MB) }
