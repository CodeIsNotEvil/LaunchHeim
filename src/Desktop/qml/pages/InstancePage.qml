import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import Qt.labs.platform 1.1 as Platform
import "../components"
import "../dialogs"

Item {
  id: page

  Loader {
    anchors.fill: parent
    active: Vm.selected !== null && Vm.selected !== undefined
    sourceComponent: content
  }

  Component {
    id: content

    Item {
      id: root

      readonly property var inst: Vm.selected
      // Held here so the item wrappers stay alive; see Vm.qml.
      readonly property var mods: Net.toVariantList(inst.filteredMods)
      readonly property var configFiles: Net.toVariantList(inst.configFiles)

      RenameDialog { id: renameDialog; instance: root.inst }
      ConfirmDialog {
        id: deleteDialog
        title: "Delete instance?"
        text: "“" + root.inst.name + "” and all its mods and configs will be removed from disk. This cannot be undone."
        onConfirmed: root.inst.delete()
      }

      Platform.FileDialog {
        id: fileDialog
        title: "Install a mod from a file"
        nameFilters: ["Mod archives and plugins (*.zip *.7z *.rar *.dll)", "All files (*)"]
        onAccepted: root.inst.installFile(file.toString())
      }

      ColumnLayout {
        anchors.fill: parent
        anchors.margins: 32
        anchors.bottomMargin: 20
        spacing: 18

        // Header
        RowLayout {
          spacing: 20
          Layout.fillWidth: true

          IconButton {
            iconName: "back"
            tip: "Back to the library"
            Layout.alignment: Qt.AlignTop
            onClicked: App.navigate("library")
          }

          Avatar {
            width: 84
            height: 84
            initials: root.inst.initials
            tint: root.inst.color
          }

          ColumnLayout {
            spacing: 4
            Layout.fillWidth: true

            ElidedTitle {
              text: root.inst.name
              font.pointSize: Theme.title
              font.bold: true
              spacing: 6
              Layout.fillWidth: true

              IconButton { iconName: "edit"; tip: "Rename"; onClicked: renameDialog.open() }
              Badge { visible: root.inst.isRunning; text: "Running"; tint: Theme.positive; solid: true }
            }

            RowLayout {
              spacing: 14
              Stat { iconName: "check"; visible: root.inst.hasLoader; text: "BepInEx " + root.inst.loaderVersion }
              Stat { iconName: "package"; text: root.inst.modCount + " mods, " + root.inst.enabledModCount + " enabled" }
              Stat { iconName: "clock"; text: root.inst.lastPlayedText }
            }
          }

          LhButton {
            text: "Add mods"
            iconName: "browse"
            large: true
            onClicked: root.inst.browseMods()
          }

          LhButton {
            text: root.inst.isRunning ? "Running" : "Play"
            iconName: "play"
            kind: "primary"
            large: true
            enabled: !App.isGameRunning && Vm.settings.gameFound && root.inst.hasLoader
            onClicked: root.inst.play()
          }

          IconButton {
            iconName: "more"
            tip: "More"
            onClicked: menu.popup()

            Menu {
              id: menu
              MenuItem { text: "Install from file…"; onTriggered: fileDialog.open() }
              MenuItem { text: "Check for updates"; onTriggered: root.inst.checkUpdates() }
              MenuSeparator {}
              MenuItem { text: "Open instance folder"; onTriggered: root.inst.openFolder() }
              MenuItem { text: "Open config folder"; onTriggered: root.inst.openConfigFolder() }
              MenuItem { text: "Open BepInEx log"; onTriggered: root.inst.openLog() }
              MenuSeparator {}
              MenuItem { text: "Duplicate"; onTriggered: root.inst.duplicate() }
              MenuItem { text: "Delete…"; onTriggered: deleteDialog.open() }
            }
          }
        }

        Banner {
          visible: !root.inst.hasLoader && !root.inst.isBusy
          kind: "warning"
          text: "BepInEx is not installed in this instance, so mods will not load."
          actionText: "Install BepInEx"
          onActionTriggered: root.inst.installLoader()
          Layout.fillWidth: true
        }

        TabBar {
          id: tabs
          Layout.fillWidth: true
          background: Rectangle {
            color: "transparent"
            Rectangle { anchors.bottom: parent.bottom; width: parent.width; height: 1; color: Theme.divider }
          }

          TabButton { text: "Mods (" + root.inst.modCount + ")"; width: implicitWidth + 24; font.capitalization: Font.MixedCase }
          TabButton { text: "Config files (" + root.configFiles.length + ")"; width: implicitWidth + 24; font.capitalization: Font.MixedCase }
          TabButton { text: "Launch options"; width: implicitWidth + 24; font.capitalization: Font.MixedCase }
        }

        StackLayout {
          currentIndex: tabs.currentIndex
          Layout.fillWidth: true
          Layout.fillHeight: true

          // Mods
          ColumnLayout {
            spacing: 12

            RowLayout {
              spacing: 10
              Layout.fillWidth: true

              SearchField {
                placeholderText: "Filter installed mods"
                text: root.inst.filter
                onTextChanged: root.inst.filter = text
                Layout.preferredWidth: 320
              }

              BusyIndicator {
                running: root.inst.isBusy
                visible: running
                implicitWidth: 32
                implicitHeight: 32
              }

              Item { Layout.fillWidth: true }

              LhButton { text: "Install from file"; iconName: "file"; kind: "ghost"; onClicked: fileDialog.open() }
              LhButton { text: "Check for updates"; iconName: "refresh"; kind: "ghost"; onClicked: root.inst.checkUpdates() }
              LhButton {
                visible: root.inst.updateCount > 0
                text: "Update all (" + root.inst.updateCount + ")"
                iconName: "update"
                kind: "primary"
                onClicked: root.inst.updateAll()
              }
            }

            Card {
              Layout.fillWidth: true
              Layout.fillHeight: true

              ListView {
                id: modList
                anchors.fill: parent
                anchors.margins: 6
                clip: true
                spacing: 2
                model: root.mods
                boundsBehavior: Flickable.StopAtBounds
                ScrollBar.vertical: ScrollBar {}

                delegate: ModRow {
                  mod: modelData
                  width: modList.width - 12
                }
              }

              EmptyState {
                anchors.centerIn: parent
                visible: root.inst.modCount === 0 && root.inst.filter.length === 0
                iconName: "browse"
                title: "No mods yet"
                text: "Find mods on Thunderstore, Nexus Mods or CurseForge. Dependencies and BepInEx are installed for you."

                LhButton { text: "Browse mods"; iconName: "browse"; kind: "primary"; onClicked: root.inst.browseMods() }
                LhButton { text: "Install from file"; iconName: "file"; onClicked: fileDialog.open() }
              }
            }
          }

          // Config files
          ColumnLayout {
            spacing: 12

            RowLayout {
              Layout.fillWidth: true

              Label {
                text: "Mods create their .cfg files on the first launch. Edit them while the game is closed."
                color: Theme.textMuted
                wrapMode: Text.Wrap
                Layout.fillWidth: true
              }

              LhButton { text: "Open config folder"; iconName: "folder"; onClicked: root.inst.openConfigFolder() }
            }

            Card {
              Layout.fillWidth: true
              Layout.fillHeight: true

              ListView {
                id: configList
                anchors.fill: parent
                anchors.margins: 6
                clip: true
                model: root.configFiles
                ScrollBar.vertical: ScrollBar {}

                delegate: ItemDelegate {
                  width: configList.width
                  hoverEnabled: true
                  onClicked: modelData.open()

                  contentItem: RowLayout {
                    spacing: 12
                    Icon { iconName: "file"; color: Theme.textMuted }
                    Label { text: modelData.name; Layout.fillWidth: true; elide: Text.ElideMiddle }
                    Label { text: modelData.sizeText + " · " + modelData.modifiedText; color: Theme.textMuted; font.pointSize: Theme.small }
                    Icon { iconName: "external"; size: 14; color: Theme.textMuted }
                  }
                }
              }

              EmptyState {
                anchors.centerIn: parent
                visible: root.configFiles.length === 0
                iconName: "file"
                title: "No config files yet"
                text: "Play this instance once and the mods will write their settings here."
              }
            }
          }

          // Launch options
          Flickable {
            contentHeight: optionsColumn.implicitHeight
            clip: true

            ColumnLayout {
              id: optionsColumn
              width: Math.min(parent.width, 760)
              spacing: 16

              Card {
                Layout.fillWidth: true
                implicitHeight: argsColumn.implicitHeight + 40

                ColumnLayout {
                  id: argsColumn
                  anchors.fill: parent
                  anchors.margins: 20
                  spacing: 10

                  Label { text: "Launch arguments"; font.bold: true; font.pointSize: Theme.heading }
                  Label {
                    text: "Passed to Valheim when this instance starts, for example -console, -windowed or +connect host:2456."
                    color: Theme.textMuted
                    wrapMode: Text.Wrap
                    Layout.fillWidth: true
                  }

                  RowLayout {
                    spacing: 8
                    LhTextField {
                      id: argsField
                      text: root.inst.launchArguments
                      placeholderText: "No extra arguments"
                      selectByMouse: true
                      font.family: "monospace"
                      Layout.fillWidth: true
                      onAccepted: root.inst.saveLaunchArguments(text)
                    }
                    LhButton { text: "Save"; kind: "primary"; onClicked: root.inst.saveLaunchArguments(argsField.text) }
                  }
                }
              }

              Card {
                Layout.fillWidth: true
                implicitHeight: locationColumn.implicitHeight + 40

                ColumnLayout {
                  id: locationColumn
                  anchors.fill: parent
                  anchors.margins: 20
                  spacing: 10

                  Label { text: "Location"; font.bold: true; font.pointSize: Theme.heading }
                  Label {
                    text: "BepInEx is pointed at this folder when the instance starts, so plugins, configs and logs live here and the game folder stays untouched."
                    color: Theme.textMuted
                    wrapMode: Text.Wrap
                    Layout.fillWidth: true
                  }

                  RowLayout {
                    Label { text: root.inst.directory; font.family: "monospace"; elide: Text.ElideMiddle; Layout.fillWidth: true }
                    LhButton { text: "Open"; iconName: "folder"; onClicked: root.inst.openFolder() }
                  }
                }
              }

              Card {
                Layout.fillWidth: true
                implicitHeight: dangerRow.implicitHeight + 40
                border.color: Theme.alpha(Theme.negative, 0.4)

                RowLayout {
                  id: dangerRow
                  anchors.fill: parent
                  anchors.margins: 20

                  ColumnLayout {
                    Layout.fillWidth: true
                    Label { text: "Delete this instance"; font.bold: true }
                    Label { text: "Removes the folder with all mods and configs."; color: Theme.textMuted }
                  }

                  LhButton { text: "Delete…"; kind: "danger"; iconName: "trash"; onClicked: deleteDialog.open() }
                }
              }
            }
          }
        }
      }
    }
  }
}
