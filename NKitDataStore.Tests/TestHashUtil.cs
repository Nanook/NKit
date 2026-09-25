using System.Reflection;

namespace NKitDataStore.Tests
{
    /// <summary>
    /// Test utility class to avoid namespace conflicts between NKit and NKitDataStore assemblies.
    /// Both assemblies have Crc and XXHash64 classes in the same namespace, causing ambiguity.
    /// </summary>
    internal static class TestHashUtil
    {
        /// <summary>
        /// Computes CRC32 using the NKit library implementation.
        /// </summary>
        public static uint ComputeCrc32(byte[] data)
        {
            // Use the full assembly-qualified type name to avoid ambiguity
            Type crcType = Type.GetType("Nanook.NKit.Crc, NKitLib");
            if (crcType == null)
                throw new InvalidOperationException("Could not load Nanook.NKit.Crc from NKitLib assembly");

            MethodInfo computeMethod = crcType.GetMethod("Compute", new[] { typeof(byte[]) });
            if (computeMethod == null)
                throw new InvalidOperationException("Could not find Compute method on Crc class");

            return (uint)computeMethod.Invoke(null, new object[] { data })!;
        }

        /// <summary>
        /// Computes XXHash64 using the Gr indCore XXHash library implementation.
        /// </summary>
        public static ulong ComputeXXHash64(byte[] data) =>
            // Use the GrindCore implementation directly
            Nanook.GrindCore.XXHash.XXHash64.Compute(data);
    }
}