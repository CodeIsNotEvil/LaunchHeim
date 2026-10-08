import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

// One screenshot as large as the window allows, with the ones before and after it a key or click away
// (left/right arrows, Escape closes). The file manager and the picture viewer are one click from here.
Popup {
  id: viewer

  property int index: -1
  readonly property int count: Vm.screenshotItems.length
  // A refresh may shorten the list under an open viewer; null then empties it rather than failing.
  readonly property var shot: index >= 0 && index < count ? Vm.screenshotItems[index] : null

  function show(i) {
    index = i
    open()
  }

  function step(delta) {
    index = Math.max(0, Math.min(count - 1, index + delta))
  }

  parent: Overlay.overlay
  x: 0
  y: 0
  width: parent ? parent.width : 0
  height: parent ? parent.height : 0
  modal: true
  padding: 24
  closePolicy: Popup.CloseOnEscape
  onOpened: keys.forceActiveFocus()

  background: Rectangle { color: Theme.alpha(Theme.window, 0.97) }
  Overlay.modal: Rectangle { color: "transparent" }

  contentItem: Item {
    id: keys
    focus: true
    Keys.onLeftPressed: viewer.step(-1)
    Keys.onRightPressed: viewer.step(1)

    ColumnLayout {
      anchors.fill: parent
      spacing: 14

      RowLayout {
        spacing: 8
        Layout.fillWidth: true

        ColumnLayout {
          spacing: 2
          Layout.fillWidth: true

          Label {
            text: viewer.shot ? viewer.shot.taken : ""
            font.pointSize: Theme.heading
            font.bold: true
            elide: Text.ElideRight
            Layout.fillWidth: true
          }

          Label {
            // The size is known once the full picture is decoded.
            text: (viewer.shot ? viewer.shot.fileName : "")
              + (full.status === Image.Ready ? " · " + full.implicitWidth + " × " + full.implicitHeight : "")
              + (viewer.count > 1 ? " · " + (viewer.index + 1) + " of " + viewer.count : "")
            color: Theme.textMuted
            elide: Text.ElideRight
            Layout.fillWidth: true
          }
        }

        LhButton {
          text: "Show in folder"
          iconName: "folder"
          enabled: viewer.shot !== null
          onClicked: viewer.shot.showInFolder()
        }

        LhButton {
          text: "Open"
          iconName: "external"
          enabled: viewer.shot !== null
          onClicked: viewer.shot.open()
        }

        IconButton {
          iconName: "close"
          tip: "Close (Esc)"
          onClicked: viewer.close()
        }
      }

      // The arrows get columns of their own beside the picture, so they never cover any of it.
      RowLayout {
        spacing: 12
        Layout.fillWidth: true
        Layout.fillHeight: true

        IconButton {
          iconName: "previous"
          tip: "Newer (←)"
          kind: "secondary"
          large: true
          enabled: viewer.index > 0
          onClicked: viewer.step(-1)
        }

        Item {
          Layout.fillWidth: true
          Layout.fillHeight: true

          // The thumbnail stands in while the full picture loads, so stepping through never flashes empty.
          Image {
            anchors.fill: parent
            source: viewer.shot ? viewer.shot.thumbnailUrl : ""
            visible: full.status !== Image.Ready
            fillMode: Image.PreserveAspectFit
            asynchronous: true
          }

          // Full size, scaled to fit with mipmaps, so it looks right at any window size and stays sharp
          // when the window grows.
          Image {
            id: full
            anchors.fill: parent
            source: viewer.visible && viewer.shot ? viewer.shot.url : ""
            fillMode: Image.PreserveAspectFit
            asynchronous: true
            mipmap: true
            smooth: true
          }

          BusyIndicator {
            anchors.centerIn: parent
            running: full.status === Image.Loading
            visible: running
          }

          // Opens the picture viewer, like double-clicking it in the file manager.
          MouseArea {
            anchors.fill: parent
            onDoubleClicked: if (viewer.shot) viewer.shot.open()
          }
        }

        IconButton {
          iconName: "next"
          tip: "Older (→)"
          kind: "secondary"
          large: true
          enabled: viewer.index < viewer.count - 1
          onClicked: viewer.step(1)
        }
      }
    }
  }
}
