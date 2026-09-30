import QtQuick 2.15
import QtQuick.Window 2.15

// The LH logo with the glint of the website and the C profile picture: a light band crosses the
// letters diagonally, from top-left to bottom-right, in 0.96 s once every 3.84 s. That is the GIF's
// pace (about one logo-diagonal per second).
//
// The band is drawn on a Canvas clipped to the logo's own path, so only the letters light up. A
// Canvas rather than QtGraphicalEffects (a package the Linux builds don't depend on) or a
// ShaderEffect (which Qt's software renderer skips): it works on every backend. The logo itself stays
// a plain Image underneath, so it looks the same even if the Canvas couldn't draw.
Item {
  id: root

  implicitWidth: 36
  implicitHeight: 36

  // Where the band's centre is along the diagonal: 0 at the top-left corner, 1 at the bottom-right.
  // Outside (0, 1) nothing is drawn.
  property real progress: 0

  Image {
    anchors.fill: parent
    source: Qt.resolvedUrl("../icons/logo.svg")
    sourceSize: Qt.size(root.width, root.height)
  }

  Canvas {
    id: canvas
    anchors.fill: parent
    // In device pixels, so the band's edge along the letters stays sharp on HiDPI screens.
    canvasSize: Qt.size(width * Screen.devicePixelRatio, height * Screen.devicePixelRatio)

    onPaint: {
      var ctx = getContext("2d")
      ctx.reset()
      var p = root.progress
      if (p <= 0 || p >= 1) {
        return
      }

      var s = canvasSize.width
      // icons/logo.svg: a 512 x 512 view box, and the path's own transform inside it.
      ctx.scale(s / 512, s / 512)
      ctx.translate(43.45, 379.99)
      ctx.scale(0.35424, -0.35424)
      ctx.path = "M58 700V138L149 0H542V186H421V113H210L179 158V700ZM658 700H779V398L837 430H966L1021 398V700H1142V0H1021V276L993 318H796L779 299V0H658Z"
      ctx.clip()
      // Back to canvas pixels for the band. Only the transform is reset: restore() would also throw
      // the clip away, because the clip is part of the saved state.
      ctx.setTransform(1, 0, 0, 1, 0, 0)

      // A band 0.27 diagonals wide, the width of the GIF's, with the same brightness profile.
      var half = 0.135 * s
      var gradient = ctx.createLinearGradient(p * s - half, p * s - half, p * s + half, p * s + half)
      gradient.addColorStop(0, Qt.rgba(1, 1, 1, 0))
      gradient.addColorStop(0.307, Qt.rgba(1, 1, 1, 0.35))
      gradient.addColorStop(0.5, Qt.rgba(1, 1, 1, 0.85))
      gradient.addColorStop(0.693, Qt.rgba(1, 1, 1, 0.35))
      gradient.addColorStop(1, Qt.rgba(1, 1, 1, 0))
      ctx.fillStyle = gradient
      ctx.fillRect(0, 0, s, s)
    }
  }

  onProgressChanged: canvas.requestPaint()

  SequentialAnimation {
    // Only while LaunchHeim is the active window: nothing needs to shine behind a running game.
    running: Qt.application.state === Qt.ApplicationActive
    loops: Animation.Infinite
    PauseAnimation { duration: 2880 }
    NumberAnimation { target: root; property: "progress"; from: 0; to: 1; duration: 960 }
  }
}
