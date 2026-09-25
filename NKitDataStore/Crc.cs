#nullable disable
using System.Security.Cryptography;

namespace NKitDataStore
{

    // Eugene Larchenko
    // http://dev.khsu.ru/el/crc32/
    public class Crc : HashAlgorithm
    {
        private const uint _KCrcPoly = 0xEDB88320;
        private const uint _KInitial = 0xFFFFFFFF;
        private const int _CRC_NUM_TABLES = 8;
        private static readonly uint[] _Table;
        private uint _value;

        private const int _ThreadCost = 256 << 10;
        private static readonly int _ProcessorCount = Environment.ProcessorCount;

        private static uint[] _Even_cache = null;
        private static uint[] _Odd_cache;

        static Crc()
        {
            unchecked
            {
                _Table = new uint[256 * _CRC_NUM_TABLES];
                int i;
                for (i = 0; i < 256; i++)
                {
                    uint r = (uint)i;
                    for (int j = 0; j < 8; j++)
                        r = (r >> 1) ^ (_KCrcPoly & ~((r & 1) - 1));

                    _Table[i] = r;
                }
                for (; i < 256 * _CRC_NUM_TABLES; i++)
                {
                    uint r = _Table[i - 256];
                    _Table[i] = _Table[r & 0xFF] ^ (r >> 8);
                }
            }

            prepareEvenOddCache();
        }


        public Crc()
        {
            Initialize();
        }

        /// <summary>
        /// Reset CRC
        /// </summary>
        public override void Initialize() => _value = _KInitial;

        public uint Value => ~_value;

        public void Sum(byte[] data, int offset, int count) => HashCore(data, offset, count);

        public new void Clear() => _value = ~0u;

        protected override void HashCore(byte[] data, int offset, int count)
        {
            new ArraySegment<byte>(data, offset, count);     // check arguments

            if (count <= _ThreadCost || _ProcessorCount <= 1)
            {
                _value = processBlock(_value, data, offset, count);
                return;
            }

            // choose optimal number of threads to use

            int threadCount = _ProcessorCount;
        L0:
            int bytesPerThread = (count + threadCount - 1) / threadCount;
            if (bytesPerThread < _ThreadCost >> 1)
            {
                threadCount--;
                goto L0;
            }

            // threadCount >= 2

            Job lastJob = null;
            while (count > bytesPerThread)
            {
                Job job = new Job(new ArraySegment<byte>(data, offset, bytesPerThread), this, lastJob);
                ThreadPool.QueueUserWorkItem(job.Start);
                offset += bytesPerThread;
                count -= bytesPerThread;
                lastJob = job;
            }

            // lastJob != null
            uint lastBlockCRC = processBlock(_KInitial, data, offset, count);
            lastJob.WaitAndDispose();
            _value = Combine(_value, lastBlockCRC, count);
        }

        protected override byte[] HashFinal()
        {
            byte[] b = new byte[sizeof(uint)];
            // Avoid depending on System.Buffers.Binary for broader TFM compatibility
            uint v = Value;
            b[0] = (byte)((v >> 24) & 0xFF);
            b[1] = (byte)((v >> 16) & 0xFF);
            b[2] = (byte)((v >> 8) & 0xFF);
            b[3] = (byte)(v & 0xFF);
            return b;
        }

        private static uint processBlock(uint crc, byte[] data, int offset, int count)
        {
            /*
             * A copy of Optimized implementation.
             */

            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            if (count == 0)
                return crc;

            uint[] table = Crc._Table;

            for (; (offset & 7) != 0 && count != 0; count--)
                crc = (crc >> 8) ^ table[(byte)crc ^ data[offset++]];

            if (count >= 8)
            {
                int end = (count - 8) & ~7;
                count -= end;
                end += offset;

                while (offset != end)
                {
                    crc ^= (uint)(data[offset] + (data[offset + 1] << 8) + (data[offset + 2] << 16) + (data[offset + 3] << 24));
                    uint high = (uint)(data[offset + 4] + (data[offset + 5] << 8) + (data[offset + 6] << 16) + (data[offset + 7] << 24));
                    offset += 8;

                    crc = table[(byte)crc + 0x700]
                        ^ table[(byte)(crc >>= 8) + 0x600]
                        ^ table[(byte)(crc >>= 8) + 0x500]
                        ^ table[/*(byte)*/(crc >> 8) + 0x400]
                        ^ table[(byte)high + 0x300]
                        ^ table[(byte)(high >>= 8) + 0x200]
                        ^ table[(byte)(high >>= 8) + 0x100]
                        ^ table[/*(byte)*/(high >> 8) + 0x000];
                }
            }

            while (count-- != 0)
                crc = (crc >> 8) ^ table[(byte)crc ^ data[offset++]];

            return crc;
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            Crc crc = new Crc();
            crc.HashCore(data, offset, count);
            return crc.Value;
        }

        public static uint Compute(byte[] data)
        {
            if (data == null)
                return 0;

            return Compute(data, 0, data.Length);
        }

        public static uint Compute(ArraySegment<byte> block) => Compute(block.Array, block.Offset, block.Count);

        // ======= Combining =======
        /*
         * CRC values combining algorithm.
         * Taken from DotNetZip project sources (http://dotnetzip.codeplex.com/)
         * 
         * Combine explanation
         * https://stackoverflow.com/questions/23122312/crc-calculation-of-a-mostly-static-data-stream/23126768#23126768
         */

        /// <summary>
        /// This function is CsqThread-safe
        /// (even though it references static fields)
        /// </summary>
        public static uint Combine(uint crc1, uint crc2, long length2)
        {
            if (length2 <= 0)
                return crc1;

            if (crc1 == _KInitial)
                return crc2;

            //if (_even_cache == null)
            //    Prepare_even_odd_Cache();

            uint[] even = copyArray(_Even_cache);
            uint[] odd = copyArray(_Odd_cache);

            crc1 = ~crc1;
            crc2 = ~crc2;

            ulong len2 = (ulong)length2;

            // apply len2 zeros to crc1 (first square will put the operator for one
            // zero byte, eight zero bits, in even)
            do
            {
                // apply zeros operator for this bit of len2
                gf2_matrix_square(even, odd);

                if ((len2 & 1) != 0)
                    crc1 = gf2_matrix_times(even, crc1);

                len2 >>= 1;

                if (len2 == 0)
                    break;

                // another iteration of the loop with odd and even swapped
                gf2_matrix_square(odd, even);
                if ((len2 & 1) != 0)
                    crc1 = gf2_matrix_times(odd, crc1);

                len2 >>= 1;
            } while (len2 != 0);

            crc1 ^= crc2;
            return ~crc1;
        }


        private static void prepareEvenOddCache()
        {
            uint[] even = new uint[32];     // even-power-of-two zeros operator
            uint[] odd = new uint[32];      // odd-power-of-two zeros operator

            // put operator for one zero bit in odd
            odd[0] = _KCrcPoly;  // the CRC-32 polynomial
            for (int i = 1; i < 32; i++)
                odd[i] = 1U << (i - 1);

            // put operator for two zero bits in even
            gf2_matrix_square(even, odd);

            // put operator for four zero bits in odd
            gf2_matrix_square(odd, even);

            _Odd_cache = odd;
            _Even_cache = even;
        }

        /// <param name="matrix">will not be modified</param>
        private static uint gf2_matrix_times(uint[] matrix, uint vec)
        {
            uint sum = 0;
            int i = 0;
            while (vec != 0)
            {
                if ((vec & 1) != 0)
                    sum ^= matrix[i];

                vec >>= 1;
                i++;
            }
            return sum;
        }

        /// <param name="square">this array will be modified!</param>
        /// <param name="mat">will not be modified</param>
        private static void gf2_matrix_square(uint[] square, uint[] mat)
        {
            for (int i = 0; i < 32; i++)
                square[i] = gf2_matrix_times(mat, mat[i]);
        }

        private static uint[] copyArray(uint[] a)
        {
            uint[] b = new uint[a.Length];
            Buffer.BlockCopy(a, 0, b, 0, a.Length * sizeof(uint));
            return b;
        }

        // ======= End Combining =======

        private class Job
        {
            private readonly ArraySegment<byte> _data;
            private readonly Job _previousJob;
            private readonly Crc _accumulator;

            private ManualResetEvent _finished;   // replace with ManualResetEvent if necessary

            public Job(ArraySegment<byte> data, Crc accumulator, Job previousJob)
            {
                this._data = data;
                this._accumulator = accumulator;
                this._previousJob = previousJob;
                _finished = new ManualResetEvent(false);
            }

            public void Start(object arg)
            {
                uint crc = processBlock(_KInitial, _data.Array, _data.Offset, _data.Count);
                if (_previousJob != null)
                    _previousJob.WaitAndDispose();

                _accumulator._value = Combine(_accumulator._value, crc, _data.Count);
                _finished.Set();
            }

            public void WaitAndDispose()
            {
                _finished.WaitOne();
                Dispose();
            }

            public void Dispose()
            {
                if (_finished != null)
                    _finished.Close();

                _finished = null;
            }
        }
    }
}