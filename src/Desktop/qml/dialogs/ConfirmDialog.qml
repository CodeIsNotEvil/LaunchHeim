import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

LhDialog {
  id: dialog

  property alias text: body.text
  property string confirmText: "Delete"
  signal confirmed()

  ColumnLayout {
    anchors.fill: parent
    spacing: 18

    Label {
      id: body
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: dialog.confirmText; kind: "danger"; iconName: "trash"; onClicked: { dialog.confirmed(); dialog.close() } }
    }
  }
}
