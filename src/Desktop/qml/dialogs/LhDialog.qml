import QtQuick 2.15
import QtQuick.Controls 2.15
import "../components"

Dialog {
  id: dialog

  parent: Overlay.overlay
  anchors.centerIn: parent
  modal: true
  width: 480
  padding: 24
  topPadding: 20

  background: Rectangle {
    radius: Theme.radius + 2
    color: Theme.card
    border.width: 1
    border.color: Theme.divider
  }

  header: Label {
    text: dialog.title
    font.pointSize: Theme.heading
    font.bold: true
    leftPadding: 24
    topPadding: 22
    rightPadding: 24
    elide: Text.ElideRight
  }

  Overlay.modal: Rectangle { color: Qt.rgba(0, 0, 0, 0.45) }
}
