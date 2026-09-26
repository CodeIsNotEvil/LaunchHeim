import QtQuick 2.15
import LaunchHeim 1.0
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Drawer {
  id: drawer

  readonly property var details: Vm.details

  edge: Qt.RightEdge
  width: Math.min(680, parent.width * 0.6)
  height: parent.height
  visible: Vm.browse.detailsOpen
  onClosed: Vm.browse.closeDetails()

  background: Rectangle {
    color: Theme.window
    Rectangle { width: 1; height: parent.height; color: Theme.divider }
  }

  Loader {
    anchors.fill: parent
    active: drawer.details !== null && drawer.details !== undefined
    sourceComponent: detailsContent
  }

  Component {
    id: detailsContent

    ColumnLayout {
      readonly property var d: drawer.details

      spacing: 0

      // Header
      ColumnLayout {
        spacing: 14
        Layout.fillWidth: true
        Layout.margins: 24

        RowLayout {
          spacing: 18

          Avatar {
            width: 88
            height: 88
            initials: d.initial
            tint: Theme.sourceColor(d.source)
            imageSource: d.iconSource
          }

          ColumnLayout {
            spacing: 4
            Layout.fillWidth: true

            Label { text: d.name; font.pointSize: Theme.title; font.bold: true; elide: Text.ElideRight; Layout.fillWidth: true }
            Label { text: "by " + d.author + (d.version.length > 0 ? "  ·  v" + d.version : ""); color: Theme.textMuted }

            RowLayout {
              spacing: 14
              Layout.topMargin: 4
              Stat { iconName: "download"; text: d.downloadsText }
              Stat { iconName: "star"; text: d.likesText }
              Stat { iconName: "clock"; text: "Updated " + d.updatedText }
            }
          }

          IconButton { iconName: "close"; tip: "Close"; Layout.alignment: Qt.AlignTop; onClicked: drawer.close() }
        }

        Label {
          text: d.shortDescription
          wrapMode: Text.Wrap
          Layout.fillWidth: true
        }

        RowLayout {
          spacing: 8

          LhButton {
            visible: !d.isInstalled || d.hasUpdate
            text: (d.hasUpdate ? "Update in " : "Install into ") + Vm.browse.targetName
            iconName: d.hasUpdate ? "update" : "download"
            kind: "primary"
            enabled: Vm.browse.targetName.length > 0
            onClicked: d.install()
          }

          Badge { visible: d.isInstalled; text: "Installed " + d.installedVersion; tint: d.hasUpdate ? Theme.neutral : Theme.positive }

          LhButton { text: "Open website"; iconName: "external"; kind: "ghost"; onClicked: d.openWebsite() }

          Item { Layout.fillWidth: true }

          Label { text: d.categories; color: Theme.textMuted; font.pointSize: Theme.small; elide: Text.ElideRight; Layout.maximumWidth: 220 }
        }
      }

      TabBar {
        id: detailTabs
        Layout.fillWidth: true
        Layout.leftMargin: 24
        Layout.rightMargin: 24
        background: Rectangle {
          color: "transparent"
          Rectangle { anchors.bottom: parent.bottom; width: parent.width; height: 1; color: Theme.divider }
        }

        TabButton { text: "Description"; width: implicitWidth + 24; font.capitalization: Font.MixedCase }
        TabButton {
          text: (d.source === "thunderstore" ? "Versions (" : "Files (") + Vm.detailFiles.length + ")"
          width: implicitWidth + 24
          font.capitalization: Font.MixedCase
        }
      }

      StackLayout {
        currentIndex: detailTabs.currentIndex
        Layout.fillWidth: true
        Layout.fillHeight: true

        Flickable {
          id: descriptionFlick
          clip: true
          contentHeight: description.implicitHeight + 48
          boundsBehavior: Flickable.StopAtBounds
          ScrollBar.vertical: ScrollBar {}

          Text {
            id: description
            x: 24
            y: 20
            width: descriptionFlick.width - 48
            // linkColor only applies to StyledText; rich text takes its link color from CSS.
            text: "<style>a { color: " + Theme.link + "; } td, th { padding: 2px 6px; } pre, code { background-color: " + Theme.viewAlt + "; }</style>" + d.descriptionHtml
            textFormat: Text.RichText
            wrapMode: Text.Wrap
            color: Theme.text
            linkColor: Theme.link
            font.family: Theme.fontFamily
            font.pointSize: Theme.fontPointSize
            onLinkActivated: d.openLink(link)

            MouseArea {
              anchors.fill: parent
              acceptedButtons: Qt.NoButton
              cursorShape: parent.hoveredLink.length > 0 ? Qt.PointingHandCursor : Qt.ArrowCursor
            }
          }

          BusyIndicator {
            anchors.horizontalCenter: parent.horizontalCenter
            y: 60
            running: d.isLoading
            visible: running
          }

          Label {
            x: 24
            y: 20
            visible: d.error.length > 0
            text: d.error
            color: Theme.negative
            wrapMode: Text.Wrap
            width: descriptionFlick.width - 48
          }
        }

        ListView {
          id: fileList
          clip: true
          spacing: 6
          topMargin: 16
          bottomMargin: 16
          model: Vm.detailFiles
          ScrollBar.vertical: ScrollBar {}

          delegate: Card {
            width: fileList.width - 48
            x: 24
            implicitHeight: fileRow.implicitHeight + 24

            RowLayout {
              id: fileRow
              anchors.fill: parent
              anchors.margins: 12
              spacing: 12

              ColumnLayout {
                spacing: 3
                Layout.fillWidth: true

                ElidedTitle {
                  text: modelData.name
                  font.bold: true
                  Layout.fillWidth: true

                  Badge { visible: modelData.isRecommended; text: "Recommended"; tint: Theme.accent }
                  Badge { visible: !modelData.isRecommended && modelData.category.length > 0; text: modelData.category; tint: Theme.textMuted }
                }

                Label {
                  text: (modelData.version.length > 0 ? "v" + modelData.version + "  ·  " : "") + modelData.dateText + (modelData.sizeText.length > 0 ? "  ·  " + modelData.sizeText : "")
                  color: Theme.textMuted
                  font.pointSize: Theme.small
                }

                Text {
                  visible: modelData.descriptionHtml.length > 0
                  text: modelData.descriptionHtml
                  textFormat: Text.RichText
                  wrapMode: Text.Wrap
                  maximumLineCount: 3
                  elide: Text.ElideRight
                  color: Theme.textMuted
                  font.pointSize: Theme.small
                  Layout.fillWidth: true
                }
              }

              Badge { visible: modelData.isInstalled; text: "Installed"; tint: Theme.positive }

              LhButton {
                visible: !modelData.isInstalled
                text: "Install"
                iconName: "download"
                kind: modelData.isRecommended ? "primary" : "secondary"
                enabled: Vm.browse.targetName.length > 0
                onClicked: modelData.install()
              }
            }
          }
        }
      }
    }
  }
}
