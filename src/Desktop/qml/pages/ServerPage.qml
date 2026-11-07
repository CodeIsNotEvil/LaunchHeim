import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import Qt.labs.platform 1.1 as Platform
import "../components"
import "../dialogs"

// One dedicated server (ServerViewModel): start and stop, how to join, its settings, and which of its
// instance's mods it runs. The settings are one form saved together, so Valheim's rules (a public
// server's password against the world name) are checked on what will actually be used.
Item {
  id: page

  Loader {
    anchors.fill: parent
    active: Vm.server !== null && Vm.server !== undefined
    sourceComponent: content
  }

  // Faster while the server starts, so the steps of the start show as they happen.
  Timer {
    interval: Vm.server && Vm.server.isStarting ? 1500 : 3000
    repeat: true
    running: page.visible && Vm.server !== null && Vm.server !== undefined
    onTriggered: Vm.servers.refreshStatus()
  }

  Component {
    id: content

    Item {
      id: root

      readonly property var server: Vm.server
      readonly property var instanceNames: Vm.servers.instanceChoices.split("\u001f")
      readonly property var instanceIds: Vm.servers.instanceChoiceIds.split("\u001f")
      readonly property var presets: ["", "normal", "casual", "easy", "hard", "hardcore", "immersive", "hammer"]
      readonly property var presetNames: ["The world's own settings", "Normal", "Casual", "Easy", "Hard", "Hardcore", "Immersive", "Hammer (building only)"]
      readonly property var binaries: ["steam", "launchheim", "custom"]
      property string customDirectory: ""
      property string saveDirectory: ""
      property string problems: ""

      // The form starts from the saved profile, and again when another server is opened.
      function load() {
        nameField.text = server.name
        worldField.text = server.world
        portField.text = server.port
        passwordField.text = server.password
        publicSwitch.checked = server.isPublic
        crossplaySwitch.checked = server.crossplay
        presetBox.currentIndex = Math.max(0, presets.indexOf(server.preset.toLowerCase()))
        intervalField.text = Math.round(server.saveInterval / 60)
        backupsField.text = server.backups
        extraField.text = server.extraArguments
        consoleSwitch.checked = server.consoleEnabled
        binaryBox.currentIndex = Math.max(0, binaries.indexOf(server.binary))
        instanceBox.currentIndex = Math.max(0, instanceIds.indexOf(server.instanceId))
        customDirectory = server.customDirectory
        saveDirectory = server.saveDirectoryIsCustom ? server.saveDirectory : ""
        problems = ""
      }

      function save() {
        problems = server.apply(JSON.stringify({
          name: nameField.text,
          world: worldField.text,
          port: parseInt(portField.text) || 0,
          password: passwordField.text,
          isPublic: publicSwitch.checked,
          crossplay: crossplaySwitch.checked,
          preset: presets[presetBox.currentIndex],
          saveInterval: (parseInt(intervalField.text) || 30) * 60,
          backups: parseInt(backupsField.text) || 0,
          extraArguments: extraField.text,
          consoleEnabled: consoleSwitch.checked,
          binary: binaries[binaryBox.currentIndex],
          customDirectory: customDirectory,
          saveDirectory: saveDirectory,
          instanceId: instanceIds[instanceBox.currentIndex]
        }))
      }

      Component.onCompleted: load()
      onServerChanged: if (server) load()

      JoinServerDialog { id: joinDialog; server: root.server }

      Connections {
        target: Vm.servers
        function onJoinDialogRequested() { joinDialog.open() }
      }

      // The remembered character joins at once; the first time, or with "Change", the dialog asks.
      function join() {
        if (server.hasJoinChoice)
          server.join()
        else
          joinDialog.open()
      }

      readonly property bool canJoin: root.server.isOnline && !App.isGameRunning && App.steamStatus.length === 0 && Vm.settings.gameFound

      ConfirmDialog {
        id: deleteDialog
        title: "Delete server?"
        text: "“" + root.server.name + "” and its folder will be removed from disk" + (root.server.saveDirectoryIsCustom ? ". Its worlds in " + root.server.saveDirectory + " stay." : ", with its worlds. This cannot be undone.")
        onConfirmed: root.server.delete()
      }

      ConfirmDialog {
        id: killDialog
        title: "Kill the server?"
        confirmText: "Kill"
        text: "“" + root.server.name + "” is ended at once, without saving. The world goes back to its last save, so whatever players did since is lost."
        onConfirmed: root.server.kill()
      }

      Platform.FolderDialog {
        id: customDialog
        title: "Choose the dedicated server's folder"
        onAccepted: root.customDirectory = folder.toString()
      }

      Platform.FolderDialog {
        id: savesDialog
        title: "Choose where the server keeps its worlds"
        onAccepted: root.saveDirectory = folder.toString()
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
          width: Math.max(0, Math.min(flick.width - 64, 980))
          spacing: 16

          RowLayout {
            spacing: 12
            Layout.fillWidth: true

            IconButton { iconName: "back"; tip: "All servers"; onClicked: App.navigate("servers") }

            PageHeader {
              title: root.server.name
              Layout.fillWidth: true

              LhButton { text: "Console"; iconName: "log"; onClicked: root.server.openConsole() }
              LhButton {
                visible: root.server.isRunning
                text: "Restart"
                iconName: "refresh"
                enabled: !root.server.isBusy
                onClicked: root.server.restart()
              }
              LhButton {
                visible: root.server.isOnline
                text: "Join"
                iconName: "play"
                kind: "primary"
                large: true
                enabled: root.canJoin
                onClicked: root.join()
              }
              LhButton {
                text: root.server.isRunning ? "Stop" : "Start"
                iconName: root.server.isRunning ? "power" : "play"
                kind: root.server.isRunning ? "secondary" : "primary"
                large: !root.server.isRunning
                enabled: !root.server.isBusy && (root.server.isRunning || (root.server.serverDirectory.length > 0 && root.server.problems.length === 0))
                onClicked: root.server.isRunning ? root.server.stop() : root.server.start()
              }
            }
          }

          Banner {
            visible: root.server.serverDirectory.length === 0
            kind: "warning"
            text: root.server.serverMissingText
            actionText: root.server.binary === "steam" ? "Install with Steam" : root.server.binary === "launchheim" ? "Download" : ""
            onActionTriggered: root.server.binary === "steam" ? Vm.servers.installWithSteam() : Vm.servers.downloadServer()
            Layout.fillWidth: true
          }

          Banner {
            visible: root.server.instanceMissing
            kind: "warning"
            text: "The instance this server took its mods from was deleted. Choose another one below, or Vanilla."
            Layout.fillWidth: true
          }

          Banner {
            visible: root.server.problems.length > 0
            kind: "error"
            text: root.server.problems
            Layout.fillWidth: true
          }

          // The start, step by step, from the server's log: nobody should need the console to know it's coming.
          Card {
            visible: root.server.isStarting || root.server.isBusy
            Layout.fillWidth: true
            implicitHeight: startColumn.implicitHeight + 36
            color: Theme.alpha(Theme.accent, 0.10)

            ColumnLayout {
              id: startColumn
              anchors.fill: parent
              anchors.margins: 18
              spacing: 10

              RowLayout {
                spacing: 12
                Layout.fillWidth: true

                BusyIndicator {
                  running: parent.visible
                  Layout.preferredWidth: 32
                  Layout.preferredHeight: 32
                }

                ColumnLayout {
                  spacing: 2
                  Layout.fillWidth: true

                  Label {
                    text: root.server.isBusy ? root.server.busyText : root.server.startPhaseText
                    font.bold: true
                    font.pointSize: Theme.heading
                    wrapMode: Text.Wrap
                    Layout.fillWidth: true
                  }

                  Label {
                    text: root.server.isBusy ? "" : "Step " + root.server.startStep + " of " + root.server.startSteps + (root.server.startElapsed.length > 0 ? " · " + root.server.startElapsed : "") + ". Players can join once it's online; LaunchHeim tells you."
                    visible: text.length > 0
                    color: Theme.textMuted
                    wrapMode: Text.Wrap
                    Layout.fillWidth: true
                  }
                }

                LhButton { text: "Show log"; iconName: "log"; kind: "ghost"; onClicked: root.server.openConsole() }
              }

              ProgressBar {
                Layout.fillWidth: true
                from: 0
                to: root.server.startSteps
                value: root.server.isBusy ? 0 : root.server.startStep
                indeterminate: root.server.isBusy
              }
            }
          }

          // Status and how to get in.
          SettingsSection {
            iconName: "server"
            // The card above already says how the start is going.
            title: root.server.isStarting ? "Not joinable yet" : root.server.statusText
            description: root.server.isRunning
              ? "Players on this PC's network join with “Join IP” and this PC's address (port " + root.server.port + "); from the internet, forward UDP " + root.server.port + "–" + (root.server.port + 1) + " to this PC in your router."
              : "Stopping saves the world first. Closing LaunchHeim leaves a running server running."
            Layout.fillWidth: true

            // Join, with the server's own instance and password; the character is remembered.
            RowLayout {
              visible: root.server.isOnline
              spacing: 10

              LhButton { text: "Join"; iconName: "play"; kind: "primary"; enabled: root.canJoin; onClicked: root.join() }
              Label {
                text: root.server.hasJoinChoice ? "as " + root.server.joinChoiceText + " with " + root.server.instanceName : "Pick your character the first time"
                color: Theme.textMuted
              }
              LhButton { text: "Change"; kind: "ghost"; visible: root.server.hasJoinChoice; enabled: root.canJoin; onClicked: joinDialog.open() }
            }

            Label {
              visible: root.server.isOnline && App.isGameRunning
              text: "Valheim is already running. Close it to join from here."
              color: Theme.textMuted
              font.pointSize: Theme.small
            }

            RowLayout {
              spacing: 8
              Label { text: "On this PC:"; color: Theme.textMuted }
              Label { text: root.server.joinAddress; font.family: Theme.fontFamily; font.bold: true }
              IconButton { iconName: "copy"; tip: "Copy the address"; onClicked: Clipboard.copy(root.server.joinAddress) }
              Item { Layout.fillWidth: true }
              LhButton { text: "Kill…"; kind: "danger"; visible: root.server.isRunning; onClicked: killDialog.open() }
            }
          }

          SettingsSection {
            iconName: "settings"
            title: "Settings"
            description: "Used from the next start. A private server isn't listed in Valheim's server browser; friends join by address."
            Layout.fillWidth: true

            GridLayout {
              columns: 2
              columnSpacing: 14
              rowSpacing: 10
              Layout.fillWidth: true

              Label { text: "Name" }
              LhTextField { id: nameField; Layout.fillWidth: true }

              Label { text: "World" }
              LhTextField { id: worldField; placeholderText: "Dedicated"; Layout.fillWidth: true }

              Label { text: "Port" }
              LhTextField {
                id: portField
                validator: IntValidator { bottom: 1; top: 65534 }
                Layout.preferredWidth: 140
              }

              Label { text: "Password" }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true

                LhTextField {
                  id: passwordField
                  echoMode: revealPassword.checked ? TextInput.Normal : TextInput.Password
                  placeholderText: publicSwitch.checked ? "At least 5 characters, not part of the world name" : "Empty for none"
                  Layout.fillWidth: true
                }

                IconButton { id: revealPassword; checkable: true; iconName: "key"; tip: checked ? "Hide password" : "Show password" }
              }

              Label { text: "World preset" }
              ComboBox {
                id: presetBox
                model: root.presetNames
                Layout.fillWidth: true
                Material.foreground: Theme.text
              }

              Label { text: "Mods from" }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true

                ComboBox {
                  id: instanceBox
                  model: root.instanceNames
                  Layout.fillWidth: true
                  Material.foreground: Theme.text
                }

                LhButton { text: "Open"; kind: "ghost"; iconName: "external"; visible: root.server.instanceId.length > 0 && !root.server.instanceMissing; onClicked: root.server.openInstance() }
              }

              Label { text: "Server program" }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true

                ComboBox {
                  id: binaryBox
                  model: ["Steam's Valheim Dedicated Server", "LaunchHeim's download (SteamCMD)", "A folder I choose"]
                  Layout.fillWidth: true
                  Material.foreground: Theme.text
                }

                LhButton {
                  visible: binaryBox.currentIndex === 1
                  text: Vm.servers.isDownloading ? "Downloading…" : Vm.servers.downloadedServerFound ? "Update" : "Download"
                  iconName: "download"
                  kind: "ghost"
                  enabled: !Vm.servers.isDownloading
                  onClicked: Vm.servers.downloadServer()
                }

                LhButton { visible: binaryBox.currentIndex === 2; text: "Choose…"; iconName: "folder"; kind: "ghost"; onClicked: customDialog.open() }
              }

              Label { text: ""; visible: binaryBox.currentIndex === 2 }
              Label {
                visible: binaryBox.currentIndex === 2
                text: root.customDirectory.length > 0 ? decodeURIComponent(root.customDirectory.replace(/^file:\/\//, "")) : "No folder chosen"
                color: Theme.textMuted
                font.family: Theme.fontFamily
                elide: Text.ElideMiddle
                Layout.fillWidth: true
              }

              Label { text: "Worlds saved in" }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true

                Label {
                  text: root.saveDirectory.length > 0 ? decodeURIComponent(root.saveDirectory.replace(/^file:\/\//, "")) : "The server's own folder"
                  color: Theme.textMuted
                  font.family: Theme.fontFamily
                  elide: Text.ElideMiddle
                  Layout.fillWidth: true
                }

                LhButton { text: "Choose…"; iconName: "folder"; kind: "ghost"; onClicked: savesDialog.open() }
                LhButton { text: "Default"; kind: "ghost"; visible: root.saveDirectory.length > 0; onClicked: root.saveDirectory = "" }
                IconButton { iconName: "folder"; tip: "Open the saves"; onClicked: root.server.openSaves() }
              }

              Label { text: "Save every" }
              RowLayout {
                spacing: 8
                LhTextField {
                  id: intervalField
                  validator: IntValidator { bottom: 1; top: 1440 }
                  Layout.preferredWidth: 90
                }
                Label { text: "minutes, keeping"; color: Theme.textMuted }
                LhTextField {
                  id: backupsField
                  validator: IntValidator { bottom: 0; top: 100 }
                  Layout.preferredWidth: 70
                }
                Label { text: "backups of the world"; color: Theme.textMuted }
              }

              Label { text: "More arguments" }
              LhTextField { id: extraField; placeholderText: "e.g. -modifier raids none"; font.family: Theme.fontFamily; Layout.fillWidth: true }
            }

            LhSwitch { id: publicSwitch; text: "List it in Valheim's community server browser" }
            LhSwitch { id: crossplaySwitch; text: "Crossplay (Xbox and Game Pass players can join too)" }
            LhSwitch { id: consoleSwitch; text: "Console with LaunchHeim's server plugin (RCON on TCP " + root.server.rconPort + ", this PC only)" }

            Label {
              text: "The plugin adds BepInEx and itself to the server, even a vanilla one. Players don't need it, and the server stays vanilla for them. Without it there are no console commands, and stopping relies on the system."
              visible: consoleSwitch.checked
              wrapMode: Text.Wrap
              color: Theme.textMuted
              font.pointSize: Theme.small
              Layout.fillWidth: true
            }

            Label {
              text: root.problems
              visible: root.problems.length > 0
              color: Theme.negative
              wrapMode: Text.Wrap
              Layout.fillWidth: true
            }

            RowLayout {
              spacing: 8
              LhButton { text: "Save"; kind: "primary"; iconName: "check"; onClicked: root.save() }
              LhButton { text: "Undo changes"; kind: "ghost"; onClicked: root.load() }
            }
          }

          SettingsSection {
            iconName: "package"
            title: "Mods · " + root.server.modSummary
            description: root.server.instanceId.length === 0
              ? "A vanilla server. Choose an instance under “Mods from” to run its mods; manage them on the instance as usual."
              : "Copied from the instance at every start. Thunderstore's Client-side mods (maps, UI, graphics) stay off unless you switch them on. Configs are only copied while the server has none of that name, so changes made on the server stay."
            Layout.fillWidth: true

            Repeater {
              model: Vm.serverMods

              delegate: RowLayout {
                spacing: 10
                Layout.fillWidth: true

                LhSwitch {
                  checked: modelData.included
                  onToggled: modelData.setIncluded(checked)
                }

                Label {
                  text: modelData.name
                  elide: Text.ElideRight
                  Layout.fillWidth: true
                  Layout.minimumWidth: 0
                }

                Badge { visible: modelData.clientOnly; text: "Client-side"; tint: Theme.neutral }
                Label { text: modelData.version; color: Theme.textMuted; font.pointSize: Theme.small }
              }
            }

            RowLayout {
              visible: root.server.instanceId.length > 0
              spacing: 8
              LhButton {
                text: "Start with the instance's configs"
                iconName: "refresh"
                kind: "ghost"
                enabled: !root.server.isRunning && !root.server.isBusy && root.server.serverDirectory.length > 0
                onClicked: root.server.startWithInstanceConfigs()
              }
            }
          }

          SettingsSection {
            iconName: "folder"
            title: "Files"
            description: "The server's folder holds its BepInEx, mods, configs and log; its worlds too unless you chose another place."
            Layout.fillWidth: true

            RowLayout {
              spacing: 8
              LhButton { text: "Open folder"; iconName: "folder"; onClicked: root.server.openFolder() }
              LhButton { text: "Open saves"; iconName: "folder"; kind: "ghost"; onClicked: root.server.openSaves() }
              Item { Layout.fillWidth: true }
              LhButton { text: "Delete…"; kind: "danger"; iconName: "trash"; enabled: !root.server.isRunning; onClicked: deleteDialog.open() }
            }
          }
        }
      }
    }
  }
}
