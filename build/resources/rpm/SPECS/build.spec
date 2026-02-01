Name: opendownloader
Version: %_version
Release: 1
Summary: Open-source Downloader
License: MIT
URL: https://github.com/percy/OpenDownloader
Source: https://github.com/percy/OpenDownloader/archive/refs/tags/v%_version.tar.gz
Requires: libX11.so.6()(%{__isa_bits}bit)
Requires: libSM.so.6()(%{__isa_bits}bit)
Requires: libicu
Requires: xdg-utils

%define _build_id_links none

%description
Open-source Downloader

%install
mkdir -p %{buildroot}/opt/opendownloader
mkdir -p %{buildroot}/%{_bindir}
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons
cp -r %{_topdir}/../../OpenDownloader/* %{buildroot}/opt/opendownloader/
ln -rsf %{buildroot}/opt/opendownloader/OpenDownloader %{buildroot}/opt/opendownloader/opendownloader
ln -rsf %{buildroot}/opt/opendownloader/opendownloader %{buildroot}/%{_bindir}
cp -r %{_topdir}/../_common/applications %{buildroot}/%{_datadir}
cp -r %{_topdir}/../_common/icons %{buildroot}/%{_datadir}
chmod 755 -R %{buildroot}/opt/opendownloader
chmod 755 %{buildroot}/%{_datadir}/applications/opendownloader.desktop

%files
%dir /opt/opendownloader/
/opt/opendownloader/*
/usr/share/applications/opendownloader.desktop
/usr/share/icons/*
%{_bindir}/opendownloader

%changelog
# skip
