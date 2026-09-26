import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Column {
  spacing: 8
  width: 380

  Repeater {
    model: Vm.toasts

    delegate: Rectangle {
      id: toast

      readonly property color tint: modelData.kind === "error" ? Theme.negative : modelData.kind === "success" ? Theme.positive : Theme.accent

      width: 380
      height: toastRow.implicitHeight + 24
      radius: Theme.radius
      color: Theme.card
      border.width: 1
      border.color: Theme.alpha(tint, 0.5)

      Rectangle {
        width: 4
        radius: 2
        color: toast.tint
        anchors { left: parent.left; top: parent.top; bottom: parent.bottom; margins: 8 }
      }

      RowLayout {
        id: toastRow
        anchors.fill: parent
        anchors.margins: 12
        anchors.leftMargin: 22
        spacing: 10

        Icon {
          iconName: modelData.kind === "error" ? "alert" : modelData.kind === "success" ? "check" : "info"
          color: toast.tint
          Layout.alignment: Qt.AlignTop
        }

        ColumnLayout {
          spacing: 2
          Layout.fillWidth: true

          Label {
            text: modelData.title
            font.bold: true
            wrapMode: Text.Wrap
            Layout.fillWidth: true
          }

          Label {
            text: modelData.message
            visible: text.length > 0
            color: Theme.textMuted
            wrapMode: Text.Wrap
            Layout.fillWidth: true
          }

          LhButton {
            visible: modelData.actionLabel.length > 0
            text: modelData.actionLabel
            kind: "ghost"
            onClicked: modelData.runAction()
          }
        }

        IconButton {
          iconName: "close"
          implicitHeight: 26
          Layout.alignment: Qt.AlignTop
          onClicked: modelData.dismiss()
        }
      }
    }
  }
}
