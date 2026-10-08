import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"
import "../dialogs"

// The screenshots Steam's overlay saved in Valheim (ScreenshotsViewModel), newest first. A click opens
// the large preview, which also leads to the file manager and the picture viewer.
Item {
  id: page

  ScreenshotViewer { id: viewer }

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 32
    spacing: 14

    PageHeader {
      title: "Screenshots"
      subtitle: Vm.screenshots.count > 0
        ? Vm.screenshots.count + (Vm.screenshots.count === 1 ? " screenshot" : " screenshots") + " taken with Steam (F12 in the game). Steam keeps them in " + Vm.screenshots.folder
        : "Pictures you take with Steam while playing Valheim (F12 by default) show up here."
      Layout.fillWidth: true

      LhButton {
        text: "Open folder"
        iconName: "folder"
        enabled: Vm.screenshots.folder.length > 0
        onClicked: Vm.screenshots.openFolder()
      }

      LhButton {
        text: "Refresh"
        iconName: "refresh"
        enabled: !Vm.screenshots.isLoading
        onClicked: Vm.screenshots.refresh()
      }
    }

    Item {
      visible: Vm.screenshots.count === 0 && !Vm.screenshots.isLoading
      Layout.fillWidth: true
      Layout.fillHeight: true

      EmptyState {
        anchors.centerIn: parent
        width: Math.min(parent.width, 520)
        iconName: "image"
        title: "No screenshots yet"
        text: "Press F12 while playing to take one with Steam's overlay. They show up here when you are back from the game."
      }
    }

    GridView {
      id: grid

      // As many columns of at least 280 pixels as fit; the cards share the width that is left.
      readonly property int columns: Math.max(1, Math.floor(width / 280))

      visible: Vm.screenshots.count > 0
      clip: true
      model: Vm.screenshotItems
      cellWidth: Math.floor(width / columns)
      cellHeight: Math.round((cellWidth - 24) * 9 / 16) + 52
      boundsBehavior: Flickable.StopAtBounds
      ScrollBar.vertical: ScrollBar {}
      Layout.fillWidth: true
      Layout.fillHeight: true

      delegate: Item {
        width: grid.cellWidth
        height: grid.cellHeight

        Card {
          anchors.fill: parent
          anchors.margins: 6
          hoverable: true
          hovered: mouse.containsMouse

          // Steam's 200-pixel thumbnail first, so the grid fills at once; the sharper copy replaces it.
          Image {
            id: thumbnail
            x: 6
            y: 6
            width: parent.width - 12
            height: Math.round(width * 9 / 16)
            source: modelData.thumbnailUrl
            visible: picture.status !== Image.Ready
            fillMode: Image.PreserveAspectCrop
            asynchronous: true
          }

          // Scaled down while decoding (sourceSize), so a page of 1440p screenshots stays light.
          Image {
            id: picture
            anchors.fill: thumbnail
            source: modelData.url
            sourceSize.width: 640
            fillMode: Image.PreserveAspectCrop
            asynchronous: true
            clip: true
          }

          RowLayout {
            anchors { left: parent.left; right: parent.right; bottom: parent.bottom; margins: 10 }
            spacing: 8

            Label {
              text: modelData.taken
              font.bold: true
              elide: Text.ElideRight
              Layout.fillWidth: true
            }

            Label {
              text: modelData.ago
              color: Theme.textMuted
              font.pointSize: Theme.small
            }
          }

          MouseArea {
            id: mouse
            anchors.fill: parent
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: viewer.show(index)
          }
        }
      }
    }
  }
}
