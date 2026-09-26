import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Card {
  id: section

  property string iconName
  property string title
  property string description
  default property alias content: body.data

  implicitHeight: layout.implicitHeight + 44

  RowLayout {
    id: layout
    anchors.fill: parent
    anchors.margins: 22
    spacing: 18

    Rectangle {
      width: 40
      height: 40
      radius: 12
      color: Theme.accentSoft
      Layout.alignment: Qt.AlignTop

      Icon { anchors.centerIn: parent; iconName: section.iconName; color: Theme.accent }
    }

    ColumnLayout {
      id: body
      spacing: 10
      Layout.fillWidth: true

      Label { text: section.title; font.bold: true; font.pointSize: Theme.heading; Layout.fillWidth: true }
      Label {
        text: section.description
        visible: text.length > 0
        color: Theme.textMuted
        wrapMode: Text.Wrap
        textFormat: Text.StyledText
        linkColor: Theme.link
        onLinkActivated: App.openUrl(link)
        Layout.fillWidth: true
      }
    }
  }
}
