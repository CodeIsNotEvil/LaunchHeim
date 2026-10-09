// Gives LaunchHeim's windows its icon in the title bar and the taskbar. Qml.Net has no binding for
// QGuiApplication's window icon or desktop file name, and Qt 5's QML can't set a window icon, so this
// small library makes the two calls for C# (Hosting/AppIcon.cs). On Windows it also gives the windows
// a dark title bar in dark mode (launchheim_set_dark_frames, below). It also puts files on the
// clipboard (launchheim_copy_files, Hosting/FileClipboard.cs), which QML can't either: it only reaches
// the clipboard as text, through a TextEdit.
//
// Both icon calls are needed, because the desktops look for the icon in different places:
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

#include <QtCore/QList>
#include <QtCore/QMimeData>
#include <QtCore/QString>
#include <QtCore/QUrl>
#include <QtGui/QClipboard>
#include <QtGui/QGuiApplication>
#include <QtGui/QIcon>

// Puts files on the clipboard the way a file manager's Copy does, so they can be pasted into a chat
// (Discord, Steam) or a folder. Needs the QGuiApplication and its thread. Returns how many were put there.
// Strings are UTF-16, as for launchheim_set_app_icon.
//
// text/uri-list is what Qt, KDE and Chromium-based apps read. Qt turns it into CF_HDROP on Windows,
// which is what Explorer's Copy puts there. GNOME's file manager and GTK apps look for their own
// x-special/gnome-copied-files instead. Plain text is left out on purpose: an app that takes text first
// would paste the path rather than the file.
extern "C" Q_DECL_EXPORT int launchheim_copy_files(const QChar* const* files, int count) {
  QList<QUrl> urls;
  QByteArray gnome("copy");
  for (int i = 0; i < count; i++) {
    const QUrl url = QUrl::fromLocalFile(QString(files[i]));
    urls.append(url);
    gnome.append('\n').append(url.toEncoded());
  }

  auto* data = new QMimeData();
  data->setUrls(urls);
#ifndef Q_OS_WIN
  data->setData(QStringLiteral("x-special/gnome-copied-files"), gnome);
#endif
  // The clipboard takes ownership of the QMimeData.
  QGuiApplication::clipboard()->setMimeData(data);
  return urls.size();
}

#ifdef Q_OS_WIN
#include <QtCore/QEvent>
#include <QtCore/QObject>
#include <QtGui/QWindow>
#include <QtGui/qevent.h>
#include <windows.h>
#include <dwmapi.h>
#endif

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

#ifdef Q_OS_WIN
// Windows draws a window's title bar and its buttons itself, light unless the window asks for dark with
// DWMWA_USE_IMMERSIVE_DARK_MODE. Qt 5.15 only asks with -platform windows:darkmode=1, which follows
// Windows' setting at the time each window is created, not LaunchHeim's theme. Asking here keeps the
// frame in step with the QML theme (Theming/WindowsColorScheme.cs), which is read once at start.
namespace {
// 20 since Windows 10 20H1; 1809 to 1909 knew the same switch as 19. Older Windows has neither and
// keeps the light frame.
constexpr DWORD DarkModeAttribute = 20;
constexpr DWORD DarkModeAttributeBefore20H1 = 19;

void setDarkFrame(QWindow* window, bool dark) {
  const BOOL value = dark ? TRUE : FALSE;
  const HWND hwnd = reinterpret_cast<HWND>(window->winId());
  if (FAILED(DwmSetWindowAttribute(hwnd, DarkModeAttribute, &value, sizeof(value)))) {
    DwmSetWindowAttribute(hwnd, DarkModeAttributeBefore20H1, &value, sizeof(value));
  }
}

// Sees every window's native handle the moment it exists, before its first frame, so the title bar
// never shows light first. That covers windows QML creates later too, such as the console.
class DarkFrameFilter : public QObject {
public:
  explicit DarkFrameFilter(bool dark) : m_dark(dark) {}

  bool eventFilter(QObject* watched, QEvent* event) override {
    if (event->type() == QEvent::PlatformSurface
        && static_cast<QPlatformSurfaceEvent*>(event)->surfaceEventType() == QPlatformSurfaceEvent::SurfaceCreated) {
      if (auto* window = qobject_cast<QWindow*>(watched)) {
        setDarkFrame(window, m_dark);
      }
    }

    return false;
  }

private:
  bool m_dark;
};
}

// Needs the QGuiApplication; call it before the QML window is loaded. Windows that already exist are
// switched right away. Returns how many that were.
extern "C" Q_DECL_EXPORT int launchheim_set_dark_frames(int dark) {
  QCoreApplication::instance()->installEventFilter(new DarkFrameFilter(dark != 0));
  const auto windows = QGuiApplication::topLevelWindows();
  for (QWindow* window : windows) {
    if (window->handle()) {
      setDarkFrame(window, dark != 0);
    }
  }

  return windows.size();
}
#endif
