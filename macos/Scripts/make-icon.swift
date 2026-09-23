// Llumi's approved Raspberry gauge. Native deterministic export; no screenshot inputs.
import AppKit
let directory = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
func draw(_ pixels: Int, light: Bool = false, mono: Bool = false) -> Data {
  let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
  NSGraphicsContext.saveGraphicsState()
  NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
  let context = NSGraphicsContext.current!.cgContext
  context.scaleBy(x: CGFloat(pixels) / 1024, y: CGFloat(pixels) / 1024)
  context.translateBy(x: 0, y: 1024); context.scaleBy(x: 1, y: -1)
  if !mono {
    let background = CGPath(roundedRect: CGRect(x: 64, y: 64, width: 896, height: 896), cornerWidth: 210, cornerHeight: 210, transform: nil)
    context.saveGState(); context.addPath(background); context.clip()
    let colors = light ? [NSColor(white: 0.995, alpha: 1).cgColor, NSColor(white: 0.94, alpha: 1).cgColor]
      : [NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.16, alpha: 1).cgColor, NSColor(calibratedRed: 0.025, green: 0.035, blue: 0.045, alpha: 1).cgColor]
    context.drawLinearGradient(CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors as CFArray, locations: [0, 1])!, start: CGPoint(x:512,y:64), end:CGPoint(x:512,y:960), options: [])
    context.restoreGState()
  }
  let arc = CGMutablePath()
  arc.move(to: CGPoint(x:215,y:625))
  arc.addCurve(to:CGPoint(x:809,y:625),control1:CGPoint(x:215,y:229),control2:CGPoint(x:809,y:229))
  let gauge = arc.copy(strokingWithWidth: 92, lineCap: .round, lineJoin: .round, miterLimit: 1)
  let ink = light ? NSColor(white:0.045,alpha:1) : NSColor(white:0.98,alpha:1)
  if !mono {
    context.saveGState()
    context.setShadow(offset:.zero,blur:32,color:NSColor(calibratedRed:0.85,green:0.1,blue:0.55,alpha:0.2).cgColor)
    context.addPath(gauge);context.setFillColor(NSColor(calibratedRed:0.68,green:0.1,blue:0.75,alpha:1).cgColor);context.fillPath()
    context.restoreGState()
  }
  context.saveGState();context.addPath(gauge);context.clip()
  if mono { context.setFillColor(ink.cgColor);context.fill(CGRect(x:0,y:0,width:1024,height:1024)) }
  else {
    let colors=[NSColor(calibratedRed:0.57,green:0.14,blue:0.98,alpha:1).cgColor,NSColor(calibratedRed:0.91,green:0.22,blue:0.94,alpha:1).cgColor,NSColor(calibratedRed:1,green:0.16,blue:0.48,alpha:1).cgColor]
    context.drawLinearGradient(CGGradient(colorsSpace:CGColorSpaceCreateDeviceRGB(),colors:colors as CFArray,locations:[0,0.52,1])!,start:CGPoint(x:170,y:0),end:CGPoint(x:855,y:0),options:[])
  }
  context.restoreGState()
  let needle=CGMutablePath();needle.move(to:CGPoint(x:478,y:598));needle.addLine(to:CGPoint(x:710,y:425));needle.addLine(to:CGPoint(x:548,y:653));needle.closeSubpath()
  context.setFillColor(ink.cgColor);context.addPath(needle);context.fillPath()
  context.fillEllipse(in:CGRect(x:464,y:587,width:100,height:100))
  NSGraphicsContext.restoreGraphicsState()
  return bitmap.representation(using:.png,properties:[:])!
}
for size in [16,32,128,256,512] {
  for scale in [1,2] {
    try draw(size*scale).write(to:directory.appendingPathComponent("icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"))
  }
}
for size in [16,20,24,32,40,48,64,128,256] {
  try draw(size).write(to:directory.appendingPathComponent("windows-\(size).png"))
}
for (name,light,mono) in [("dark",false,false),("light",true,false),("mono-dark",false,true),("mono-light",true,true)] {
  try draw(1024,light:light,mono:mono).write(to:directory.appendingPathComponent(name+".png"))
}
