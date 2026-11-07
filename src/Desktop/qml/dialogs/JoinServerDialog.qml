import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// Picks the character for joining one of LaunchHeim's own servers (ServerViewModel.joinWith). The mods
// and the password aren't choices: they're the server's, so the game always matches it. The choice is
// remembered, and the server's Join button needs no dialog next time.
LhDialog {
  id: dialog

  property var server: null
  readonly property bool canJoin: Vm.play.characterCount > 0 && !App.isGameRunning && App.steamStatus.length === 0 && Vm.settings.gameFound

  title: server ? "Join " + server.name : ""
  width: 520

  onOpened: {
    characterBox.currentIndex = server.joinCharacterIndex
    instantSwitch.checked = server.joinInstant
  }

  function join() {
    if (!canJoin)
      return
    server.joinWith(characterBox.currentIndex, instantSwitch.checked)
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 10

    Label {
      text: dialog.server
        ? "Valheim starts with " + dialog.server.instanceName + (dialog.server.instanceId.length > 0 ? ", the server's mods," : "") + " and joins " + dialog.server.name + " at " + dialog.server.joinAddress + (dialog.server.password.length > 0 ? " with its password." : ".")
        : ""
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
      Layout.bottomMargin: 4
    }

    Label { text: "Character"; font.bold: true }

    ComboBox {
      id: characterBox
      model: Vm.play.characterCount > 0 ? Vm.play.characterNames.split("\u001f") : []
      enabled: count > 0
      displayText: count > 0 ? currentText : "No characters found"
      Layout.fillWidth: true
      Material.foreground: Theme.text
    }

    LhSwitch {
      id: instantSwitch
      text: "Start instantly (skip Valheim's menus)"
      Layout.topMargin: 4
    }

    Label {
      text: "LaunchHeim remembers the character, so the next Join is one click. Change it here any time."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      font.pointSize: Theme.small
      Layout.fillWidth: true
      Layout.topMargin: 6
    }

    Label {
      visible: Vm.play.characterCount === 0
      text: "No Valheim characters found. Create one in the game first."
      color: Theme.negative
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Join"; kind: "primary"; iconName: "play"; enabled: dialog.canJoin; onClicked: dialog.join() }
    }
  }
}
