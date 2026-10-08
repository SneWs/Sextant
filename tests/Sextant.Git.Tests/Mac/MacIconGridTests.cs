using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Sextant.Git.Tests;

/// <summary>
/// macOS maps an .icns canvas onto a fixed region and keeps whatever transparent margin the
/// artwork carries, so a tile whose corners are cut back too far renders small and the
/// leftover canvas reads as a light border around the Dock icon. The artwork is authored that
/// way on purpose, so make-icns.sh repaints it onto the grid before packing. These tests run
/// the real script and measure what actually lands in the .icns.
/// </summary>
public class MacIconGridTests
{
    // Comfortably inside the radius macOS is willing to scale up to the grid, and above the
    // 20% the normaliser draws at.
    private const double MaxCornerRadiusFraction = 0.25;
    private const byte Opaque = 128;

    [Fact]
    public void Packed_icon_tile_reaches_the_canvas_edges()
    {
        var tile = PackedTile();
        var box = tile.OpaqueBox();

        Assert.InRange(box.Left, 0, 1);
        Assert.InRange(box.Top, 0, 1);
        Assert.InRange(box.Right, tile.Width - 2, tile.Width - 1);
        Assert.InRange(box.Bottom, tile.Height - 2, tile.Height - 1);

        var mid = tile.Width / 2;
        Assert.Equal(255, tile.Alpha(mid, 0));
        Assert.Equal(255, tile.Alpha(mid, tile.Height - 1));
        Assert.Equal(255, tile.Alpha(0, mid));
        Assert.Equal(255, tile.Alpha(tile.Width - 1, mid));
    }

    [Fact]
    public void Packed_icon_tile_corner_radius_fits_the_macos_grid()
    {
        var tile = PackedTile();
        var box = tile.OpaqueBox();
        var side = box.Right - box.Left + 1;

        // On a rounded square the topmost opaque row only spans the straight part of the top
        // edge, so the two corners account for the width that row is missing.
        var radius = (side - tile.OpaqueSpanInRow(box.Top)) / 2.0;

        Assert.True(radius / side <= MaxCornerRadiusFraction,
            $"packed tile has a {radius / side:P1} corner radius. macOS auto-scales the artwork " +
            $"instead of fitting it to the grid above roughly {MaxCornerRadiusFraction:P0}, " +
            $"which shows up as a border around the Dock icon.");
    }

    [Fact]
    public void Packed_icon_tile_keeps_the_mark()
    {
        // The normaliser rescales the artwork to cover the grid; if that went wrong we would
        // have a correct shape with no sextant in it, so check the mark itself survived.
        var tile = PackedTile();
        var white = 0;
        for (var i = 0; i < tile.Pixels.Length; i += 4)
        {
            if (tile.Pixels[i + 3] > 200 && tile.Pixels[i] > 180 && tile.Pixels[i + 1] > 180 && tile.Pixels[i + 2] > 180)
                white++;
        }
        Assert.True(white > 10_000, $"only {white} light pixels survived the grid repaint.");
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom);

    private readonly record struct Tile(int Width, int Height, byte[] Pixels)
    {
        public byte Alpha(int x, int y) => Pixels[(y * Width + x) * 4 + 3];

        public Box OpaqueBox()
        {
            var left = Width;
            var top = Height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (Alpha(x, y) > Opaque)
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
            return new Box(left, top, right, bottom);
        }

        public int OpaqueSpanInRow(int y)
        {
            var first = -1;
            var last = -1;
            for (var x = 0; x < Width; x++)
            {
                if (Alpha(x, y) > Opaque)
                {
                    if (first < 0)
                        first = x;
                    last = x;
                }
            }
            return last - first + 1;
        }
    }

    /// <summary>
    /// Runs make-icns.sh over the shipped artwork and returns the largest tile from the
    /// resulting .icns, read back out with iconutil so the assertion covers what we ship.
    /// </summary>
    private static Tile PackedTile()
    {
        var root = RepoRoot() ?? throw new FileNotFoundException("cannot find src/tools/make-icns.sh above the tests.");
        var script = Path.Combine(root, "src", "tools", "make-icns.sh");
        var artwork = Path.Combine(root, "src", "Assets", "sextant.png");

        var work = Path.Combine(Path.GetTempPath(), "sextant-icns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var icns = Path.Combine(work, "sextant.icns");
            Run("bash", $"\"{script}\" \"{artwork}\" \"{icns}\"", root);
            Assert.True(File.Exists(icns), "make-icns.sh produced no .icns");

            var iconset = Path.Combine(work, "sextant.iconset");
            Run("iconutil", $"-c iconset \"{icns}\" -o \"{iconset}\"", root);

            // The 512@2x slot is the 1024 canvas the grid is defined against.
            var largest = Path.Combine(iconset, "icon_512x512@2x.png");
            Assert.True(File.Exists(largest), "packed .icns has no 1024px representation");

            using var decoded = SKBitmap.Decode(File.ReadAllBytes(largest))
                ?? throw new InvalidOperationException(largest + " is not a decodable PNG.");
            using var image = SKImage.FromBitmap(decoded);
            var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            Assert.True(image.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0), "cannot read " + largest);

            var pixels = new byte[info.Width * info.Height * 4];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            return new Tile(info.Width, info.Height, pixels);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static void Run(string fileName, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("cannot start " + fileName);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {arguments} failed ({process.ExitCode}):\n{stdout}\n{stderr}");
    }

    private static string? RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "tools", "make-icns.sh")))
                return dir.FullName;
        }
        return null;
    }
}
