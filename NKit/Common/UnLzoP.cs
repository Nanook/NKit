using System;

namespace Nanook.NKit
{
    internal class UnLzoP
    {
        private const int EOF = -1;

        private static int mgetc(Span<byte> s, ref int idx)
        {
            if (idx >= s.Length)
                return EOF;
            return s[idx++];
        }

        private static int mcopy(Span<byte> mdec, ref int dIdx, int c, Span<byte> menc, ref int eIdx)
        {
            if (dIdx + c > mdec.Length || eIdx + c > menc.Length)
                return EOF;
            for (int i = 0; i < c; i++)
                mdec[dIdx++] = menc[eIdx++];
            return c;
        }

        private static int mwrite(Span<byte> src, int sIdx, int count, Span<byte> dst, ref int dIdx)
        {
            for (int i = 0, p = sIdx; i < count; i++)
            {
                dst[dIdx++] = src[p++];
                if (p == src.Length)
                    p = sIdx;
            }
            return count;
        }

        /* lzo1xy_decompress - easy replacement for lzo1x_dec* */
        public static int Lzo1xyDecompress(Span<byte> mdec, Span<byte> menc, int lzo1y)
        {
            int c, arg1, arg2, count, back, state = 0, error = 0;

            int dIdx = 0;
            int eIdx = 0;

#pragma warning disable CA2265
            if (mdec == null || menc == null) return -1;
#pragma warning restore CA2265

            c = mgetc(menc, ref eIdx);
            if (c > 17)
            {
                if (mcopy(mdec, ref dIdx, c - 17, menc, ref eIdx) < c - 17)
                    return 1;
                if ((c = mgetc(menc, ref eIdx)) == EOF)
                    return 1;
                if (c < 16)
                    return 1;
            }
            while (error == 0)
            {
                if (c > 63)
                {
                    if ((arg1 = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    if (lzo1y != 0)
                    {
                        count = (c >> 4) - 3;
                        back = ((c >> 2) & 3) + (arg1 << 2) + 1;
                    }
                    else
                    {
                        count = (c >> 5) - 1;
                        back = ((c >> 2) & 7) + (arg1 << 3) + 1;
                    }
                }
                else if (c > 31)
                {
                    count = c & 31;
                    if (count == 0)
                    { //c==32
                        for (; ; )
                        {
                            arg1 = mgetc(menc, ref eIdx);
                            if (arg1 == EOF)
                                return -1;
                            if (arg1 != 0)
                                break;
                            count += 255;
                        }
                        count += arg1 + 31;
                    }
                    if ((arg1 = mgetc(menc, ref eIdx)) == EOF || (arg2 = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    back = (arg1 >> 2) + (arg2 << 6) + 1;
                    c = arg1;
                }
                else if (c > 15)
                {
                    count = c & 7;
                    if (count == 0)
                    { //c==16,24
                        for (; ; )
                        {
                            arg1 = mgetc(menc, ref eIdx);
                            if (arg1 == EOF)
                                return -1;
                            if (arg1 != 0)
                                break;
                            count += 255;
                        }
                        count += arg1 + 7;
                    }
                    if ((arg1 = mgetc(menc, ref eIdx)) == EOF || (arg2 = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    back = (1 << 14) + ((c & 8) << 11);
                    back += (arg1 >> 2) + (arg2 << 6);
                    c = arg1;
                    if (back == 1 << 14)
                    {
                        if (count != 1)
                            return 1;
                        break;
                    }
                }
                else if (state == 0)
                {
                    count = c & 15;
                    if (count == 0)
                    {
                        for (; ; )
                        {
                            arg1 = mgetc(menc, ref eIdx);
                            if (arg1 == EOF)
                                return -1;
                            if (arg1 != 0)
                                break;
                            count += 255;
                        }
                        count += arg1 + 15;
                    }
                    if (mcopy(mdec, ref dIdx, count + 3, menc, ref eIdx) < count + 3)
                        return 1;
                    if ((c = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    if (c > 15)
                        continue;
                    count = 1;
                    if ((arg1 = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    back = (1 << 11) + (c >> 2) + (arg1 << 2) + 1;
                }
                else
                {
                    count = 0;
                    if ((arg1 = mgetc(menc, ref eIdx)) == EOF)
                        return 1;
                    back = (c >> 2) + (arg1 << 2) + 1;
                }
                if (dIdx < back || back <= 0)
                    return 2;

                mwrite(mdec, dIdx - back, count + 2, mdec, ref dIdx);
                state = count = c & 3;
                if (mcopy(mdec, ref dIdx, count, menc, ref eIdx) < count)
                    return 1;
                if ((c = mgetc(menc, ref eIdx)) == EOF)
                    return 1;
            }
            return 0;
        }

    }
}