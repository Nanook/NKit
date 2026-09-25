using System;
using System.Diagnostics;
using System.IO;

namespace Tmds.Fuse
{
    public static class Fuse
    {
        public static IFuseMount Mount(string mountPoint, IFuseFileSystem fileSystem, MountOptions options = null)
        {
            if (options == null)
            {
                options = new MountOptions();
            }

            FuseMount mount = new FuseMount(mountPoint, fileSystem, options);
            mount.Mount();
            return mount;
        }

        public static bool CheckDependencies()
        {
#if LINUX
            return LibFuse.IsAvailable && HasFusermount;
#elif MACOS
            // FUSE-T doesn't use fusermount; macOS uses native mount/umount
            return LibFuse.IsAvailable;
#else
            return false;
#endif
        }

        public static string InstallationInstructions
        {
            get
            {
#if LINUX
                return "To run, libfuse (libfuse3.so.3) and fusermount3 must be installed.\n"
                     + "  Debian/Ubuntu: sudo apt install fuse3 libfuse3-dev\n"
                     + "  Fedora/RHEL:   sudo dnf install fuse3 fuse3-devel\n"
                     + "  Arch:          sudo pacman -S fuse3";
#elif MACOS
                return "To run, FUSE-T must be installed.\n"
                     + "  Install via Homebrew: brew install macos-fuse-t/homebrew-cask/fuse-t\n"
                     + "  Or download from: https://github.com/macos-fuse-t/fuse-t/releases";
#else
                return "FUSE is not supported on this platform.";
#endif
            }
        }



        public static void LazyUnmount(string mountPoint)
        {
#if LINUX
            var psi = new ProcessStartInfo
            {
                FileName = "fusermount3",
                Arguments = $"-u -q -z {mountPoint}",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
#elif MACOS
            var psi = new ProcessStartInfo
            {
                FileName = "umount",
                Arguments = mountPoint,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
#endif
        }

#if LINUX
        private static bool HasFusermount => HasProgramOnPath("fusermount3");

        private static bool HasProgramOnPath(string program)
        {
            string pathEnvVar = Environment.GetEnvironmentVariable("PATH");
            if (pathEnvVar != null)
            {
                var segments = pathEnvVar.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var subPath in segments)
                {
                    string path = Path.Combine(subPath, program);
                    if (File.Exists(path))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
#endif
    }
}
