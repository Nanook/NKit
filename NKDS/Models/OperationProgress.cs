namespace NKDS.Models;

/// <summary>
/// Progress report for long-running operations.
/// Percentage is clamped to 0–100. CurrentItem is truncated to 260 characters.
/// </summary>
public sealed class OperationProgress
{
    private int _percentage;
    private string _currentItem = "";

    /// <summary>
    /// Percentage complete, clamped to [0, 100].
    /// </summary>
    public int Percentage
    {
        get => _percentage;
        init => _percentage = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Current item being processed, truncated to a maximum of 260 characters.
    /// </summary>
    public string CurrentItem
    {
        get => _currentItem;
        init => _currentItem = (value ?? "").Length > 260 ? value![..260] : value ?? "";
    }

    public int ItemsProcessed { get; init; }
    public int TotalItems { get; init; }
    public TimeSpan Elapsed { get; init; }
}