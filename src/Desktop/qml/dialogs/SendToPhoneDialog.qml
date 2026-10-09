import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// Sends one thing to the companion app, or to any LocalSend device: an instance as an .r2z pack (from
// its page) or Valheim's server list (from the Play page). The view model knows which.
LhDialog {
  id: dialog

  readonly property var phone: Vm.phone
  property string peerId: ""

  title: phone.sendTitle
  width: 560
  visible: phone.sendVisible
  closePolicy: phone.sending ? Popup.NoAutoClose : Popup.CloseOnEscape
  onClosed: if (phone.sendVisible) phone.closeSend()
  onOpened: peerId = ""

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
      text: dialog.phone.sendText
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    Label {
      text: "Open LaunchHeim Companion on your phone, on the same network."
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
      text: "Looking for devices… If your phone doesn't show up, check that the companion is open and that both are on the same Wi-Fi. Some networks keep devices apart (guest Wi-Fi, client isolation), and a firewall on this PC may drop the phone's answers (Settings → Phone sync)."
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
        enabled: !dialog.phone.sending && dialog.peerId.length > 0
        onClicked: dialog.phone.send(dialog.peerId)
      }
    }
  }
}
