import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import "../components"
import "../dialogs"

Item {
  id: page

  NewInstanceDialog { id: newDialog }
  ImportDialog { id: importDialog }
  RenameDialog { id: renameDialog }
  ConfirmDialog {
    id: deleteDialog
    property var instance: null
    title: "Delete instance?"
    text: instance ? "“" + instance.name + "” and all its mods and configs will be removed from disk. This cannot be undone." : ""
    onConfirmed: instance.delete()
  }

  Flickable {
    id: flick
    anchors.fill: parent
    contentHeight: column.height + 64
    clip: true
    boundsBehavior: Flickable.StopAtBounds
    ScrollBar.vertical: ScrollBar {}

    ColumnLayout {
      id: column
      x: 32
      y: 32
      width: Math.max(0, flick.width - 64)
      spacing: 20

      PageHeader {
        title: "Library"
        subtitle: "Your modded Valheim setups. Each one has its own BepInEx, mods and configs."
        Layout.fillWidth: true

        LhButton {
          text: "Import game folder"
          iconName: "import"
          visible: Vm.settings.gameFolderHasBepInEx
          onClicked: importDialog.open()
        }

        LhButton {
          text: "New instance"
          iconName: "plus"
          kind: "primary"
          onClicked: newDialog.open()
        }
      }

      Banner {
        visible: !Vm.settings.gameFound
        kind: "warning"
        text: "Valheim was not found in your Steam libraries. Set its folder in Settings to play."
        actionText: "Open Settings"
        onActionTriggered: App.navigate("settings")
        Layout.fillWidth: true
      }

      // "Continue playing" hero for the instance played last.
      Card {
        id: hero

        readonly property var instance: Vm.recent

        visible: instance !== null && instance !== undefined
        Layout.fillWidth: true
        implicitHeight: heroRow.implicitHeight + 56
        clip: true

        Rectangle {
          anchors.fill: parent
          anchors.margins: 1
          radius: Theme.radius
          gradient: Gradient {
            orientation: Gradient.Horizontal
            GradientStop { position: 0; color: hero.instance ? Theme.alpha(hero.instance.color, 0.42) : "transparent" }
            GradientStop { position: 0.7; color: "transparent" }
          }
        }

        // A large, faint Hagalaz rune (the "H" in LaunchHeim) as decoration.
        Icon {
          iconName: "rune"
          size: 240
          opacity: 0.05
          anchors.right: parent.right
          anchors.rightMargin: 10
          anchors.verticalCenter: parent.verticalCenter
        }

        RowLayout {
          id: heroRow
          anchors.fill: parent
          anchors.margins: 28
          spacing: 24

          Avatar {
            width: 112
            height: 112
            initials: hero.instance ? hero.instance.initials : ""
            tint: hero.instance ? hero.instance.color : Theme.accent
          }

          ColumnLayout {
            spacing: 6
            Layout.fillWidth: true

            Label {
              text: "CONTINUE PLAYING"
              color: Theme.textMuted
              font.pointSize: Theme.small
              font.bold: true
              font.letterSpacing: 1.5
            }

            Label {
              text: hero.instance ? hero.instance.name : ""
              font.pointSize: Theme.title * 1.1
              font.bold: true
              elide: Text.ElideRight
              Layout.fillWidth: true
            }

            RowLayout {
              spacing: 14
              Stat { iconName: "package"; text: hero.instance ? hero.instance.summary : "" }
              Stat { iconName: "clock"; text: hero.instance ? hero.instance.lastPlayedText : "" }
              Stat {
                iconName: "check"
                visible: hero.instance ? hero.instance.hasLoader : false
                text: hero.instance ? "BepInEx " + hero.instance.loaderVersion : ""
              }
            }

            RowLayout {
              Layout.topMargin: 10
              spacing: 8

              LhButton {
                text: hero.instance && hero.instance.isRunning ? "Running" : "Play"
                iconName: "play"
                kind: "primary"
                large: true
                enabled: !App.isGameRunning && Vm.settings.gameFound
                onClicked: hero.instance.play()
              }

              LhButton {
                text: "Manage"
                large: true
                onClicked: App.openInstance(hero.instance.id)
              }

              LhButton {
                text: "Add mods"
                iconName: "browse"
                kind: "ghost"
                large: true
                onClicked: hero.instance.browseMods()
              }
            }
          }
        }
      }

      Label {
        text: "All instances"
        visible: App.instanceCount > 0
        font.pointSize: Theme.heading
        font.bold: true
        Layout.topMargin: 6
      }

      GridLayout {
        id: grid

        readonly property int cardWidth: 300

        visible: App.instanceCount > 0
        columns: Math.max(1, Math.floor((column.width + columnSpacing) / (cardWidth + columnSpacing)))
        columnSpacing: 16
        rowSpacing: 16
        Layout.fillWidth: true

        Repeater {
          model: Vm.instances

          delegate: InstanceCard {
            instance: modelData
            Layout.fillWidth: true
            Layout.preferredWidth: grid.cardWidth
            Layout.preferredHeight: 196
            onRenameRequested: { renameDialog.instance = modelData; renameDialog.open() }
            onDeleteRequested: { deleteDialog.instance = modelData; deleteDialog.open() }
          }
        }
      }

      EmptyState {
        visible: App.instanceCount === 0
        iconName: "package"
        title: "No instances yet"
        text: "Create an instance to start modding. LaunchHeim installs BepInEx into it and keeps your game folder clean, so you can switch between modpacks with one click."
        Layout.fillWidth: true
        Layout.topMargin: 60

        LhButton { text: "New instance"; iconName: "plus"; kind: "primary"; large: true; onClicked: newDialog.open() }
        LhButton {
          text: "Import game folder"
          iconName: "import"
          large: true
          visible: Vm.settings.gameFolderHasBepInEx
          onClicked: importDialog.open()
        }
      }
    }
  }
}
