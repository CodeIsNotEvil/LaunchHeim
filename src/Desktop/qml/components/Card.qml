import QtQuick 2.15

Rectangle {
  property bool hoverable: false
  property bool hovered: false

  radius: Theme.radius
  color: hoverable && hovered ? Theme.cardHover : Theme.card
  border.width: 1
  border.color: Theme.divider

  Behavior on color { ColorAnimation { duration: 120 } }
}
