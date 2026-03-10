using RemoteLink.Shared.Models;

namespace RemoteLink.Shared.Services;

public sealed class PresentationAnnotationBoard
{
    private readonly Dictionary<string, PresentationAnnotation> _annotations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public event EventHandler<PresentationAnnotationMessage>? Changed;

    public IReadOnlyList<PresentationAnnotation> GetAnnotations()
    {
        lock (_sync)
        {
            return _annotations.Values
                .Select(CloneAnnotation)
                .ToList();
        }
    }

    public PresentationAnnotationMessage Apply(PresentationAnnotationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        PresentationAnnotationMessage? raisedMessage = null;
        lock (_sync)
        {
            switch (message.Action)
            {
                case PresentationAnnotationAction.Upsert:
                    if (message.Annotation is null)
                        throw new ArgumentException("Annotation payload is required for upsert actions.", nameof(message));

                    var annotation = CloneAnnotation(message.Annotation);
                    if (string.IsNullOrWhiteSpace(annotation.AnnotationId))
                        annotation.AnnotationId = Guid.NewGuid().ToString("N");

                    _annotations[annotation.AnnotationId] = annotation;
                    raisedMessage = CloneMessage(message, annotation.AnnotationId, annotation);
                    break;

                case PresentationAnnotationAction.Remove:
                    if (string.IsNullOrWhiteSpace(message.AnnotationId))
                        throw new ArgumentException("AnnotationId is required for remove actions.", nameof(message));

                    _annotations.Remove(message.AnnotationId);
                    raisedMessage = CloneMessage(message, message.AnnotationId, null);
                    break;

                case PresentationAnnotationAction.Clear:
                    _annotations.Clear();
                    raisedMessage = CloneMessage(message, null, null);
                    break;
            }
        }

        if (raisedMessage is null)
            throw new InvalidOperationException($"Unsupported annotation action: {message.Action}.");

        Changed?.Invoke(this, raisedMessage);
        return raisedMessage;
    }

    public void Clear(string? changedByDeviceId = null)
    {
        Apply(new PresentationAnnotationMessage
        {
            Action = PresentationAnnotationAction.Clear,
            ChangedByDeviceId = changedByDeviceId ?? string.Empty,
            ChangedAtUtc = DateTime.UtcNow
        });
    }

    private static PresentationAnnotationMessage CloneMessage(
        PresentationAnnotationMessage source,
        string? annotationId,
        PresentationAnnotation? annotation)
    {
        return new PresentationAnnotationMessage
        {
            Action = source.Action,
            AnnotationId = annotationId,
            Annotation = annotation is null ? null : CloneAnnotation(annotation),
            ChangedByDeviceId = source.ChangedByDeviceId,
            ChangedAtUtc = source.ChangedAtUtc
        };
    }

    private static PresentationAnnotation CloneAnnotation(PresentationAnnotation annotation)
    {
        return new PresentationAnnotation
        {
            AnnotationId = annotation.AnnotationId,
            Kind = annotation.Kind,
            Text = annotation.Text,
            CreatedByDeviceId = annotation.CreatedByDeviceId,
            CreatedAtUtc = annotation.CreatedAtUtc,
            Style = new PresentationAnnotationStyle
            {
                StrokeColor = annotation.Style.StrokeColor,
                FillColor = annotation.Style.FillColor,
                StrokeWidth = annotation.Style.StrokeWidth,
                FontSize = annotation.Style.FontSize,
                Opacity = annotation.Style.Opacity
            },
            Points = annotation.Points
                .Select(point => new PresentationAnnotationPoint { X = point.X, Y = point.Y })
                .ToList()
        };
    }
}
