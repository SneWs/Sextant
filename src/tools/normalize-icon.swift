// Repaint app-icon artwork onto the macOS icon grid before it is packed into an .icns.
//
// macOS maps the .icns canvas onto a fixed region and keeps whatever transparent margin
// the artwork carries, so a tile whose corners are cut back too far ends up rendering
// small, with the leftover canvas reading as a light border around the icon. The sextant
// mark is authored as a rounded square whose corners are cut well past what the grid
// allows, so we redraw the tile at the grid radius and let the artwork's own purple bleed
// into the corners instead of asking the designer to redraw the mark.
//
// The foreground keeps the artwork at its natural geometry, so the mark is never rescaled.
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
      let cg = art.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot read \(args[1])\n".utf8))
    exit(1)
}

// 20% keeps the tile inside the radius macOS is willing to scale up to the grid.
let radius = Double(side) * 0.20
// Blow the artwork up past the corners so its own purple, gradient and all, fills the
// tile where the artwork is transparent. The mark sits well inside this and stays hidden.
let backdrop: Double = 1.31

guard let ctx = CGContext(data: nil,
                          width: side,
                          height: side,
                          bitsPerComponent: 8,
                          bytesPerRow: 0,
                          space: CGColorSpaceCreateDeviceRGB(),
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
    FileHandle.standardError.write(Data("normalize-icon: cannot create context\n".utf8))
    exit(1)
}

let canvas = CGRect(x: 0, y: 0, width: side, height: side)
ctx.clear(canvas)
ctx.saveGState()
ctx.addPath(CGPath(roundedRect: canvas, cornerWidth: radius, cornerHeight: radius, transform: nil))
ctx.clip()

let inset = Double(side) * (backdrop - 1) / 2
ctx.draw(cg, in: canvas.insetBy(dx: -inset, dy: -inset))
ctx.draw(cg, in: canvas)
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
