import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"
import "../dialogs"

// The dedicated servers LaunchHeim runs on this PC (ServersViewModel). A click on a row opens the
// server's own page; Start and Stop work right from the list.
Item {
  id: page

  NewServerDialog { id: newDialog }

  // Shown pages only, as on the Play page: a stopped server costs a file read, a running one a query.
  // Faster while one starts, so its progress moves.
  Timer {
    interval: Vm.servers.startingCount > 0 ? 1500 : 5000
    repeat: true
    running: page.visible && Vm.servers.count > 0
    onTriggered: Vm.servers.refreshStatus()
  }

  readonly property bool serverInstalled: Vm.servers.steamServerFound || Vm.servers.downloadedServerFound

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
        title: "Servers"
        Layout.fillWidth: true

        LhButton {
          text: "Refresh"
          iconName: "refresh"
          onClicked: Vm.servers.refreshStatus()
        }

        LhButton {
          text: "New server"
          iconName: "plus"
          kind: "primary"
          onClicked: newDialog.open()
        }
      }

      // Without Valheim's dedicated server nothing here can start. Two ways to get it, the Steam one first
      // because Steam then keeps it updated with the game.
      Card {
        visible: !page.serverInstalled
        Layout.fillWidth: true
        implicitHeight: installColumn.implicitHeight + 32

        ColumnLayout {
          id: installColumn
          anchors.fill: parent
          anchors.margins: 16
          spacing: 10

          RowLayout {
            spacing: 10
            Icon { iconName: "download"; color: Theme.accent }
            Label { text: "Get Valheim's dedicated server"; font.bold: true; font.pointSize: Theme.heading }
          }

          Label {
            text: "It's free and separate from the game. Steam lists it under Library → Tools as \"Valheim Dedicated Server\" and keeps it updated. Without Steam, LaunchHeim downloads it with Valve's SteamCMD, no account needed."
            wrapMode: Text.Wrap
            color: Theme.textMuted
            Layout.fillWidth: true
          }

          RowLayout {
            spacing: 8
            LhButton { text: "Install with Steam"; iconName: "external"; kind: "primary"; onClicked: Vm.servers.installWithSteam() }
            LhButton {
              text: Vm.servers.isDownloading ? "Downloading…" : "Download with LaunchHeim"
              iconName: "download"
              enabled: !Vm.servers.isDownloading
              onClicked: Vm.servers.downloadServer()
            }
          }
        }
      }

      Item {
        visible: Vm.servers.count === 0
        Layout.fillWidth: true
        Layout.preferredHeight: 320

        EmptyState {
          anchors.centerIn: parent
          width: Math.min(parent.width, 560)
          iconName: "server"
          title: "No servers yet"
          text: "Host a world for your friends right from this PC: a dedicated server with the mods of one of your instances, a console and a clean stop that saves the world."

          LhButton { text: "New server"; iconName: "plus"; kind: "primary"; onClicked: newDialog.open() }
        }
      }

      Card {
        visible: Vm.servers.count > 0
        Layout.fillWidth: true
        implicitHeight: serverColumn.implicitHeight + 12

        ColumnLayout {
          id: serverColumn
          anchors { left: parent.left; right: parent.right; top: parent.top; margins: 6 }
          spacing: 2

          Repeater {
            model: Vm.serverItems

            delegate: Rectangle {
              id: row

              readonly property var server: modelData

              Layout.fillWidth: true
              implicitHeight: 66
              radius: Theme.smallRadius + 2
              color: rowMouse.containsMouse ? Theme.hover : "transparent"

              MouseArea {
                id: rowMouse
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: Vm.servers.open(row.server.id)
              }

              RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 14
                anchors.rightMargin: 8
                spacing: 14

                Rectangle {
                  width: 42
                  height: 42
                  radius: Theme.smallRadius
                  color: Theme.accentSoft

                  Icon { anchors.centerIn: parent; iconName: "server"; color: Theme.accent }

                  // The state at a glance: green online, amber starting, none when stopped.
                  Rectangle {
                    visible: row.server.isRunning
                    width: 12
                    height: 12
                    radius: 6
                    anchors.right: parent.right
                    anchors.bottom: parent.bottom
                    anchors.margins: -2
                    border.width: 2
                    border.color: Theme.card
                    color: row.server.isOnline ? Theme.positive : Theme.neutral
                  }
                }

                ColumnLayout {
                  spacing: 3
                  Layout.fillWidth: true
                  Layout.minimumWidth: 0

                  Label {
                    text: row.server.name
                    font.bold: true
                    elide: Text.ElideRight
                    Layout.fillWidth: true
                  }

                  Label {
                    text: row.server.statusText + " · " + row.server.world + " · " + row.server.modSummary
                    color: Theme.textMuted
                    font.pointSize: Theme.small
                    elide: Text.ElideRight
                    Layout.fillWidth: true
                  }
                }

                // While it starts: how far, so nobody needs the console for that.
                ProgressBar {
                  visible: row.server.isStarting
                  from: 0
                  to: row.server.startSteps
                  value: row.server.startStep
                  Layout.preferredWidth: 120
                }

                IconButton {
                  iconName: "log"
                  tip: "Console"
                  onClicked: row.server.openConsole()
                }

                // Joins with the remembered character; the first time the server's page asks which one.
                LhButton {
                  visible: row.server.isOnline
                  text: "Join"
                  iconName: "play"
                  kind: "primary"
                  enabled: !App.isGameRunning && App.steamStatus.length === 0 && Vm.settings.gameFound
                  onClicked: {
                    if (row.server.hasJoinChoice) {
                      row.server.join()
                    } else {
                      Vm.servers.open(row.server.id)
                      Vm.servers.requestJoinDialog()
                    }
                  }
                }

                LhButton {
                  text: row.server.isRunning ? "Stop" : "Start"
                  iconName: row.server.isRunning ? "power" : "play"
                  kind: row.server.isRunning ? "ghost" : "primary"
                  enabled: !row.server.isBusy && (row.server.isRunning || row.server.serverDirectory.length > 0)
                  onClicked: row.server.isRunning ? row.server.stop() : row.server.start()
                }
              }
            }
          }
        }
      }
    }
  }
}
