import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// A new dedicated server: a name and the instance whose mods it runs (ServersViewModel.create). The rest
// has defaults that work and is set on the server's page.
LhDialog {
  id: dialog

  title: "New server"
  onOpened: {
    nameField.text = ""
    // The selected instance is the likely one: the modpack being played with friends.
    var ids = Vm.servers.instanceChoiceIds.split("\u001f")
    instanceBox.currentIndex = Vm.selected ? Math.max(0, ids.indexOf(Vm.selected.id)) : 0
    nameField.forceActiveFocus()
  }

  function create() {
    if (nameField.text.trim().length === 0)
      return
    Vm.servers.create(nameField.text, Vm.servers.instanceChoiceIds.split("\u001f")[instanceBox.currentIndex])
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: "A dedicated server runs on this PC while you play or not. It takes its mods from an instance, so everyone joining with that instance has what the server has."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    LhTextField {
      id: nameField
      placeholderText: "Name, e.g. Friday night Valheim"
      Layout.fillWidth: true
      onAccepted: dialog.create()
    }

    Label { text: "Mods from"; font.bold: true }

    ComboBox {
      id: instanceBox
      model: Vm.servers.instanceChoices.split("\u001f")
      Layout.fillWidth: true
      Material.foreground: Theme.text
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
