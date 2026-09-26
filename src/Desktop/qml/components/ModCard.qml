import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

Card {
  id: card

  property var mod
  property string source: "thunderstore"

  hoverable: true
  hovered: mouse.containsMouse || installButton.hovered

  MouseArea {
    id: mouse
    anchors.fill: parent
    hoverEnabled: true
    cursorShape: Qt.PointingHandCursor
    onClicked: card.mod.openDetails()
  }

  RowLayout {
    anchors.fill: parent
    anchors.margins: 16
    spacing: 16

    Avatar {
      width: 76
      height: 76
      initials: card.mod.initial
      tint: Theme.sourceColor(card.source)
      imageSource: card.mod.iconSource
      Layout.alignment: Qt.AlignTop
    }

    ColumnLayout {
      spacing: 4
      Layout.fillWidth: true
      Layout.fillHeight: true

      RowLayout {
        spacing: 8
        Layout.fillWidth: true

        Label {
          text: card.mod.name
          font.bold: true
          font.pointSize: Theme.fontPointSize * 1.15
          elide: Text.ElideRight
          Layout.fillWidth: true
        }

        Badge { visible: card.mod.isDeprecated; text: "Deprecated"; tint: Theme.negative }
      }

      Label {
        text: "by " + card.mod.author + (card.mod.version.length > 0 ? "  ·  v" + card.mod.version : "")
        color: Theme.textMuted
        font.pointSize: Theme.small
        elide: Text.ElideRight
        Layout.fillWidth: true
      }

      Label {
        text: card.mod.description
        wrapMode: Text.Wrap
        maximumLineCount: 2
        elide: Text.ElideRight
        color: Theme.alpha(Theme.text, 0.85)
        Layout.fillWidth: true
        Layout.topMargin: 2
      }

      Item { Layout.fillHeight: true }

      RowLayout {
        spacing: 14
        Layout.fillWidth: true

        Stat { iconName: "download"; text: card.mod.downloadsText }
        Stat { iconName: "star"; text: card.mod.likesText }
        Stat { iconName: "clock"; text: card.mod.updatedText }

        Item { Layout.fillWidth: true }

        Badge {
          visible: card.mod.isInstalled && !card.mod.hasUpdate
          text: "Installed " + card.mod.installedVersion
          tint: Theme.positive
        }

        LhButton {
          id: installButton
          visible: !card.mod.isInstalled || card.mod.hasUpdate
          text: card.mod.hasUpdate ? "Update" : "Install"
          iconName: card.mod.hasUpdate ? "update" : "download"
          kind: card.mod.hasUpdate ? "secondary" : "primary"
          onClicked: card.mod.install()
        }
      }
    }
  }
}
