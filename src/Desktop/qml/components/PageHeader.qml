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
      wrapMode: Text.Wrap
      Layout.fillWidth: true
    }
  }

  RowLayout {
    id: actionRow
    spacing: 8
  }
}
