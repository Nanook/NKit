using System;

namespace CUETools.Codecs.Flake
{
    internal static class Crc16
    {
        const int _GF2_DIM = 16;
        public static ushort[] table = new ushort[256];
        private static readonly ushort[,] _CombineTable = new ushort[_GF2_DIM, _GF2_DIM];
        private static readonly ushort[,] _SubstractTable = new ushort[_GF2_DIM, _GF2_DIM];

        public static unsafe ushort ComputeChecksum(ushort crc, byte[] bytes, int pos, int count)
        {
            fixed (byte* bs = bytes)
                return ComputeChecksum(crc, bs + pos, count);
        }

        public static unsafe ushort ComputeChecksum(ushort crc, byte* bytes, int count)
        {
            fixed (ushort* t = table)
                for (int i = count; i > 0; i--)
                {
                    crc = (ushort)((crc << 8) ^ t[(crc >> 8) ^ *bytes++]);
                }
            return crc;
        }

        const ushort _Polynomial = 0x8005;
        const ushort _ReversePolynomial = 0x4003;

        static unsafe Crc16()
        {
            for (ushort i = 0; i < table.Length; i++)
            {
                int crc = i;
                for (int j = 0; j < _GF2_DIM; j++)
                {
                    if ((crc & (1U << (_GF2_DIM - 1))) != 0)
                        crc = (crc << 1) ^ _Polynomial;
                    else
                        crc <<= 1;
                }
                table[i] = (ushort)(crc & ((1 << _GF2_DIM) - 1));
            }

            _CombineTable[0, 0] = Reflect(_Polynomial);
            _SubstractTable[0, _GF2_DIM - 1] = _ReversePolynomial;
            for (int n = 1; n < _GF2_DIM; n++)
            {
                _CombineTable[0, n] = (ushort)(1 << (n - 1));
                _SubstractTable[0, n - 1] = (ushort)(1 << n);
            }

            fixed (ushort* ct = &_CombineTable[0, 0], st = &_SubstractTable[0, 0])
            {
                //for (int i = 0; i < GF2_DIM; i++)
                //	st[32 + i] = ct[i];
                //invert_binary_matrix(st + 32, st, GF2_DIM);

                for (int i = 1; i < _GF2_DIM; i++)
                {
                    gf2_matrix_square(ct + (i * _GF2_DIM), ct + ((i - 1) * _GF2_DIM));
                    gf2_matrix_square(st + (i * _GF2_DIM), st + ((i - 1) * _GF2_DIM));
                }
            }
        }

        private static unsafe ushort gf2_matrix_times(ushort* mat, ushort uvec)
        {
            int vec = uvec << 16;
            return (ushort)(
                (*mat++ & (vec << 15 >> 31)) ^
                (*mat++ & (vec << 14 >> 31)) ^
                (*mat++ & (vec << 13 >> 31)) ^
                (*mat++ & (vec << 12 >> 31)) ^
                (*mat++ & (vec << 11 >> 31)) ^
                (*mat++ & (vec << 10 >> 31)) ^
                (*mat++ & (vec << 09 >> 31)) ^
                (*mat++ & (vec << 08 >> 31)) ^
                (*mat++ & (vec << 07 >> 31)) ^
                (*mat++ & (vec << 06 >> 31)) ^
                (*mat++ & (vec << 05 >> 31)) ^
                (*mat++ & (vec << 04 >> 31)) ^
                (*mat++ & (vec << 03 >> 31)) ^
                (*mat++ & (vec << 02 >> 31)) ^
                (*mat++ & (vec << 01 >> 31)) ^
                (*mat++ & (vec >> 31)));
        }

        private static unsafe void gf2_matrix_square(ushort* square, ushort* mat)
        {
            for (int n = 0; n < _GF2_DIM; n++)
                square[n] = gf2_matrix_times(mat, mat[n]);
        }

        public static ushort Reflect(ushort crc) => (ushort)Crc32.Reflect(crc, 16);

        public static unsafe ushort Combine(ushort crc1, ushort crc2, long len2)
        {
            crc1 = Reflect(crc1);
            crc2 = Reflect(crc2);

            /* degenerate case */
            if (len2 == 0)
                return crc1;
            if (crc1 == 0)
                return crc2;
            if (len2 < 0)
                throw new ArgumentException("crc.Combine length cannot be negative", "len2");

            fixed (ushort* ct = &_CombineTable[0, 0])
            {
                int n = 3;
                do
                {
                    /* apply zeros operator for this bit of len2 */
                    if ((len2 & 1) != 0)
                        crc1 = gf2_matrix_times(ct + (_GF2_DIM * n), crc1);
                    len2 >>= 1;
                    n = (n + 1) & (_GF2_DIM - 1);
                    /* if no more bits set, then done */
                } while (len2 != 0);
            }

            /* return combined crc */
            crc1 ^= crc2;
            crc1 = Reflect(crc1);
            return crc1;
        }

        public static unsafe ushort Subtract(ushort crc1, ushort crc2, long len2)
        {
            crc1 = Reflect(crc1);
            crc2 = Reflect(crc2);
            /* degenerate case */
            if (len2 == 0)
                return crc1;
            if (len2 < 0)
                throw new ArgumentException("crc.Combine length cannot be negative", "len2");

            crc1 ^= crc2;

            fixed (ushort* st = &_SubstractTable[0, 0])
            {
                int n = 3;
                do
                {
                    /* apply zeros operator for this bit of len2 */
                    if ((len2 & 1) != 0)
                        crc1 = gf2_matrix_times(st + (_GF2_DIM * n), crc1);
                    len2 >>= 1;
                    n = (n + 1) & (_GF2_DIM - 1);
                    /* if no more bits set, then done */
                } while (len2 != 0);
            }

            /* return combined crc */
            crc1 = Reflect(crc1);
            return crc1;
        }
    }
}
