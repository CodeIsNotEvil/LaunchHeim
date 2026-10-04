import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// Picks the character and setup for one server or world. Whatever is played here is remembered for
// it (PlayChoice), so the Play button on the row needs no dialog next time.
LhDialog {
  id: dialog

  property var destination: null
  readonly property bool isServer: destination !== null && destination.kind === "server"
  readonly property bool canPlay: Vm.play.characterCount > 0 && !App.isGameRunning && App.steamStatus.length === 0 && Vm.settings.gameFound

  title: destination ? destination.name : ""
  width: 520

  onOpened: {
    characterBox.currentIndex = destination.characterIndex
    setupBox.currentIndex = destination.setupIndex
    passwordField.text = destination.password
  }

  function play() {
    if (!canPlay)
      return
    destination.playWith(characterBox.currentIndex, setupBox.currentIndex, passwordField.text)
    dialog.close()
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 10

    // What Valheim can do differs: +connect joins from the character selection, worlds have no such option.
    Label {
      text: dialog.isServer
        ? "Valheim opens at the character selection with this character picked. Start joins " + (dialog.destination ? dialog.destination.address : "") + "."
        : "Valheim can't load a world by itself, so it opens with this character and world already selected. Click Start until you're in."
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
      // Qt 5.15's Material ComboBox reports a foreground binding loop without this (see BrowsePage).
      Material.foreground: Theme.text
    }

    Label { text: "Play with"; font.bold: true; Layout.topMargin: 4 }

    ComboBox {
      id: setupBox
      model: Vm.play.setupNames.split("\u001f")
      Layout.fillWidth: true
      Material.foreground: Theme.text
    }

    Label { text: "Server password"; font.bold: true; visible: dialog.isServer; Layout.topMargin: 4 }

    RowLayout {
      visible: dialog.isServer
      spacing: 8
      Layout.fillWidth: true

      LhTextField {
        id: passwordField
        echoMode: reveal.checked ? TextInput.Normal : TextInput.Password
        placeholderText: "Empty if the server has none"
        selectByMouse: true
        Layout.fillWidth: true
        onAccepted: dialog.play()
      }

      IconButton {
        id: reveal
        checkable: true
        iconName: "key"
        tip: checked ? "Hide password" : "Show password"
      }
    }

    Label {
      text: dialog.isServer
        ? "LaunchHeim remembers the character, setup and password for this server."
        : "LaunchHeim remembers the character and setup for this world."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      font.pointSize: Theme.small
      Layout.fillWidth: true
      Layout.topMargin: 6
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Play"; kind: "primary"; iconName: "play"; enabled: dialog.canPlay; onClicked: dialog.play() }
    }
  }
}
