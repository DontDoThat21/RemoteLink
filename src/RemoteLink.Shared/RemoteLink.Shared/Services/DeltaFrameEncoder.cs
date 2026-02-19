using RemoteLink.Shared.Interfaces;
using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

/// <summary>
/// Encodes frames as deltas (only changed regions) to reduce bandwidth
/// </summary>
public class DeltaFrameEncoder : IDeltaFrameEncoder
{
    private byte[]? _previousFrameData;
    private string? _previousFrameId;
    private int _previousWidth;
    private int _previousHeight;
    private int _deltaThreshold = 5; // Default: 5% changed pixels triggers delta
    private readonly object _lock = new();

    /// <summary>
    /// Encode a frame as a delta (only changed regions) or full frame.
    /// Returns a tuple of (encoded frame, isDelta).
    /// </summary>
    public Task<(ScreenData EncodedFrame, bool IsDelta)> EncodeFrameAsync(ScreenData currentFrame)
    {
        lock (_lock)
        {
            // First frame or dimensions changed: send full frame
            if (_previousFrameData == null ||
                _previousWidth != currentFrame.Width ||
                _previousHeight != currentFrame.Height ||
                currentFrame.Format != ScreenDataFormat.Raw) // Only delta-encode raw frames
            {
                StorePreviousFrame(currentFrame);
                return Task.FromResult((currentFrame, false));
            }

            // Compute changed regions
            var deltaRegions = ComputeChangedRegions(
                _previousFrameData,
                currentFrame.ImageData,
                currentFrame.Width,
                currentFrame.Height);

            // If too many pixels changed, send full frame instead
            int totalPixels = currentFrame.Width * currentFrame.Height;
            int changedPixels = deltaRegions.Sum(r => r.Width * r.Height);
            double changePercentage = (changedPixels * 100.0) / totalPixels;

            if (changePercentage > _deltaThreshold)
            {
                StorePreviousFrame(currentFrame);
                return Task.FromResult((currentFrame, false));
            }

            // Build delta frame with only changed regions
            var deltaFrame = BuildDeltaFrame(currentFrame, deltaRegions);
            StorePreviousFrame(currentFrame);
            return Task.FromResult((deltaFrame, true));
        }
    }

    /// <summary>
    /// Reset the encoder state (clears reference frame)
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _previousFrameData = null;
            _previousFrameId = null;
            _previousWidth = 0;
            _previousHeight = 0;
        }
    }

    /// <summary>
    /// Set the minimum changed pixel percentage to trigger delta encoding (0-100)
    /// </summary>
    public void SetDeltaThreshold(int percentageThreshold)
    {
        lock (_lock)
        {
            _deltaThreshold = Math.Clamp(percentageThreshold, 0, 100);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void StorePreviousFrame(ScreenData frame)
    {
        _previousFrameData = (byte[])frame.ImageData.Clone();
        _previousFrameId = frame.FrameId;
        _previousWidth = frame.Width;
        _previousHeight = frame.Height;
    }

    private List<DeltaRegion> ComputeChangedRegions(
        byte[] previous,
        byte[] current,
        int width,
        int height)
    {
        const int bytesPerPixel = 4; // BGRA
        const int blockSize = 32; // 32x32 pixel blocks

        var changedRegions = new List<DeltaRegion>();

        for (int y = 0; y < height; y += blockSize)
        {
            for (int x = 0; x < width; x += blockSize)
            {
                int blockWidth = Math.Min(blockSize, width - x);
                int blockHeight = Math.Min(blockSize, height - y);

                if (IsBlockChanged(previous, current, x, y, blockWidth, blockHeight, width, bytesPerPixel))
                {
                    changedRegions.Add(new DeltaRegion
                    {
                        X = x,
                        Y = y,
                        Width = blockWidth,
                        Height = blockHeight,
                        DataOffset = 0, // Will be set in BuildDeltaFrame
                        DataLength = blockWidth * blockHeight * bytesPerPixel
                    });
                }
            }
        }

        return changedRegions;
    }

    private bool IsBlockChanged(
        byte[] previous,
        byte[] current,
        int x,
        int y,
        int blockWidth,
        int blockHeight,
        int frameWidth,
        int bytesPerPixel)
    {
        for (int dy = 0; dy < blockHeight; dy++)
        {
            int rowOffset = ((y + dy) * frameWidth + x) * bytesPerPixel;
            int rowLength = blockWidth * bytesPerPixel;

            for (int i = 0; i < rowLength; i++)
            {
                if (previous[rowOffset + i] != current[rowOffset + i])
                {
                    return true; // Block has changed
                }
            }
        }

        return false; // Block unchanged
    }

    private ScreenData BuildDeltaFrame(ScreenData originalFrame, List<DeltaRegion> deltaRegions)
    {
        const int bytesPerPixel = 4; // BGRA

        // Compute total data size for all changed regions
        int totalDataSize = deltaRegions.Sum(r => r.DataLength);
        byte[] deltaData = new byte[totalDataSize];

        int currentOffset = 0;
        foreach (var region in deltaRegions)
        {
            region.DataOffset = currentOffset;

            // Copy changed block pixels to delta data
            for (int dy = 0; dy < region.Height; dy++)
            {
                int srcOffset = ((region.Y + dy) * originalFrame.Width + region.X) * bytesPerPixel;
                int length = region.Width * bytesPerPixel;

                Array.Copy(originalFrame.ImageData, srcOffset, deltaData, currentOffset, length);
                currentOffset += length;
            }
        }

        return new ScreenData
        {
            FrameId = originalFrame.FrameId,
            Timestamp = originalFrame.Timestamp,
            ImageData = deltaData,
            Width = originalFrame.Width,
            Height = originalFrame.Height,
            Format = originalFrame.Format,
            Quality = originalFrame.Quality,
            IsDelta = true,
            ReferenceFrameId = _previousFrameId,
            DeltaRegions = deltaRegions
        };
    }
}
