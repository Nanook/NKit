using System;

namespace Nanook.NKit.Chd
{
    internal class BitStream
    {
        private uint buffer;
        private int bits;
        private byte[] readBuffer;
        private int doffset;
        private int dlength;

        private int initialOffset = 0;

        public bool overflow() => doffset - (bits / 8) > dlength;

        /*-------------------------------------------------
        *  create_bitstream - constructor
        *-------------------------------------------------
        */
        public BitStream(byte[] src, int offset, int length)
        {
            buffer = 0;
            bits = 0;
            readBuffer = src;
            doffset = initialOffset = offset;
            dlength = offset + length;
        }

        /*-----------------------------------------------------
        *  bitstream_peek - fetch the requested number of bits
        *  but don't advance the input pointer
        *-----------------------------------------------------
        */
        public uint peek(int numbits)
        {
            if (numbits == 0)
                return 0;

            /* fetch data if we need more */
            if (numbits > bits)
            {
                while (bits <= 24)
                {
                    if (doffset < dlength)
                        buffer |= (uint)readBuffer[doffset] << (24 - bits);
                    doffset++;
                    bits += 8;
                }
            }

            /* return the data */
            return buffer >> (32 - numbits);
        }

        /*-----------------------------------------------------
        *  bitstream_remove - advance the input pointer by the
        *  specified number of bits
        *-----------------------------------------------------
        */
        public void remove(int numbits)
        {
            buffer <<= numbits;
            bits -= numbits;
        }


        /*-----------------------------------------------------
        *  bitstream_read - fetch the requested number of bits
        *-----------------------------------------------------
        */
        public uint read(int numbits)
        {
            uint result = peek(numbits);
            remove(numbits);
            return result;
        }

        /*-------------------------------------------------
        *  flush - flush to the nearest byte
        *-------------------------------------------------
        */

        public int flush()
        {
            while (bits >= 8)
            {
                doffset--;
                bits -= 8;
            }
            bits = 0;
            buffer = 0;
            return doffset - initialOffset;
        }

        //-------------------------------------------------
        //  write - write the given number of bits to the
        //  data stream
        //-------------------------------------------------
        public void write(uint newbits, int numbits)
        {
            newbits <<= 32 - numbits;

            // flush the CiBuffer if we're going to overflow it
            while (bits + numbits >= 32 && numbits > 0)
            {
                while (bits >= 8)
                {
                    if (doffset < dlength)
                        readBuffer[doffset] = (byte)(buffer >> 24);
                    doffset++;
                    buffer <<= 8;
                    bits -= 8;
                }

                // offload more bits if it'll still overflow the CiBuffer
                if (bits + numbits >= 32)
                {
                    int rem = Math.Min(32 - bits, numbits);
                    buffer |= newbits >> bits;
                    bits += rem;
                    newbits <<= rem;
                    numbits -= rem;
                }
            }

            if (numbits <= 0)
                return;

            // now shift it down to account for the number of bits we already have and OR them in
            buffer |= newbits >> bits;
            bits += numbits;
        }
    }
}