import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "components"
import "pages"
import "dialogs"
import "windows"

ApplicationWindow {
  id: window

  visible: true
  width: 1320
  height: 820
  minimumWidth: 980
  minimumHeight: 620
  title: App.currentPage === "instance" && Vm.selected ? Vm.selected.name + " — LaunchHeim" : "LaunchHeim"
  color: Theme.window

  font.family: Theme.fontFamily
  font.pointSize: Theme.fontPointSize
  Material.theme: Theme.dark ? Material.Dark : Material.Light
  Material.accent: Theme.accent
  Material.primary: Theme.accent
  Material.background: Theme.window

  // The console is a window of its own and would otherwise keep LaunchHeim running on its own.
  onClosing: Vm.debugConsole.close()

  // Kode Mono ships with the app (qml/fonts, SIL OFL 1.1) as static Regular and Bold TTFs. Qt 5 reads
  // neither woff2 nor variable-font weights, so Scripts/kodemono_static.py cuts these out of the
  // variable font GroceryTracker uses. Once loaded they are ordinary "Kode Mono" in font.family.
  FontLoader { source: "fonts/KodeMono-Regular.ttf" }
  FontLoader { source: "fonts/KodeMono-Bold.ttf" }

  // An opaque root, so screenshots taken with LAUNCHHEIM_SCREENSHOT have the real background.
  Rectangle {
    id: root
    anchors.fill: parent
    color: Theme.window

    RowLayout {
      anchors.fill: parent
      spacing: 0

      Sidebar {
        Layout.preferredWidth: 244
        Layout.fillHeight: true
      }

      Rectangle {
        width: 1
        color: Theme.divider
        Layout.fillHeight: true
      }

      StackLayout {
        currentIndex: ["library", "instance", "browse", "settings", "play"].indexOf(App.currentPage)
        Layout.fillWidth: true
        Layout.fillHeight: true

        LibraryPage {}
        InstancePage {}
        BrowsePage {}
        SettingsPage {}
        PlayPage {}
      }
    }

    ToastArea {
      anchors.right: parent.right
      anchors.bottom: parent.bottom
      anchors.margins: 20
      z: 100
    }
  }

  BrowserPromptDialog {}
  SendToPhoneDialog {}
  IncomingDialog {}
  PackUpdateDialog {}
  UpdateDialog {}
  ConsoleWindow { id: consoleWindow }

  Connections {
    target: App
    function onActivateRequested() {
      window.show()
      window.raise()
      window.requestActivate()
    }
  }

  // Development aid: LAUNCHHEIM_SCREENSHOT=/tmp/x.png renders the window once and exits, so the UI
  // can be checked without a person at the screen. With the console open, that window is taken.
  Timer {
    running: screenshotPath.length > 0
    interval: screenshotDelay
    onTriggered: (consoleWindow.visible ? consoleWindow.screenshotRoot : root).grabToImage(function(result) {
      result.saveToFile(screenshotPath)
      Qt.quit()
    })
  }
}
