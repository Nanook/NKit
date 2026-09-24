using NkdsUi.Services;
using ReactiveUI.Primitives;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for ErrorNotificationService.
///
/// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.6
/// </summary>
public class ErrorNotificationServiceTests
{
    [Fact]
    public void PublishOperationError_EmitsToOperationErrorsStream()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        OperationError received = null;
        service.OperationErrors.Subscribe(e => received = e);

        service.PublishOperationError("Something went wrong");

        Assert.NotNull(received);
        Assert.Equal("Something went wrong", received.Message);
        Assert.False(received.IsCancellation);
    }

    [Fact]
    public void PublishCancellation_EmitsWithIsCancellationTrue()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        OperationError received = null;
        service.OperationErrors.Subscribe(e => received = e);

        service.PublishCancellation("Compact");

        Assert.NotNull(received);
        Assert.True(received.IsCancellation);
        Assert.Contains("Compact", received.Message);
        Assert.Contains("cancelled", received.Message);
    }

    [Fact]
    public void PublishImageError_EmitsToImageErrorsStream()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        ImageError received = null;
        service.ImageErrors.Subscribe(e => received = e);

        service.PublishImageError("SetA", 42, "File not found");

        Assert.NotNull(received);
        Assert.Equal("SetA", received.SetName);
        Assert.Equal(42, received.ImageId);
        Assert.Equal("File not found", received.Message);
    }

    [Fact]
    public void PublishImageError_ShortMessage_NotTruncated()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        ImageError received = null;
        service.ImageErrors.Subscribe(e => received = e);

        string message = new string('x', 256);
        service.PublishImageError("SetA", 1, message);

        Assert.NotNull(received);
        Assert.Equal(256, received.Message.Length);
        Assert.Equal(message, received.Message);
    }

    [Fact]
    public void PublishImageError_LongMessage_TruncatedTo256WithEllipsis()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        ImageError received = null;
        service.ImageErrors.Subscribe(e => received = e);

        string message = new string('x', 300);
        service.PublishImageError("SetA", 1, message);

        Assert.NotNull(received);
        Assert.Equal(256, received.Message.Length);
        Assert.EndsWith("...", received.Message);
        Assert.StartsWith(new string('x', 253), received.Message);
    }

    [Fact]
    public void PublishImageError_ExactlyAtBoundary_NotTruncated()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        ImageError received = null;
        service.ImageErrors.Subscribe(e => received = e);

        string message = new string('a', 256);
        service.PublishImageError("SetB", 99, message);

        Assert.NotNull(received);
        Assert.Equal(256, received.Message.Length);
        Assert.DoesNotContain("...", received.Message);
    }

    [Fact]
    public void PublishImageError_OneOverBoundary_IsTruncated()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        ImageError received = null;
        service.ImageErrors.Subscribe(e => received = e);

        string message = new string('b', 257);
        service.PublishImageError("SetC", 7, message);

        Assert.NotNull(received);
        Assert.Equal(256, received.Message.Length);
        Assert.EndsWith("...", received.Message);
    }

    [Fact]
    public void OperationErrors_DoesNotReceiveImageErrors()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        List<OperationError> operationErrors = new List<OperationError>();
        service.OperationErrors.Subscribe(e => operationErrors.Add(e));

        service.PublishImageError("SetA", 1, "image error");

        Assert.Empty(operationErrors);
    }

    [Fact]
    public void ImageErrors_DoesNotReceiveOperationErrors()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        List<ImageError> imageErrors = new List<ImageError>();
        service.ImageErrors.Subscribe(e => imageErrors.Add(e));

        service.PublishOperationError("operation error");

        Assert.Empty(imageErrors);
    }

    [Fact]
    public void MultipleSubscribers_AllReceiveNotifications()
    {
        ErrorNotificationService service = new ErrorNotificationService();
        OperationError received1 = null;
        OperationError received2 = null;
        service.OperationErrors.Subscribe(e => received1 = e);
        service.OperationErrors.Subscribe(e => received2 = e);

        service.PublishOperationError("shared error");

        Assert.NotNull(received1);
        Assert.NotNull(received2);
        Assert.Equal("shared error", received1.Message);
        Assert.Equal("shared error", received2.Message);
    }
}