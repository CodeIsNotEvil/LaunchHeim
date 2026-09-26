import QtQuick 2.15
import QtQuick.Controls.impl 2.15

// Tints the monochrome SVGs in qml/icons with any color, the way Breeze icons follow the text color.
IconImage {
  property string iconName
  property int size: 18

  source: iconName.length > 0 ? Qt.resolvedUrl("../icons/" + iconName + ".svg") : ""
  sourceSize: Qt.size(size, size)
  width: size
  height: size
  color: Theme.text
}
