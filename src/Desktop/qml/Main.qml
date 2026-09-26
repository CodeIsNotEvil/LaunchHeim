import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "components"
import "pages"
import "dialogs"

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
        currentIndex: ["library", "instance", "browse", "settings"].indexOf(App.currentPage)
        Layout.fillWidth: true
        Layout.fillHeight: true

        LibraryPage {}
        InstancePage {}
        BrowsePage {}
        SettingsPage {}
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

  Connections {
    target: App
    function onActivateRequested() {
      window.show()
      window.raise()
      window.requestActivate()
    }
  }

  // Development aid: LAUNCHHEIM_SCREENSHOT=/tmp/x.png renders the window once and exits, so the UI
  // can be checked without a person at the screen.
  Timer {
    running: screenshotPath.length > 0
    interval: screenshotDelay
    onTriggered: root.grabToImage(function(result) {
      result.saveToFile(screenshotPath)
      Qt.quit()
    })
  }
}
