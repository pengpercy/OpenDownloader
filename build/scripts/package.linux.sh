#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

arch=
appimage_arch=
target=
case "$RUNTIME" in
    linux-x64)
        arch=amd64
        appimage_arch=x86_64
        target=x86_64;;
    linux-arm64)
        arch=arm64
        appimage_arch=aarch64 # AppImage usually uses aarch64
        target=aarch64;;
    *)
        echo "Unknown runtime $RUNTIME"
        exit 1;;
esac

APPIMAGETOOL_URL=https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

cd build

if [[ ! -f "appimagetool" ]]; then
    curl -o appimagetool -L "$APPIMAGETOOL_URL"
    chmod +x appimagetool
fi

# Cleanup unwanted binaries from publish output (OpenDownloader)
echo "Cleaning up binaries for $RUNTIME..."
find OpenDownloader/Assets/Binaries -mindepth 1 -maxdepth 1 -type d -not -name "linux" -exec rm -rf {} +
if [ "$arch" == "amd64" ]; then
    rm -rf OpenDownloader/Assets/Binaries/linux/arm64
elif [ "$arch" == "arm64" ]; then
    rm -rf OpenDownloader/Assets/Binaries/linux/x64
fi

rm -f OpenDownloader/*.dbg

mkdir -p OpenDownloader.AppDir/opt
mkdir -p OpenDownloader.AppDir/usr/share/metainfo
mkdir -p OpenDownloader.AppDir/usr/share/applications

cp -r OpenDownloader OpenDownloader.AppDir/opt/opendownloader
desktop-file-install resources/_common/applications/opendownloader.desktop --dir OpenDownloader.AppDir/usr/share/applications \
    --set-icon com.opendownloader.app --set-key=Exec --set-value=AppRun
mv OpenDownloader.AppDir/usr/share/applications/{opendownloader,com.opendownloader.app}.desktop
cp resources/_common/icons/opendownloader.png OpenDownloader.AppDir/com.opendownloader.app.png
ln -rsf OpenDownloader.AppDir/opt/opendownloader/OpenDownloader OpenDownloader.AppDir/opt/opendownloader/opendownloader
ln -rsf OpenDownloader.AppDir/opt/opendownloader/opendownloader OpenDownloader.AppDir/AppRun
ln -rsf OpenDownloader.AppDir/usr/share/applications/com.opendownloader.app.desktop OpenDownloader.AppDir
cp resources/appimage/opendownloader.appdata.xml OpenDownloader.AppDir/usr/share/metainfo/com.opendownloader.app.appdata.xml

ARCH="$appimage_arch" ./appimagetool -v OpenDownloader.AppDir "opendownloader-$VERSION.linux.$appimage_arch.AppImage"

mkdir -p resources/deb/opt/opendownloader/
mkdir -p resources/deb/usr/bin
mkdir -p resources/deb/usr/share/applications
mkdir -p resources/deb/usr/share/icons
cp -a OpenDownloader/. resources/deb/opt/opendownloader/
ln -rsf resources/deb/opt/opendownloader/OpenDownloader resources/deb/opt/opendownloader/opendownloader
ln -rsf resources/deb/opt/opendownloader/opendownloader resources/deb/usr/bin
cp -r resources/_common/applications resources/deb/usr/share
cp -r resources/_common/icons resources/deb/usr/share
# Calculate installed size in KB
installed_size=$(du -sk resources/deb | cut -f1)
# Update the control file
sed -i -e "s/^Version:.*/Version: $VERSION/" \
    -e "s/^Architecture:.*/Architecture: $arch/" \
    -e "s/^Installed-Size:.*/Installed-Size: $installed_size/" \
    resources/deb/DEBIAN/control
# Build deb package with gzip compression
dpkg-deb -Zgzip --root-owner-group --build resources/deb "opendownloader_$VERSION-1_$arch.deb"

rpmbuild -bb --target="$target" resources/rpm/SPECS/build.spec --define "_topdir $(pwd)/resources/rpm" --define "_version $VERSION"
mv "resources/rpm/RPMS/$target/opendownloader-$VERSION-1.$target.rpm" ./
