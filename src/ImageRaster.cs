using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;
using Svg;
using Svg.Skia;

namespace Sextant;

/// <summary>
/// Turns SVG, TIFF, and TGA into PNG for the image preview. Other formats are left as they are.
/// SVG scripts stay off, and the preview does not fetch images, entities, or files the document points at.
/// TIFF uses the first page. TGA is shown upright, and a file whose header has no alpha bits stays opaque.
/// </summary>
public static class ImageRaster
{
    private const int MaxSide = 1024;
    private const int MaxPixels = 16_000_000;
    private const float IconSide = 256f;
    private const float MaxScale = 32f;

    static ImageRaster()
    {
        // These are process-wide. The preview never turns them back on.
        SvgDocument.ResolveExternalImages = ExternalType.None;
        SvgDocument.ResolveExternalXmlEntites = ExternalType.None;
        SvgDocument.ResolveExternalElements = ExternalType.None;
        Tiff.SetErrorHandler(new QuietTiff());
    }

    public static byte[]? Prepare(string? path, byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        var ext = string.IsNullOrEmpty(path) ? "" : Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext is ".svg")
                return SvgToPng(data);
            if (ext is ".tif" or ".tiff")
                return TiffToPng(data);
            if (ext is ".tga" or ".targa")
                return TgaToPng(data);
        }
        catch (Exception)
        {
            return null;
        }

        return data;
    }

    private static byte[]? SvgToPng(byte[] data)
    {
        using var svg = new SKSvg();
        svg.Settings.EnableJavaScript = false;
        svg.Settings.EnableExternalJavaScript = false;
        svg.Settings.EnableBrokenImagePlaceholders = false;
        using var input = new MemoryStream(data, writable: false);
        var picture = svg.Load(input);
        if (picture is null)
            return null;
        var cull = picture.CullRect;
        if (!(cull.Width > 0) || !(cull.Height > 0) || float.IsInfinity(cull.Width) || float.IsInfinity(cull.Height))
            return null;
        var scale = Scale(cull.Width, cull.Height);
        var width = (int)Math.Ceiling(cull.Width * scale);
        var height = (int)Math.Ceiling(cull.Height * scale);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            return null;
        using var output = new MemoryStream();
        if (!svg.Save(output, SKColors.Transparent, SKEncodedImageFormat.Png, 100, scale, scale))
            return null;
        return output.ToArray();
    }

    private static float Scale(float width, float height)
    {
        var longSide = Math.Max(width, height);
        if (longSide > MaxSide)
            return MaxSide / longSide;
        if (longSide < IconSide)
            return Math.Min(MaxScale, IconSide / longSide);
        return 1f;
    }

    private static byte[]? TiffToPng(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var tif = Tiff.ClientOpen("preview", "r", stream, StreamTiff.Instance);
        if (tif is null)
            return null;
        var width = FieldInt(tif, TiffTag.IMAGEWIDTH);
        var height = FieldInt(tif, TiffTag.IMAGELENGTH);
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaxPixels)
            return null;
        var raster = new int[width * height];
        if (!tif.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT, false))
            return null;

        var pixels = new byte[raster.Length * 4];
        for (var i = 0; i < raster.Length; i++)
        {
            var pixel = raster[i];
            var offset = i * 4;
            pixels[offset] = (byte)(pixel & 0xff);
            pixels[offset + 1] = (byte)((pixel >> 8) & 0xff);
            pixels[offset + 2] = (byte)((pixel >> 16) & 0xff);
            pixels[offset + 3] = (byte)((pixel >> 24) & 0xff);
        }

        return RgbaToPng(width, height, pixels);
    }

    private static byte[]? TgaToPng(byte[] data)
    {
        if (data.Length < 18)
            return null;
        var colorMapType = data[1];
        var imageType = data[2];
        if (colorMapType is not (0 or 1))
            return null;
        var mapped = imageType is 1 or 9;
        var trueColor = imageType is 2 or 10;
        var gray = imageType is 3 or 11;
        if (!mapped && !trueColor && !gray)
            return null;

        var colorMapFirst = ReadU16(data, 3);
        var colorMapLength = ReadU16(data, 5);
        var colorMapBits = data[7];
        var width = ReadU16(data, 12);
        var height = ReadU16(data, 14);
        var depth = data[16];
        var descriptor = data[17];
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaxPixels)
            return null;
        if (mapped && (colorMapType != 1 || colorMapLength == 0 || depth is not (8 or 16)))
            return null;
        if (trueColor && depth is not (15 or 16 or 24 or 32))
            return null;
        if (gray && depth is not (8 or 16))
            return null;
        var pixelBytes = BytesPerPixel(depth);
        if (pixelBytes == 0)
            return null;

        var offset = 18 + data[0];
        if (offset > data.Length)
            return null;
        byte[]? palette = null;
        if (colorMapType == 1)
        {
            var entryBytes = BytesPerPixel(colorMapBits);
            if (colorMapBits is not (15 or 16 or 24 or 32) || entryBytes == 0)
                return null;
            var mapBytes = (long)colorMapLength * entryBytes;
            if (mapBytes > data.Length - offset)
                return null;
            if (mapped)
            {
                if ((long)colorMapFirst + colorMapLength > 65536)
                    return null;
                palette = new byte[colorMapLength * 4];
                for (var i = 0; i < colorMapLength; i++)
                {
                    if (!WriteTgaColor(data.AsSpan(offset + i * entryBytes, entryBytes), colorMapBits, descriptor & 0x0F, gray: false, palette.AsSpan(i * 4, 4)))
                        return null;
                }
            }

            offset += (int)mapBytes;
        }

        var count = width * height;
        var pixels = new byte[count * 4];
        var rightToLeft = (descriptor & 0x10) != 0;
        var topToBottom = (descriptor & 0x20) != 0;
        var alphaBits = descriptor & 0x0F;
        var rle = imageType is 9 or 10 or 11;
        if (!ReadTgaPixels(data, offset, count, pixelBytes, depth, alphaBits, gray, palette, colorMapFirst, colorMapLength, rle, width, rightToLeft, topToBottom, pixels))
            return null;
        return RgbaToPng(width, height, pixels);
    }

    private static bool ReadTgaPixels(
        byte[] data,
        int offset,
        int count,
        int pixelBytes,
        int depth,
        int alphaBits,
        bool gray,
        byte[]? palette,
        int colorMapFirst,
        int colorMapLength,
        bool rle,
        int width,
        bool rightToLeft,
        bool topToBottom,
        byte[] pixels)
    {
        var written = 0;
        while (written < count)
        {
            var run = 1;
            var repeated = false;
            if (rle)
            {
                if (offset >= data.Length)
                    return false;
                var packet = data[offset++];
                run = (packet & 0x7F) + 1;
                repeated = (packet & 0x80) != 0;
                if (written + run > count)
                    return false;
            }
            else if (written + run > count)
            {
                return false;
            }

            byte[]? repeatedPixel = null;
            for (var n = 0; n < run; n++)
            {
                ReadOnlySpan<byte> source;
                if (repeated && repeatedPixel is not null)
                {
                    source = repeatedPixel;
                }
                else
                {
                    if ((long)pixelBytes > data.Length - offset)
                        return false;
                    if (repeated)
                    {
                        repeatedPixel = data.AsSpan(offset, pixelBytes).ToArray();
                        source = repeatedPixel;
                    }
                    else
                    {
                        source = data.AsSpan(offset, pixelBytes);
                    }

                    offset += pixelBytes;
                }

                var x = written % width;
                var y = written / width;
                if (rightToLeft)
                    x = width - 1 - x;
                if (!topToBottom)
                    y = count / width - 1 - y;
                var dst = pixels.AsSpan((y * width + x) * 4, 4);
                if (palette is not null)
                {
                    var index = pixelBytes == 1 ? source[0] : source[0] | (source[1] << 8);
                    var entry = index - colorMapFirst;
                    if (entry < 0 || entry >= colorMapLength)
                        return false;
                    palette.AsSpan(entry * 4, 4).CopyTo(dst);
                }
                else if (!WriteTgaColor(source, depth, alphaBits, gray, dst))
                {
                    return false;
                }

                written++;
            }
        }

        return true;
    }

    private static bool WriteTgaColor(ReadOnlySpan<byte> pixel, int depth, int alphaBits, bool gray, Span<byte> dst)
    {
        if (gray)
        {
            if (depth == 8)
            {
                dst[0] = dst[1] = dst[2] = pixel[0];
                dst[3] = 255;
                return true;
            }

            if (depth == 16 && pixel.Length >= 2)
            {
                dst[0] = dst[1] = dst[2] = pixel[0];
                dst[3] = alphaBits == 0 ? (byte)255 : pixel[1];
                return true;
            }

            return false;
        }

        if (depth == 24 && pixel.Length >= 3)
        {
            dst[0] = pixel[2];
            dst[1] = pixel[1];
            dst[2] = pixel[0];
            dst[3] = 255;
            return true;
        }

        if (depth == 32 && pixel.Length >= 4)
        {
            dst[0] = pixel[2];
            dst[1] = pixel[1];
            dst[2] = pixel[0];
            // No attribute bits means the fourth byte is padding. Treating it as alpha hides the picture.
            dst[3] = alphaBits == 0 ? (byte)255 : pixel[3];
            return true;
        }

        if ((depth is 15 or 16) && pixel.Length >= 2)
        {
            var value = pixel[0] | (pixel[1] << 8);
            var blue = value & 31;
            var green = (value >> 5) & 31;
            var red = (value >> 10) & 31;
            dst[0] = Spread5(red);
            dst[1] = Spread5(green);
            dst[2] = Spread5(blue);
            dst[3] = alphaBits == 0 || (value & 0x8000) != 0 ? (byte)255 : (byte)0;
            return true;
        }

        return false;
    }

    private static byte Spread5(int value) => (byte)((value << 3) | (value >> 2));

    private static int ReadU16(byte[] data, int offset) => data[offset] | (data[offset + 1] << 8);

    private static int BytesPerPixel(int bits) => bits switch
    {
        8 => 1,
        15 or 16 => 2,
        24 => 3,
        32 => 4,
        _ => 0,
    };

    private static byte[]? RgbaToPng(int width, int height, byte[] pixels)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero)
            return null;
        Marshal.Copy(pixels, 0, ptr, pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    private static int FieldInt(Tiff tif, TiffTag tag)
    {
        var field = tif.GetField(tag);
        if (field is null || field.Length == 0)
            return 0;
        return field[0].ToInt();
    }

    private sealed class QuietTiff : TiffErrorHandler
    {
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }

        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }

    private sealed class StreamTiff : TiffStream
    {
        public static readonly StreamTiff Instance = new();

        public override int Read(object clientData, byte[] buffer, int offset, int count) =>
            ((Stream)clientData).Read(buffer, offset, count);

        public override void Write(object clientData, byte[] buffer, int offset, int count) =>
            ((Stream)clientData).Write(buffer, offset, count);

        public override long Seek(object clientData, long offset, SeekOrigin origin) =>
            ((Stream)clientData).Seek(offset, origin);

        public override void Close(object clientData)
        {
        }

        public override long Size(object clientData) => ((Stream)clientData).Length;
    }
}
