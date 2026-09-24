using System;

namespace CUETools.Codecs.Flake
{
    internal class BitWriter
    {
        private ushort _crc16_m;
        private ulong _bit_buf_m;
        private int _bit_left_m;
        private byte[] _buffer;
        private int _buf_start, _buf_ptr_m, _buf_end;
        private bool _eof;

        public byte[] Buffer => _buffer;

        public int Length
        {
            get => _buf_ptr_m - _buf_start;
            set
            {
                flush();
                _buf_ptr_m = _buf_start + value;
            }
        }

        public int BitLength => (_buf_ptr_m * 8) + 64 - _bit_left_m;

        public ushort get_crc16() => _crc16_m;

        public BitWriter(byte[] buf, int pos, int len)
        {
            _buffer = buf;
            _buf_start = pos;
            _buf_ptr_m = pos;
            _buf_end = pos + len;
            _bit_left_m = 64;
            _bit_buf_m = 0;
            _crc16_m = 0;
            _eof = false;
        }

        public void Reset()
        {
            _buf_ptr_m = _buf_start;
            _bit_left_m = 64;
            _bit_buf_m = 0;
            _crc16_m = 0;
            _eof = false;
        }

        public void writebytes(int bytes, byte c)
        {
            for (; bytes > 0; bytes--)
            {
                writebits(8, c);
            }
        }

        public unsafe void writeints(int len, int pos, byte* buf)
        {
            int old_pos = BitLength;
            int start = old_pos / 8;
            int start1 = pos / 8;
            int end = (old_pos + len) / 8;
            int end1 = (pos + len) / 8;
            flush();
            byte start_val = old_pos % 8 != 0 ? _buffer[start] : (byte)0;
            fixed (byte* buf1 = &_buffer[0])
            {
                if (old_pos % 8 != 0)
                    _crc16_m = Crc16.Subtract(_crc16_m, 0, 1);
                _crc16_m = Crc16.ComputeChecksum(_crc16_m, buf + start1, end - start);
                AudioSamples.MemCpy(buf1 + start, buf + start1, end - start);
                buf1[start] |= start_val;
            }
            _buf_ptr_m = end;
            if ((old_pos + len) % 8 != 0)
                writebits((old_pos + len) % 8, buf[end1] >> (8 - ((old_pos + len) % 8)));
        }

        public void write(params char[] chars)
        {
            foreach (char c in chars)
                writebits(8, (byte)c);
        }

        public void write(string s)
        {
            for (int i = 0; i < s.Length; i++)
                writebits(8, (byte)s[i]);
        }

        public void write(byte[] s)
        {
            for (int i = 0; i < s.Length; i++)
                writebits(8, s[i]);
        }

        public void writebits_signed(int bits, int val) => writebits(bits, val & ((1 << bits) - 1));

        public void writebits_signed(uint bits, int val) => writebits((int)bits, val & ((1 << (int)bits) - 1));

        public void writebits(int bits, int val) => writebits(bits, (ulong)val);

        public void writebits(DateTime val)
        {
            TimeSpan span = val.ToUniversalTime() - new DateTime(1904, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            writebits(32, (ulong)span.TotalSeconds);
        }

        public void writebits(int bits, uint val) => writebits(bits, (ulong)val);

        public void writebits(int bits, ulong val)
        {
            //assert(bits == 32 || val < (1U << bits));

            if (bits == 0 || _eof) return;
            if (bits < _bit_left_m)
            {
                _bit_left_m -= bits;
                _bit_buf_m |= val << _bit_left_m;
            }
            else
            {
                ulong bb = _bit_buf_m | (val >> (bits - _bit_left_m));
                if (_buffer != null)
                {
                    if (_buf_ptr_m + 8 > _buf_end)
                    {
                        _eof = true;
                        return;
                    }

                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 56)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 48)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 40)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 32)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 24)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 16)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)(bb >> 8)]);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ (byte)bb]);

                    _buffer[_buf_ptr_m + 7] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 6] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 5] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 4] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 3] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 2] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 1] = (byte)(bb & 0xFF); bb >>= 8;
                    _buffer[_buf_ptr_m + 0] = (byte)(bb & 0xFF);
                    _buf_ptr_m += 8;
                }
                // cannot do this in one shift, because bit_left_m can be 64,
                // 
                _bit_left_m += 64 - bits;
                _bit_buf_m = _bit_left_m == 64 ? 0 : val << _bit_left_m;
            }
        }

        /// <summary>
        /// Assumes there's enough space, CiBuffer != null and bits is in range 1..31
        /// </summary>
        /// <param name="bits"></param>
        /// <param name="val"></param>
        //        unsafe void writebits_fast(int bits, uint val, ref byte* buf)
        //        {
        //#if DEBUG
        //            if ((buf_ptr + 3) >= buf_end)
        //            {
        //                eof = true;
        //                return;
        //            }
        //#endif
        //            if (bits < bit_left)
        //            {
        //                bit_buf = (bit_buf << bits) | val;
        //                bit_left -= bits;
        //            }
        //            else
        //            {
        //                uint bb = (bit_buf << bit_left) | (val >> (bits - bit_left));
        //                bit_left += (32 - bits);

        //                *(buf++) = (byte)(bb >> 24);
        //                *(buf++) = (byte)(bb >> 16);
        //                *(buf++) = (byte)(bb >> 8);
        //                *(buf++) = (byte)(bb);

        //                bit_buf = val;
        //            }
        //        }

        public void writeUtf8(int val) => write_utf8((uint)val);

        public void write_utf8(uint val)
        {
            if (val < 0x80)
            {
                writebits(8, val);
                return;
            }
            int bytes = (BitReader.log2i(val) + 4) / 5;
            int shift = (bytes - 1) * 6;
            writebits(8, (256U - (256U >> bytes)) | (val >> shift));
            while (shift >= 6)
            {
                shift -= 6;
                writebits(8, 0x80 | ((val >> shift) & 0x3F));
            }
        }

        public void write_unary_signed(int val)
        {
            // convert signed to unsigned
            int v = (-2 * val) - 1;
            v ^= v >> 31;

            // write quotient in unary
            int q = v + 1;
            while (q > 31)
            {
                writebits(31, 0);
                q -= 31;
            }
            writebits(q, 1);
        }

        public void write_rice_signed(int k, int val)
        {
            // convert signed to unsigned
            int v = (-2 * val) - 1;
            v ^= v >> 31;

            // write quotient in unary
            int q = (v >> k) + 1;
            while (q + k > 31)
            {
                int b = Math.Min(q + k - 31, 31);
                writebits(b, 0);
                q -= b;
            }

            // write remainder in binary using 'k' bits
            writebits(k + q, (v & ((1 << k) - 1)) | (1 << k));
        }

        public unsafe void write_rice_block_signed(byte* fixedbuf, int k, int* residual, int count)
        {
            byte* buf = &fixedbuf[_buf_ptr_m];
            ulong bit_buf = _bit_buf_m;
            int bit_left = _bit_left_m;
            ushort crc16 = _crc16_m;
            fixed (ushort* crc16_t = Crc16.table)
                for (int i = count; i > 0; i--)
                {
                    int vi = *residual++;
                    uint v = (uint)((vi << 1) ^ (vi >> 31));

                    // write quotient in unary
                    int q = (int)(v >> k) + 1;
                    int bits = k + q;
                    while (bits > 64)
                    {
#if DEBUG
                        if (buf + 1 > fixedbuf + _buf_end)
                        {
                            _eof = true;
                            return;
                        }
#endif
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bit_buf >> 56))]);
                        bit_buf <<= 8;
                        bits -= 8;
                    }

                    // write remainder in binary using 'k' bits
                    //writebits_fast(k + q, (uint)((v & ((1 << k) - 1)) | (1 << k)), ref buf);
                    ulong val = (v & ((1U << k) - 1)) | (1U << k);
                    if (bits < bit_left)
                    {
                        bit_left -= bits;
                        bit_buf |= val << bit_left;
                    }
                    else
                    {
                        ulong bb = bit_buf | (val >> (bits - bit_left));
#if DEBUG
                        if (buf + 8 > fixedbuf + _buf_end)
                        {
                            _eof = true;
                            return;
                        }
#endif

                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 56))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 48))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 40))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 32))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 24))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 16))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)(bb >> 8))]);
                        crc16 = (ushort)((crc16 << 8) ^ crc16_t[(crc16 >> 8) ^ (*buf++ = (byte)bb)]);

                        bit_left += 64 - bits;
                        bit_buf = val << (bit_left - 1) << 1;
                    }
                }
            _crc16_m = crc16;
            _buf_ptr_m = (int)(buf - fixedbuf);
            _bit_buf_m = bit_buf;
            _bit_left_m = bit_left;
        }

        public void flush()
        {
            while (_bit_left_m < 64 && !_eof)
            {
                if (_buf_ptr_m >= _buf_end)
                {
                    _eof = true;
                    break;
                }
                if (_buffer != null)
                {
                    byte b = (byte)(_bit_buf_m >> 56);
                    _crc16_m = (ushort)((_crc16_m << 8) ^ Crc16.table[(_crc16_m >> 8) ^ b]);
                    _buffer[_buf_ptr_m] = b;
                }
                _buf_ptr_m++;
                _bit_buf_m <<= 8;
                _bit_left_m += 8;
            }
            _bit_left_m = 64;
            _bit_buf_m = 0;
        }
    }
}
