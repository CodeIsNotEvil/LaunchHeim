import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

// A newer LaunchHeim: the download page, the commands that update this kind of install, and the two
// ways to stop being told (this version, or ever). A copy the Windows setup installed gets "Update now",
// which downloads and starts the new setup, then closes LaunchHeim so its files can be replaced.
LhDialog {
  id: dialog

  readonly property var updates: Vm.updates

  title: updates.text
  width: 640
  visible: updates.dialogVisible
  onClosed: if (updates.dialogVisible) updates.closeDialog()

  function copyCommands() {
    clipboard.text = dialog.updates.commands
    clipboard.selectAll()
    clipboard.copy()
    clipboard.text = ""
    copied.visible = true
  }

  // TextEdit is the only way QML 2 reaches the clipboard (the console window does the same).
  TextEdit { id: clipboard; visible: false }

  Connections {
    target: dialog.updates
    function onQuitRequested() { Qt.quit() }
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 12

    Label {
      text: dialog.updates.canInstall
        ? "You have " + App.version + ". Update now to download and install the new version; LaunchHeim "
          + "closes and starts again when it's done. Your instances and settings are kept."
        : "You have " + App.version + ". Do you want to open the download page to get the new version? "
          + "Your instances and settings are kept when you update."
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    Label {
      text: "<a href=\"notes\">What's new in " + dialog.updates.version + "</a>"
      textFormat: Text.StyledText
      linkColor: Theme.link
      onLinkActivated: dialog.updates.openReleaseNotes()
    }

    // The commands for this system, or only the link when it couldn't be told which one this is.
    ColumnLayout {
      visible: dialog.updates.hasCommands
      spacing: 6
      Layout.fillWidth: true
      Layout.topMargin: 4

      RowLayout {
        Layout.fillWidth: true
        Label { text: "Or update from a terminal on " + dialog.updates.systemName + ":"; font.bold: true; Layout.fillWidth: true }
        Label { id: copied; text: "Copied"; color: Theme.positive; visible: false; font.pointSize: Theme.small }
        LhButton { text: "Copy"; iconName: "copy"; kind: "ghost"; onClicked: dialog.copyCommands() }
      }

      Rectangle {
        color: Theme.window
        radius: Theme.radius
        border.width: 1
        border.color: Theme.divider
        implicitHeight: commandText.implicitHeight + 20
        Layout.fillWidth: true

        TextEdit {
          id: commandText
          anchors.fill: parent
          anchors.margins: 10
          text: dialog.updates.commands
          readOnly: true
          selectByMouse: true
          wrapMode: TextEdit.WrapAnywhere
          color: Theme.text
          selectionColor: Theme.accent
          font.family: Theme.fontFamily
          font.pointSize: Theme.small
        }
      }
    }

    Label {
      visible: !dialog.updates.hasCommands
      text: "Download it from <a href=\"" + dialog.updates.downloadUrl + "\">" + dialog.updates.downloadUrl + "</a>"
      textFormat: Text.StyledText
      linkColor: Theme.link
      wrapMode: Text.WrapAnywhere
      onLinkActivated: dialog.updates.openDownloadPage()
      Layout.fillWidth: true
    }

    // Downloading the setup, then why it failed if it did.
    ProgressBar {
      visible: dialog.updates.installing
      from: 0
      to: 1
      value: dialog.updates.installProgress
      Layout.fillWidth: true
    }

    Label {
      visible: dialog.updates.installError.length > 0
      text: dialog.updates.installError
      color: Theme.negative
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    // Two rows: four buttons side by side are wider than the dialog, and a RowLayout then pushes the
    // whole dialog wider instead of shrinking.
    RowLayout {
      Layout.topMargin: 6
      spacing: 8

      LhButton { text: "Skip this version"; kind: "ghost"; onClicked: dialog.updates.skip() }
      LhButton { text: "Don't remind me again"; kind: "ghost"; onClicked: dialog.updates.never() }
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      spacing: 8

      LhButton { text: "Not now"; kind: "ghost"; onClicked: dialog.updates.closeDialog() }
      LhButton {
        text: "Open download page"
        kind: dialog.updates.canInstall ? "ghost" : "primary"
        iconName: "external"
        onClicked: dialog.updates.openDownloadPage()
      }
      LhButton {
        visible: dialog.updates.canInstall
        enabled: !dialog.updates.installing
        text: dialog.updates.installing ? "Downloading…" : "Update now"
        kind: "primary"
        iconName: "download"
        onClicked: dialog.updates.installNow()
      }
    }
  }
}
