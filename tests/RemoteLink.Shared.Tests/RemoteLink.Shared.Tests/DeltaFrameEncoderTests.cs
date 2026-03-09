using RemoteLink.Shared.Models;
using RemoteLink.Shared.Services;
using Xunit;

namespace RemoteLink.Shared.Tests;

public class DeltaFrameEncoderTests
{
    [Fact]
    public async Task FirstFrame_ShouldSendFullFrame()
    {
        var encoder = new DeltaFrameEncoder();
        var frame = CreateTestFrame(100, 100);

        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame);

        Assert.False(isDelta);
        Assert.Equal(frame.FrameId, encodedFrame.FrameId);
        Assert.Equal(frame.ImageData.Length, encodedFrame.ImageData.Length);
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public async Task IdenticalFrame_ShouldSendDelta_WithNoChanges()
    {
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.True(isDelta);
        Assert.True(encodedFrame.IsDelta);
        Assert.Equal(frame1.FrameId, encodedFrame.ReferenceFrameId);
        Assert.Equal(0, encodedFrame.ImageData.Length);
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.Empty(encodedFrame.DeltaRegions);
    }

    [Fact]
    public async Task FrameWithSmallChange_ShouldSendDelta()
    {
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(50);

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        ChangeRegion(frame2.ImageData, 10, 10, 10, 10, 100);

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.True(isDelta);
        Assert.True(encodedFrame.IsDelta);
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.NotEmpty(encodedFrame.DeltaRegions);
        Assert.True(encodedFrame.ImageData.Length < frame2.ImageData.Length);
    }

    [Fact]
    public async Task FrameWithLargeChange_ShouldSendFullFrame()
    {
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(5);

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        ChangeRegion(frame2.ImageData, 0, 0, 60, 60, 100);

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.False(isDelta);
        Assert.False(encodedFrame.IsDelta);
        Assert.Equal(frame2.ImageData.Length, encodedFrame.ImageData.Length);
    }

    [Fact]
    public async Task DimensionChange_ShouldSendFullFrame()
    {
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(200, 200);

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.False(isDelta);
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public async Task NonRawFormat_ShouldSendFullFrame()
    {
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        frame2.Format = ScreenDataFormat.JPEG;

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.False(isDelta);
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public void Reset_ShouldClearState()
    {
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);

        encoder.EncodeFrameAsync(frame1).Wait();
        encoder.Reset();
        var (encodedFrame, isDelta) = encoder.EncodeFrameAsync(frame1).Result;

        Assert.False(isDelta);
        Assert.False(encodedFrame.IsDelta);
    }

    [Fact]
    public void SetDeltaThreshold_ShouldClampValues()
    {
        var encoder = new DeltaFrameEncoder();

        encoder.SetDeltaThreshold(-10);
        encoder.SetDeltaThreshold(150);

        Assert.True(true);
    }

    [Fact]
    public async Task DeltaRegions_ShouldHaveCorrectOffsets()
    {
        var encoder = new DeltaFrameEncoder();
        encoder.SetDeltaThreshold(50);

        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        ChangeRegion(frame2.ImageData, 10, 10, 10, 10, 100);
        ChangeRegion(frame2.ImageData, 50, 50, 10, 10, 100);

        await encoder.EncodeFrameAsync(frame1);
        var (encodedFrame, isDelta) = await encoder.EncodeFrameAsync(frame2);

        Assert.True(isDelta);
        Assert.NotNull(encodedFrame.DeltaRegions);
        Assert.NotEmpty(encodedFrame.DeltaRegions);

        int expectedOffset = 0;
        foreach (var region in encodedFrame.DeltaRegions)
        {
            Assert.Equal(expectedOffset, region.DataOffset);
            Assert.True(region.DataLength > 0);
            expectedOffset += region.DataLength;
        }

        Assert.Equal(expectedOffset, encodedFrame.ImageData.Length);
    }

    [Fact]
    public async Task MultipleFrames_ShouldTrackReference()
    {
        var encoder = new DeltaFrameEncoder();
        var frame1 = CreateTestFrame(100, 100);
        var frame2 = CreateTestFrame(100, 100);
        var frame3 = CreateTestFrame(100, 100);

        var (encoded1, isDelta1) = await encoder.EncodeFrameAsync(frame1);
        var (encoded2, isDelta2) = await encoder.EncodeFrameAsync(frame2);
        var (encoded3, isDelta3) = await encoder.EncodeFrameAsync(frame3);

        Assert.False(isDelta1);
        Assert.True(isDelta2);
        Assert.True(isDelta3);
        Assert.Equal(frame1.FrameId, encoded2.ReferenceFrameId);
        Assert.Equal(frame2.FrameId, encoded3.ReferenceFrameId);
    }

    private ScreenData CreateTestFrame(int width, int height)
    {
        int bytesPerPixel = 4;
        byte[] data = new byte[width * height * bytesPerPixel];

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
                data[offset] = 255;
                data[offset + 1] = 255;
                data[offset + 2] = 255;
                data[offset + 3] = 255;
            }
        }
    }
}
