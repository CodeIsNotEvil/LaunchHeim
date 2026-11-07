import QtQuick 2.15
import QtQuick.Controls 2.15

// A text box in LaunchHeim's style. With errorText set it's outlined red and says what's wrong right
// under the box, taking that room from the layout, so a form shows each missing field where it is.
TextField {
  id: field

  property string errorText: ""
  readonly property bool hasError: errorText.length > 0
  readonly property int errorHeight: hasError ? errorLabel.implicitHeight + 4 : 0

  implicitHeight: 38 + errorHeight
  // Material pads 8 above and 16 below to leave room for its underline. The box here has none, so the
  // text (and the placeholder, which follows these) sat 4 px high; equal padding centres it. The error
  // line below the box adds to the bottom padding, so the text stays inside the box.
  topPadding: 8
  bottomPadding: 8 + errorHeight
  leftPadding: 12
  rightPadding: 12
  selectByMouse: true
  color: Theme.text
  placeholderTextColor: Theme.textMuted
  verticalAlignment: TextInput.AlignVCenter

  background: Item {
    implicitHeight: field.implicitHeight

    Rectangle {
      width: parent.width
      height: 38
      radius: Theme.smallRadius + 2
      color: Theme.view
      border.width: field.activeFocus || field.hasError ? 2 : 1
      border.color: field.hasError ? Theme.negative : field.activeFocus ? Theme.accent : Theme.divider
    }

    Text {
      id: errorLabel
      y: 41
      width: parent.width
      visible: field.hasError
      text: field.errorText
      color: Theme.negative
      font.family: Theme.fontFamily
      font.pointSize: Theme.small
      wrapMode: Text.Wrap
    }
  }
}
