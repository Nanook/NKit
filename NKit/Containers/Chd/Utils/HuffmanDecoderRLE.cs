namespace Nanook.NKit.Chd
{
    internal class HuffmanDecoderRLE : Huffman
    {
        private int rlecount = 0;
        private uint prevdata = 0;

        public HuffmanDecoderRLE(uint numcodes, byte maxbits, BitStream bitbuf, ushort[] buffLookup) : base(numcodes, maxbits, bitbuf, buffLookup)
        { }

        public void Reset()
        {
            rlecount = 0;
            prevdata = 0;
        }
        public void FlushRLE() => rlecount = 0;

        public new uint DecodeOne()
        {
            // return RLE data if we still have some
            if (rlecount != 0)
            {
                rlecount--;
                return prevdata;
            }

            // fetch the data and process
            uint data = base.DecodeOne();
            if (data < 0x100)
            {
                prevdata += data;
                return prevdata;
            }
            else
            {
                rlecount = CodeToRLECount((int)data);
                rlecount--;
                return prevdata;
            }
        }

        public int CodeToRLECount(int code)
        {
            if (code == 0x00)
                return 1;
            if (code <= 0x107)
                return 8 + (code - 0x100);
            return 16 << (code - 0x108);
        }
    }
}