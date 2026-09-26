import QtQuick 2.15
import QtQuick.Controls 2.15

// A title that elides only when it has to, followed by inline extras such as a version or badges.
// Deliberately built without layouts: a RowLayout in Qt 5 does not reliably keep a label at its
// natural width when it also has to be allowed to shrink, and truncates names that would fit.
Item {
  id: container

  property alias text: label.text
  property alias font: label.font
  property alias color: label.color
  property int spacing: 8
  default property alias trailing: rest.data

  implicitWidth: label.implicitWidth + spacing + rest.implicitWidth
  implicitHeight: Math.max(label.implicitHeight, rest.implicitHeight)

  Label {
    id: label
    width: Math.max(0, Math.min(implicitWidth, container.width - rest.implicitWidth - container.spacing))
    elide: Text.ElideRight
    anchors.verticalCenter: parent.verticalCenter
  }

  Row {
    id: rest
    x: label.width + container.spacing
    spacing: container.spacing
    anchors.verticalCenter: parent.verticalCenter
  }
}
