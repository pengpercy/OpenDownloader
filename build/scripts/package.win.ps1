Remove-Item -Path build\OpenDownloader\*.pdb -Force

# Cleanup binaries
# Keep only win32
Get-ChildItem -Path "build\OpenDownloader\Assets\Binaries" -Directory | Where-Object { $_.Name -ne "win32" } | Remove-Item -Recurse -Force
if ($env:RUNTIME -eq "win-x64") {
    Remove-Item -Path "build\OpenDownloader\Assets\Binaries\win32\ia32" -Recurse -Force
} elseif ($env:RUNTIME -eq "win-arm64") {
    Remove-Item -Path "build\OpenDownloader\Assets\Binaries\win32\x64" -Recurse -Force
    Remove-Item -Path "build\OpenDownloader\Assets\Binaries\win32\ia32" -Recurse -Force
}

Compress-Archive -Path build\OpenDownloader -DestinationPath "build\opendownloader_${env:VERSION}.${env:RUNTIME}.zip" -Force
