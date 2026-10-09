import QtQuick 2.15

// An icon tile: letters on a color, or a picture once one is set (a mod's downloaded icon, or the one
// chosen for an instance, server or world). Without letters it shows the glyph, if any.
Rectangle {
  property string initials: ""
  property color tint: Theme.accent
  property string imageSource: ""
  /// Drawn when there are no letters: the server or world symbol on the Play page.
  property string glyph: ""
  /// The quiet default of a server or world: the tint faint behind, letters or glyph in the tint.
  property bool plain: false

  radius: Math.round(width * 0.22)
  clip: true
  // Always the one inline gradient: a gradient switched on and off through a binding (gradient: plain ?
  // null : …) drew nothing at all in Qt 5. Plain gives both stops the same faint tint.
  gradient: Gradient {
    GradientStop { position: 0; color: plain ? Theme.alpha(tint, 0.18) : Qt.lighter(tint, 1.15) }
    GradientStop { position: 1; color: plain ? Theme.alpha(tint, 0.18) : Qt.darker(tint, 1.45) }
  }

  Text {
    anchors.centerIn: parent
    visible: image.status !== Image.Ready && parent.initials.length > 0
    text: parent.initials
    color: parent.plain ? parent.tint : "white"
    font.family: Theme.fontFamily
    font.bold: true
    font.pixelSize: parent.height * 0.38
  }

  Icon {
    anchors.centerIn: parent
    visible: image.status !== Image.Ready && parent.initials.length === 0 && parent.glyph.length > 0
    iconName: parent.glyph
    color: parent.plain ? parent.tint : "white"
    size: Math.round(parent.height * 0.43)
  }

  Image {
    id: image
    anchors.fill: parent
    source: parent.imageSource
    visible: status === Image.Ready
    fillMode: Image.PreserveAspectCrop
    asynchronous: true
    smooth: true
    mipmap: true
    sourceSize: Qt.size(width * 2, height * 2)
  }
}
