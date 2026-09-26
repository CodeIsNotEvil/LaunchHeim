import QtQuick 2.15
import QtQuick.Controls 2.15

LhButton {
  property string tip: ""

  kind: "ghost"
  text: ""
  implicitWidth: implicitHeight
  ToolTip.visible: hovered && tip.length > 0
  ToolTip.text: tip
  ToolTip.delay: 500
}
