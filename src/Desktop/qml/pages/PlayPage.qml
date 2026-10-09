import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import Qt.labs.platform 1.1 as Platform
import "../components"
import "../dialogs"

// Valheim's own servers and worlds, each started straight away with the character and setup played
// there last time (PlayViewModel).
Item {
  id: page

  PlayDialog { id: playDialog }

  // One dialog for every row; it remembers which server or world asked.
  Platform.FileDialog {
    id: imageDialog
    property var destination: null
    title: destination ? "Choose a picture for " + destination.name : ""
    folder: Platform.StandardPaths.writableLocation(Platform.StandardPaths.PicturesLocation)
    nameFilters: ["Images (*.png *.jpg *.jpeg *.webp *.gif *.bmp)"]
    onAccepted: destination.setImage(file.toString())
  }

  // Player counts change while the page is open. StackLayout hides the pages that aren't shown, so the
  // servers are only asked while someone can see the answer.
  Timer {
    interval: 30000
    repeat: true
    running: page.visible && Vm.play.serverCount > 0
    onTriggered: Vm.play.refreshServerStatus()
  }

  function choose(destination) {
    playDialog.destination = destination
    playDialog.open()
  }

  function chooseImage(destination) {
    imageDialog.destination = destination
    imageDialog.open()
  }

  Flickable {
    id: flick
    anchors.fill: parent
    contentHeight: column.height + 64
    clip: true
    boundsBehavior: Flickable.StopAtBounds
    ScrollBar.vertical: ScrollBar {}

    ColumnLayout {
      id: column
      x: 32
      y: 32
      width: Math.max(0, flick.width - 64)
      spacing: 14

      PageHeader {
        title: "Play"
        Layout.fillWidth: true

        LhButton {
          text: "Refresh"
          iconName: "refresh"
          enabled: !Vm.play.isLoading
          onClicked: Vm.play.refresh()
        }
      }

      Banner {
        visible: !Vm.settings.gameFound
        kind: "warning"
        text: "Valheim was not found in your Steam libraries. Set its folder in Settings to play."
        actionText: "Open Settings"
        onActionTriggered: App.navigate("settings")
        Layout.fillWidth: true
      }

      Banner {
        visible: !Vm.play.isLoading && Vm.play.characterCount === 0
        kind: "info"
        text: "No Valheim characters found in Steam Cloud or " + Vm.play.dataDirectory + ". Create one in the game first."
        Layout.fillWidth: true
      }

      RowLayout {
        spacing: 10
        Layout.topMargin: 6
        Layout.fillWidth: true

        Label { text: "Servers"; font.pointSize: Theme.heading; font.bold: true }
        Badge { visible: Vm.play.serverCount > 0; text: Vm.play.serverCount; tint: Theme.neutral }
        Item { Layout.fillWidth: true }
        // The companion app shows who's online on these. An empty string means the server list.
        LhButton {
          visible: Vm.play.serverCount > 0
          text: "Send to phone"
          iconName: "phone"
          kind: "ghost"
          onClicked: Vm.phone.openSend("")
        }
      }

      Label {
        visible: Vm.play.serverCount === 0 && !Vm.play.isLoading
        text: "No servers yet. Servers in Valheim's Favorites and Recent lists show up here."
        color: Theme.textMuted
        wrapMode: Text.Wrap
        Layout.fillWidth: true
      }

      Card {
        visible: Vm.play.serverCount > 0
        Layout.fillWidth: true
        implicitHeight: serverColumn.implicitHeight + 12

        ColumnLayout {
          id: serverColumn
          anchors { left: parent.left; right: parent.right; top: parent.top; margins: 6 }
          spacing: 2

          Repeater {
            model: Vm.servers
            delegate: DestinationRow {
              destination: modelData
              Layout.fillWidth: true
              onChooseRequested: page.choose(modelData)
              onImageRequested: page.chooseImage(modelData)
            }
          }
        }
      }

      RowLayout {
        spacing: 10
        Layout.topMargin: 14

        Label { text: "Worlds"; font.pointSize: Theme.heading; font.bold: true }
        Badge { visible: Vm.play.worldCount > 0; text: Vm.play.worldCount; tint: Theme.neutral }
      }

      Label {
        visible: Vm.play.worldCount === 0 && !Vm.play.isLoading
        text: "No worlds yet. Create one in Valheim, and it shows up here."
        color: Theme.textMuted
        wrapMode: Text.Wrap
        Layout.fillWidth: true
      }

      Card {
        visible: Vm.play.worldCount > 0
        Layout.fillWidth: true
        implicitHeight: worldColumn.implicitHeight + 12

        ColumnLayout {
          id: worldColumn
          anchors { left: parent.left; right: parent.right; top: parent.top; margins: 6 }
          spacing: 2

          Repeater {
            model: Vm.worlds
            delegate: DestinationRow {
              destination: modelData
              Layout.fillWidth: true
              onChooseRequested: page.choose(modelData)
              onImageRequested: page.chooseImage(modelData)
            }
          }
        }
      }
    }
  }
}
