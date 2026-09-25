using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Services;
using ReactiveUI.Primitives;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for Error Stream Routing.
///
/// Property 3: Error Stream Routing
///
/// **Validates: Requirements 3.2, 3.6**
///
/// Generate random error scenarios (failure types + cancellations). Subscribe to both streams.
/// Verify each error appears on exactly one stream with correct IsCancellation flag.
/// </summary>
public class ErrorStreamRoutingPropertyTests
{
    /// <summary>
    /// Represents a single error scenario to publish.
    /// </summary>
    private enum ErrorKind
    {
        OperationFailure,
        Cancellation,
        ImageError
    }

    /// <summary>
    /// Represents a generated error scenario with its kind and associated data.
    /// </summary>
    private record ErrorScenario(ErrorKind Kind, string Message, string SetName, long ImageId);

    /// <summary>
    /// **Validates: Requirements 3.2, 3.6**
    ///
    /// Property 3: Error Stream Routing.
    /// For any random sequence of error scenarios (operation failures, cancellations, image errors):
    /// 1. Operation failures appear only on the OperationErrors stream with IsCancellation = false
    /// 2. Cancellations appear only on the OperationErrors stream with IsCancellation = true
    /// 3. Image errors appear only on the ImageErrors stream
    /// 4. No error appears on both streams simultaneously
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ErrorsRouteToCorrectStream_WithCorrectIsCancellationFlag(
        PositiveInt scenarioCount, NonNegativeInt seed)
    {
        int count = Math.Min(scenarioCount.Get, 20); // Cap at 20 scenarios per test
        Random rng = new Random(seed.Get);
        List<ErrorScenario> scenarios = generateScenarios(count, rng);

        return runRoutingScenario(scenarios);
    }

    /// <summary>
    /// **Validates: Requirements 3.2, 3.6**
    ///
    /// Verifies that operation failures (non-cancellation) always have IsCancellation = false.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OperationFailure_AlwaysHasIsCancellationFalse(NonEmptyString message)
    {
        ErrorNotificationService service = new ErrorNotificationService();
        OperationError received = null;
        List<ImageError> imageErrors = new List<ImageError>();

        service.OperationErrors.Subscribe(e => received = e);
        service.ImageErrors.Subscribe(e => imageErrors.Add(e));

        service.PublishOperationError(message.Get);

        // Must appear on operation stream with IsCancellation = false
        if (received == null) return false;
        if (received.IsCancellation) return false;
        if (received.Message != message.Get) return false;

        // Must NOT appear on image stream
        if (imageErrors.Count != 0) return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.6**
    ///
    /// Verifies that cancellations always have IsCancellation = true and route to operation stream only.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Cancellation_AlwaysHasIsCancellationTrue_RoutesToOperationStream(
        NonEmptyString operationName)
    {
        ErrorNotificationService service = new ErrorNotificationService();
        OperationError received = null;
        List<ImageError> imageErrors = new List<ImageError>();

        service.OperationErrors.Subscribe(e => received = e);
        service.ImageErrors.Subscribe(e => imageErrors.Add(e));

        service.PublishCancellation(operationName.Get);

        // Must appear on operation stream with IsCancellation = true
        if (received == null) return false;
        if (!received.IsCancellation) return false;
        if (!received.Message.Contains(operationName.Get)) return false;
        if (!received.Message.Contains("cancelled")) return false;

        // Must NOT appear on image stream
        if (imageErrors.Count != 0) return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.2**
    ///
    /// Verifies that image errors route exclusively to the image stream and never to the operation stream.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ImageError_RoutesExclusivelyToImageStream(
        NonEmptyString setName, PositiveInt imageId, NonEmptyString message)
    {
        ErrorNotificationService service = new ErrorNotificationService();
        List<OperationError> operationErrors = new List<OperationError>();
        ImageError received = null;

        service.OperationErrors.Subscribe(e => operationErrors.Add(e));
        service.ImageErrors.Subscribe(e => received = e);

        service.PublishImageError(setName.Get, imageId.Get, message.Get);

        // Must appear on image stream
        if (received == null) return false;
        if (received.SetName != setName.Get) return false;
        if (received.ImageId != imageId.Get) return false;

        // Must NOT appear on operation stream
        if (operationErrors.Count != 0) return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.2, 3.6**
    ///
    /// Verifies that a mixed sequence of errors routes each to exactly one stream
    /// with the correct IsCancellation flag, and no cross-contamination occurs.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MixedErrorSequence_EachAppearsOnExactlyOneStream(
        PositiveInt scenarioCount, NonNegativeInt seed)
    {
        int count = Math.Min(scenarioCount.Get, 30);
        Random rng = new Random(seed.Get);
        List<ErrorScenario> scenarios = generateScenarios(count, rng);

        ErrorNotificationService service = new ErrorNotificationService();
        List<OperationError> operationErrors = new List<OperationError>();
        List<ImageError> imageErrors = new List<ImageError>();

        service.OperationErrors.Subscribe(e => operationErrors.Add(e));
        service.ImageErrors.Subscribe(e => imageErrors.Add(e));

        int expectedOperationCount = 0;
        int expectedImageCount = 0;

        foreach (ErrorScenario scenario in scenarios)
        {
            switch (scenario.Kind)
            {
                case ErrorKind.OperationFailure:
                    service.PublishOperationError(scenario.Message);
                    expectedOperationCount++;
                    break;
                case ErrorKind.Cancellation:
                    service.PublishCancellation(scenario.Message);
                    expectedOperationCount++;
                    break;
                case ErrorKind.ImageError:
                    service.PublishImageError(scenario.SetName!, scenario.ImageId, scenario.Message);
                    expectedImageCount++;
                    break;
            }
        }

        // Total counts must match exactly — no duplication, no loss
        if (operationErrors.Count != expectedOperationCount) return false;
        if (imageErrors.Count != expectedImageCount) return false;

        // Verify IsCancellation flags for operation errors
        int opIdx = 0;
        foreach (ErrorScenario scenario in scenarios)
        {
            if (scenario.Kind == ErrorKind.OperationFailure)
            {
                if (operationErrors[opIdx].IsCancellation) return false;
                if (operationErrors[opIdx].Message != scenario.Message) return false;
                opIdx++;
            }
            else if (scenario.Kind == ErrorKind.Cancellation)
            {
                if (!operationErrors[opIdx].IsCancellation) return false;
                if (!operationErrors[opIdx].Message.Contains(scenario.Message)) return false;
                opIdx++;
            }
        }

        // Verify image errors match their scenarios
        int imgIdx = 0;
        foreach (ErrorScenario scenario in scenarios)
        {
            if (scenario.Kind == ErrorKind.ImageError)
            {
                if (imageErrors[imgIdx].SetName != scenario.SetName) return false;
                if (imageErrors[imgIdx].ImageId != scenario.ImageId) return false;
                imgIdx++;
            }
        }

        return true;
    }

    private static List<ErrorScenario> generateScenarios(int count, Random rng)
    {
        List<ErrorScenario> scenarios = new List<ErrorScenario>(count);
        for (int i = 0; i < count; i++)
        {
            ErrorKind kind = (ErrorKind)rng.Next(3);
            string message = $"Error_{i}_{rng.Next(10000)}";
            string setName = kind == ErrorKind.ImageError ? $"Set_{rng.Next(100)}" : null;
            long imageId = kind == ErrorKind.ImageError ? rng.Next(1, 100000) : 0;
            scenarios.Add(new ErrorScenario(kind, message, setName, imageId));
        }
        return scenarios;
    }

    private static bool runRoutingScenario(List<ErrorScenario> scenarios)
    {
        ErrorNotificationService service = new ErrorNotificationService();
        List<OperationError> operationErrors = new List<OperationError>();
        List<ImageError> imageErrors = new List<ImageError>();

        service.OperationErrors.Subscribe(e => operationErrors.Add(e));
        service.ImageErrors.Subscribe(e => imageErrors.Add(e));

        foreach (ErrorScenario scenario in scenarios)
        {
            switch (scenario.Kind)
            {
                case ErrorKind.OperationFailure:
                    service.PublishOperationError(scenario.Message);
                    break;
                case ErrorKind.Cancellation:
                    service.PublishCancellation(scenario.Message);
                    break;
                case ErrorKind.ImageError:
                    service.PublishImageError(scenario.SetName!, scenario.ImageId, scenario.Message);
                    break;
            }
        }

        // Verify: each error appears on exactly one stream
        int expectedOpErrors = scenarios.Count(s => s.Kind is ErrorKind.OperationFailure or ErrorKind.Cancellation);
        int expectedImgErrors = scenarios.Count(s => s.Kind == ErrorKind.ImageError);

        if (operationErrors.Count != expectedOpErrors) return false;
        if (imageErrors.Count != expectedImgErrors) return false;

        // Verify IsCancellation flag correctness
        int opIdx = 0;
        foreach (ErrorScenario scenario in scenarios.Where(s => s.Kind is ErrorKind.OperationFailure or ErrorKind.Cancellation))
        {
            OperationError error = operationErrors[opIdx];
            if (scenario.Kind == ErrorKind.Cancellation)
            {
                if (!error.IsCancellation) return false;
                if (!error.Message.Contains("cancelled")) return false;
            }
            else
            {
                if (error.IsCancellation) return false;
                if (error.Message != scenario.Message) return false;
            }
            opIdx++;
        }

        return true;
    }
}