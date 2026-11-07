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
      // {"field": "message"} from the view model; shown once the player tried to save or act, then kept
      // current while they type, so the outlines go away as the fields get filled in.
      property var errors: ({})
      property bool showErrors: false
      readonly property bool hasErrors: Object.keys(errors).length > 0

      function err(field) {
        return showErrors && errors[field] !== undefined ? errors[field] : ""
      }

      function takeErrors(json) {
        errors = json.length > 0 ? JSON.parse(json) : ({})
        showErrors = true
        return !hasErrors
      }

      // After each change, once errors are showing: the form as it stands, checked without saving.
      function recheck() {
        if (!showErrors)
          return
        errors = JSON.parse(server.isHosted ? server.checkHosted(hostedForm()) : server.check(localForm()))
      }
      property string keyFile: ""
      onKeyFileChanged: recheck()
      onCustomDirectoryChanged: recheck()
      readonly property var protocols: ["sftp", "ftps", "ftp"]

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
        errors = ({})
        showErrors = false
        if (server.isHosted)
          loadHosted()
      }

      function loadHosted() {
        hostNameField.text = server.name
        protocolBox.currentIndex = Math.max(0, protocols.indexOf(server.hostProtocol))
        hostAddressField.text = server.hostAddress
        hostPortField.text = server.hostPort > 0 ? server.hostPort : ""
        hostUserField.text = server.hostUser
        hostPasswordField.text = server.hostPassword
        keyFile = server.hostKeyFile
        hostDirectoryField.text = server.hostDirectory
        hostLogField.text = server.hostLogFile
        gameAddressField.text = server.gameAddress
        panelField.text = server.panelUrl
        rconPortField.text = server.hostRconPort > 0 ? server.hostRconPort : ""
        platformBox.currentIndex = server.hostPlatform === "windows" ? 1 : 0
        serverPasswordField.text = server.password
        hostConsoleSwitch.checked = server.consoleEnabled
        hostInstanceBox.currentIndex = Math.max(0, instanceIds.indexOf(server.instanceId))
        errors = ({})
        showErrors = false
      }

      function hostedForm() {
        return JSON.stringify({
          name: hostNameField.text,
          protocol: protocols[protocolBox.currentIndex],
          address: hostAddressField.text,
          port: parseInt(hostPortField.text) || 0,
          user: hostUserField.text,
          password: hostPasswordField.text,
          keyFile: keyFile,
          directory: hostDirectoryField.text,
          logFile: hostLogField.text,
          gameAddress: gameAddressField.text,
          panelUrl: panelField.text,
          rconPort: parseInt(rconPortField.text) || 0,
          platform: platformBox.currentIndex === 1 ? "windows" : "linux",
          serverPassword: serverPasswordField.text,
          consoleEnabled: hostConsoleSwitch.checked,
          instanceId: instanceIds[hostInstanceBox.currentIndex]
        })
      }

      function saveHosted() {
        takeErrors(server.saveHosted(hostedForm()))
      }

      function testHosted() {
        takeErrors(server.testConnection(hostedForm()))
      }

      function localForm() {
        return JSON.stringify({
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
        })
      }

      function save() {
        takeErrors(server.apply(localForm()))
      }

      Component.onCompleted: load()
      onServerChanged: if (server) load()

      JoinServerDialog { id: joinDialog; server: root.server }

      // Start or Upload with something missing: outline it, and bring the form into view.
      Connections {
        target: root.server
        function onSettingsIncomplete() {
          if (root.server.isHosted)
            root.loadHosted()
          root.takeErrors(root.server.fieldErrors)
          flick.contentY = Math.max(0, (root.server.isHosted ? connectionSection.y : settingsSection.y) - 16)
        }
      }

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
        text: root.server.isHosted
          ? "“" + root.server.name + "” is removed from LaunchHeim. Nothing at the host is touched: the server, its mods and its worlds stay there."
          : "“" + root.server.name + "” and its folder will be removed from disk" + (root.server.saveDirectoryIsCustom ? ". Its worlds in " + root.server.saveDirectory + " stay." : ", with its worlds. This cannot be undone.")
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

      Platform.FileDialog {
        id: keyDialog
        title: "Choose your SSH private key"
        onAccepted: root.keyFile = file.toString()
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
                visible: root.server.isRunning && !root.server.isHosted
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
                visible: !root.server.isHosted
                text: root.server.isRunning ? "Stop" : "Start"
                iconName: root.server.isRunning ? "power" : "play"
                kind: root.server.isRunning ? "secondary" : "primary"
                large: !root.server.isRunning
                enabled: !root.server.isBusy && (root.server.isRunning || root.server.serverDirectory.length > 0)
                onClicked: root.server.isRunning ? root.server.stop() : root.server.start()
              }
              // A hosted server starts in the host's panel; stopping goes through the plugin.
              LhButton {
                visible: root.server.isHosted && root.server.panelUrl.length > 0
                text: "Host panel"
                iconName: "external"
                onClicked: root.server.openPanel()
              }
              LhButton {
                visible: root.server.isHosted && root.server.isOnline && root.server.consoleEnabled
                text: "Stop"
                iconName: "power"
                enabled: !root.server.isBusy
                onClicked: root.server.stop()
              }
            }
          }

          Banner {
            visible: !root.server.isHosted && root.server.serverDirectory.length === 0
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
            visible: root.server.problems.length > 0 && !root.showErrors
            kind: "warning"
            text: "Some settings are missing. Save or start once and LaunchHeim outlines them below."
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
            description: root.server.isHosted
              ? (root.server.gameAddress.length === 0
                ? "Enter the address players join at in the connection settings to see whether it's online."
                : root.server.isOnline ? "Players join at " + root.server.gameAddress + "." : "It didn't answer at " + root.server.gameAddress + ". Start it in your host's panel.")
              : root.server.isRunning
                ? "Your friends join with Valheim's “Join IP” and one of the addresses below."
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
              visible: root.server.isHosted && root.server.gameAddress.length > 0
              spacing: 8
              Label { text: "Players join at:"; color: Theme.textMuted }
              Label { text: root.server.gameAddress; font.family: Theme.fontFamily; font.bold: true }
              IconButton { iconName: "copy"; tip: "Copy the address"; onClicked: Clipboard.copy(root.server.gameAddress) }
            }

            RowLayout {
              visible: !root.server.isHosted
              spacing: 8
              Label { text: "On this PC:"; color: Theme.textMuted }
              Label { text: root.server.joinAddress; font.family: Theme.fontFamily; font.bold: true }
              IconButton { iconName: "copy"; tip: "Copy the address"; onClicked: Clipboard.copy(root.server.joinAddress) }
              Item { Layout.fillWidth: true }
              LhButton { text: "Kill…"; kind: "danger"; visible: root.server.isRunning; onClicked: killDialog.open() }
            }
          }

          // The addresses friends type into "Join IP", for each way they can reach this PC.
          SettingsSection {
            visible: !root.server.isHosted
            iconName: "link"
            title: "How friends join"
            description: "In Valheim: Start game → pick a character → Join game → Join IP, then one of these. Same network means the same home Wi-Fi or LAN."
            Layout.fillWidth: true

            Repeater {
              model: Vm.serverAddresses

              delegate: RowLayout {
                spacing: 10
                Layout.fillWidth: true

                Label { text: modelData.address; font.family: Theme.fontFamily; font.bold: true; Layout.preferredWidth: 230 }
                IconButton { iconName: "copy"; tip: "Copy " + modelData.address; onClicked: Clipboard.copy(modelData.address) }
                Label {
                  text: modelData.label + " · " + modelData.interface
                  color: Theme.textMuted
                  elide: Text.ElideRight
                  Layout.fillWidth: true
                }
              }
            }

            Label {
              visible: Vm.serverAddresses.length === 0
              text: "This PC has no network connection right now."
              color: Theme.textMuted
            }

            Rectangle { height: 1; color: Theme.divider; Layout.fillWidth: true; Layout.topMargin: 4; Layout.bottomMargin: 4 }

            // The internet address is only known to the outside, so it's looked up on request (ServerAddresses).
            RowLayout {
              spacing: 10
              Layout.fillWidth: true

              Label {
                visible: root.server.internetAddress.length > 0
                text: root.server.internetAddress
                font.family: Theme.fontFamily
                font.bold: true
                Layout.preferredWidth: 230
              }
              IconButton {
                visible: root.server.internetAddress.length > 0
                iconName: "copy"
                tip: "Copy " + root.server.internetAddress
                onClicked: Clipboard.copy(root.server.internetAddress)
              }
              Label {
                visible: root.server.internetAddress.length > 0
                text: "Internet (friends anywhere)"
                color: Theme.textMuted
                Layout.fillWidth: true
              }
              LhButton {
                visible: root.server.internetAddress.length === 0
                text: Vm.servers.isFindingPublic ? "Looking it up…" : "Show my internet address"
                iconName: "search"
                enabled: !Vm.servers.isFindingPublic
                onClicked: Vm.servers.findPublicAddress()
              }
              Label {
                visible: root.server.internetAddress.length === 0
                text: "Asks api.ipify.org, which sees your address anyway."
                color: Theme.textMuted
                font.pointSize: Theme.small
                Layout.fillWidth: true
              }
            }

            Label {
              text: "Friends from the internet need your router to forward UDP ports " + root.server.port + "–" + (root.server.port + 1) + " to " + root.server.forwardTo + " (look for “port forwarding” in its settings). Without that, only the same network works. Your internet address can change; most home connections get a new one now and then."
              wrapMode: Text.Wrap
              color: Theme.textMuted
              font.pointSize: Theme.small
              Layout.fillWidth: true
            }

            RowLayout {
              visible: root.server.joinCode.length > 0
              spacing: 10

              Label { text: "Join code: " + root.server.joinCode; font.bold: true }
              IconButton { iconName: "copy"; tip: "Copy the join code"; onClicked: Clipboard.copy(root.server.joinCode) }
              Label { text: "crossplay: anyone can join with it, Xbox and Game Pass players too, usually without port forwarding"; color: Theme.textMuted }
            }
          }

          // How LaunchHeim reaches a hosted server. Saved as one form, like the local settings.
          SettingsSection {
            id: connectionSection
            visible: root.server.isHosted
            iconName: "link"
            title: "Connection"
            description: "Your host's FTP or SFTP login, from its panel. LaunchHeim uploads the mods and its plugin there, follows the log and sends console commands through files."
            Layout.fillWidth: true

            GridLayout {
              columns: 2
              columnSpacing: 14
              rowSpacing: 10
              Layout.fillWidth: true

              Label { text: "Name"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField { id: hostNameField; errorText: root.err("name"); onTextChanged: root.recheck(); Layout.fillWidth: true }

              Label { text: "Protocol" }
              ComboBox {
                id: protocolBox
                onCurrentIndexChanged: root.recheck()
                model: ["SFTP (SSH)", "FTPS (FTP with TLS)", "FTP (unencrypted)"]
                Layout.fillWidth: true
                Material.foreground: Theme.text
              }

              Label { text: "Host"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true
                LhTextField { id: hostAddressField; Layout.alignment: Qt.AlignTop; errorText: root.err("address"); onTextChanged: root.recheck(); placeholderText: "e.g. ftp.example-host.com or 203.0.113.7"; Layout.fillWidth: true }
                // Top-aligned: a field showing its error grows downwards, and the others stay level with it.
                Label { text: "Port"; color: Theme.textMuted; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
                LhTextField {
                  id: hostPortField; errorText: root.err("port"); onTextChanged: root.recheck()
                  Layout.alignment: Qt.AlignTop
                  placeholderText: protocolBox.currentIndex === 0 ? "22" : "21"
                  validator: IntValidator { bottom: 1; top: 65535 }
                  Layout.preferredWidth: 90
                }
              }

              Label { text: "User"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField { id: hostUserField; errorText: root.err("user"); onTextChanged: root.recheck(); Layout.fillWidth: true }

              Label { text: "Password"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true
                LhTextField {
                  id: hostPasswordField; errorText: root.err("password"); onTextChanged: root.recheck()
                  Layout.alignment: Qt.AlignTop
                  echoMode: revealHost.checked ? TextInput.Normal : TextInput.Password
                  placeholderText: root.keyFile.length > 0 ? "The key's passphrase, if it has one" : ""
                  Layout.fillWidth: true
                }
                IconButton { id: revealHost; checkable: true; iconName: "key"; tip: checked ? "Hide password" : "Show password"; Layout.alignment: Qt.AlignTop }
              }

              Label { text: "SSH key"; visible: protocolBox.currentIndex === 0 }
              RowLayout {
                visible: protocolBox.currentIndex === 0
                spacing: 8
                Layout.fillWidth: true
                Label {
                  text: root.err("keyFile").length > 0 ? root.err("keyFile")
                    : root.keyFile.length > 0 ? decodeURIComponent(root.keyFile.replace(/^file:\/\//, "")) : "None, log in with the password"
                  color: root.err("keyFile").length > 0 ? Theme.negative : Theme.textMuted
                  font.family: Theme.fontFamily
                  elide: Text.ElideMiddle
                  Layout.fillWidth: true
                }
                LhButton { text: "Choose…"; iconName: "key"; kind: "ghost"; onClicked: keyDialog.open() }
                LhButton { text: "None"; kind: "ghost"; visible: root.keyFile.length > 0; onClicked: root.keyFile = "" }
              }

              Label { text: "Server folder" }
              LhTextField { id: hostDirectoryField; placeholderText: "Empty for where the login starts, or e.g. /home/valheim/server"; Layout.fillWidth: true }

              Label { text: "Log file" }
              LhTextField { id: hostLogField; placeholderText: "BepInEx/LogOutput.log"; font.family: Theme.fontFamily; Layout.fillWidth: true }

              Label { text: "Players join at"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField { id: gameAddressField; errorText: root.err("gameAddress"); onTextChanged: root.recheck(); placeholderText: "host:port from your host's panel, e.g. 203.0.113.7:2456"; Layout.fillWidth: true }

              Label { text: "Server password" }
              LhTextField { id: serverPasswordField; echoMode: TextInput.Password; placeholderText: "For Join; empty if it has none"; Layout.fillWidth: true }

              Label { text: "Host panel" }
              LhTextField { id: panelField; placeholderText: "The web address of your host's panel, where you start the server"; Layout.fillWidth: true }

              Label { text: "Mods from" }
              ComboBox {
                id: hostInstanceBox
                model: root.instanceNames
                Layout.fillWidth: true
                Material.foreground: Theme.text
              }

              Label { text: "RCON port"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true
                LhTextField {
                  id: rconPortField; errorText: root.err("rconPort"); onTextChanged: root.recheck()
                  placeholderText: "None"
                  validator: IntValidator { bottom: 1; top: 65535 }
                  Layout.preferredWidth: 90
                }
                Label {
                  text: "Only if your host forwards a TCP port to the server for it. Without one, commands go through files, which takes a few seconds."
                  color: Theme.textMuted
                  font.pointSize: Theme.small
                  wrapMode: Text.Wrap
                  Layout.fillWidth: true
                }
              }

              Label { text: "Host system" }
              ComboBox {
                id: platformBox
                model: ["Linux", "Windows"]
                Layout.fillWidth: true
                Material.foreground: Theme.text
              }
            }

            LhSwitch { id: hostConsoleSwitch; text: "Console with LaunchHeim's server plugin (uploaded with the mods)" }

            RowLayout {
              spacing: 8
              visible: root.server.hostFingerprint.length > 0
              Label {
                text: (root.server.hostProtocol === "sftp" ? "Host key: " : "Certificate: ") + root.server.hostFingerprint
                color: Theme.textMuted
                font.pointSize: Theme.small
                font.family: Theme.fontFamily
                elide: Text.ElideMiddle
                Layout.fillWidth: true
              }
              LhButton { text: "Forget"; kind: "ghost"; onClicked: root.server.clearFingerprint() }
            }

            Label {
              text: "Fill in the outlined fields, then save."
              visible: root.showErrors && root.hasErrors
              color: Theme.negative
              wrapMode: Text.Wrap
              Layout.fillWidth: true
            }

            RowLayout {
              spacing: 8
              LhButton { text: "Save"; kind: "primary"; iconName: "check"; onClicked: root.saveHosted() }
              LhButton { text: "Undo changes"; kind: "ghost"; onClicked: root.loadHosted() }
              LhButton { text: "Test connection"; iconName: "refresh"; kind: "ghost"; onClicked: root.testHosted() }
            }

            Label {
              text: root.server.hostStatus
              visible: text.length > 0
              wrapMode: Text.Wrap
              color: root.server.hostStatus.indexOf("Couldn't") === 0 ? Theme.negative : Theme.textMuted
              Layout.fillWidth: true
            }
          }

          SettingsSection {
            id: settingsSection
            visible: !root.server.isHosted
            iconName: "settings"
            title: "Settings"
            description: "Used from the next start. A private server isn't listed in Valheim's server browser; friends join by address."
            Layout.fillWidth: true

            GridLayout {
              columns: 2
              columnSpacing: 14
              rowSpacing: 10
              Layout.fillWidth: true

              Label { text: "Name"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField { id: nameField; errorText: root.err("name"); onTextChanged: root.recheck(); Layout.fillWidth: true }

              Label { text: "World"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField { id: worldField; errorText: root.err("world"); onTextChanged: root.recheck(); placeholderText: "Dedicated"; Layout.fillWidth: true }

              Label { text: "Port"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              LhTextField {
                id: portField; errorText: root.err("port"); onTextChanged: root.recheck()
                validator: IntValidator { bottom: 1; top: 65534 }
                Layout.preferredWidth: 140
              }

              Label { text: "Password"; Layout.alignment: Qt.AlignTop; Layout.topMargin: 10 }
              RowLayout {
                spacing: 8
                Layout.fillWidth: true

                LhTextField {
                  id: passwordField; errorText: root.err("password"); onTextChanged: root.recheck()
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
                  onCurrentIndexChanged: root.recheck()
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
                text: root.err("customDirectory").length > 0 ? root.err("customDirectory")
                  : root.customDirectory.length > 0 ? decodeURIComponent(root.customDirectory.replace(/^file:\/\//, "")) : "No folder chosen"
                color: root.err("customDirectory").length > 0 ? Theme.negative : Theme.textMuted
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
              text: "Fill in the outlined fields, then save."
              visible: root.showErrors && root.hasErrors
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
              : (root.server.isHosted
                ? "Uploaded to the host when you click Upload mods; only changed files go up. "
                : "Copied from the instance at every start. ")
                + "Thunderstore's Client-side mods (maps, UI, graphics) stay off unless you switch them on. Configs are only copied while the server has none of that name, so changes made on the server stay."
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

            // Uploading to a host: BepInEx, the mods and the plugin, with progress.
            ColumnLayout {
              visible: root.server.isHosted
              spacing: 8
              Layout.fillWidth: true

              RowLayout {
                spacing: 8
                LhButton { text: "Upload mods"; iconName: "export"; kind: "primary"; enabled: !root.server.isDeploying; onClicked: root.server.deployMods() }
                LhButton { text: "With the instance's configs"; kind: "ghost"; visible: root.server.instanceId.length > 0; enabled: !root.server.isDeploying; onClicked: root.server.deployModsWithConfigs() }
                LhButton { text: "Everything again"; kind: "ghost"; enabled: !root.server.isDeploying; onClicked: root.server.deployModsAgain() }
              }

              ProgressBar {
                visible: root.server.isDeploying
                from: 0
                to: 1
                value: Math.max(0, root.server.deployProgress)
                indeterminate: root.server.deployProgress < 0
                Layout.fillWidth: true
              }

              Label {
                text: root.server.deployText + (root.server.isDeploying ? "" : " The host's server has to start with BepInEx (often a “mods” or “BepInEx” switch in its panel) for them to load.")
                color: Theme.textMuted
                wrapMode: Text.Wrap
                Layout.fillWidth: true
              }
            }

            RowLayout {
              visible: root.server.instanceId.length > 0 && !root.server.isHosted
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

          // For a VPS: Valheim's dedicated server itself, uploaded from LaunchHeim's SteamCMD download.
          SettingsSection {
            visible: root.server.isHosted
            iconName: "download"
            title: "Server program"
            description: "Game hosts install Valheim's server themselves. On a VPS that doesn't have it yet, LaunchHeim downloads it with SteamCMD for the host's system (" + (root.server.hostPlatform === "windows" ? "Windows" : "Linux") + ") and uploads it into the server folder: about 2 GB the first time, only changes after an update."
            Layout.fillWidth: true

            RowLayout {
              spacing: 8
              LhButton {
                text: "Upload Valheim's server"
                iconName: "export"
                enabled: !root.server.isDeploying && !Vm.servers.isDownloading
                onClicked: root.server.uploadServerProgram()
              }
            }

            Label {
              text: "LaunchHeim can't start programs over FTP or SFTP. Start it on the VPS with start_server_bepinex.sh (with mods) or start_headless_server.sh, from a shell, a service or your provider's console."
              color: Theme.textMuted
              font.pointSize: Theme.small
              wrapMode: Text.Wrap
              Layout.fillWidth: true
            }
          }

          SettingsSection {
            visible: root.server.isHosted
            iconName: "folder"
            title: "In LaunchHeim"
            description: "Removing the server here leaves everything at the host as it is."
            Layout.fillWidth: true

            RowLayout {
              spacing: 8
              Item { Layout.fillWidth: true }
              LhButton { text: "Remove…"; kind: "danger"; iconName: "trash"; onClicked: deleteDialog.open() }
            }
          }

          SettingsSection {
            visible: !root.server.isHosted
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
