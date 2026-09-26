import QtQuick 2.15
import QtQuick.Controls 2.15

// Segmented control for the three mod sites, each marked with its brand color.
Rectangle {
  id: switcher

  property string current: "thunderstore"
  signal selected(string source)

  implicitWidth: row.implicitWidth + 8
  implicitHeight: 38
  radius: Theme.smallRadius + 4
  color: Theme.view
  border.width: 1
  border.color: Theme.divider

  Row {
    id: row
    anchors.centerIn: parent
    spacing: 2

    Repeater {
      model: ["thunderstore", "nexus", "curseforge"]

      delegate: AbstractButton {
        id: segment

        readonly property bool active: switcher.current === modelData

        implicitHeight: 30
        implicitWidth: segmentRow.implicitWidth + 26
        hoverEnabled: true
        onClicked: switcher.selected(modelData)

        background: Rectangle {
          radius: Theme.smallRadius + 2
          color: segment.active ? Theme.alpha(Theme.sourceColor(modelData), 0.22) : segment.hovered ? Theme.hover : "transparent"
          border.width: segment.active ? 1 : 0
          border.color: Theme.alpha(Theme.sourceColor(modelData), 0.6)
        }

        contentItem: Row {
          id: segmentRow
          spacing: 8
          anchors.centerIn: parent

          Rectangle {
            width: 8
            height: 8
            radius: 4
            color: Theme.sourceColor(modelData)
            anchors.verticalCenter: parent.verticalCenter
          }

          Text {
            text: Theme.sourceName(modelData)
            color: Theme.text
            font.bold: segment.active
            font.family: Theme.fontFamily
            font.pointSize: Theme.fontPointSize
            anchors.verticalCenter: parent.verticalCenter
          }
        }
      }
    }
  }
}
