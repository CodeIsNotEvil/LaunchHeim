# Packages a LaunchHeim that build-packages.sh already published and laid out with stage.sh, so the
# rpm, the deb and the Arch package contain the same build. See packaging/README.md.

# The app is framework-dependent .NET plus two prebuilt native libraries: nothing to debug or strip.
%global debug_package %{nil}
%global __strip /bin/true
# libQmlNet.so and the signal fix are private to the app. Don't advertise them to the rest of the system.
%global __provides_exclude_from ^%{_libdir}/launchheim/.*$
%global __requires_exclude ^libQmlNet\\.so.*$

Name:           launchheim
Version:        %{app_version}
Release:        1%{?dist}
Summary:        Valheim mod launcher with separate modded instances
License:        MIT
URL:            https://github.com/CodeIsNotEvil/MyMonoRepo/tree/main/Applications/LaunchHeim
Source0:        launchheim-root.tar
Source1:        LICENSE
ExclusiveArch:  x86_64

# Qt comes from the distribution instead of the runtime Qml.Net would otherwise download.
Requires:       dotnet-runtime-10.0
Requires:       qt5-qtbase-gui
Requires:       qt5-qtdeclarative
Requires:       qt5-qtquickcontrols2
Requires:       qt5-qtsvg
Requires:       qt5-qtwayland
Requires:       xdg-utils
Requires:       hicolor-icon-theme

%description
LaunchHeim keeps modded Valheim setups apart. Every instance has its own BepInEx,
plugins and configs, and the game folder is never changed. Mods come from
Thunderstore, Nexus Mods and CurseForge, with dependencies and BepInEx installed
automatically.

%prep
cp %{SOURCE1} .

%build

%install
tar -xf %{SOURCE0} -C %{buildroot}

%files
%license LICENSE
%{_libdir}/launchheim/
%{_bindir}/launchheim
%{_datadir}/applications/launchheim.desktop
%{_datadir}/icons/hicolor/scalable/apps/launchheim.svg
%{_datadir}/metainfo/io.github.codeisnotevil.LaunchHeim.metainfo.xml

%changelog
* Sat Sep 26 2026 CodeIsNotEvil <noreply@github.com> - 0.1.0-1
- First package
