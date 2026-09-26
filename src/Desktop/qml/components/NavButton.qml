import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

ItemDelegate {
  id: control

  property string iconName
  property bool active: false
  property string count: ""
  property color dot: "transparent"

  implicitHeight: 38
  hoverEnabled: true
  leftPadding: 12
  rightPadding: 10

  background: Rectangle {
    radius: Theme.smallRadius + 2
    color: control.active ? Theme.accentSoft : control.hovered ? Theme.hover : "transparent"

    Rectangle {
      visible: control.active
      width: 3
      height: parent.height - 16
      radius: 2
      color: Theme.accent
      anchors.left: parent.left
      anchors.verticalCenter: parent.verticalCenter
    }
  }

  contentItem: RowLayout {
    spacing: 10

    Icon {
      visible: control.iconName.length > 0
      iconName: control.iconName
      color: control.active ? Theme.accent : Theme.text
    }

    Rectangle {
      visible: control.iconName.length === 0
      width: 10
      height: 10
      radius: 3
      color: control.dot
      Layout.leftMargin: 4
      Layout.rightMargin: 4
    }

    Label {
      text: control.text
      elide: Text.ElideRight
      font.bold: control.active
      Layout.fillWidth: true
    }

    Label {
      text: control.count
      visible: text.length > 0
      color: Theme.textMuted
      font.pointSize: Theme.small
    }
  }
}
