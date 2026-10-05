import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import Qt.labs.platform 1.1 as Platform
import "../components"

Item {
  id: page

  readonly property var settings: Vm.settings

  Platform.FolderDialog {
    id: folderDialog
    title: "Choose the Valheim folder"
    onAccepted: page.settings.setGameDirectory(folder.toString())
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
      width: Math.max(0, Math.min(flick.width - 64, 900))
      spacing: 16

      PageHeader {
        title: "Settings"
        subtitle: "LaunchHeim follows " + page.settings.themeSource + " automatically."
        Layout.fillWidth: true
      }

      SettingsSection {
        iconName: "play"
        title: "Game"
        description: "Found through your Steam libraries. Instances run this installation with their own BepInEx; the folder itself is never modified."
        Layout.fillWidth: true

        RowLayout {
          spacing: 10
          Icon { iconName: page.settings.gameFound ? "check" : "alert"; color: page.settings.gameFound ? Theme.positive : Theme.negative }
          Label {
            text: page.settings.gameDirectory.length > 0 ? page.settings.gameDirectory : "Valheim was not found"
            font.family: Theme.fontFamily
            elide: Text.ElideMiddle
            Layout.fillWidth: true
          }
          Badge { visible: page.settings.gameDirectoryIsCustom; text: "Chosen by hand"; tint: Theme.textMuted }
        }

        RowLayout {
          spacing: 8
          LhButton { text: "Choose folder…"; iconName: "folder"; onClicked: folderDialog.open() }
          LhButton { text: "Detect again"; iconName: "refresh"; kind: "ghost"; onClicked: page.settings.detectGame() }
          LhButton { text: "Open"; iconName: "external"; kind: "ghost"; enabled: page.settings.gameFound; onClicked: page.settings.openGameDirectory() }
        }
      }

      SettingsSection {
        iconName: "key"
        title: "Nexus Mods"
        description: "Browsing works without an account. Installing needs your personal API key from <a href=\"https://www.nexusmods.com/users/myaccount?tab=api\">your Nexus account</a>. Premium members download directly; free accounts confirm each download on the Nexus page, which then hands it to LaunchHeim."
        Layout.fillWidth: true

        SecretField {
          text: page.settings.nexusApiKey
          placeholderText: "Personal API key"
          Layout.fillWidth: true
          onSaved: page.settings.saveNexusApiKey(value)
        }

        Label {
          visible: text.length > 0
          text: page.settings.nexusStatus
          color: text.indexOf("Could not") === 0 ? Theme.negative : Theme.positive
          wrapMode: Text.Wrap
          Layout.fillWidth: true
        }

        RowLayout {
          spacing: 10
          Layout.topMargin: 4

          ColumnLayout {
            spacing: 2
            Layout.fillWidth: true
            Label { text: "Handle “Mod Manager Download” links"; font.bold: true }
            Label {
              text: page.settings.nxmHint
              color: Theme.textMuted
              wrapMode: Text.Wrap
              Layout.fillWidth: true
            }
          }

          Badge {
            text: page.settings.nxmRegistered ? "Registered" : "Not registered"
            tint: page.settings.nxmRegistered ? Theme.positive : Theme.textMuted
          }

          LhButton { text: page.settings.nxmRegistered ? "Register again" : "Register"; onClicked: page.settings.registerNxmHandler() }
        }
      }

      SettingsSection {
        iconName: "key"
        title: "CurseForge"
        description: "CurseForge only answers apps that send an API key. Create one for free in the <a href=\"https://console.curseforge.com/\">CurseForge for Studios console</a>. Some authors disable downloads outside the website; LaunchHeim then opens the page instead."
        Layout.fillWidth: true

        SecretField {
          text: page.settings.curseForgeApiKey
          placeholderText: "API key"
          Layout.fillWidth: true
          onSaved: page.settings.saveCurseForgeApiKey(value)
        }

        Label {
          visible: text.length > 0
          text: page.settings.curseForgeStatus
          color: text.indexOf("Could not") === 0 ? Theme.negative : Theme.positive
          wrapMode: Text.Wrap
          Layout.fillWidth: true
        }
      }

      SettingsSection {
        iconName: "phone"
        title: "Phone sync"
        description: "LaunchHeim Companion for Android keeps your mod lists on the phone, browses Thunderstore and shows who's online on your servers. It talks to LaunchHeim over the local network with the LocalSend protocol, which the LocalSend app understands too. Sending to the phone works either way; receiving needs this switch."
        Layout.fillWidth: true

        LhSwitch {
          text: "Receive mod lists from phones while LaunchHeim runs"
          checked: Vm.phone.enabled
          onToggled: Vm.phone.setEnabled(checked)
        }

        RowLayout {
          spacing: 10
          Icon { iconName: Vm.phone.running ? "check" : "info"; color: Vm.phone.running ? Theme.positive : Theme.textMuted }
          Label { text: Vm.phone.statusText; elide: Text.ElideRight; Layout.fillWidth: true }
          LhButton { text: "Send to phone…"; iconName: "phone"; onClicked: Vm.phone.openSend("") }
        }

        RowLayout {
          spacing: 8
          LhTextField {
            id: aliasField
            text: Vm.phone.alias
            placeholderText: "Device name"
            selectByMouse: true
            Layout.preferredWidth: 280
            onAccepted: Vm.phone.setAlias(text)
          }
          LhButton { text: "Rename"; kind: "ghost"; enabled: aliasField.text.trim() !== Vm.phone.alias; onClicked: Vm.phone.setAlias(aliasField.text) }
        }
      }

      SettingsSection {
        iconName: "browse"
        title: "Browsing"
        Layout.fillWidth: true

        LhSwitch {
          text: "Show mods marked as adult content"
          checked: page.settings.showNsfw
          onToggled: page.settings.setShowNsfw(checked)
        }
      }

      SettingsSection {
        iconName: "folder"
        title: "Storage"
        description: "Instances live in the data folder. Downloaded archives and the Thunderstore list are cached and can be cleared at any time."
        Layout.fillWidth: true

        RowLayout {
          Label { text: page.settings.dataDirectory; font.family: Theme.fontFamily; elide: Text.ElideMiddle; Layout.fillWidth: true }
          LhButton { text: "Open"; iconName: "folder"; kind: "ghost"; onClicked: page.settings.openDataDirectory() }
          LhButton { text: "Clear download cache"; iconName: "trash"; onClicked: page.settings.clearDownloadCache() }
        }
      }

      SettingsSection {
        iconName: "log"
        title: "Console and logs"
        description: "The console follows BepInEx's log, Unity's Player.log and LaunchHeim's own log live. LaunchHeim's log records every launch (paths, arguments, Steam) and every error, which helps when reporting a problem."
        Layout.fillWidth: true

        LhSwitch {
          text: "Open the console every time the game starts"
          checked: page.settings.openConsoleOnLaunch
          onToggled: page.settings.setOpenConsoleOnLaunch(checked)
        }

        RowLayout {
          Label { text: page.settings.logFile; font.family: Theme.fontFamily; elide: Text.ElideMiddle; Layout.fillWidth: true }
          LhButton { text: "Open"; iconName: "folder"; kind: "ghost"; onClicked: page.settings.openLogFolder() }
          LhButton { text: "Open console"; iconName: "log"; onClicked: Vm.debugConsole.show("app") }
        }
      }

      SettingsSection {
        iconName: "info"
        title: "About"
        Layout.fillWidth: true

        GridLayout {
          columns: 2
          columnSpacing: 24
          rowSpacing: 6
          Layout.fillWidth: true

          Label { text: "Version"; color: Theme.textMuted }
          Label { text: page.settings.version }
          Label { text: "Qt runtime"; color: Theme.textMuted }
          Label { text: page.settings.qtRuntime }
          Label { text: "UI"; color: Theme.textMuted }
          Label { text: "Qt Quick through Qml.Net, colored by " + page.settings.themeSource }
          Label { text: "License"; color: Theme.textMuted }
          Label { text: "MIT, Copyright (c) 2026 CodeIsNotEvil" }
          // LGPL-3.0 section 4: a prominent notice that Qt is used, and Qt's copyright among the notices
          // the app shows. The other libraries are listed in the notices file.
          Label { text: "Built with"; color: Theme.textMuted; Layout.alignment: Qt.AlignTop }
          Label {
            text: "Qt 5.15, Copyright (c) The Qt Company Ltd. and other contributors, used under the GNU LGPL v3. "
              + "Also Qml.Net, NetNativeLibLoader (LGPL v3), SharpCompress, Markdig, Newtonsoft.Json, .NET and the Kode Mono font."
            wrapMode: Text.WordWrap
            Layout.fillWidth: true
          }
        }

        LhButton {
          text: "Third-party licenses"
          iconName: "external"
          kind: "ghost"
          onClicked: page.settings.openThirdPartyNotices()
        }
      }
    }
  }
}
