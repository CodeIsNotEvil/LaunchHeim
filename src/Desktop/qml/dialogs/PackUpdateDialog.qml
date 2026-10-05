import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

// An instance's mod list came back edited from the phone: what would change, before it does.
LhDialog {
  id: dialog

  readonly property var phone: Vm.phone

  title: phone.updateTitle
  width: 560
  visible: phone.updateVisible
  closePolicy: Popup.NoAutoClose

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    ScrollView {
      clip: true
      implicitHeight: Math.min(changes.implicitHeight, 320)
      Layout.fillWidth: true

      Label {
        id: changes
        width: dialog.availableWidth
        text: dialog.phone.updateText
        wrapMode: Text.Wrap
        lineHeight: 1.15
      }
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Skip"; kind: "ghost"; onClicked: dialog.phone.skipUpdate() }
      LhButton { text: "Import as a copy"; iconName: "import"; onClicked: dialog.phone.importUpdateAsCopy() }
      LhButton { text: "Update instance"; kind: "primary"; iconName: "update"; onClicked: dialog.phone.applyUpdate() }
    }
  }
}
