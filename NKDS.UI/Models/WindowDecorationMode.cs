namespace NkdsUi.Models;

/// <summary>
/// Window decoration rendering mode (Linux only).
/// </summary>
public enum WindowDecorationMode
{
    /// <summary>Application renders its own title bar (default).</summary>
    ClientSideDecorations,

    /// <summary>OS window manager renders the title bar.</summary>
    NativeTitleBar
}