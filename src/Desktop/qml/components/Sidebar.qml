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

      Image {
        source: Qt.resolvedUrl("../icons/logo.svg")
        sourceSize: Qt.size(36, 36)
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

    Card {
      Layout.fillWidth: true
      Layout.topMargin: 6
      implicitHeight: statusRow.implicitHeight + 20
      color: App.isGameRunning ? Theme.alpha(Theme.positive, 0.15) : Theme.card

      RowLayout {
        id: statusRow
        anchors.fill: parent
        anchors.margins: 10
        spacing: 10

        Rectangle {
          width: 10
          height: 10
          radius: 5
          color: App.isGameRunning ? Theme.positive : Theme.textMuted

          SequentialAnimation on opacity {
            running: App.isGameRunning
            loops: Animation.Infinite
            NumberAnimation { to: 0.3; duration: 800 }
            NumberAnimation { to: 1; duration: 800 }
          }
        }

        ColumnLayout {
          spacing: 0
          Layout.fillWidth: true

          Label {
            text: App.isGameRunning ? "Valheim is running" : Vm.settings.gameFound ? "Valheim ready" : "Valheim not found"
            font.bold: true
          }

          Label {
            text: App.isGameRunning ? App.runningName : Vm.settings.gameFound ? "Vanilla, without mods" : "Set the folder in Settings"
            color: Theme.textMuted
            font.pointSize: Theme.small
            elide: Text.ElideRight
            Layout.fillWidth: true
          }
        }

        IconButton {
          iconName: "play"
          tip: "Play vanilla Valheim"
          visible: !App.isGameRunning && Vm.settings.gameFound
          onClicked: App.launchVanilla()
        }
      }
    }
  }
}
