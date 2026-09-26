import QtQuick 2.15
import QtQuick.Controls 2.15

Row {
  property string iconName
  property alias text: label.text

  spacing: 4

  Icon {
    iconName: parent.iconName
    size: 14
    color: Theme.textMuted
    anchors.verticalCenter: parent.verticalCenter
  }

  Label {
    id: label
    color: Theme.textMuted
    font.pointSize: Theme.small
    anchors.verticalCenter: parent.verticalCenter
  }
}
