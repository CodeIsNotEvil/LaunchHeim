import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

Item {
  id: page

  readonly property var browse: Vm.browse

  ModDetailsDrawer {}

  ColumnLayout {
    anchors.fill: parent
    anchors.margins: 32
    anchors.bottomMargin: 16
    spacing: 16

    PageHeader {
      title: "Browse mods"
      subtitle: "Search Thunderstore, Nexus Mods and CurseForge. Dependencies and BepInEx are installed for you."
      Layout.fillWidth: true

      Label { text: "Install into"; color: Theme.textMuted }

      ComboBox {
        id: targetBox
        model: page.browse.targetNames.length > 0 ? page.browse.targetNames.split("\u001f") : []
        currentIndex: page.browse.targetIndex
        enabled: count > 0
        displayText: count > 0 ? currentText : "No instances"
        implicitWidth: 240
        // Qt 5.15's Material ComboBox binds its foreground to primaryTextColor, which reports a
        // binding loop on every start. Setting it here replaces that binding with the same colour.
        Material.foreground: Theme.text
        onActivated: page.browse.setTargetIndex(index)
      }
    }

    RowLayout {
      spacing: 12
      Layout.fillWidth: true

      SourceSwitcher {
        current: page.browse.source
        onSelected: page.browse.setSource(source)
      }

      SearchField {
        id: searchField
        placeholderText: "Search " + page.browse.sourceTitle
        Layout.fillWidth: true
        onAccepted: page.browse.submitQuery(text)
        onTextEdited: debounce.restart()

        Timer {
          id: debounce
          interval: 450
          onTriggered: page.browse.submitQuery(searchField.text)
        }
      }

      ComboBox {
        model: page.browse.sortLabels.split("\u001f")
        currentIndex: page.browse.sortIndex
        implicitWidth: 200
        Material.foreground: Theme.text
        onActivated: page.browse.sortIndex = index
      }
    }

    Banner {
      visible: page.browse.setupHint.length > 0
      kind: "warning"
      text: page.browse.setupHint
      actionText: "Open Settings"
      onActionTriggered: App.navigate("settings")
      Layout.fillWidth: true
    }

    Banner {
      visible: page.browse.notice.length > 0
      kind: "info"
      text: page.browse.notice
      actionText: "Add API key"
      onActionTriggered: App.navigate("settings")
      Layout.fillWidth: true
    }

    Banner {
      visible: page.browse.error.length > 0
      kind: "error"
      text: page.browse.error
      actionText: "Try again"
      onActionTriggered: page.browse.search()
      Layout.fillWidth: true
    }

    RowLayout {
      spacing: 12
      Layout.fillWidth: true

      Label { text: page.browse.resultSummary; font.bold: true }
      Label { text: page.browse.indexStatus; color: Theme.textMuted; font.pointSize: Theme.small; visible: text.length > 0 }
      IconButton {
        iconName: "refresh"
        tip: "Download the latest Thunderstore list"
        visible: page.browse.source === "thunderstore"
        implicitHeight: 28
        onClicked: page.browse.refreshIndex()
      }

      Item { Layout.fillWidth: true }

      IconButton { iconName: "previous"; tip: "Previous page"; enabled: page.browse.page > 0; onClicked: page.browse.previousPage() }
      Label { text: "Page " + (page.browse.page + 1) + " of " + page.browse.pageCount; color: Theme.textMuted }
      IconButton { iconName: "next"; tip: "Next page"; enabled: page.browse.page + 1 < page.browse.pageCount; onClicked: page.browse.nextPage() }
    }

    ProgressBar {
      indeterminate: true
      visible: page.browse.isLoading
      Layout.fillWidth: true
      Layout.topMargin: -10
      Layout.bottomMargin: -6
    }

    GridView {
      id: grid

      readonly property int columns: Math.max(1, Math.floor(width / 440))

      clip: true
      cellWidth: width / columns
      cellHeight: 172
      model: Vm.results
      boundsBehavior: Flickable.StopAtBounds
      Layout.fillWidth: true
      Layout.fillHeight: true
      ScrollBar.vertical: ScrollBar {}
      onModelChanged: positionViewAtBeginning()

      delegate: Item {
        width: grid.cellWidth
        height: grid.cellHeight

        ModCard {
          anchors.fill: parent
          anchors.margins: 6
          mod: modelData
          source: page.browse.source
        }
      }

      EmptyState {
        anchors.centerIn: parent
        visible: grid.count === 0 && !page.browse.isLoading && page.browse.setupHint.length === 0 && page.browse.error.length === 0
        iconName: "search"
        title: "Nothing found"
        text: "Try another search term, or look on another site."
      }

      BusyIndicator {
        anchors.centerIn: parent
        running: page.browse.isLoading && grid.count === 0
        visible: running
      }
    }
  }
}
