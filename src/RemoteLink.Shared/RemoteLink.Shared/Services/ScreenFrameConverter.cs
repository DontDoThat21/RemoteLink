using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Converts a <see cref="ScreenData"/> payload into a byte array or stream
/// that can be rendered by MAUI's <c>ImageSource.FromStream</c> on any platform.
///
/// Supported conversions:
/// <list type="bullet">
///   <item><term>Raw (BGRA)</term><description>Encoded as 32-bpp BMP — no extra libraries required.</description></item>
///   <item><term>JPEG / PNG</term><description>Returned as-is; already platform-decodable.</description></item>
/// </list>
/// </summary>
public static class ScreenFrameConverter
{
    private const int FileHeaderSize  = 14;
    private const int InfoHeaderSize  = 40;
    private const int TotalHeaderSize = FileHeaderSize + InfoHeaderSize;
    private const int BytesPerPixel   = 4;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns image bytes ready for MAUI's <c>ImageSource.FromStream</c>.
    /// <list type="bullet">
    ///   <item>JPEG / PNG frames: returned verbatim (already encoded).</item>
    ///   <item>Raw BGRA frames: encoded as 32-bpp BMP.</item>
    /// </list>
    /// Returns <c>null</c> when the input is invalid or the pixel buffer is
    /// too small.
    /// </summary>
    public static byte[]? ToImageBytes(ScreenData? screenData)
    {
        if (screenData is null)                           return null;
        if (screenData.ImageData is null)                 return null;
        if (screenData.ImageData.Length == 0)             return null;

        return screenData.Format switch
        {
            ScreenDataFormat.JPEG => screenData.ImageData,
            ScreenDataFormat.PNG  => screenData.ImageData,
            ScreenDataFormat.Raw  => RawToBmp(screenData),
            _                     => null
        };
    }

    /// <summary>
    /// Convenience overload: wraps <see cref="ToImageBytes"/> in a
    /// <see cref="MemoryStream"/> suitable for
    /// <c>ImageSource.FromStream(() => ToImageStream(frame))</c>.
    /// Returns <c>null</c> when encoding fails.
    /// </summary>
    public static MemoryStream? ToImageStream(ScreenData? screenData)
    {
        var bytes = ToImageBytes(screenData);
        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }

    // ── Raw → BMP encoder ─────────────────────────────────────────────────────

    /// <summary>
    /// Encodes raw BGRA pixel data as a 32-bpp BMP.
    /// Returns <c>null</c> when dimensions are invalid or the buffer is too small.
    /// </summary>
    private static byte[]? RawToBmp(ScreenData screenData)
    {
        int width  = screenData.Width;
        int height = screenData.Height;
        byte[] pixels = screenData.ImageData;

        if (width <= 0 || height <= 0)
            return null;

        int requiredBytes = width * height * BytesPerPixel;

        if (pixels.Length < requiredBytes)
            return null;

        int pixelDataSize = requiredBytes;
        int fileSize      = TotalHeaderSize + pixelDataSize;

        var bmp  = new byte[fileSize];
        var span = bmp.AsSpan();

        WriteBitmapFileHeader(span, fileSize);
        WriteBitmapInfoHeader(span, width, height, pixelDataSize);

        // Copy pixel data directly — BGRA is the native Windows GDI order
        // and is compatible with BMP 32-bpp BI_RGB (alpha byte is ignored
        // by the BMP specification, so no re-ordering is needed).
        pixels.AsSpan(0, pixelDataSize).CopyTo(span[TotalHeaderSize..]);

        return bmp;
    }

    // ── BMP header writers ────────────────────────────────────────────────────

    /// <summary>Writes the 14-byte BITMAPFILEHEADER at offset 0.</summary>
    private static void WriteBitmapFileHeader(Span<byte> buf, int fileSize)
    {
        // bfType: 'BM'
        buf[0] = (byte)'B';
        buf[1] = (byte)'M';
        // bfSize
        WriteInt32(buf, 2, fileSize);
        // bfReserved1 + bfReserved2
        WriteInt32(buf, 6, 0);
        // bfOffBits: offset to pixel data
        WriteInt32(buf, 10, TotalHeaderSize);
    }

    /// <summary>Writes the 40-byte BITMAPINFOHEADER at offset 14.</summary>
    private static void WriteBitmapInfoHeader(
        Span<byte> buf, int width, int height, int pixelDataSize)
    {
        const int baseOffset = FileHeaderSize;

        // biSize
        WriteInt32(buf,  baseOffset +  0, InfoHeaderSize);
        // biWidth
        WriteInt32(buf,  baseOffset +  4, width);
        // biHeight — negative = top-down scanline order (same as GDI capture)
        WriteInt32(buf,  baseOffset +  8, -height);
        // biPlanes = 1
        WriteUInt16(buf, baseOffset + 12, 1);
        // biBitCount = 32
        WriteUInt16(buf, baseOffset + 14, 32);
        // biCompression = BI_RGB (0)
        WriteInt32(buf,  baseOffset + 16, 0);
        // biSizeImage
        WriteInt32(buf,  baseOffset + 20, pixelDataSize);
        // biXPelsPerMeter
        WriteInt32(buf,  baseOffset + 24, 0);
        // biYPelsPerMeter
        WriteInt32(buf,  baseOffset + 28, 0);
        // biClrUsed
        WriteInt32(buf,  baseOffset + 32, 0);
        // biClrImportant
        WriteInt32(buf,  baseOffset + 36, 0);
    }

    // ── Byte-order helpers (little-endian) ────────────────────────────────────

    private static void WriteInt32(Span<byte> buf, int offset, int value)
    {
        buf[offset]     = (byte)( value        & 0xFF);
        buf[offset + 1] = (byte)((value >>  8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt16(Span<byte> buf, int offset, ushort value)
    {
        buf[offset]     = (byte)( value        & 0xFF);
        buf[offset + 1] = (byte)((value >>  8) & 0xFF);
    }
}
