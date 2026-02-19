namespace RemoteLink.Shared.Models;

/// <summary>
/// Statistics for delta frame encoder performance
/// </summary>
public class DeltaEncoderStats
{
    public long TotalFrames { get; set; }
    public long DeltaFrames { get; set; }
    public long FullFrames { get; set; }
    public long BytesSaved { get; set; }
    public double AverageCompressionRatio { get; set; }
}
