import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15

RowLayout {
  id: secret

  property alias text: field.text
  property alias placeholderText: field.placeholderText
  signal saved(string value)

  spacing: 8

  LhTextField {
    id: field
    echoMode: reveal.checked ? TextInput.Normal : TextInput.Password
    selectByMouse: true
    font.family: "monospace"
    Layout.fillWidth: true
    onAccepted: secret.saved(text)
  }

  IconButton {
    id: reveal
    checkable: true
    iconName: "key"
    tip: checked ? "Hide key" : "Show key"
  }

  LhButton { text: "Save"; kind: "primary"; onClicked: secret.saved(field.text) }
}
