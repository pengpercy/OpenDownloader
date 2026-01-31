#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

# Cleanup binaries
echo "Cleaning up binaries for $RUNTIME..."
# OpenDownloader is the directory
find OpenDownloader/Assets/Binaries -mindepth 1 -maxdepth 1 -type d -not -name "darwin" -exec rm -rf {} +
if [ "$RUNTIME" == "osx-x64" ]; then
    rm -rf OpenDownloader/Assets/Binaries/darwin/arm64
elif [ "$RUNTIME" == "osx-arm64" ]; then
    rm -rf OpenDownloader/Assets/Binaries/darwin/x64
fi

mkdir -p OpenDownloader.app/Contents/MacOS
mkdir -p OpenDownloader.app/Contents/Resources
cp -r OpenDownloader/* OpenDownloader.app/Contents/MacOS/
rm -rf OpenDownloader
cp resources/app/App.icns OpenDownloader.app/Contents/Resources/App.icns
sed "s/OPENDOWNLOADER_VERSION/$VERSION/g" resources/app/App.plist > OpenDownloader.app/Contents/Info.plist
rm -rf OpenDownloader.app/Contents/MacOS/OpenDownloader.dsym

zip "opendownloader_$VERSION.$RUNTIME.zip" -r OpenDownloader.app

# Create DMG
DMG_NAME="opendownloader_$VERSION.$RUNTIME.dmg"
echo "Creating DMG: $DMG_NAME"
rm -f "$DMG_NAME"

# Create a temporary folder for DMG content
DMG_SOURCE="dmg_source"
mkdir -p "$DMG_SOURCE"
cp -r "OpenDownloader.app" "$DMG_SOURCE/"
ln -s /Applications "$DMG_SOURCE/Applications"

# Create DMG using hdiutil
hdiutil create -volname "OpenDownloader" \
    -srcfolder "$DMG_SOURCE" \
    -ov -format UDZO \
    "$DMG_NAME"

# Cleanup
rm -rf "$DMG_SOURCE"
echo "Done packaging for $RUNTIME. Zip and DMG created."
