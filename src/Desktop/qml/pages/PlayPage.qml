import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"
import "../dialogs"

// Valheim's own servers and worlds, each started straight away with the character and setup played
// there last time (PlayViewModel).
Item {
  id: page

  PlayDialog { id: playDialog }

  function choose(destination) {
    playDialog.destination = destination
    playDialog.open()
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
        subtitle: "Join a server or load a world from Valheim's lists. LaunchHeim remembers the character and setup for each one."
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

        Label { text: "Servers"; font.pointSize: Theme.heading; font.bold: true }
        Badge { visible: Vm.play.serverCount > 0; text: Vm.play.serverCount; tint: Theme.neutral }
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
            }
          }
        }
      }
    }
  }
}
