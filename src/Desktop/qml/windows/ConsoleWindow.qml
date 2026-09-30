import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Controls.Material 2.15
import QtQuick.Layouts 1.15
import "../components"

// The console: a second top-level window that follows one log live (BepInEx, Unity or LaunchHeim's
// own), so it can sit next to the game. The lines come from Vm.debugConsole.poll() as JSON batches
// and are only ever appended to a QML ListModel, which keeps the scroll position while the log grows.
ApplicationWindow {
  id: window

  readonly property var log: Vm.debugConsole
  readonly property Item screenshotRoot: contentRoot
  // Scrolled to the bottom and staying there as lines arrive. Scrolling up stops it.
  property bool follow: true
  // Set while the window moves the view itself, so that doesn't count as the user scrolling away.
  property bool autoScrolling: false

  readonly property var sources: [
    { key: "bepinex", name: "BepInEx" },
    { key: "unity", name: "Unity" },
    { key: "app", name: "LaunchHeim" }
  ]

  function sourceName(key) {
    for (var i = 0; i < sources.length; i++)
      if (sources[i].key === key)
        return sources[i].name
    return key
  }

  function pull() {
    var batch = JSON.parse(log.poll())
    if (batch.reset) {
      lines.clear()
      follow = true
    }

    if (batch.lines.length > 0) {
      autoScrolling = true
      lines.append(batch.lines.map(function(line) { return { t: line[0], l: line[1] } }))
      var excess = lines.count - log.maxLineCount
      if (excess > 0)
        lines.remove(0, excess)
      autoScrolling = false
    }

    if (follow && (batch.reset || batch.lines.length > 0))
      Qt.callLater(scrollToEnd)
  }

  function scrollToEnd() {
    autoScrolling = true
    list.positionViewAtEnd()
    autoScrolling = false
    follow = true
  }

  function copyShown() {
    clipboard.text = log.shownText()
    clipboard.selectAll()
    clipboard.copy()
    clipboard.text = ""
  }

  title: sourceName(log.source) + " log" + (log.source === "bepinex" && log.hasInstance ? " — " + log.instanceName : "") + " — LaunchHeim"
  width: 1040
  height: 660
  minimumWidth: 680
  minimumHeight: 380
  visible: log.visible
  color: Theme.window

  font.family: Theme.fontFamily
  font.pointSize: Theme.fontPointSize
  Material.theme: Theme.dark ? Material.Dark : Material.Light
  Material.accent: Theme.accent
  Material.primary: Theme.accent
  Material.background: Theme.window

  onClosing: log.close()

  Connections {
    target: window.log
    function onRaiseRequested() {
      window.show()
      window.raise()
      window.requestActivate()
    }
  }

  Timer {
    interval: 300
    repeat: true
    running: window.visible
    triggeredOnStart: true
    onTriggered: window.pull()
  }

  Shortcut { sequence: StandardKey.Find; onActivated: filterField.forceActiveFocus() }

  // TextEdit is the only way QML 2 reaches the clipboard.
  TextEdit { id: clipboard; visible: false }

  ListModel { id: lines }

  // An opaque root, like the main window's, so LAUNCHHEIM_SCREENSHOT has something to grab.
  Rectangle {
    id: contentRoot
    anchors.fill: parent
    color: Theme.window

    ColumnLayout {
      anchors.fill: parent
      anchors.margins: 16
      spacing: 12

      RowLayout {
        spacing: 10
        Layout.fillWidth: true

        // Which log, as a segmented control like the mod-site switcher on Browse.
        Rectangle {
          implicitWidth: sourceRow.implicitWidth + 8
          implicitHeight: 38
          radius: Theme.smallRadius + 4
          color: Theme.view
          border.width: 1
          border.color: Theme.divider

          Row {
            id: sourceRow
            anchors.centerIn: parent
            spacing: 2

            Repeater {
              model: window.sources

              delegate: AbstractButton {
                id: segment

                readonly property bool active: window.log.source === modelData.key

                implicitHeight: 30
                implicitWidth: segmentLabel.implicitWidth + 28
                hoverEnabled: true
                onClicked: window.log.setSource(modelData.key)

                background: Rectangle {
                  radius: Theme.smallRadius + 2
                  color: segment.active ? Theme.accentSoft : segment.hovered ? Theme.hover : "transparent"
                  border.width: segment.active ? 1 : 0
                  border.color: Theme.alpha(Theme.accent, 0.6)
                }

                contentItem: Text {
                  id: segmentLabel
                  text: modelData.name
                  color: Theme.text
                  font.bold: segment.active
                  font.family: Theme.fontFamily
                  font.pointSize: Theme.fontPointSize
                  horizontalAlignment: Text.AlignHCenter
                  verticalAlignment: Text.AlignVCenter
                }
              }
            }
          }
        }

        Label {
          text: window.log.source === "bepinex" && window.log.hasInstance ? window.log.instanceName : ""
          visible: text.length > 0
          font.bold: true
          elide: Text.ElideRight
          Layout.maximumWidth: 260
        }

        Item { Layout.fillWidth: true }

        // Clicking a count shows only the problems, the usual first step when a modpack misbehaves.
        Badge {
          text: window.log.errorCount + (window.log.errorCount === 1 ? " error" : " errors")
          tint: Theme.negative
          solid: window.log.problemsOnly
          opacity: window.log.errorCount > 0 ? 1 : 0.5
          MouseArea { anchors.fill: parent; cursorShape: Qt.PointingHandCursor; onClicked: window.log.problemsOnly = !window.log.problemsOnly }
        }

        Badge {
          text: window.log.warningCount + (window.log.warningCount === 1 ? " warning" : " warnings")
          tint: Theme.neutral
          solid: window.log.problemsOnly
          opacity: window.log.warningCount > 0 ? 1 : 0.5
          MouseArea { anchors.fill: parent; cursorShape: Qt.PointingHandCursor; onClicked: window.log.problemsOnly = !window.log.problemsOnly }
        }
      }

      RowLayout {
        spacing: 10
        Layout.fillWidth: true

        SearchField {
          id: filterField
          placeholderText: "Filter lines (Ctrl+F)"
          text: window.log.filter
          Layout.preferredWidth: 300
          onTextChanged: {
            if (window.log.filter !== text) {
              window.log.filter = text
              window.pull()
            }
          }
        }

        LhSwitch {
          text: "Only warnings and errors"
          checked: window.log.problemsOnly
          onToggled: { window.log.problemsOnly = checked; window.pull() }
        }

        Item { Layout.fillWidth: true }

        IconButton { iconName: "copy"; tip: "Copy the lines shown"; enabled: lines.count > 0; onClicked: window.copyShown() }
        IconButton { iconName: "trash"; tip: "Clear the window (the file stays)"; onClicked: { window.log.clear(); window.pull() } }
        IconButton { iconName: "file"; tip: "Open the log file"; enabled: window.log.fileExists; onClicked: window.log.openFile() }
        IconButton { iconName: "folder"; tip: "Open the folder"; onClicked: window.log.openFolder() }
      }

      Card {
        Layout.fillWidth: true
        Layout.fillHeight: true
        color: Theme.view

        ListView {
          id: list
          anchors.fill: parent
          anchors.margins: 8
          clip: true
          model: lines
          boundsBehavior: Flickable.StopAtBounds
          // Lines are selectable text, so the mouse selects rather than drags; the wheel and bar scroll.
          interactive: false
          ScrollBar.vertical: ScrollBar { id: bar }

          onContentYChanged: {
            if (!window.autoScrolling)
              window.follow = atYEnd
          }

          delegate: Rectangle {
            width: list.width - bar.width - 4
            height: lineText.implicitHeight + 2
            color: model.l === "e" ? Theme.alpha(Theme.negative, 0.12) : model.l === "w" ? Theme.alpha(Theme.neutral, 0.10) : "transparent"

            TextEdit {
              id: lineText
              width: parent.width - 12
              x: 6
              y: 1
              text: model.t
              textFormat: TextEdit.PlainText
              readOnly: true
              selectByMouse: true
              wrapMode: TextEdit.WrapAnywhere
              color: model.l === "e" ? Theme.negative : model.l === "w" ? Theme.neutral : model.l === "d" ? Theme.textMuted : Theme.text
              selectionColor: Theme.accent
              selectedTextColor: Theme.accentText
              font.family: Theme.fontFamily
              font.pointSize: Theme.small
            }
          }
        }

        // A non-interactive Flickable ignores the wheel, so it's handled here, over the list. Clicks pass
        // through to the lines.
        MouseArea {
          anchors.fill: list
          acceptedButtons: Qt.NoButton
          onWheel: {
            var delta = wheel.pixelDelta.y !== 0 ? wheel.pixelDelta.y : wheel.angleDelta.y
            var bottom = list.originY + list.contentHeight - list.height
            list.contentY = Math.max(list.originY, Math.min(bottom, list.contentY - delta))
          }
        }

        EmptyState {
          anchors.centerIn: parent
          width: Math.min(parent.width - 48, 520)
          visible: lines.count === 0
          iconName: "log"
          title: !window.log.fileExists ? "No log yet"
            : window.log.filter.length > 0 || window.log.problemsOnly ? "Nothing matches" : "Waiting for new lines"
          text: !window.log.fileExists ? window.log.missingText
            : window.log.filter.length > 0 || window.log.problemsOnly ? "No line matches the filter. Clear it to see the whole log."
            : "New lines show up here as they're written."
        }

        LhButton {
          anchors.right: parent.right
          anchors.bottom: parent.bottom
          anchors.margins: 16
          visible: !window.follow && lines.count > 0
          text: "Follow"
          iconName: "download"
          kind: "primary"
          onClicked: window.scrollToEnd()
        }
      }

      RowLayout {
        spacing: 10
        Layout.fillWidth: true

        Label {
          text: window.log.filePath
          color: Theme.textMuted
          font.pointSize: Theme.small
          elide: Text.ElideMiddle
          Layout.fillWidth: true
        }

        Label {
          text: lines.count + (lines.count === 1 ? " line" : " lines") + (window.follow ? " · following" : "")
          color: Theme.textMuted
          font.pointSize: Theme.small
        }
      }
    }
  }
}
