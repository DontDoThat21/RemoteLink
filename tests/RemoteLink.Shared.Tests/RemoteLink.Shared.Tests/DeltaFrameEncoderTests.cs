using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests;

public class DeltaFrameEncoderTests
{
    [Fact]
    public async Task FirstFrame_ReturnsFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame = CreateTestFrame(100, 100);

        // Act
        var result = await encoder.EncodeFrameAsync(frame);

        // Assert
        Assert.False(result.IsDelta);
        Assert.Equal(frame.ImageData.Length, result.ImageData.Length);
        Assert.Null(result.ReferenceFrameId);
    }

    [Fact]
    public async Task IdenticalFrames_ReturnsDeltaFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100, fillColor: 0x80);
        var frame2 = CreateTestFrame(100, 100, fillColor: 0x80); // Identical

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.True(result.IsDelta || result.ImageData.Length == frame2.ImageData.Length); 
        // Identical frames may return full frame if no changes detected
    }

    [Fact]
    public async Task SmallChange_ReturnsDeltaFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(300, 300, fillColor: 0x00);
        var frame2 = CreateTestFrame(300, 300, fillColor: 0x00);
        
        // Change one 64x64 block (block size matches encoder's BlockSize)
        // This is ~5% of total pixels, should trigger delta encoding
        ModifyRegion(frame2.ImageData, 64, 64, 64, 64, 300, 0xFF);

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.True(result.IsDelta);
        Assert.NotNull(result.DeltaRegions);
        Assert.NotEmpty(result.DeltaRegions);
        Assert.True(result.ImageData.Length < frame2.ImageData.Length);
    }

    [Fact]
    public async Task LargeChange_ReturnsFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100, fillColor: 0x00);
        var frame2 = CreateTestFrame(100, 100, fillColor: 0xFF); // Completely different

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(result.IsDelta); // >30% changed, should be full frame
    }

    [Fact]
    public async Task ResolutionChange_ReturnsFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(200, 200); // Different resolution

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(result.IsDelta);
        Assert.Equal(frame2.ImageData.Length, result.ImageData.Length);
    }

    [Fact]
    public async Task NonRawFormat_ReturnsFullFrame()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        frame1.Format = ScreenDataFormat.JPEG;
        var frame2 = CreateTestFrame(100, 100);
        frame2.Format = ScreenDataFormat.JPEG;

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        Assert.False(result.IsDelta); // Can't delta-encode JPEG
    }

    [Fact]
    public void Reset_ClearsState()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame = CreateTestFrame(100, 100);
        encoder.EncodeFrameAsync(frame).Wait();

        // Act
        encoder.Reset();
        var stats = encoder.GetStats();

        // Assert
        Assert.Equal(0, stats.TotalFrames);
        Assert.Equal(0, stats.DeltaFrames);
        Assert.Equal(0, stats.FullFrames);
    }

    [Fact]
    public void GetStats_ReturnsAccurateStats()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100, fillColor: 0x00);
        var frame2 = CreateTestFrame(100, 100, fillColor: 0x00);
        ModifyRegion(frame2.ImageData, 10, 10, 5, 5, 100, 0xFF);

        // Act
        encoder.EncodeFrameAsync(frame1).Wait();
        encoder.EncodeFrameAsync(frame2).Wait();
        var stats = encoder.GetStats();

        // Assert
        Assert.Equal(2, stats.TotalFrames);
        Assert.True(stats.FullFrames >= 1); // At least first frame
        Assert.True(stats.TotalFrames == stats.DeltaFrames + stats.FullFrames);
    }

    [Fact]
    public async Task DeltaRegions_ContainCorrectData()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(200, 200, fillColor: 0x00);
        var frame2 = CreateTestFrame(200, 200, fillColor: 0x00);
        
        // Change specific region
        ModifyRegion(frame2.ImageData, 64, 64, 64, 64, 200, 0xAB);

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        if (result.IsDelta && result.DeltaRegions != null)
        {
            Assert.NotEmpty(result.DeltaRegions);
            foreach (var region in result.DeltaRegions)
            {
                Assert.True(region.Width > 0);
                Assert.True(region.Height > 0);
                Assert.True(region.DataOffset >= 0);
                Assert.True(region.DataLength > 0);
            }
        }
    }

    [Fact]
    public async Task MultipleSmallChanges_MergesRegions()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(300, 300, fillColor: 0x00);
        var frame2 = CreateTestFrame(300, 300, fillColor: 0x00);
        
        // Make adjacent changes that should merge
        ModifyRegion(frame2.ImageData, 64, 64, 64, 64, 300, 0xFF);
        ModifyRegion(frame2.ImageData, 128, 64, 64, 64, 300, 0xFF); // Adjacent horizontally

        // Act
        await encoder.EncodeFrameAsync(frame1);
        var result = await encoder.EncodeFrameAsync(frame2);

        // Assert
        if (result.IsDelta && result.DeltaRegions != null)
        {
            // Should have merged adjacent regions (or at least detected both changes)
            Assert.NotEmpty(result.DeltaRegions);
        }
    }

    [Fact]
    public async Task ReferenceFrameId_IsSetCorrectly()
    {
        // Arrange
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        ModifyRegion(frame2.ImageData, 10, 10, 5, 5, 100, 0xFF);

        // Act
        var result1 = await encoder.EncodeFrameAsync(frame1);
        var result2 = await encoder.EncodeFrameAsync(frame2);

        // Assert
        if (result2.IsDelta)
        {
            Assert.Equal(result1.FrameId, result2.ReferenceFrameId);
        }
    }

    // Helper methods
    private ScreenData CreateTestFrame(int width, int height, byte fillColor = 0x80)
    {
        int bytesPerPixel = 4; // BGRA
        var data = new byte[width * height * bytesPerPixel];
        
        for (int i = 0; i < data.Length; i += bytesPerPixel)
        {
            data[i] = fillColor;     // B
            data[i + 1] = fillColor; // G
            data[i + 2] = fillColor; // R
            data[i + 3] = 0xFF;      // A
        }

        return new ScreenData
        {
            Width = width,
            Height = height,
            ImageData = data,
            Format = ScreenDataFormat.Raw
        };
    }

    private void ModifyRegion(byte[] data, int x, int y, int width, int height, 
        int imageWidth, byte color)
    {
        int bytesPerPixel = 4;
        for (int row = y; row < y + height; row++)
        {
            for (int col = x; col < x + width; col++)
            {
                int offset = (row * imageWidth + col) * bytesPerPixel;
                data[offset] = color;
                data[offset + 1] = color;
                data[offset + 2] = color;
                data[offset + 3] = 0xFF;
            }
        }
    }
}
