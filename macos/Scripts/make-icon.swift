// Development icon: the existing AgentMeter three-bar identity rendered locally.
import AppKit
let directory = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
for size in [16,32,128,256,512] {
  for scale in [1,2] {
    let pixels = size * scale
    let image = NSImage(size: NSSize(width: pixels, height: pixels))
    image.lockFocus()
    let factor = CGFloat(pixels) / 1024
    let transform = NSAffineTransform(); transform.scale(by: factor); transform.concat()
    NSColor(white:0.94,alpha:1).setFill()
    NSBezierPath(roundedRect:NSRect(x:70,y:70,width:884,height:884),xRadius:196,yRadius:196).fill()
    for (index,height) in [240,400,570].enumerated() {
      NSColor(white:0.14 + CGFloat(2-index)*0.09,alpha:1).setFill()
      NSBezierPath(roundedRect:NSRect(x:245+index*190,y:227,width:145,height:height),xRadius:30,yRadius:30).fill()
    }
    image.unlockFocus()
    let bitmap = NSBitmapImageRep(data:image.tiffRepresentation!)!
    try bitmap.representation(using:.png,properties:[:])!.write(to:directory.appendingPathComponent("icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"))
  }
}
