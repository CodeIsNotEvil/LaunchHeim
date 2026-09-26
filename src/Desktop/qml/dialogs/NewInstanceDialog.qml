import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

LhDialog {
  id: dialog

  title: "New instance"
  onOpened: { nameField.text = ""; loaderBox.checked = true; nameField.forceActiveFocus() }

  function create() {
    if (nameField.text.trim().length === 0)
      return
    App.createInstance(nameField.text, loaderBox.checked)
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: "An instance is a separate Valheim setup with its own mods and configs. Your game folder is never changed."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    LhTextField {
      id: nameField
      placeholderText: "Name, e.g. Survival with friends"
      selectByMouse: true
      Layout.fillWidth: true
      onAccepted: dialog.create()
    }

    CheckBox {
      id: loaderBox
      text: "Install BepInEx now (recommended)"
      checked: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Create"; kind: "primary"; iconName: "plus"; enabled: nameField.text.trim().length > 0; onClicked: dialog.create() }
    }
  }
}
