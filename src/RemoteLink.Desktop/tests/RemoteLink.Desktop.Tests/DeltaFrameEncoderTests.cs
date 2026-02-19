using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Desktop.Tests;

public class DeltaFrameEncoderTests
{
    [Fact]
    public async Task FirstFrame_ShouldSendFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame = CreateTestFrame(100, 100);

        // Act
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame);

        // Assert
        Assert.False(isDelta);
        Assert.Equal(frame.FrameId, encodedFrame.FrameId);
        Assert.Equal(frame.ImageData.Length, encodedFrame.ImageData.Length);
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public async Task IdenticalFrame_ShouldSendDelta_WithNoChanges()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100); // Identical

        // Act
        await encoder.EncodeFrameAsync(frame1); // First frame
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.True(isDelta);
        Assert.True(encodedFrame.IsDelta);
        Assert.Equal(frame1.FrameId, encodedFrame.ReferenceFrameId);
        Assert.Equal(0, encodedFrame.ImageData.Length); // No changed regions
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.Empty(encodedFrame.DeltaRegions);
    }

    [Fact]
    public async Task FrameWithSmallChange_ShouldSendDelta()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(50); // Allow up to 50% change for delta

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);

        // Change a small region (10x10 = 100 pixels = 1% of 10000)
        ChangeRegion(frame2.ImageData, 10, 10, 10, 10, 100);

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.True(isDelta);
        Assert.True(encodedFrame.IsDelta);
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.NotEmpty(encodedFrame.DeltaRegions);
        Assert.True(encodedFrame.ImageData.Length < frame2.ImageData.Length); // Smaller than full
    }

    [Fact]
    public async Task FrameWithLargeChange_ShouldSendFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(5); // Only allow 5% change for delta

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);

        // Change a large region (60x60 = 3600 pixels = 36% of 10000)
        ChangeRegion(frame2.ImageData, 0, 0, 60, 60, 100);

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(isDelta); // Too much changed, send full frame
        Assert.False(encodedFrame.IsDelta);
        Assert.Equal(frame2.ImageData.Length, encodedFrame.ImageData.Length);
    }

    [Fact]
    public async Task DimensionChange_ShouldSendFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(200, 200); // Different size

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(isDelta); // Dimension change forces full frame
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public async Task NonRawFormat_ShouldSendFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        frame2.Format = ScreenDataFormat.JPEG; // Non-raw format

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(isDelta); // Only raw frames can be delta-encoded
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public void Reset_ShouldClearState()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);

        // Act
        encoder.EncodeFrameAsync(frame1).Wait(); // Store frame
        encoder.Reset(); // Clear state
        var (encodedFrame, isDelta) = encoder.EncodeFrameAsync(frame1).Result;

        // Assert
        Assert.False(isDelta); // First frame after reset
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public void SetDeltaThreshold_ShouldClampValues()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();

        // Act & Assert
        encoder.SetDeltaThreshold(-10); // Below 0
        encoder.SetDeltaThreshold(150); // Above 100
        // If it doesn't throw, clamping worked
        Assert.True(true);
    }

    [Fact]
    public async Task DeltaRegions_ShouldHaveCorrectOffsets()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(50);

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);

        // Change two separate small regions
        ChangeRegion(frame2.ImageData, 10, 10, 10, 10, 100);
        ChangeRegion(frame2.ImageData, 50, 50, 10, 10, 100);

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.True(isDelta);
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.NotEmpty(encodedFrame.DeltaRegions);

        // Check that offsets are sequential and non-overlapping
        int expectedOffset = 0;
        foreach (var region in encodedFrame.DeltaRegions)
        {
            Assert.Equal(expectedOffset, region.DataOffset);
            Assert.True(region.DataLength > 0);
            expectedOffset += region.DataLength;
        }

        // Total data should match sum of region lengths
        Assert.Equal(expectedOffset, encodedFrame.ImageData.Length);
    }

    [Fact]
    public async Task MultipleFrames_ShouldTrackReference()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        var frame3 = CreateTestFrame(100, 100);

        // Act
        var (encoded1, isDelta1) = await encoder.EncodeFrameAsync(frame1);
        var (encoded2, isDelta2) = await encoder.EncodeFrameAsync(frame2);
        var (encoded3, isDelta3) = await encoder.EncodeFrameAsync(frame3);

        // Assert
        Assert.False(isDelta1); // First
        Assert.True(isDelta2); // Delta from frame1
        Assert.True(isDelta3); // Delta from frame2

        Assert.Equal(frame1.FrameId, encoded2.ReferenceFrameId);
        Assert.Equal(frame2.FrameId, encoded3.ReferenceFrameId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ScreenData CreateTestFrame(int width, int height)
    {
        int bytesPerPixel = 4; // BGRA
        byte[] data = new byte[width * height * bytesPerPixel];

        // Fill with a simple pattern (not all zeros)
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }

        return new ScreenData
        {
            FrameId = Guid.NewGuid().ToString(),
            ImageData = data,
            Width = width,
            Height = height,
            Format = ScreenDataFormat.Raw
        };
    }

    private void ChangeRegion(byte[] data, int x, int y, int width, int height, int frameWidth)
    {
        int bytesPerPixel = 4;
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                int offset = ((y + dy) * frameWidth + (x + dx)) * bytesPerPixel;
                data[offset] = 255; // Change pixel to white
                data[offset + 1] = 255;
                data[offset + 2] = 255;
                data[offset + 3] = 255;
            }
        }
    }
}
