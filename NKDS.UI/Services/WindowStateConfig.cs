namespace NkdsUi.Services;

/// <summary>
/// Represents persisted window position and size state.
/// </summary>
public sealed class WindowStateConfig
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}