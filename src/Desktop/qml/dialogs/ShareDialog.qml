import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"

// Hands an instance to a friend as a modpack file, without any integration: the file is written to the
// cache when the dialog opens, then dragged anywhere that takes a file (a chat, a file manager), copied
// to the clipboard, or shown in the file manager. No app is named: dragging and pasting work wherever
// files are accepted (the owner checked Discord and the file manager on 2026-10-09), and Steam's chat,
// the obvious other place, only takes images and videos.
LhDialog {
  id: dialog

  property var instance: null
  readonly property bool ready: instance !== null && instance.shareFile.length > 0

  title: "Share with a friend"
  width: 560
  onOpened: if (instance) instance.prepareShare()

  ColumnLayout {
    anchors.fill: parent
    spacing: 14

    Label {
      text: "Send this modpack to a friend. It holds the mod list and your configs; the mods themselves are downloaded on their side when they import it with Import modpack in LaunchHeim, or in r2modman."
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    // The file itself, as a drag source. Drag.Automatic hands the drag to the system, so it can be
    // dropped on any app; text/uri-list is the file manager format Qt turns into CF_HDROP on Windows.
    Rectangle {
      id: tile
      implicitHeight: 76
      radius: 10
      color: dragHandler.active ? Theme.cardHover : Theme.card
      border.color: Theme.alpha(Theme.accent, dialog.ready ? 0.6 : 0.2)
      border.width: 1
      opacity: dialog.ready ? 1 : 0.6
      Layout.fillWidth: true

      Drag.dragType: Drag.Automatic
      Drag.supportedActions: Qt.CopyAction
      Drag.mimeData: { "text/uri-list": dialog.ready ? dialog.instance.shareUrl + "\r\n" : "" }
      Drag.active: dragHandler.active

      // target: null keeps the tile in place; only the system drag moves.
      DragHandler { id: dragHandler; target: null; enabled: dialog.ready }
      HoverHandler { cursorShape: dialog.ready ? Qt.OpenHandCursor : Qt.ArrowCursor }

      RowLayout {
        anchors.fill: parent
        anchors.margins: 14
        spacing: 14

        Icon { iconName: "package"; color: Theme.accent; size: 28 }
        ColumnLayout {
          spacing: 2
          Layout.fillWidth: true
          Label {
            text: dialog.ready ? "Drag this into a chat or a folder" : "Preparing…"
            font.bold: true
            elide: Text.ElideRight
            Layout.fillWidth: true
          }
          Label {
            text: dialog.instance ? dialog.instance.shareSummary : ""
            color: Theme.textMuted
            font.pointSize: Theme.small
            wrapMode: Text.Wrap
            Layout.fillWidth: true
          }
        }
      }
    }

    Label {
      text: "Or copy the file and paste it where it should go. Show in folder opens it in your file manager."
      color: Theme.textMuted
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 4
      spacing: 8

      LhButton { text: "Close"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Show in folder"; iconName: "folder"; enabled: dialog.ready; onClicked: dialog.instance.showShare() }
      LhButton { text: "Copy file"; iconName: "copy"; kind: "primary"; enabled: dialog.ready; onClicked: dialog.instance.copyShare() }
    }
  }
}
