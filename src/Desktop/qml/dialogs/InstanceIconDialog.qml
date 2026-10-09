import QtQuick 2.15
import QtQuick.Controls 2.15
import QtQuick.Layouts 1.15
import Qt.labs.platform 1.1 as Platform
import "../components"

// Changes how an instance's icon looks: a color from Theme.instanceColors, its letters, or a picture in
// place of the letters. Everything is a draft shown in the preview until Save; empty color or letters
// mean automatic (from the id and the name).
LhDialog {
  id: dialog

  property var instance: null

  // The draft.
  property string draftColor: ""
  property string pictureUrl: ""
  property bool removePicture: false

  readonly property string shownColor: draftColor.length > 0 ? draftColor : (instance ? instance.automaticColor : Theme.accent)
  readonly property string shownPicture: pictureUrl.length > 0 ? pictureUrl : (removePicture || !instance ? "" : instance.iconUrl)

  title: "Change icon"
  width: 560

  onOpened: {
    draftColor = instance ? instance.customColor : ""
    lettersField.text = instance ? instance.customInitials : ""
    pictureUrl = ""
    removePicture = false
  }

  function save() {
    if (instance)
      instance.setIcon(draftColor, lettersField.text, pictureUrl, removePicture)
    dialog.close()
  }

  Platform.FileDialog {
    id: pictureDialog
    title: "Choose a picture for the icon"
    folder: Platform.StandardPaths.writableLocation(Platform.StandardPaths.PicturesLocation)
    nameFilters: ["Images (*.png *.jpg *.jpeg *.webp *.gif *.bmp)"]
    onAccepted: { dialog.pictureUrl = file.toString(); dialog.removePicture = false }
  }

  // One swatch: a color, or empty for automatic (drawn with the automatic color and an "A").
  component Swatch: Rectangle {
    id: swatch
    property string value: ""
    property string label: ""
    readonly property bool selected: dialog.draftColor === value

    // Small enough for automatic plus the twelve colors on one row of the 560-wide dialog.
    width: 30
    height: 30
    radius: width / 2
    color: value.length > 0 ? value : (dialog.instance ? dialog.instance.automaticColor : Theme.accent)
    border.width: selected ? 3 : 0
    border.color: Theme.text

    Text {
      anchors.centerIn: parent
      visible: swatch.value.length === 0
      text: "A"
      color: "white"
      font.family: Theme.fontFamily
      font.bold: true
    }

    MouseArea {
      id: swatchMouse
      anchors.fill: parent
      hoverEnabled: true
      cursorShape: Qt.PointingHandCursor
      onClicked: dialog.draftColor = swatch.value
    }

    ToolTip.visible: swatchMouse.containsMouse
    ToolTip.text: swatch.label
    ToolTip.delay: 400
  }

  ColumnLayout {
    anchors.fill: parent
    spacing: 16

    RowLayout {
      spacing: 20
      Layout.fillWidth: true

      Avatar {
        width: 96
        height: 96
        tint: dialog.shownColor
        initials: lettersField.text.trim().length > 0 ? lettersField.text.trim().slice(0, 3) : (dialog.instance ? dialog.instance.automaticInitials : "")
        imageSource: dialog.shownPicture
      }

      ColumnLayout {
        spacing: 8
        Layout.fillWidth: true

        Label { text: "Picture"; font.bold: true }
        Label {
          text: dialog.shownPicture.length > 0 ? "Shown instead of the color and letters." : "None. The color and letters below make the icon."
          color: Theme.textMuted
          wrapMode: Text.Wrap
          Layout.fillWidth: true
        }
        RowLayout {
          spacing: 8
          LhButton { text: "Choose picture…"; iconName: "image"; onClicked: pictureDialog.open() }
          LhButton {
            text: "Remove picture"
            iconName: "trash"
            kind: "ghost"
            visible: dialog.shownPicture.length > 0
            onClicked: { dialog.pictureUrl = ""; dialog.removePicture = true }
          }
        }
      }
    }

    Label { text: "Color"; font.bold: true }
    Flow {
      spacing: 8
      Layout.fillWidth: true

      Swatch { value: ""; label: "Automatic" }
      Repeater {
        model: Theme.instanceColors
        delegate: Swatch { value: modelData.color; label: modelData.name }
      }
    }

    Label { text: "Letters"; font.bold: true }
    LhTextField {
      id: lettersField
      placeholderText: (dialog.instance ? dialog.instance.automaticInitials : "") + " (from the name)"
      maximumLength: 3
      selectByMouse: true
      Layout.preferredWidth: 220
      onAccepted: dialog.save()
    }

    RowLayout {
      Layout.alignment: Qt.AlignRight
      Layout.topMargin: 4
      spacing: 8

      LhButton { text: "Cancel"; kind: "ghost"; onClicked: dialog.close() }
      LhButton { text: "Save"; kind: "primary"; onClicked: dialog.save() }
    }
  }
}
