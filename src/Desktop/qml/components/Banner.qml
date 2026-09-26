import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

// Kirigami-style inline message.
Rectangle {
  id: banner

  property string kind: "info"
  property alias text: message.text
  property string actionText: ""
  signal actionTriggered()

  readonly property color tint: kind === "error" ? Theme.negative : kind === "warning" ? Theme.neutral : kind === "success" ? Theme.positive : Theme.accent

  implicitHeight: row.implicitHeight + 20
  radius: Theme.smallRadius + 2
  color: Theme.alpha(tint, 0.14)
  border.width: 1
  border.color: Theme.alpha(tint, 0.45)

  RowLayout {
    id: row
    anchors.fill: parent
    anchors.margins: 10
    anchors.leftMargin: 14
    spacing: 12

    Icon {
      iconName: banner.kind === "error" || banner.kind === "warning" ? "alert" : "info"
      color: banner.tint
    }

    Label {
      id: message
      Layout.fillWidth: true
      wrapMode: Text.Wrap
    }

    LhButton {
      visible: banner.actionText.length > 0
      text: banner.actionText
      onClicked: banner.actionTriggered()
    }
  }
}
