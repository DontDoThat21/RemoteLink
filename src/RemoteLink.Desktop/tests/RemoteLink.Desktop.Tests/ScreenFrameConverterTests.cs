using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Desktop.Tests;

/// <summary>
/// Unit tests for <see cref="ScreenFrameConverter"/>.
///
/// Coverage:
///   - Null / invalid input guards
///   - JPEG / PNG pass-through
///   - Raw → BMP encoding (header structure + pixel data)
///   - BMP output size formula
///   - ToBmpStream convenience wrapper
/// </summary>
public class ScreenFrameConverterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal valid ScreenData with Raw BGRA pixel data.</summary>
    private static ScreenData MakeRaw(int width, int height, byte fillByte = 0xAB)
    {
        int pixelCount = width * height * 4;
        var pixels = new byte[pixelCount];
        if (fillByte != 0)
            Array.Fill(pixels, fillByte);

        return new ScreenData
        {
            Width     = width,
            Height    = height,
            ImageData = pixels,
            Format    = ScreenDataFormat.Raw
        };
    }

    /// <summary>Creates fake JPEG/PNG data (just enough to be non-empty).</summary>
    private static ScreenData MakeEncoded(ScreenDataFormat format, int width = 4, int height = 4)
        => new ScreenData
        {
            Width     = width,
            Height    = height,
            ImageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, // JPEG SOI marker
            Format    = format
        };

    // Expected BMP output size for a Raw frame
    private static int ExpectedBmpSize(int width, int height) =>
        14 + 40 + width * height * 4;   // file-header + info-header + pixels

    // ── Null / invalid guards ─────────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_NullScreenData_ReturnsNull()
    {
        Assert.Null(ScreenFrameConverter.ToImageBytes(null));
    }

    [Fact]
    public void ToImageBytes_NullImageData_ReturnsNull()
    {
        var sd = new ScreenData { Width = 4, Height = 4, ImageData = null!, Format = ScreenDataFormat.Raw };
        Assert.Null(ScreenFrameConverter.ToImageBytes(sd));
    }

    [Fact]
    public void ToImageBytes_EmptyImageData_ReturnsNull()
    {
        var sd = new ScreenData { Width = 4, Height = 4, ImageData = Array.Empty<byte>(), Format = ScreenDataFormat.Raw };
        Assert.Null(ScreenFrameConverter.ToImageBytes(sd));
    }

    [Fact]
    public void ToImageBytes_Raw_ZeroWidth_ReturnsNull()
    {
        var sd = MakeRaw(0, 4);
        Assert.Null(ScreenFrameConverter.ToImageBytes(sd));
    }

    [Fact]
    public void ToImageBytes_Raw_ZeroHeight_ReturnsNull()
    {
        var sd = MakeRaw(4, 0);
        Assert.Null(ScreenFrameConverter.ToImageBytes(sd));
    }

    [Fact]
    public void ToImageBytes_Raw_BufferTooSmall_ReturnsNull()
    {
        // Claim 8×8 but only provide 4×4 worth of bytes
        var sd = new ScreenData
        {
            Width     = 8,
            Height    = 8,
            ImageData = new byte[4 * 4 * 4],   // only 64 bytes, need 256
            Format    = ScreenDataFormat.Raw
        };
        Assert.Null(ScreenFrameConverter.ToImageBytes(sd));
    }

    // ── JPEG / PNG pass-through ───────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_Jpeg_ReturnsImageDataVerbatim()
    {
        var sd = MakeEncoded(ScreenDataFormat.JPEG);
        var result = ScreenFrameConverter.ToImageBytes(sd);
        Assert.Same(sd.ImageData, result);
    }

    [Fact]
    public void ToImageBytes_Png_ReturnsImageDataVerbatim()
    {
        var sd = MakeEncoded(ScreenDataFormat.PNG);
        var result = ScreenFrameConverter.ToImageBytes(sd);
        Assert.Same(sd.ImageData, result);
    }

    // ── Raw → BMP: output size ────────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_Raw_1x1_ReturnsCorrectSize()
    {
        var sd     = MakeRaw(1, 1);
        var result = ScreenFrameConverter.ToImageBytes(sd);
        Assert.NotNull(result);
        Assert.Equal(ExpectedBmpSize(1, 1), result!.Length);
    }

    [Fact]
    public void ToImageBytes_Raw_4x4_ReturnsCorrectSize()
    {
        var sd     = MakeRaw(4, 4);
        var result = ScreenFrameConverter.ToImageBytes(sd);
        Assert.NotNull(result);
        Assert.Equal(ExpectedBmpSize(4, 4), result!.Length);
    }

    [Fact]
    public void ToImageBytes_Raw_1920x1080_ReturnsCorrectSize()
    {
        var sd     = MakeRaw(1920, 1080);
        var result = ScreenFrameConverter.ToImageBytes(sd);
        Assert.NotNull(result);
        Assert.Equal(ExpectedBmpSize(1920, 1080), result!.Length);
    }

    // ── Raw → BMP: file header ────────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_Raw_StartsWithBmpSignature()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        Assert.Equal((byte)'B', result[0]);
        Assert.Equal((byte)'M', result[1]);
    }

    [Fact]
    public void ToImageBytes_Raw_FileSizeFieldMatchesArrayLength()
    {
        var sd     = MakeRaw(4, 4);
        var result = ScreenFrameConverter.ToImageBytes(sd)!;

        int storedSize = ReadInt32LE(result, 2);
        Assert.Equal(result.Length, storedSize);
    }

    [Fact]
    public void ToImageBytes_Raw_PixelDataOffsetIs54()
    {
        // BITMAPFILEHEADER(14) + BITMAPINFOHEADER(40) = 54
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        int offset = ReadInt32LE(result, 10);
        Assert.Equal(54, offset);
    }

    // ── Raw → BMP: info header ────────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_Raw_InfoHeaderSizeIs40()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        int biSize = ReadInt32LE(result, 14);
        Assert.Equal(40, biSize);
    }

    [Fact]
    public void ToImageBytes_Raw_WidthFieldIsCorrect()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(16, 9))!;
        int biWidth = ReadInt32LE(result, 18);
        Assert.Equal(16, biWidth);
    }

    [Fact]
    public void ToImageBytes_Raw_HeightFieldIsNegative_TopDown()
    {
        // Negative biHeight = top-down scanline order (matches GDI capture output)
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(4, 8))!;
        int biHeight = ReadInt32LE(result, 22);
        Assert.Equal(-8, biHeight);
    }

    [Fact]
    public void ToImageBytes_Raw_BitCountIs32()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        ushort biBitCount = ReadUInt16LE(result, 28);
        Assert.Equal(32, biBitCount);
    }

    [Fact]
    public void ToImageBytes_Raw_CompressionIsZero_BiRgb()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        int biCompression = ReadInt32LE(result, 30);
        Assert.Equal(0, biCompression);
    }

    [Fact]
    public void ToImageBytes_Raw_PlanesIsOne()
    {
        var result = ScreenFrameConverter.ToImageBytes(MakeRaw(2, 2))!;
        ushort biPlanes = ReadUInt16LE(result, 26);
        Assert.Equal(1, biPlanes);
    }

    // ── Raw → BMP: pixel data ─────────────────────────────────────────────────

    [Fact]
    public void ToImageBytes_Raw_PixelDataMatchesInput()
    {
        var sd     = MakeRaw(2, 2, fillByte: 0x42);
        var result = ScreenFrameConverter.ToImageBytes(sd)!;

        // Pixels start at offset 54
        var pixelSlice = result.AsSpan(54);
        Assert.True(pixelSlice.SequenceEqual(sd.ImageData.AsSpan(0, 2 * 2 * 4)));
    }

    [Fact]
    public void ToImageBytes_Raw_PixelDataDistinctValues_PreservedCorrectly()
    {
        // Use a 1×2 frame with two distinct pixel colours
        // Pixel 0: B=0x11, G=0x22, R=0x33, A=0xFF
        // Pixel 1: B=0x44, G=0x55, R=0x66, A=0xFF
        var pixels = new byte[]
        {
            0x11, 0x22, 0x33, 0xFF,
            0x44, 0x55, 0x66, 0xFF
        };
        var sd = new ScreenData
        {
            Width     = 2,
            Height    = 1,
            ImageData = pixels,
            Format    = ScreenDataFormat.Raw
        };

        var result = ScreenFrameConverter.ToImageBytes(sd)!;

        // Pixel bytes start at offset 54
        Assert.Equal(0x11, result[54]);
        Assert.Equal(0x22, result[55]);
        Assert.Equal(0x33, result[56]);
        Assert.Equal(0xFF, result[57]);
        Assert.Equal(0x44, result[58]);
        Assert.Equal(0x55, result[59]);
        Assert.Equal(0x66, result[60]);
        Assert.Equal(0xFF, result[61]);
    }

    // ── ToImageStream ─────────────────────────────────────────────────────────

    [Fact]
    public void ToImageStream_NullInput_ReturnsNull()
    {
        Assert.Null(ScreenFrameConverter.ToImageStream(null));
    }

    [Fact]
    public void ToImageStream_ValidRawFrame_ReturnsReadableStream()
    {
        var sd     = MakeRaw(2, 2);
        var stream = ScreenFrameConverter.ToImageStream(sd);

        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ToImageStream_ContentMatchesToImageBytes()
    {
        var sd     = MakeRaw(2, 2);
        var bytes  = ScreenFrameConverter.ToImageBytes(sd)!;
        var stream = ScreenFrameConverter.ToImageStream(sd)!;

        Assert.Equal(bytes.Length, stream.Length);
        Assert.True(stream.ToArray().SequenceEqual(bytes));
    }

    [Fact]
    public void ToImageStream_Jpeg_ReturnsNonNullStream()
    {
        var sd     = MakeEncoded(ScreenDataFormat.JPEG);
        var stream = ScreenFrameConverter.ToImageStream(sd);
        Assert.NotNull(stream);
    }

    // ── Little-endian read helpers ────────────────────────────────────────────

    private static int ReadInt32LE(byte[] buf, int offset) =>
        buf[offset]
        | (buf[offset + 1] << 8)
        | (buf[offset + 2] << 16)
        | (buf[offset + 3] << 24);

    private static ushort ReadUInt16LE(byte[] buf, int offset) =>
        (ushort)(buf[offset] | (buf[offset + 1] << 8));
}
