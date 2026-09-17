import CoreGraphics
import Foundation

enum MonitorGeometry {
  static func frame(screen: CGRect, visible: CGRect, safeTop: CGFloat, contentWidth: CGFloat)
    -> CGRect
  {
    let width = min(max(160, contentWidth + 24), visible.width)
    let height = min(24, visible.height)
    let x = max(visible.minX, min(screen.midX - width / 2, visible.maxX - width))
    let y = max(visible.minY, min(visible.maxY, screen.maxY - safeTop) - height - 2)
    return CGRect(x: x, y: y, width: width, height: height)
  }
}
