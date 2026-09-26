import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

LhDialog {
  id: dialog

  property var instance: null

  title: "Rename instance"
  onOpened: { nameField.text = instance ? instance.name : ""; nameField.selectAll(); nameField.forceActiveFocus() }

  function save() {
    if (instance)
      instance.rename(nameField.text)
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    LhTextField {
      id: nameField
      selectByMouse: true
      Layout.fillWidth: true
      onAccepted: dialog.save()
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Rename"; kind: "primary"; enabled: nameField.text.trim().length > 0; onClicked: dialog.save() }
    }
  }
}
