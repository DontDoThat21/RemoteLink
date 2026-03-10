namespace RemoteLink.Shared.Models;

public sealed class PresentationJoinRequest
{
    public string ViewerDeviceId { get; set; } = string.Empty;
    public string ViewerDeviceName { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PresentationJoinResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? SessionName { get; set; }
    public SessionPermissionSet? SessionPermissions { get; set; }
}

public sealed class PresentationBroadcastResult
{
    public int SuccessfulViewerCount { get; set; }
    public long TotalBytesSent { get; set; }
    public long MaxSendLatencyMs { get; set; }
}

public enum PresentationAnnotationKind
{
    Freehand,
    Arrow,
    Rectangle,
    Ellipse,
    Text
}

public enum PresentationAnnotationAction
{
    Upsert,
    Remove,
    Clear
}

public sealed class PresentationAnnotationPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class PresentationAnnotationStyle
{
    public string StrokeColor { get; set; } = "#FF3B30";
    public string? FillColor { get; set; }
    public double StrokeWidth { get; set; } = 3;
    public double FontSize { get; set; } = 16;
    public double Opacity { get; set; } = 1;
}

public sealed class PresentationAnnotation
{
    public string AnnotationId { get; set; } = Guid.NewGuid().ToString("N");
    public PresentationAnnotationKind Kind { get; set; }
    public List<PresentationAnnotationPoint> Points { get; set; } = new();
    public string? Text { get; set; }
    public PresentationAnnotationStyle Style { get; set; } = new();
    public string CreatedByDeviceId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PresentationAnnotationMessage
{
    public PresentationAnnotationAction Action { get; set; } = PresentationAnnotationAction.Upsert;
    public string? AnnotationId { get; set; }
    public PresentationAnnotation? Annotation { get; set; }
    public string ChangedByDeviceId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
}
