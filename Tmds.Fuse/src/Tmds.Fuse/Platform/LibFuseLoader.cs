using System;
using System.Runtime.InteropServices;

#nullable enable

namespace Tmds.Fuse;

/// <summary>
/// Loads the native FUSE library and resolves function symbols.
/// Uses NativeLibrary on both platforms. On Linux, falls back to dlvsym
/// for versioned symbol lookup required by libfuse3.
/// </summary>
internal static class LibFuseLoader
{
    public static IntPtr LoadLibrary()
    {
#if LINUX
        // Try SONAME first, then unversioned
        if (NativeLibrary.TryLoad("libfuse3.so.3", out var handle))
            return handle;
        if (NativeLibrary.TryLoad("libfuse3.so", out handle))
            return handle;
        return IntPtr.Zero;
#elif MACOS
        // FUSE-T installs to /usr/local/lib
        if (NativeLibrary.TryLoad("/usr/local/lib/libfuse-t.dylib", out var handle))
            return handle;
        if (NativeLibrary.TryLoad("libfuse-t", out handle))
            return handle;
        return IntPtr.Zero;
#else
        return IntPtr.Zero;
#endif
    }

    public static IntPtr GetExport(IntPtr libHandle, string name, string? version = null)
    {
#if LINUX
        // Try versioned lookup first (libfuse3 uses symbol versioning)
        if (version != null)
        {
            var ptr = DlVsym(libHandle, name, version);
            if (ptr != IntPtr.Zero)
                return ptr;
        }
#endif
        // Fall back to unversioned (works on macOS, fallback on Linux)
        if (NativeLibrary.TryGetExport(libHandle, name, out nint address))
            return address;
        return IntPtr.Zero;
    }

#if LINUX
    [DllImport("libdl.so.2", EntryPoint = "dlvsym")]
    private static extern IntPtr DlVsym(IntPtr handle, string symbol, string version);
#endif
}
