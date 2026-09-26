import QtQuick 2.15

// An instance's initials on its color, or a mod's icon once it has been downloaded.
Rectangle {
  property string initials: ""
  property color tint: Theme.accent
  property string imageSource: ""

  radius: Math.round(width * 0.22)
  clip: true
  gradient: Gradient {
    GradientStop { position: 0; color: Qt.lighter(tint, 1.15) }
    GradientStop { position: 1; color: Qt.darker(tint, 1.45) }
  }

  Text {
    anchors.centerIn: parent
    visible: image.status !== Image.Ready
    text: parent.initials
    color: "white"
    font.bold: true
    font.pixelSize: parent.height * 0.38
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
