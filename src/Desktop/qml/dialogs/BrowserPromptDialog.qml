import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

LhDialog {
  id: dialog

  readonly property var prompt: Vm.prompt

  title: prompt.title
  width: 540
  visible: prompt.visible
  onClosed: prompt.close()

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: dialog.prompt.reason
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    Banner {
      visible: dialog.prompt.waitsForNxm
      kind: Vm.settings.nxmRegistered ? "info" : "warning"
      text: Vm.settings.nxmRegistered
        ? "Nexus hands the download back to LaunchHeim, which installs it into " + Vm.browse.targetName + "."
        : "LaunchHeim is not yet registered for Nexus links, so the browser would not hand the download back."
      actionText: Vm.settings.nxmRegistered ? "" : "Register now"
      onActionTriggered: Vm.settings.registerNxmHandler()
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.prompt.close() }
      LhButton { text: "Open in browser"; kind: "primary"; iconName: "external"; onClicked: dialog.prompt.open() }
    }
  }
}
