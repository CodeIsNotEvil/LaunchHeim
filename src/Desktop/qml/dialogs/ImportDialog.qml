import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

LhDialog {
  id: dialog

  title: "Import from the game folder"
  onOpened: { nameField.text = "My current mods"; nameField.selectAll(); nameField.forceActiveFocus() }

  function importNow() {
    App.importGameFolder(nameField.text)
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: "BepInEx is installed directly in your Valheim folder. LaunchHeim copies BepInEx, your plugins and all configs into a new instance. The game folder itself is left as it is."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    Label {
      text: Vm.settings.gameDirectory
      font.family: "monospace"
      font.pointSize: Theme.small
      elide: Text.ElideMiddle
      Layout.fillWidth: true
    }

    LhTextField {
      id: nameField
      placeholderText: "Instance name"
      selectByMouse: true
      Layout.fillWidth: true
      onAccepted: dialog.importNow()
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Import"; kind: "primary"; iconName: "import"; enabled: nameField.text.trim().length > 0; onClicked: dialog.importNow() }
    }
  }
}
