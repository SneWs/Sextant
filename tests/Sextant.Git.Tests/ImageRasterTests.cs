using System.Text;
using BitMiracle.LibTiff.Classic;
using Sextant;
using Sextant.Git;
using SkiaSharp;

namespace Sextant.Git.Tests;

public class ImageRasterTests
{
    [Fact]
    public void Image_paths_include_svg_tiff_and_the_common_rasters()
    {
        Assert.True(ImageFiles.IsImagePath("a.svg"));
        Assert.True(ImageFiles.IsImagePath("a.SVG"));
        Assert.True(ImageFiles.IsImagePath("a.png"));
        Assert.True(ImageFiles.IsImagePath("a.jpg"));
        Assert.True(ImageFiles.IsImagePath("a.jpeg"));
        Assert.True(ImageFiles.IsImagePath("a.tga"));
        Assert.True(ImageFiles.IsImagePath("a.TGA"));
        Assert.True(ImageFiles.IsImagePath("a.targa"));
        Assert.True(ImageFiles.IsImagePath("a.tif"));
        Assert.True(ImageFiles.IsImagePath("a.tiff"));
        Assert.False(ImageFiles.IsImagePath("a.txt"));
        Assert.False(ImageFiles.IsImagePath("a.fbx"));
        Assert.True(ModelFiles.IsFbxPath("model.FBX"));
        Assert.False(ModelFiles.IsFbxPath("model.png"));
    }

    [Fact]
    public void Svg_preview_is_a_png_and_does_not_fetch_a_file_it_points_at()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sextant-svg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var secret = Path.Combine(dir, "secret.png");
            File.WriteAllBytes(secret, BluePng());
            var href = secret.Replace('\\', '/');
            var svg = """
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="8" height="8">
                  <rect width="8" height="8" fill="#ff0000"/>
                  <image href="file:///HREF" xlink:href="file:///HREF" width="8" height="8"/>
                </svg>
                """.Replace("HREF", href, StringComparison.Ordinal);
            var png = ImageRaster.Prepare("mark.svg", Encoding.UTF8.GetBytes(svg));
            var pixel = Pixel(png);
            Assert.True(pixel.Red > 200 && pixel.Green < 40 && pixel.Blue < 40, pixel.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Tiff_preview_is_a_png_of_the_first_page()
    {
        var png = ImageRaster.Prepare("scan.tiff", RedBlueTiff());
        using var bitmap = Decode(png);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
        var left = bitmap.GetPixel(0, 0);
        var right = bitmap.GetPixel(1, 0);
        Assert.True(left.Red > 200 && left.Green < 40 && left.Blue < 40, left.ToString());
        Assert.True(right.Blue > 200 && right.Red < 40 && right.Green < 40, right.ToString());
    }

    [Fact]
    public void Tga_preview_is_upright_and_ignores_a_footer()
    {
        // File order is bottom row first: red, green, then blue, white.
        var pixels = new byte[]
        {
            0, 0, 255, 0, 255, 0,
            255, 0, 0, 255, 255, 255,
        };
        var footer = Encoding.ASCII.GetBytes("TRUEVISION-XFILE.\0");
        var png = ImageRaster.Prepare("tex.tga", Tga(2, 2, 2, 24, 0, pixels, id: Encoding.ASCII.GetBytes("mark"), footer: footer));
        using var bitmap = Decode(png);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(2, bitmap.Height);
        AssertColor(bitmap.GetPixel(0, 0), 0, 0, 255);
        AssertColor(bitmap.GetPixel(1, 0), 255, 255, 255);
        AssertColor(bitmap.GetPixel(0, 1), 255, 0, 0);
        AssertColor(bitmap.GetPixel(1, 1), 0, 255, 0);

        var flipped = ImageRaster.Prepare("tex.targa", Tga(2, 2, 1, 24, 0x10, new byte[] { 0, 0, 255, 255, 0, 0 }));
        using var wide = Decode(flipped);
        AssertColor(wide.GetPixel(0, 0), 0, 0, 255);
        AssertColor(wide.GetPixel(1, 0), 255, 0, 0);
    }

    [Fact]
    public void Tga_rle_keeps_alpha_and_padding_stays_opaque()
    {
        // A run of two red pixels at alpha 128, then one raw blue pixel at alpha 255. Origin is the top.
        var rle = new byte[]
        {
            0x81, 0, 0, 255, 128,
            0x00, 255, 0, 0, 255,
        };
        var png = ImageRaster.Prepare("tex.tga", Tga(10, 3, 1, 32, 0x28, rle));
        using var bitmap = Decode(png);
        Assert.Equal(3, bitmap.Width);
        AssertColor(bitmap.GetPixel(0, 0), 255, 0, 0, 128);
        AssertColor(bitmap.GetPixel(1, 0), 255, 0, 0, 128);
        AssertColor(bitmap.GetPixel(2, 0), 0, 0, 255, 255);

        var padded = ImageRaster.Prepare("pad.tga", Tga(2, 1, 1, 32, 0x20, new byte[] { 0, 0, 255, 0 }));
        AssertColor(Pixel(padded), 255, 0, 0, 255);
    }

    [Fact]
    public void Tga_palette_and_gray_decode()
    {
        var palette = new byte[] { 0, 0, 255, 255, 0, 0 };
        var indexes = new byte[] { 0x01, 0, 1 };
        var png = ImageRaster.Prepare("map.tga", Tga(9, 2, 1, 8, 0x20, indexes, palette: palette));
        using var bitmap = Decode(png);
        AssertColor(bitmap.GetPixel(0, 0), 255, 0, 0);
        AssertColor(bitmap.GetPixel(1, 0), 0, 0, 255);

        var gray = ImageRaster.Prepare("gray.tga", Tga(3, 1, 1, 8, 0, new byte[] { 200 }));
        AssertColor(Pixel(gray), 200, 200, 200);
    }

    [Fact]
    public void Broken_tga_is_not_a_preview()
    {
        Assert.Null(ImageRaster.Prepare("a.tga", new byte[17]));
        Assert.Null(ImageRaster.Prepare("a.tga", new byte[18]));
        Assert.Null(ImageRaster.Prepare("a.tga", Tga(2, 2, 2, 24, 0, new byte[] { 0, 0, 255 })));
        Assert.Null(ImageRaster.Prepare("a.tga", Tga(2, 20000, 1, 24, 0, Array.Empty<byte>())));
    }

    [Fact]
    public void Png_bytes_are_left_unchanged()
    {
        var png = BluePng();
        Assert.Same(png, ImageRaster.Prepare("a.png", png));
        Assert.Same(png, ImageRaster.Prepare("a.jpg", png));
        Assert.Same(png, ImageRaster.Prepare("a.jpeg", png));
    }

    private static void AssertColor(SKColor color, byte red, byte green, byte blue, byte alpha = 255) =>
        Assert.True(color.Red == red && color.Green == green && color.Blue == blue && color.Alpha == alpha, color.ToString());

    private static byte[] Tga(
        int imageType,
        int width,
        int height,
        int depth,
        int descriptor,
        byte[] pixels,
        byte[]? id = null,
        byte[]? palette = null,
        byte[]? footer = null)
    {
        id ??= [];
        var entryBytes = palette is null ? 0 : 3;
        var colors = palette is null ? 0 : palette.Length / entryBytes;
        var header = new byte[18];
        header[0] = (byte)id.Length;
        header[1] = palette is null ? (byte)0 : (byte)1;
        header[2] = (byte)imageType;
        header[5] = (byte)colors;
        header[6] = (byte)(colors >> 8);
        header[7] = palette is null ? (byte)0 : (byte)24;
        header[12] = (byte)width;
        header[13] = (byte)(width >> 8);
        header[14] = (byte)height;
        header[15] = (byte)(height >> 8);
        header[16] = (byte)depth;
        header[17] = (byte)descriptor;
        using var stream = new MemoryStream();
        stream.Write(header);
        stream.Write(id);
        if (palette is not null)
            stream.Write(palette);
        stream.Write(pixels);
        if (footer is not null)
            stream.Write(footer);
        return stream.ToArray();
    }

    private static SKColor Pixel(byte[]? png)
    {
        using var bitmap = Decode(png);
        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKBitmap Decode(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        var bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        return bitmap;
    }

    private static byte[] BluePng()
    {
        using var bitmap = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0, 0, 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] RedBlueTiff()
    {
        using var stream = new MemoryStream();
        using (var tif = Tiff.ClientOpen("preview", "w", stream, new Writer()))
        {
            Assert.NotNull(tif);
            tif.SetField(TiffTag.IMAGEWIDTH, 2);
            tif.SetField(TiffTag.IMAGELENGTH, 1);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 3);
            tif.SetField(TiffTag.BITSPERSAMPLE, 8);
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tif.SetField(TiffTag.COMPRESSION, Compression.NONE);
            tif.SetField(TiffTag.ROWSPERSTRIP, 1);
            Assert.True(tif.WriteScanline(new byte[] { 255, 0, 0, 0, 0, 255 }, 0));
        }

        return stream.ToArray();
    }

    private sealed class Writer : TiffStream
    {
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
