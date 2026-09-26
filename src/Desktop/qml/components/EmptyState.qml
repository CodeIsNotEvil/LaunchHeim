import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

ColumnLayout {
  property string iconName: "package"
  property alias title: titleLabel.text
  property alias text: body.text
  default property alias actions: buttons.data

  spacing: 10

  Rectangle {
    Layout.alignment: Qt.AlignHCenter
    width: 88
    height: 88
    radius: 44
    color: Theme.accentSoft

    Icon {
      anchors.centerIn: parent
      iconName: parent.parent.iconName
      size: 40
      color: Theme.accent
    }
  }

  Label {
    id: titleLabel
    horizontalAlignment: Text.AlignHCenter
    font.pointSize: Theme.heading
    font.bold: true
    Layout.fillWidth: true
  }

  Label {
    id: body
    Layout.alignment: Qt.AlignHCenter
    Layout.maximumWidth: 460
    horizontalAlignment: Text.AlignHCenter
    wrapMode: Text.Wrap
    color: Theme.textMuted
  }

  RowLayout {
    id: buttons
    Layout.alignment: Qt.AlignHCenter
    Layout.topMargin: 8
    spacing: 8
  }
}
