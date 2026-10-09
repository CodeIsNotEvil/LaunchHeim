import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

RowLayout {
  property alias title: titleLabel.text
  property alias subtitle: subtitleLabel.text
  default property alias actions: actionRow.data

  spacing: Theme.gap

  ColumnLayout {
    spacing: 2
    Layout.fillWidth: true

    // The sentence under the title only shows while the pointer is over the title or the place where the
    // sentence goes, so a page opens with less text. Its space stays reserved (opacity, not visible), so
    // the page doesn't jump when it appears.
    HoverHandler { id: hover }

    // Both labels fill, otherwise this nested layout could not grow and the actions would not sit at
    // the right edge (a layout is never wider than its children allow).
    Label {
      id: titleLabel
      font.pointSize: Theme.title
      font.bold: true
      elide: Text.ElideRight
      Layout.fillWidth: true
    }

    Label {
      id: subtitleLabel
      color: Theme.textMuted
      visible: text.length > 0
      opacity: hover.hovered ? 1 : 0
      wrapMode: Text.Wrap
      Layout.fillWidth: true

      Behavior on opacity { NumberAnimation { duration: 150 } }
    }
  }

  RowLayout {
    id: actionRow
    spacing: 8
  }
}
