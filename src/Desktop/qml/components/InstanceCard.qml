import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Card {
  id: card

  property var instance
  signal renameRequested()
  signal deleteRequested()

  hoverable: true
  hovered: mouse.containsMouse || playButton.hovered || menuButton.hovered
  clip: true

  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    cursorShape: Qt.PointingHandCursor
    onClicked: App.openInstance(card.instance.id)
  }

  // Color band along the top in the instance's color.
  Rectangle {
    id: band
    height: 58
    anchors { left: parent.left; right: parent.right; top: parent.top; margins: 1 }
    radius: Theme.radius
    gradient: Gradient {
      orientation: Gradient.Horizontal
      GradientStop { position: 0; color: Theme.alpha(card.instance.color, 0.55) }
      GradientStop { position: 1; color: Theme.alpha(card.instance.color, 0.08) }
    }

    Rectangle {
      anchors { left: parent.left; right: parent.right; bottom: parent.bottom }
      height: parent.radius
      color: "transparent"
    }
  }

  Avatar {
    x: 16
    y: 26
    width: 60
    height: 60
    initials: card.instance.initials
    tint: card.instance.color
    border.width: 3
    border.color: card.color
  }

  Badge {
    visible: card.instance.isRunning
    text: "Running"
    tint: Theme.positive
    solid: true
    anchors { right: parent.right; top: parent.top; margins: 12 }
  }

  ColumnLayout {
    anchors { left: parent.left; right: parent.right; bottom: parent.bottom; margins: 16 }
    spacing: 6

    Label {
      text: card.instance.name
      font.bold: true
      font.pointSize: Theme.heading
      elide: Text.ElideRight
      Layout.fillWidth: true
    }

    RowLayout {
      spacing: 12
      Stat { iconName: "package"; text: card.instance.summary }
      Stat { iconName: "clock"; text: card.instance.lastPlayedText }
    }

    RowLayout {
      Layout.topMargin: 6
      spacing: 6

      LhButton {
        id: playButton
        text: card.instance.isRunning ? "Running" : "Play"
        iconName: "play"
        kind: "primary"
        enabled: !App.isGameRunning && Vm.settings.gameFound
        onClicked: card.instance.play()
      }

      Item { Layout.fillWidth: true }

      Badge {
        visible: card.instance.updateCount > 0
        text: card.instance.updateCount + (card.instance.updateCount === 1 ? " update" : " updates")
        tint: Theme.neutral
      }

      IconButton {
        id: menuButton
        iconName: "more"
        tip: "More"
        onClicked: menu.popup()

        Menu {
          id: menu
          MenuItem { text: "Open"; onTriggered: App.openInstance(card.instance.id) }
          MenuItem { text: "Browse mods for it"; onTriggered: card.instance.browseMods() }
          MenuItem { text: "Open folder"; onTriggered: card.instance.openFolder() }
          MenuSeparator {}
          MenuItem { text: "Rename…"; onTriggered: card.renameRequested() }
          MenuItem { text: "Duplicate"; onTriggered: card.instance.duplicate() }
          MenuItem { text: "Delete…"; onTriggered: card.deleteRequested() }
        }
      }
    }
  }
}
