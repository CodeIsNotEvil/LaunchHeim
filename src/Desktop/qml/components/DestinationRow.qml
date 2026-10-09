import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

// A server or world on the Play page. Play starts it with what was played there last time; without
// that (or with the pencil) the page opens the dialog to choose. The tile on the left is its icon
// (a picture, or a color and letters, set in the icon dialog), and clicking the tile changes it.
Rectangle {
  id: row

  property var destination
  signal chooseRequested()
  signal iconRequested()

  readonly property bool canPlay: !App.isGameRunning && App.steamStatus.length === 0 && Vm.settings.gameFound

  implicitHeight: 66
  radius: Theme.smallRadius + 2
  color: mouse.containsMouse ? Theme.hover : "transparent"

  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    acceptedButtons: Qt.NoButton
  }

  RowLayout {
    anchors.fill: parent
    anchors.leftMargin: 12
    anchors.rightMargin: 8
    spacing: 14

    // Without a color of its own, the quiet default: the symbol or letters in the accent.
    Avatar {
      width: 42
      height: 42
      radius: Theme.smallRadius
      plain: row.destination.customColor.length === 0
      tint: plain ? Theme.accent : row.destination.customColor
      initials: row.destination.customInitials
      glyph: row.destination.glyph
      imageSource: row.destination.imageSource

      // A pencil over the tile on hover, so it reads as something to click and not just decoration.
      Rectangle {
        anchors.fill: parent
        visible: tileMouse.containsMouse
        color: Qt.rgba(0, 0, 0, 0.45)

        Icon {
          anchors.centerIn: parent
          iconName: "edit"
          size: 16
          color: "white"
        }
      }

      MouseArea {
        id: tileMouse
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        ToolTip.visible: containsMouse
        ToolTip.text: "Change icon"
        ToolTip.delay: 500
        onClicked: row.iconRequested()
      }
    }

    // fillWidth with a zero minimum, so long server names elide instead of pushing the buttons out.
    ColumnLayout {
      spacing: 3
      Layout.fillWidth: true
      Layout.minimumWidth: 0

      RowLayout {
        spacing: 8
        Layout.fillWidth: true

        // Its natural width, shrinking towards zero only when the row is too narrow; the spacer
        // after the badges takes the rest. A filling label would share the space with the spacer.
        Label {
          text: row.destination.name
          font.bold: true
          elide: Text.ElideRight
          Layout.preferredWidth: implicitWidth
          Layout.minimumWidth: 0
        }

        Badge { visible: row.destination.isFavorite; text: "Favorite"; tint: Theme.accent }
        Badge { visible: row.destination.isRecent; text: "Recent"; tint: Theme.textMuted }

        // Who is on the server, as of the last query; the names (where a server shares them) on hover.
        Badge {
          visible: row.destination.statusText.length > 0
          text: row.destination.statusText
          tint: row.destination.isOnline ? Theme.positive : Theme.textMuted

          MouseArea {
            id: statusMouse
            anchors.fill: parent
            hoverEnabled: true
            ToolTip.visible: containsMouse
            ToolTip.text: row.destination.statusTip
            ToolTip.delay: 300
          }
        }
        Item { Layout.fillWidth: true }
      }

      RowLayout {
        spacing: 14
        Layout.fillWidth: true

        Label {
          text: row.destination.detail
          color: Theme.textMuted
          font.pointSize: Theme.small
          elide: Text.ElideRight
          Layout.preferredWidth: implicitWidth
          Layout.minimumWidth: 0
        }

        Stat {
          iconName: row.destination.hasChoice ? "check" : "info"
          text: row.destination.choiceText
        }

        Item { Layout.fillWidth: true }
      }
    }

    IconButton {
      iconName: "edit"
      tip: "Choose the character and setup"
      onClicked: row.chooseRequested()
    }

    LhButton {
      text: "Play"
      iconName: "play"
      kind: "primary"
      enabled: row.canPlay
      onClicked: row.destination.hasChoice ? row.destination.play() : row.chooseRequested()
    }
  }
}
