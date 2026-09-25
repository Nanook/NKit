using FsCheck;
using FsCheck.Xunit;
using NkdsUi.Models;
using NkdsUi.ViewModels;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for Per-Image Error Accumulation.
///
/// Property 6: Per-Image Error Accumulation
///
/// **Validates: Requirements 3.8**
///
/// Generate random sequences of error messages for the same image (already in Failed state).
/// Verify each message is appended to OutputText, not replacing previous content.
/// </summary>
public class PerImageErrorAccumulationPropertyTests
{
    /// <summary>
    /// Creates an ImageRowViewModel in Failed state with an initial error message,
    /// simulating an image that has already encountered its first failure.
    /// </summary>
    private static ImageRowViewModel CreateFailedImageRow(string initialError)
    {
        ImageRecord image = new ImageRecord
        {
            Id = 1,
            Name = "TestImage",
            SetName = "TestSet",
            Size = 1024
        };
        ImageRowViewModel row = new ImageRowViewModel(image, "session-1");
        // Set to Failed state with initial error (simulates first failure)
        row.ProcessingStatus = ImageProcessingStatus.Failed;
        row.StatusReason = initialError;
        row.AppendOutput(initialError);
        return row;
    }

    /// <summary>
    /// **Validates: Requirements 3.8**
    ///
    /// Property 6: Per-Image Error Accumulation.
    /// For any random sequence of error messages appended to an image already in Failed state:
    /// 1. Each message is appended to OutputText (not replacing previous content)
    /// 2. OutputText contains all messages in order
    /// 3. The ProcessingStatus remains Failed throughout
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ErrorMessages_AreAppended_NotReplaced(NonEmptyString initialError, NonEmptyString[] additionalErrors)
    {
        // Filter out null/empty entries and ensure we have at least one additional error
        List<string> errors = (additionalErrors ?? Array.Empty<NonEmptyString>())
            .Where(e => e != null)
            .Select(e => e.Get)
            .ToList();

        if (errors.Count == 0)
            return true; // Vacuously true — no additional errors to test

        // Arrange: create an image already in Failed state
        ImageRowViewModel row = CreateFailedImageRow(initialError.Get);
        string outputAfterInitial = row.OutputText;

        // Act: append each additional error message (simulating per-image error accumulation)
        foreach (string errorMessage in errors)
        {
            row.AppendOutput(errorMessage);
        }

        // Assert:
        // 1. OutputText still contains the initial error
        if (!row.OutputText.Contains(initialError.Get))
            return false;

        // 2. OutputText contains every additional error message
        foreach (string errorMessage in errors)
        {
            if (!row.OutputText.Contains(errorMessage))
                return false;
        }

        // 3. ProcessingStatus remains Failed
        if (row.ProcessingStatus != ImageProcessingStatus.Failed)
            return false;

        // 4. OutputText length grew (not replaced) — it must be longer than after initial error
        if (row.OutputText.Length <= outputAfterInitial.Length)
            return false;

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 3.8**
    ///
    /// Property 6: Per-Image Error Accumulation (ordering).
    /// For any random sequence of error messages, the order of messages in OutputText
    /// matches the order they were appended.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ErrorMessages_PreserveOrder(NonEmptyString[] errorMessages)
    {
        List<string> messages = (errorMessages ?? Array.Empty<NonEmptyString>())
            .Where(e => e != null)
            .Select(e => e.Get)
            .ToList();

        if (messages.Count < 2)
            return true; // Need at least 2 messages to verify ordering

        // Arrange: create an image in Failed state with first message
        ImageRowViewModel row = CreateFailedImageRow(messages[0]);

        // Act: append remaining messages
        for (int i = 1; i < messages.Count; i++)
        {
            row.AppendOutput(messages[i]);
        }

        // Assert: each message appears after the previous one in OutputText
        string output = row.OutputText;
        int lastIndex = -1;
        foreach (string msg in messages)
        {
            int idx = output.IndexOf(msg, lastIndex + 1, StringComparison.Ordinal);
            if (idx < 0)
                return false; // Message not found after previous message's position
            lastIndex = idx;
        }

        return true;
    }
}