// Repaint app-icon artwork onto the macOS icon grid before it is packed into an .icns.
//
// macOS maps the .icns canvas onto a fixed region and keeps whatever transparent margin the
// artwork carries, so a tile that does not reach the canvas edges renders small, and the
// leftover canvas reads as a light border around the Dock icon. The sextant mark is authored
// as a rounded square sitting inside a wider canvas, so it never reaches those edges.
//
// We therefore crop to the artwork's own opaque tile, scale it to cover the square canvas, and
// clip the result to the grid radius. Cropping is what makes the tile reach the edges; the clip
// is what keeps macOS from shrinking it back. Together they land the icon on the grid.
//
// Usage: normalize-icon <src.png> <dst.png> <size>

import AppKit
import CoreGraphics
import ImageIO

let args = CommandLine.arguments
guard args.count == 4, let side = Int(args[3]), side > 0 else {
    FileHandle.standardError.write(Data("normalize-icon: usage: <src> <dst> <size>\n".utf8))
    exit(2)
}
guard let art = NSImage(contentsOfFile: args[1]),
      let source = art.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot read \(args[1])\n".utf8))
    exit(1)
}

// Redraw once into a known RGBA8 layout so the alpha channel can be read without worrying
// about the source's own byte order.
func rgba8(_ image: CGImage, width: Int, height: Int) -> CGContext? {
    CGContext(data: nil,
              width: width,
              height: height,
              bitsPerComponent: 8,
              bytesPerRow: 0,
              space: CGColorSpaceCreateDeviceRGB(),
              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
}

guard let flat = rgba8(source, width: source.width, height: source.height) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot flatten \(args[1])\n".utf8))
    exit(1)
}
flat.draw(source, in: CGRect(x: 0, y: 0, width: source.width, height: source.height))
guard let flatImage = flat.makeImage(),
      let data = flatImage.dataProvider?.data,
      let ptr = CFDataGetBytePtr(data) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot read pixels\n".utf8))
    exit(1)
}

// Bounding box of the opaque artwork, which is the tile we want to stretch across the grid.
let w = flatImage.width, h = flatImage.height, rowBytes = flatImage.bytesPerRow
var minX = w, minY = h, maxX = -1, maxY = -1
for y in 0..<h {
    let row = ptr + y * rowBytes
    for x in 0..<w where row[x * 4 + 3] > 127 {
        if x < minX { minX = x }
        if x > maxX { maxX = x }
        if y < minY { minY = y }
        if y > maxY { maxY = y }
    }
}
guard maxX >= minX, maxY >= minY else {
    FileHandle.standardError.write(Data("normalize-icon: artwork is empty\n".utf8))
    exit(1)
}
guard let tile = flatImage.cropping(to: CGRect(x: minX,
                                               y: minY,
                                               width: maxX - minX + 1,
                                               height: maxY - minY + 1)) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot crop\n".utf8))
    exit(1)
}

// 20% keeps the tile inside the radius macOS is willing to scale up to the grid.
let radius = Double(side) * 0.20

guard let ctx = rgba8(tile, width: side, height: side) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot create context\n".utf8))
    exit(1)
}

let canvas = CGRect(x: 0, y: 0, width: side, height: side)
ctx.clear(canvas)
ctx.saveGState()
ctx.addPath(CGPath(roundedRect: canvas, cornerWidth: radius, cornerHeight: radius, transform: nil))
ctx.clip()

// Scale to cover, centred, so the shorter axis meets the edge and the longer one is trimmed.
// A small bleed pushes the artwork past the canvas boundary: the clip anti-aliases whatever
// it cuts, and a half-transparent outermost row reads as a light fringe on the Dock icon.
let scale = max(Double(side) / Double(tile.width), Double(side) / Double(tile.height)) * 1.02
let drawn = CGSize(width: Double(tile.width) * scale, height: Double(tile.height) * scale)
ctx.draw(tile, in: CGRect(x: (Double(side) - drawn.width) / 2,
                          y: (Double(side) - drawn.height) / 2,
                          width: drawn.width,
                          height: drawn.height))
ctx.restoreGState()

guard let out = ctx.makeImage(),
      let dest = CGImageDestinationCreateWithURL(URL(fileURLWithPath: args[2]) as CFURL,
                                                "public.png" as CFString, 1, nil) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot write \(args[2])\n".utf8))
    exit(1)
}
CGImageDestinationAddImage(dest, out, nil)
guard CGImageDestinationFinalize(dest) else {
    FileHandle.standardError.write(Data("normalize-icon: finalize failed\n".utf8))
    exit(1)
}
