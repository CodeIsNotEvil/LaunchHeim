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
# LaunchHeim is MIT; the rest is what it bundles (see THIRD-PARTY-NOTICES.txt).
License:        MIT AND LGPL-3.0-or-later AND BSD-2-Clause AND OFL-1.1
URL:            https://github.com/CodeIsNotEvil/MyMonoRepo/tree/main/Applications/LaunchHeim
Source0:        launchheim-root.tar
Source1:        LICENSE
Source2:        THIRD-PARTY-NOTICES.txt
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
cp %{SOURCE1} %{SOURCE2} .

%build

%install
tar -xf %{SOURCE0} -C %{buildroot}

%files
%license LICENSE THIRD-PARTY-NOTICES.txt
%{_libdir}/launchheim/
%{_bindir}/launchheim
%{_datadir}/applications/launchheim.desktop
%{_datadir}/icons/hicolor/scalable/apps/launchheim.svg
%{_datadir}/metainfo/io.github.codeisnotevil.LaunchHeim.metainfo.xml
# Neither firewall is required, so the directories are owned here too, as Fedora asks.
%dir %{_sysconfdir}/ufw
%dir %{_sysconfdir}/ufw/applications.d
%config(noreplace) %{_sysconfdir}/ufw/applications.d/launchheim
%dir %{_prefix}/lib/firewalld
%dir %{_prefix}/lib/firewalld/services
%{_prefix}/lib/firewalld/services/launchheim.xml

%changelog
* Fri Oct 09 2026 CodeIsNotEvil <noreply@github.com> - 0.6.0-1
- Instance, server and world icons, sharing with friends, drag-and-drop installs, firewall rules for phone sync (see CHANGELOG.md)

* Thu Oct 08 2026 CodeIsNotEvil <noreply@github.com> - 0.5.4-1
- Steam overlay in launched games, a Screenshots page, dark title bar on Windows (see CHANGELOG.md)

* Thu Oct 08 2026 CodeIsNotEvil <noreply@github.com> - 0.5.3-1
- Signed Windows build (see CHANGELOG.md)

* Tue Oct 06 2026 CodeIsNotEvil <noreply@github.com> - 0.5.2-1
- Update reminder, and Steam detection fixed on Windows (see CHANGELOG.md)

* Mon Oct 05 2026 CodeIsNotEvil <noreply@github.com> - 0.5.1-1
- Fix a crash when sending only the server list to a phone (see CHANGELOG.md)

* Mon Oct 05 2026 CodeIsNotEvil <noreply@github.com> - 0.5.0-1
- Phone sync with the LaunchHeim Companion Android app over LocalSend (see CHANGELOG.md)

* Sun Oct 04 2026 CodeIsNotEvil <noreply@github.com> - 0.4.0-1
- A Play page that joins servers and opens worlds with a remembered character (see CHANGELOG.md)

* Wed Sep 30 2026 CodeIsNotEvil <noreply@github.com> - 0.3.0-1
- Modpack export and import, and a console window for the game's logs (see CHANGELOG.md)

* Wed Sep 30 2026 CodeIsNotEvil <noreply@github.com> - 0.2.0-1
- Starts Steam before Valheim; ships third-party license notices (see CHANGELOG.md)

* Sat Sep 26 2026 CodeIsNotEvil <noreply@github.com> - 0.1.0-1
- First package
