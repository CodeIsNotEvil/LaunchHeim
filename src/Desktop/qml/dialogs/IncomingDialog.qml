import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

// A phone (or any LocalSend device) wants to send modpacks. The sender waits for this answer.
LhDialog {
  id: dialog

  readonly property var phone: Vm.phone

  title: phone.offerTitle
  width: 480
  visible: phone.offerVisible
  closePolicy: Popup.NoAutoClose

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: dialog.phone.offerText
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    Label {
      text: "A mod list of one of your instances is shown with its changes before anything is installed. Any other pack is imported as a new instance."
      wrapMode: Text.Wrap
      color: Theme.textMuted
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Decline"; kind: "ghost"; onClicked: dialog.phone.declineOffer() }
      LhButton { text: "Accept"; kind: "primary"; iconName: "download"; onClicked: dialog.phone.acceptOffer() }
    }
  }
}
