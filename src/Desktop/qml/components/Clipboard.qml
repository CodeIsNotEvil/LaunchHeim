pragma Singleton
import QtQuick 2.15

// Copies text to the system clipboard. QML 2 has no clipboard API, only the copy() of its text items,
// so a hidden TextEdit holds the text for a moment. Every Copy button goes through here, and so does the
// Windows smoke test (packaging/windows/build.ps1), which reads the clipboard back.
//
// On Windows the clipboard needs the Qt thread to be a COM single-threaded apartment; see the
// [STAThread] on Program.Main.
Item {
  function copy(text) {
    edit.text = text
    edit.selectAll()
    edit.copy()
    edit.text = ""
  }

  TextEdit { id: edit; visible: false }
}
