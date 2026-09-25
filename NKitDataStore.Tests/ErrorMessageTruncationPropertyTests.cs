using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NkdsUi.Services;
using ReactiveUI.Primitives;

namespace NKitDataStore.Tests;

/// <summary>
/// Property-based tests for Per-Image Error Message Truncation.
///
/// Property 4: Per-Image Error Message Truncation
///
/// **Validates: Requirements 3.3**
///
/// Generate random strings of varying lengths (0..1000 chars). Publish as per-image errors.
/// Verify StatusReason is ≤256 chars and correctly truncated with ellipsis when needed.
/// </summary>
public class ErrorMessageTruncationPropertyTests
{
    private const int MaxLength = 256;
    private const int TruncatedBodyLength = 253;
    private const string Ellipsis = "...";

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 4: Per-Image Error Message Truncation.
    /// For any random string of length 0..1000 published as a per-image error:
    /// 1. The resulting message (StatusReason) is ≤256 characters
    /// 2. If the original message was ≤256 chars, it is preserved unchanged
    /// 3. If the original message was >256 chars, it is truncated to 253 chars + "..."
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PerImageError_MessageTruncation_RespectsMaxLength()
    {
        Gen<string> messageGen = Gen.Choose(0, 1000).SelectMany(len =>
            Gen.Elements(Enumerable.Range(32, 95).Select(c => (char)c).ToArray())
               .ArrayOf(len)
               .Select(chars => new string(chars)));

        return Prop.ForAll(messageGen.ToArbitrary(), message =>
        {
            // Arrange
            ErrorNotificationService service = new ErrorNotificationService();
            ImageError receivedError = null;
            service.ImageErrors.Subscribe(e => receivedError = e);

            // Act
            service.PublishImageError("TestSet", 42, message);

            // Assert
            if (receivedError is null)
                return false;

            string resultMessage = receivedError.Message;

            // Property 1: Result is always ≤256 characters
            if (resultMessage.Length > MaxLength)
                return false;

            // Property 2: Short messages (≤256 chars) are preserved unchanged
            if (message.Length <= MaxLength)
            {
                if (resultMessage != message)
                    return false;
            }
            else
            {
                // Property 3: Long messages (>256 chars) are truncated to 253 + "..."
                if (resultMessage.Length != MaxLength)
                    return false;

                if (!resultMessage.EndsWith(Ellipsis))
                    return false;

                // The first 253 characters should match the original
                if (resultMessage[..TruncatedBodyLength] != message[..TruncatedBodyLength])
                    return false;
            }

            return true;
        });
    }
}