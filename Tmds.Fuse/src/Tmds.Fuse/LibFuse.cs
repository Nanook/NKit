using System;
using System.Runtime.InteropServices;
using size_t = System.UIntPtr;

namespace Tmds.Fuse
{
    static unsafe class LibFuse
    {
        private static readonly IntPtr s_libFuseHandle;

        public static bool IsAvailable => s_libFuseHandle != IntPtr.Zero;

#if LINUX
        public const string LibraryName = "libfuse3.so.3";
#elif MACOS
        public const string LibraryName = "libfuse-t.dylib";
#else
        public const string LibraryName = "libfuse3";
#endif

        public delegate fuse* fuse_new_Delegate(fuse_args* args, fuse_operations* op, size_t op_size, void* private_data);
        public static readonly fuse_new_Delegate fuse_new;

        public delegate int fuse_loop_Delegate(fuse* f);
        public static readonly fuse_loop_Delegate fuse_loop;

        public delegate int fuse_mount_Delegate(fuse* f, string mountpoint);
        public static readonly fuse_mount_Delegate fuse_mount;

        public delegate int fuse_opt_add_arg_Delegate(fuse_args* args, string arg);
        public static readonly fuse_opt_add_arg_Delegate fuse_opt_add_arg;

        public delegate void fuse_opt_free_args_Delegate(fuse_args* args);
        public static readonly fuse_opt_free_args_Delegate fuse_opt_free_args;

        public delegate int fuse_loop_mt_delegate(fuse* f, int clone_fd);
        public static readonly fuse_loop_mt_delegate fuse_loop_mt;

        public delegate void fuse_unmount_delegate(fuse* f);
        public static readonly fuse_unmount_delegate fuse_unmount;

        public delegate void fuse_destroy_delegate(fuse* f);
        public static readonly fuse_destroy_delegate fuse_destroy;

        static LibFuse()
        {
            s_libFuseHandle = LibFuseLoader.LoadLibrary();
            if (s_libFuseHandle == IntPtr.Zero)
                return;

            fuse_new = CreateDelegate<fuse_new_Delegate>("fuse_new", "FUSE_3.1");
            fuse_loop = CreateDelegate<fuse_loop_Delegate>("fuse_loop");
            fuse_mount = CreateDelegate<fuse_mount_Delegate>("fuse_mount");
            fuse_opt_add_arg = CreateDelegate<fuse_opt_add_arg_Delegate>("fuse_opt_add_arg");
            fuse_loop_mt = CreateDelegate<fuse_loop_mt_delegate>("fuse_loop_mt");
            fuse_unmount = CreateDelegate<fuse_unmount_delegate>("fuse_unmount");
            fuse_destroy = CreateDelegate<fuse_destroy_delegate>("fuse_destroy");
            fuse_opt_free_args = CreateDelegate<fuse_opt_free_args_Delegate>("fuse_opt_free_args");
        }

        private static T CreateDelegate<T>(string name, string version = "FUSE_3.0")
        {
            IntPtr functionPtr = LibFuseLoader.GetExport(s_libFuseHandle, name, version);
            if (functionPtr == IntPtr.Zero)
            {
                throw new FuseException($"Unable to resolve libfuse function {name}.");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(functionPtr);
        }
    }
}
