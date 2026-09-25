using System.Runtime.InteropServices;
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters
#pragma warning disable IDE1006 // Naming Styles

namespace Tmds.Fuse;

/// <summary>Timespec — identical layout on Linux and macOS (64-bit).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct timespec
{
    public long tv_sec;
    public long tv_nsec;
}

/// <summary>Mode type — 32-bit unsigned on both platforms in FUSE context.</summary>
public enum mode_t : uint { }

#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters
