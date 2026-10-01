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

        // A RowLayout can't shrink below the sum of its fixed-width children, so on a card just
        // over the grid's 440 px column threshold the stats pushed the Install button (and the
        // whole column, titles included) past the card's edge. The stats now share the leftover
        // width instead, and any that would run under the button are hidden rather than clipped.
        // Opacity rather than visible, because hiding one would move the next and feed back into x.
        Item {
          id: stats
          implicitWidth: statsRow.implicitWidth
          implicitHeight: statsRow.implicitHeight
          Layout.fillWidth: true
          Layout.minimumWidth: 0

          Row {
            id: statsRow
            spacing: 14

            Stat { iconName: "download"; text: card.mod.downloadsText; opacity: x + width <= stats.width ? 1 : 0 }
            Stat { iconName: "star"; text: card.mod.likesText; opacity: x + width <= stats.width ? 1 : 0 }
            Stat { iconName: "clock"; text: card.mod.updatedText; opacity: x + width <= stats.width ? 1 : 0 }
          }
        }

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
