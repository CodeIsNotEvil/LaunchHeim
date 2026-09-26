import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Rectangle {
  id: row

  property var mod

  implicitHeight: 66
  radius: Theme.smallRadius + 2
  color: mouse.containsMouse ? Theme.hover : "transparent"

  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    acceptedButtons: Qt.NoButton
  }

  RowLayout {
    anchors.fill: parent
    anchors.leftMargin: 12
    anchors.rightMargin: 8
    spacing: 14
    opacity: row.mod.enabled || row.mod.isLoader ? 1 : 0.5

    Avatar {
      width: 42
      height: 42
      initials: row.mod.initial
      tint: Theme.sourceColor(row.mod.source)
      imageSource: row.mod.iconSource
    }

    ColumnLayout {
      spacing: 3
      Layout.fillWidth: true

      ElidedTitle {
        text: row.mod.name
        font.bold: true
        Layout.fillWidth: true

        Label { text: row.mod.version; color: Theme.textMuted; visible: text.length > 0 }
        Badge { visible: row.mod.isLoader; text: "Mod loader"; tint: Theme.accent }
        Badge { visible: row.mod.isDependency && !row.mod.isLoader; text: "Dependency"; tint: Theme.textMuted }
        Badge { visible: row.mod.hasUpdate; text: "Update " + row.mod.updateVersion; tint: Theme.neutral }
      }

      RowLayout {
        spacing: 10
        Layout.fillWidth: true

        Rectangle { width: 8; height: 8; radius: 4; color: Theme.sourceColor(row.mod.source) }

        Label {
          text: row.mod.sourceLabel + (row.mod.author.length > 0 ? " · by " + row.mod.author : "")
          color: Theme.textMuted
          font.pointSize: Theme.small
        }

        Label {
          text: row.mod.requiredByText
          visible: text.length > 0
          color: Theme.textMuted
          font.pointSize: Theme.small
          font.italic: true
          elide: Text.ElideRight
          Layout.fillWidth: true
        }
      }
    }

    LhButton {
      visible: row.mod.hasUpdate
      text: "Update"
      iconName: "update"
      onClicked: row.mod.update()
    }

    LhSwitch {
      visible: !row.mod.isLoader
      checked: row.mod.enabled
      onToggled: row.mod.setEnabled(checked)
      ToolTip.visible: hovered
      ToolTip.text: checked ? "Enabled. BepInEx loads this mod." : "Disabled. Its files stay but are not loaded."
      ToolTip.delay: 600
    }

    IconButton {
      iconName: "more"
      tip: "More"
      onClicked: menu.popup()

      Menu {
        id: menu
        MenuItem { text: "Open website"; enabled: row.mod.websiteUrl.length > 0; onTriggered: row.mod.openWebsite() }
        MenuItem { text: "Update to " + row.mod.updateVersion; visible: row.mod.hasUpdate; height: visible ? implicitHeight : 0; onTriggered: row.mod.update() }
        MenuSeparator {}
        MenuItem { text: "Remove"; onTriggered: row.mod.remove() }
      }
    }
  }
}
