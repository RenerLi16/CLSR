import AppKit
import CoreText
import ImageIO

let fontURL = URL(fileURLWithPath: "/Applications/MuseScore.app/Wrapper/MuseScore.app/Stockpile/Fonts/bravura/Bravura.otf")
CTFontManagerRegisterFontsForURL(fontURL as CFURL, .process, nil)

let width = 100
let height = 100
let colorSpace = CGColorSpaceCreateDeviceGray()
guard let context = CGContext(
    data: nil,
    width: width,
    height: height,
    bitsPerComponent: 8,
    bytesPerRow: width,
    space: colorSpace,
    bitmapInfo: CGImageAlphaInfo.none.rawValue
) else { fatalError("Unable to create bitmap context") }
context.setFillColor(gray: 1, alpha: 1)
context.fill(CGRect(x: 0, y: 0, width: width, height: height))
context.setFillColor(gray: 0, alpha: 1)

let font = CTFontCreateWithName("Bravura" as CFString, 56, nil)
var character: UniChar = 0xE260
var glyph: CGGlyph = 0
guard CTFontGetGlyphsForCharacters(font, &character, &glyph, 1), glyph != 0 else {
    fatalError("Flat glyph is unavailable")
}
var position = CGPoint(x: 20, y: 20)
CTFontDrawGlyphs(font, &glyph, &position, 1, context)

guard let cgImage = context.makeImage(),
      let destination = CGImageDestinationCreateWithURL(
        URL(fileURLWithPath: "/tmp/bravura-flat.png") as CFURL,
        "public.png" as CFString,
        1,
        nil
      ) else { fatalError("Unable to create PNG destination") }
CGImageDestinationAddImage(destination, cgImage, nil)
guard CGImageDestinationFinalize(destination) else {
    fatalError("Unable to write flat glyph")
}
