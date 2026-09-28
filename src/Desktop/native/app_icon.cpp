// Gives LaunchHeim's windows its icon in the title bar and the taskbar. Qml.Net has no binding for
// QGuiApplication's window icon or desktop file name, and Qt 5's QML can't set a window icon, so this
// small library makes the two calls for C# (Hosting/AppIcon.cs).
//
// Both are needed, because the desktops look for the icon in different places:
// - The window icon is what X11 (_NET_WM_ICON) and Windows (WM_SETICON) show. Without it Windows shows
//   its stock application icon: Qt only looks for an icon resource named IDI_ICON1 in the exe, and the
//   .NET SDK embeds <ApplicationIcon> under a number, so Explorer shows the logo but the window doesn't.
// - Wayland in Qt 5 has no way to hand an icon to the compositor. KWin and the task manager look up the
//   desktop entry named by the window's app_id, which Qt takes from the desktop file name. Left unset,
//   Qt makes one up from the organization domain and the binary (local.cine.LaunchHeim), which matches
//   no desktop entry, so Plasma shows the generic Wayland icon.
//
// It is built on Linux by the csproj (against the system's Qt 5 headers, like signal_fix.cpp) and on
// Windows by packaging/windows/build.ps1 (against the Qt that ships next to LaunchHeim.exe). It links
// Qt but not Qml.Net: by the time C# loads it, Qt is already loaded, from the system or the downloaded
// runtime, and the loader reuses that copy.

#include <QtCore/QString>
#include <QtGui/QGuiApplication>
#include <QtGui/QIcon>

// Needs the QGuiApplication, and has to run before the window is created: Wayland reads the app_id
// once, when the window first appears. Returns how many icon sizes were loaded.
// Strings are UTF-16 (C#'s own encoding and QString's), because .NET can't marshal an array of UTF-8 strings.
extern "C" Q_DECL_EXPORT int launchheim_set_app_icon(const QChar* desktopFileName, const QChar* const* iconFiles, int count) {
  QGuiApplication::setDesktopFileName(QString(desktopFileName));

  // One PNG per size, each rendered from the SVG. Qt picks the closest size for each use (16 px in the
  // title bar, larger in the taskbar and Alt+Tab) instead of scaling one big image down. PNG is built
  // into QtGui, while the SVG and ICO readers are plugins that a Qt install may lack.
  QIcon icon;
  for (int i = 0; i < count; i++) {
    icon.addFile(QString(iconFiles[i]));
  }

  QGuiApplication::setWindowIcon(icon);
  return icon.availableSizes().size();
}
