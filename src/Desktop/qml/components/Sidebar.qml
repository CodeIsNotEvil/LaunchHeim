import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Rectangle {
  color: Theme.sidebar

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 12
    spacing: 4

    RowLayout {
      spacing: 10
      Layout.leftMargin: 6
      Layout.topMargin: 6
      Layout.bottomMargin: 14

      GlintLogo {
        Layout.preferredWidth: 36
        Layout.preferredHeight: 36
      }

      ColumnLayout {
        spacing: 0
        Label { text: "LaunchHeim"; font.bold: true; font.pointSize: Theme.heading }
        Label { text: "Valheim mod launcher"; color: Theme.textMuted; font.pointSize: Theme.small }
      }
    }

    NavButton {
      text: "Library"
      iconName: "library"
      count: App.instanceCount > 0 ? App.instanceCount : ""
      active: App.currentPage === "library"
      Layout.fillWidth: true
      onClicked: App.navigate("library")
    }

    NavButton {
      text: "Play"
      iconName: "play"
      active: App.currentPage === "play"
      Layout.fillWidth: true
      onClicked: App.navigate("play")
    }

    NavButton {
      text: "Browse mods"
      iconName: "browse"
      active: App.currentPage === "browse"
      Layout.fillWidth: true
      onClicked: App.navigate("browse")
    }

    NavButton {
      text: "Settings"
      iconName: "settings"
      active: App.currentPage === "settings"
      Layout.fillWidth: true
      onClicked: App.navigate("settings")
    }

    Label {
      text: "INSTANCES"
      visible: App.instanceCount > 0
      color: Theme.textMuted
      font.pointSize: Theme.small
      font.bold: true
      font.letterSpacing: 1
      Layout.topMargin: 18
      Layout.leftMargin: 12
      Layout.bottomMargin: 4
    }

    ListView {
      id: instanceList
      clip: true
      spacing: 2
      model: Vm.instances
      Layout.fillWidth: true
      Layout.fillHeight: true
      boundsBehavior: Flickable.StopAtBounds
      ScrollBar.vertical: ScrollBar { policy: ScrollBar.AsNeeded }

      delegate: NavButton {
        width: instanceList.width
        text: modelData.name
        dot: modelData.color
        count: modelData.isRunning ? "▶" : ""
        active: App.currentPage === "instance" && Vm.selected && Vm.selected.id === modelData.id
        onClicked: App.openInstance(modelData.id)
      }
    }

    // Running downloads and installs.
    Repeater {
      model: Vm.activities

      delegate: Card {
        Layout.fillWidth: true
        implicitHeight: activityColumn.implicitHeight + 20

        ColumnLayout {
          id: activityColumn
          anchors.fill: parent
          anchors.margins: 10
          spacing: 4

          Label {
            text: modelData.title
            font.bold: true
            elide: Text.ElideRight
            Layout.fillWidth: true
          }

          Label {
            text: modelData.detail
            visible: text.length > 0
            color: Theme.textMuted
            font.pointSize: Theme.small
          }

          ProgressBar {
            Layout.fillWidth: true
            from: 0
            to: 1
            value: modelData.progress < 0 ? 0 : modelData.progress
            indeterminate: modelData.progress < 0
          }
        }
      }
    }

    // A newer LaunchHeim is out. Small on purpose: it's news, not an alarm. The dialog says the rest.
    ItemDelegate {
      id: updateLink
      visible: Vm.updates.available
      hoverEnabled: true
      Layout.fillWidth: true
      Layout.topMargin: 6
      leftPadding: 10
      rightPadding: 10
      topPadding: 6
      bottomPadding: 6
      onClicked: Vm.updates.openDialog()

      background: Rectangle {
        radius: Theme.radius
        color: updateLink.hovered ? Theme.alpha(Theme.accent, 0.15) : "transparent"
      }

      contentItem: RowLayout {
        spacing: 8
        Icon { iconName: "update"; size: 16; color: Theme.accent }
        Label {
          text: Vm.updates.shortText
          color: Theme.accent
          font.pointSize: Theme.small
          font.underline: updateLink.hovered
          wrapMode: Text.Wrap
          Layout.fillWidth: true
        }
      }

      ToolTip.visible: hovered
      ToolTip.text: "How to update"
    }

    Card {
      id: statusCard
      Layout.fillWidth: true
      Layout.topMargin: 6
      implicitHeight: statusRow.implicitHeight + 20
      // While a launch waits for Steam, the card says so instead of "Valheim ready".
      readonly property bool waiting: App.steamStatus.length > 0
      color: App.isGameRunning ? Theme.alpha(Theme.positive, 0.15) : waiting ? Theme.alpha(Theme.accent, 0.15) : Theme.card

      RowLayout {
        id: statusRow
        anchors.fill: parent
        anchors.margins: 10
        spacing: 10

        Rectangle {
          width: 10
          height: 10
          radius: 5
          color: App.isGameRunning ? Theme.positive : statusCard.waiting ? Theme.accent : Theme.textMuted

          SequentialAnimation on opacity {
            running: App.isGameRunning || statusCard.waiting
            loops: Animation.Infinite
            NumberAnimation { to: 0.3; duration: 800 }
            NumberAnimation { to: 1; duration: 800 }
          }
        }

        ColumnLayout {
          spacing: 0
          Layout.fillWidth: true

          Label {
            text: App.isGameRunning ? "Valheim is running" : statusCard.waiting ? App.steamStatus : Vm.settings.gameFound ? "Valheim ready" : "Valheim not found"
            font.bold: true
          }

          Label {
            text: App.isGameRunning ? App.runningName : statusCard.waiting ? App.steamStatusDetail : Vm.settings.gameFound ? App.lastPlayName : "Set the folder in Settings"
            color: Theme.textMuted
            font.pointSize: Theme.small
            elide: Text.ElideRight
            Layout.fillWidth: true
          }
        }

        // While the game runs, the moment its log matters. Otherwise the instance page has the button.
        IconButton {
          iconName: "log"
          tip: "Console: follow the game's log live"
          visible: App.isGameRunning
          onClicked: App.openConsole()
        }

        // Starts again what was played last: a server or world with its character and setup, an
        // instance, or vanilla before anything was played.
        IconButton {
          iconName: "play"
          tip: "Play " + App.lastPlayName
          visible: !App.isGameRunning && !statusCard.waiting && Vm.settings.gameFound
          onClicked: App.playLast()
        }
      }
    }
  }
}
