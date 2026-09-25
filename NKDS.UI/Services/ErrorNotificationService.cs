using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.Services;

/// <summary>
/// Represents an operation-level error or cancellation notice.
/// </summary>
public record OperationError(string Message, bool IsCancellation);

/// <summary>
/// Represents a per-image processing error for inline display.
/// </summary>
public record ImageError(string SetName, long ImageId, string Message);

/// <summary>
/// Centralized error notification service.
/// Exposes two Rx streams: operation-level errors (title bar) and per-image errors (inline).
/// Replaces all Console.Error.WriteLine usage in NKDS.UI.
/// </summary>
public class ErrorNotificationService
{
    private const int MaxImageErrorMessageLength = 256;
    private const int TruncatedMessageLength = 253;
    private const string Ellipsis = "...";

    private readonly ReplaySignal<OperationError> _operationErrors = new();
    private readonly ReplaySignal<ImageError> _imageErrors = new();

    /// <summary>
    /// Stream of operation-level errors displayed in the title bar area.
    /// </summary>
    public IObservable<OperationError> OperationErrors => _operationErrors.AsObservable();

    /// <summary>
    /// Stream of per-image errors displayed inline on ImageRowViewModels.
    /// </summary>
    public IObservable<ImageError> ImageErrors => _imageErrors.AsObservable();

    /// <summary>
    /// Publishes an operation-level error. Displayed via SessionManager.ShowError.
    /// </summary>
    public void PublishOperationError(string message) => _operationErrors.OnNext(new OperationError(message, IsCancellation: false));

    /// <summary>
    /// Publishes a cancellation notice (informational, not failure).
    /// </summary>
    public void PublishCancellation(string operationName)
    {
        _operationErrors.OnNext(new OperationError(
            $"{operationName} was cancelled", IsCancellation: true));
    }

    /// <summary>
    /// Publishes a per-image error for inline display.
    /// Messages exceeding 256 characters are truncated with ellipsis.
    /// </summary>
    public void PublishImageError(string setName, long imageId, string message)
    {
        string truncatedMessage = message.Length > MaxImageErrorMessageLength
            ? string.Concat(message.AsSpan(0, TruncatedMessageLength), Ellipsis)
            : message;

        _imageErrors.OnNext(new ImageError(setName, imageId, truncatedMessage));
    }
}