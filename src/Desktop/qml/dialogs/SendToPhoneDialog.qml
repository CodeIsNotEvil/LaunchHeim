import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// Sends instances (as .r2z packs) and Valheim's server list to the companion app, or to any LocalSend
// device. Opened from the library (everything preselected) or an instance page (only that instance).
LhDialog {
  id: dialog

  readonly property var phone: Vm.phone
  property var selected: ({})
  property string peerId: ""
  property bool servers: true

  title: "Send to phone"
  width: 560
  visible: phone.sendVisible
  closePolicy: phone.sending ? Popup.NoAutoClose : Popup.CloseOnEscape
  onClosed: if (phone.sendVisible) phone.closeSend()

  onOpened: {
    var picked = {}
    for (var i = 0; i < Vm.instances.length; i++) {
      var id = Vm.instances[i].id
      picked[id] = phone.sendTarget.length === 0 || phone.sendTarget === id
    }
    selected = picked
    servers = phone.sendTarget.length === 0
    peerId = ""
  }

  function selectedIds() {
    var ids = []
    for (var id in selected)
      if (selected[id]) ids.push(id)
    return ids
  }

  // The first LaunchHeim phone found is picked, which is the usual case of one phone.
  Connections {
    target: Vm.phone
    function onPeersChanged() {
      if (dialog.peerId.length === 0 && Vm.peers.length > 0 && Vm.peers[0].isLaunchHeim)
        dialog.peerId = Vm.peers[0].id
    }
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 12

    Label {
      text: "Open LaunchHeim Companion on your phone, on the same network. Edited mod lists come back the same way, and LaunchHeim asks before changing an instance."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.fillWidth: true
      Label { text: "Device"; font.bold: true; Layout.fillWidth: true }
      BusyIndicator { running: Vm.peers.length === 0; visible: running; implicitWidth: 24; implicitHeight: 24 }
      LhButton { text: "Search again"; iconName: "refresh"; kind: "ghost"; onClicked: dialog.phone.scan() }
    }

    Label {
      visible: Vm.peers.length === 0
      text: "Looking for devices… If your phone doesn't show up, check that the companion is open and that both are on the same Wi-Fi. Some networks keep devices apart (guest Wi-Fi, client isolation)."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    ListView {
      visible: Vm.peers.length > 0
      model: Vm.peers
      clip: true
      interactive: contentHeight > height
      implicitHeight: Math.min(contentHeight, 168)
      Layout.fillWidth: true

      delegate: ItemDelegate {
        width: ListView.view.width
        highlighted: dialog.peerId === modelData.id
        onClicked: dialog.peerId = modelData.id

        contentItem: RowLayout {
          spacing: 12
          Icon { iconName: modelData.isPhone ? "phone" : "library"; color: modelData.isLaunchHeim ? Theme.accent : Theme.textMuted }
          ColumnLayout {
            spacing: 0
            Layout.fillWidth: true
            Label { text: modelData.alias; font.bold: true; elide: Text.ElideRight; Layout.fillWidth: true }
            Label { text: modelData.detail; color: Theme.textMuted; font.pointSize: Theme.small; elide: Text.ElideRight; Layout.fillWidth: true }
          }
          Icon { iconName: "check"; color: Theme.accent; visible: dialog.peerId === modelData.id }
        }
      }
    }

    Label { text: "Send"; font.bold: true; Layout.topMargin: 4 }

    ListView {
      model: Vm.instances
      clip: true
      interactive: contentHeight > height
      implicitHeight: Math.min(contentHeight, 200)
      Layout.fillWidth: true

      delegate: CheckBox {
        width: ListView.view.width
        text: modelData.name + "  ·  " + modelData.summary
        checked: dialog.selected[modelData.id] === true
        onToggled: {
          var copy = Object.assign({}, dialog.selected)
          copy[modelData.id] = checked
          dialog.selected = copy
        }
      }
    }

    CheckBox {
      text: "Server list from Valheim's Favorites and Recent, to see who's online"
      checked: dialog.servers
      onToggled: dialog.servers = checked
      Layout.fillWidth: true
    }

    Label {
      visible: text.length > 0
      text: dialog.phone.sendStatus
      color: dialog.phone.sending ? Theme.text : Theme.negative
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; enabled: !dialog.phone.sending; onClicked: dialog.phone.closeSend() }
      LhButton {
        text: dialog.phone.sending ? "Sending…" : "Send"
        kind: "primary"
        iconName: "export"
        enabled: !dialog.phone.sending && dialog.peerId.length > 0 && (dialog.servers || dialog.selectedIds().length > 0)
        onClicked: dialog.phone.send(dialog.peerId, dialog.selectedIds().join("\u001f"), dialog.servers)
      }
    }
  }
}
