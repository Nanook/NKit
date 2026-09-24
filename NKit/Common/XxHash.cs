using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Nanook.NKit
{
    //see details: https://github.com/Cyan4973/xxHash/blob/dev/doc/xxhash_spec.md


    /// <summary>
    /// Represents the class which provides a implementation of the xxHash64 algorithm.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public sealed class XXHash64 : HashAlgorithm
    {
        private const ulong PRIME64_1 = 11400714785074694791UL;
        private const ulong PRIME64_2 = 14029467366897019727UL;
        private const ulong PRIME64_3 = 1609587929392839161UL;
        private const ulong PRIME64_4 = 9650029242287828579UL;
        private const ulong PRIME64_5 = 2870177450012600261UL;

        private const int BLOCK_SIZE = 8 * 4;

        private static readonly Func<byte[], int, uint> FuncGetLittleEndianUInt32;
        private static readonly Func<byte[], int, ulong> FuncGetLittleEndianUInt64;
        private static readonly Func<ulong, ulong> FuncGetFinalHashUInt64;

        private ulong _ACC64_1;
        private ulong _ACC64_2;
        private ulong _ACC64_3;
        private ulong _ACC64_4;

        private ulong _seed64;
        private ulong _hash64;

        private long _fullLength;
        private byte[] _buffer;
        private int _bufferCount;


        static XXHash64()
        {
            if (BitConverter.IsLittleEndian)
            {
                FuncGetLittleEndianUInt32 = new Func<byte[], int, uint>((x, i) =>
                {
                    unsafe
                    {
                        fixed (byte* array = x)
                        {
                            return *(uint*)(array + i);
                        }
                    }
                });
                FuncGetLittleEndianUInt64 = new Func<byte[], int, ulong>((x, i) =>
                {
                    unsafe
                    {
                        fixed (byte* array = x)
                        {
                            return *(ulong*)(array + i);
                        }
                    }
                });
                FuncGetFinalHashUInt64 = new Func<ulong, ulong>(i => ((i & 0x00000000000000FFUL) << 56) | ((i & 0x000000000000FF00UL) << 40) | ((i & 0x0000000000FF0000UL) << 24) | ((i & 0x00000000FF000000UL) << 8) | ((i & 0x000000FF00000000UL) >> 8) | ((i & 0x0000FF0000000000UL) >> 24) | ((i & 0x00FF000000000000UL) >> 40) | ((i & 0xFF00000000000000UL) >> 56));
            }
            else
            {
                FuncGetLittleEndianUInt32 = new Func<byte[], int, uint>((x, i) =>
                {
                    unsafe
                    {
                        fixed (byte* array = x)
                        {
                            return (uint)(array[i++] | (array[i++] << 8) | (array[i++] << 16) | (array[i] << 24));
                        }
                    }
                });
                FuncGetLittleEndianUInt64 = new Func<byte[], int, ulong>((x, i) =>
                {
                    unsafe
                    {
                        fixed (byte* array = x)
                        {
                            return array[i++] | ((ulong)array[i++] << 8) | ((ulong)array[i++] << 16) | ((ulong)array[i++] << 24) | ((ulong)array[i++] << 32) | ((ulong)array[i++] << 40) | ((ulong)array[i++] << 48) | ((ulong)array[i] << 56);
                        }
                    }
                });
                FuncGetFinalHashUInt64 = new Func<ulong, ulong>(i => i);
            }
        }

        /// <summary>
        /// Creates an instance of <see cref="XXHash64"/> class by default seed(0).
        /// </summary>
        /// <returns></returns>
        public new static XXHash64 Create() => new XXHash64();

        /// <summary>
        /// Creates an instance of the specified implementation of XXHash64 algorithm.
        /// <para>This method always throws <see cref="NotSupportedException"/>. </para>
        /// </summary>
        /// <param name="algName">The hash algorithm implementation to use.</param>
        /// <returns>This method always throws <see cref="NotSupportedException"/>. </returns>
        /// <exception cref="NotSupportedException">This method is not be supported.</exception>
        public new static XXHash64 Create(string algName) => throw new NotSupportedException("This method is not be supported.");

        /// <summary>
        /// Initializes a new instance of the <see cref="XXHash64"/> class by default seed(0).
        /// </summary>
        public XXHash64()
        {
            Initialize(0);
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="XXHash64"/> class, and sets the <see cref="Seed"/> to the specified value.
        /// </summary>
        /// <param name="seed">Represent the seed to be used for xxHash64 computing.</param>
        public XXHash64(uint seed)
        {
            Initialize(seed);
        }


        /// <summary>
        /// Gets the <see cref="ulong"/> value of the computed hash code.
        /// </summary>
        /// <exception cref="InvalidOperationException">Computation has not yet completed.</exception>
        public ulong HashUInt64 => State == 0 ? _hash64 : throw new InvalidOperationException("Computation has not yet completed.");

        /// <summary>
        ///  Gets or sets the value of seed used by xxHash64 algorithm.
        /// </summary>
        /// <exception cref="InvalidOperationException">Computation has not yet completed.</exception>
        public ulong Seed
        {
            get => _seed64;
            set
            {
                if (value != _seed64)
                {
                    if (State != 0) throw new InvalidOperationException("Computation has not yet completed.");
                    _seed64 = value;
                    Initialize();
                }
            }
        }


        /// <summary>
        /// Initializes this instance for new hash computing.
        /// </summary>
        public override void Initialize()
        {
            _ACC64_1 = _seed64 + PRIME64_1 + PRIME64_2;
            _ACC64_2 = _seed64 + PRIME64_2;
            _ACC64_3 = _seed64 + 0;
            _ACC64_4 = _seed64 - PRIME64_1;
            _buffer = new byte[BLOCK_SIZE];
            _bufferCount = 0;
        }

        /// <summary>
        /// Routes data written to the object into the hash algorithm for computing the hash.
        /// </summary>
        /// <param name="array">The input to compute the hash code for.</param>
        /// <param name="ibStart">The offset into the byte array from which to begin using data.</param>
        /// <param name="cbSize">The number of bytes in the byte array to use as data.</param>
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            int max = ibStart + cbSize;

            if (max == 0)
                return; //random call that corrupts the hash?

            if (State != 1)
                State = 1;

            int size = cbSize; // - ibStart;

            if (_bufferCount != 0)
            {
                int take = Math.Min(BLOCK_SIZE - _bufferCount, size);
                size -= take;
                while (take-- != 0)
                    _buffer[_bufferCount++] = array[ibStart++];
                if (_bufferCount == BLOCK_SIZE)
                {
                    _ACC64_1 = Round64(_ACC64_1, FuncGetLittleEndianUInt64(_buffer, 0));
                    _ACC64_2 = Round64(_ACC64_2, FuncGetLittleEndianUInt64(_buffer, 8));
                    _ACC64_3 = Round64(_ACC64_3, FuncGetLittleEndianUInt64(_buffer, 16));
                    _ACC64_4 = Round64(_ACC64_4, FuncGetLittleEndianUInt64(_buffer, 24));
                    _bufferCount = 0; //we've processed a split CiBuffer yey
                }
            }

            int remaining = size & (BLOCK_SIZE - 1);
            if (size >= BLOCK_SIZE)
            {
                int maxfull = max - remaining;
                do
                {
                    _ACC64_1 = Round64(_ACC64_1, FuncGetLittleEndianUInt64(array, ibStart));
                    ibStart += 8;
                    _ACC64_2 = Round64(_ACC64_2, FuncGetLittleEndianUInt64(array, ibStart));
                    ibStart += 8;
                    _ACC64_3 = Round64(_ACC64_3, FuncGetLittleEndianUInt64(array, ibStart));
                    ibStart += 8;
                    _ACC64_4 = Round64(_ACC64_4, FuncGetLittleEndianUInt64(array, ibStart));
                    ibStart += 8;
                } while (ibStart < maxfull);
            }
            _fullLength += cbSize;
            while (ibStart < max)
                _buffer[_bufferCount++] = array[ibStart++];
        }

        /// <summary>
        /// Finalizes the hash computation after the last data is processed by the cryptographic stream object.
        /// </summary>
        /// <returns>The computed hash code.</returns>
        protected override byte[] HashFinal()
        {
            if (_fullLength >= BLOCK_SIZE)
            {
                _hash64 = RotateLeft64_1(_ACC64_1) + RotateLeft64_7(_ACC64_2) + RotateLeft64_12(_ACC64_3) + RotateLeft64_18(_ACC64_4);
                _hash64 = MergeRound64(_hash64, _ACC64_1);
                _hash64 = MergeRound64(_hash64, _ACC64_2);
                _hash64 = MergeRound64(_hash64, _ACC64_3);
                _hash64 = MergeRound64(_hash64, _ACC64_4);
            }
            else
            {
                _hash64 = _seed64 + PRIME64_5;
            }

            _hash64 += (ulong)_fullLength;

            int idx = 0;

            while (_bufferCount >= 8)
            {
                _hash64 = (RotateLeft64_27(_hash64 ^ Round64(0, FuncGetLittleEndianUInt64(_buffer, idx))) * PRIME64_1) + PRIME64_4;
                idx += 8;
                _bufferCount -= 8;
            }

            while (_bufferCount >= 4)
            {
                _hash64 = (RotateLeft64_23(_hash64 ^ (FuncGetLittleEndianUInt32(_buffer, idx) * PRIME64_1)) * PRIME64_2) + PRIME64_3;
                idx += 4;
                _bufferCount -= 4;
            }

            unsafe
            {
                fixed (byte* arrayPtr = _buffer)
                {
                    while (_bufferCount-- >= 1)
                        _hash64 = RotateLeft64_11(_hash64 ^ (arrayPtr[idx++] * PRIME64_5)) * PRIME64_1;
                }
            }

            _hash64 = (_hash64 ^ (_hash64 >> 33)) * PRIME64_2;
            _hash64 = (_hash64 ^ (_hash64 >> 29)) * PRIME64_3;
            _hash64 ^= _hash64 >> 32;

            _fullLength = State = 0;

            byte[] result = new byte[sizeof(ulong)];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(result, _hash64);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MergeRound64(ulong input, ulong value) => ((input ^ Round64(0, value)) * PRIME64_1) + PRIME64_4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Round64(ulong input, ulong value) => RotateLeft64_31(input + (value * PRIME64_2)) * PRIME64_1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_1(ulong value) => (value << 1) | (value >> 63); // _ACC64_1
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_7(ulong value) => (value << 7) | (value >> 57); //  _ACC64_2
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_11(ulong value) => (value << 11) | (value >> 53);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_12(ulong value) => (value << 12) | (value >> 52);// _ACC64_3
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_18(ulong value) => (value << 18) | (value >> 46); // _ACC64_4
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_23(ulong value) => (value << 23) | (value >> 41);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_27(ulong value) => (value << 27) | (value >> 37);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft64_31(ulong value) => (value << 31) | (value >> 33);


        private void Initialize(ulong seed)
        {
            HashSizeValue = 64;
            _seed64 = seed;
            Initialize();
        }

        internal static ulong Compute(byte[] data, int offset, int size)
        {
            XXHash64 xx = XXHash64.Create();
            using (Stream strm = new CryptoStream(Stream.Null, xx, CryptoStreamMode.Write))
                strm.Write(data, offset, size);
            return xx.HashUInt64;
        }
    }

}