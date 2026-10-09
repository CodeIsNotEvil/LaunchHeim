import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

// A page's title with its actions at the right edge. No sentence under the title: what each page is
// for is obvious from the page itself, so it only added text to read (removed 2026-10-09).
RowLayout {
  property alias title: titleLabel.text
  default property alias actions: actionRow.data

  spacing: Theme.gap

  // Fills, so the actions sit at the right edge.
  Label {
    id: titleLabel
    font.pointSize: Theme.title
    font.bold: true
    elide: Text.ElideRight
    Layout.fillWidth: true
  }

  RowLayout {
    id: actionRow
    spacing: 8
  }
}
