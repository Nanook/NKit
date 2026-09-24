namespace Nanook.NKit.Chd
{
    internal enum HuffmanError
    {
        HUFFERR_NONE = 0,
        HUFFERR_TOO_MANY_BITS,
        HUFFERR_INVALID_DATA,
        HUFFERR_INPUT_BUFFER_TOO_SMALL,
        HUFFERR_OUTPUT_BUFFER_TOO_SMALL,
        HUFFERR_INTERNAL_INCONSISTENCY,
        HUFFERR_TOO_MANY_CONTEXTS
    };


    internal class Node
    {
        internal Node parent;       /* pointer to parent node */
        internal uint count;          /* number of hits on this node */
        internal uint weight;         /* assigned weight of this node */
        internal uint bits;           /* bits used to encode the node */
        internal byte numbits;        /* number of bits needed for this node */
    };


    internal partial class Huffman
    {
        /* internal state */
        uint numcodes;                /* number of total codes being processed */
        byte maxbits;                 /* maximum bits per code */
        //uint prevdata;                /* value of the previous data (for delta-RLE encoding) */
        //int rleremaining;             /* number of RLE bytes remaining (for delta-RLE encoding) */
        ushort[] lookup;              /* pointer to the lookup table */
        Node[] huffnode;              /* array of nodes */
        uint[] datahisto;             /* histogram of data values */

        BitStream bitbuf;

        private static uint MAKE_LOOKUP(uint code, uint bits) => (code << 5) | (bits & 0x1f);


        /*-------------------------------------------------
        *  huffman_context_base - create an encoding/
        *  decoding context
        *-------------------------------------------------
        */
        public Huffman(uint numcodes, byte maxbits, BitStream bitbuf, ushort[] buffLookup = null)
        {
            /* limit to 24 bits */
            if (maxbits > 24)
                return;

            this.numcodes = numcodes;
            this.maxbits = maxbits;

            lookup = buffLookup == null ? (new ushort[(1 << maxbits)]) : buffLookup;

            huffnode = new Node[numcodes];
            //decoder.datahisto = null;
            //decoder.prevdata = 0;
            //decoder.rleremaining = 0;

            for (int i = 0; i < numcodes; i++)
                huffnode[i] = new Node();

            this.bitbuf = bitbuf;
        }

        public void AssignBitStream(BitStream bitbufReplace) => bitbuf = bitbufReplace;

        /*-------------------------------------------------
        *  decode_one - decode a single code from the
        *  huffman stream
        *-------------------------------------------------
        */
        public uint DecodeOne()
        {
            /* peek ahead to get maxbits worth of data */
            uint bits = bitbuf.peek(maxbits);

            /* look it up, then remove the actual number of bits for this code */
            uint lookup = this.lookup[bits];
            bitbuf.remove((int)(lookup & 0x1f));

            /* return the value */
            return lookup >> 5;
        }

        /*-------------------------------------------------
        *  import_tree_rle - import an RLE-encoded
        *  huffman tree from a source data stream
        *-------------------------------------------------
        */
        public HuffmanError ImportTreeRle()
        {

            int numbits;
            int curnode;
            HuffmanError error;

            /* bits per entry depends on the maxbits */
            if (maxbits >= 16)
                numbits = 5;
            else if (maxbits >= 8)
                numbits = 4;
            else
                numbits = 3;

            /* loop until we read all the nodes */
            for (curnode = 0; curnode < numcodes;)
            {
                /* a non-one value is just raw */
                int nodebits = (int)bitbuf.read(numbits);
                if (nodebits != 1)
                    huffnode[curnode++].numbits = (byte)nodebits;

                /* a one value is an escape code */
                else
                {
                    /* a double 1 is just a single 1 */
                    nodebits = (int)bitbuf.read(numbits);
                    if (nodebits == 1)
                        huffnode[curnode++].numbits = (byte)nodebits;

                    /* otherwise, we need one for value for the repeat count */
                    else
                    {
                        int repcount = (int)bitbuf.read(numbits) + 3;
                        if (repcount + curnode > numcodes)
                            return HuffmanError.HUFFERR_INVALID_DATA;
                        while (repcount-- != 0)
                            huffnode[curnode++].numbits = (byte)nodebits;
                    }
                }
            }

            /* make sure we ended up with the right number */
            if (curnode != numcodes)
                return HuffmanError.HUFFERR_INVALID_DATA;

            /* assign canonical codes for all nodes based on their code lengths */
            error = AssignCanonicalCodes();
            if (error != HuffmanError.HUFFERR_NONE)
                return error;

            /* build the lookup table */
            BuildLookupTable();

            /* determine final input length and report errors */
            return bitbuf.overflow() ? HuffmanError.HUFFERR_INPUT_BUFFER_TOO_SMALL : HuffmanError.HUFFERR_NONE;
        }


        /*-------------------------------------------------
        *  import_tree_huffman - import a huffman-encoded
        *  huffman tree from a source data stream
        *-------------------------------------------------
        */
        public HuffmanError ImportTreeHuffman()
        {
            int start;
            int last = 0;
            int count = 0;
            int index;
            uint curcode;
            byte rlefullbits = 0;
            uint temp;

            HuffmanError error;
            /* start by parsing the lengths for the small tree */
            Huffman smallhuff = new Huffman(24, 6, bitbuf);
            smallhuff.huffnode[0].numbits = (byte)bitbuf.read(3);
            start = (int)bitbuf.read(3) + 1;
            for (index = 1; index < 24; index++)
            {
                if (index < start || count == 7)

                    smallhuff.huffnode[index].numbits = 0;
                else
                {
                    count = (int)bitbuf.read(3);
                    smallhuff.huffnode[index].numbits = (byte)(count == 7 ? 0 : count);
                }
            }

            /* then regenerate the tree */
            error = smallhuff.AssignCanonicalCodes();
            if (error != HuffmanError.HUFFERR_NONE)
                return error;
            smallhuff.BuildLookupTable();

            /* determine the maximum length of an RLE count */
            temp = numcodes - 9;
            while (temp != 0)
            {
                temp >>= 1;
                rlefullbits++;
            }
            /* now process the rest of the data */
            for (curcode = 0; curcode < numcodes;)
            {
                int value = (int)smallhuff.DecodeOne();
                if (value != 0)
                    huffnode[curcode++].numbits = (byte)(last = value - 1);
                else
                {
                    count = (int)bitbuf.read(3) + 2;
                    if (count == 7 + 2)
                        count += (int)bitbuf.read(rlefullbits);
                    for (; count != 0 && curcode < numcodes; count--)
                        huffnode[curcode++].numbits = (byte)last;
                }
            }

            /* make sure we ended up with the right number */
            if (curcode != numcodes)
                return HuffmanError.HUFFERR_INVALID_DATA;

            /* assign canonical codes for all nodes based on their code lengths */
            error = AssignCanonicalCodes();
            if (error != HuffmanError.HUFFERR_NONE)
                return error;

            /* build the lookup table */
            BuildLookupTable();

            /* determine final input length and report errors */
            return bitbuf.overflow() ? HuffmanError.HUFFERR_INPUT_BUFFER_TOO_SMALL : HuffmanError.HUFFERR_NONE;
        }


        /*-------------------------------------------------
        *  assign_canonical_codes - assign canonical codes
        *  to all the nodes based on the number of bits
        *  in each
        *-------------------------------------------------
        */
        private HuffmanError AssignCanonicalCodes()
        {
            uint curcode;
            int codelen;
            uint curstart = 0;
            /* build up a histogram of bit lengths */
            uint[] bithisto = new uint[33];
            for (curcode = 0; curcode < numcodes; curcode++)
            {

                Node node = huffnode[curcode];
                if (node.numbits > maxbits)
                    return HuffmanError.HUFFERR_INTERNAL_INCONSISTENCY;
                if (node.numbits <= 32)
                    bithisto[node.numbits]++;
            }

            /* for each code length, determine the starting code number */
            for (codelen = 32; codelen > 0; codelen--)
            {
                uint nextstart = (curstart + bithisto[codelen]) >> 1;
                if (codelen != 1 && nextstart * 2 != curstart + bithisto[codelen])
                    return HuffmanError.HUFFERR_INTERNAL_INCONSISTENCY;
                bithisto[codelen] = curstart;
                curstart = nextstart;
            }


            /* now assign canonical codes */
            for (curcode = 0; curcode < numcodes; curcode++)
            {
                Node node = huffnode[curcode];
                if (node.numbits > 0)
                    node.bits = bithisto[node.numbits]++;
            }
            return HuffmanError.HUFFERR_NONE;
        }

        /*-------------------------------------------------
        *  build_lookup_table - build a lookup table for
        *  fast decoding
        *-------------------------------------------------
        */
        private void BuildLookupTable()
        {
            uint curcode;
            /* iterate over all codes */
            for (curcode = 0; curcode < numcodes; curcode++)
            {
                /* process all nodes which have non-zero bits */
                Node node = huffnode[curcode];
                if (node.numbits > 0)
                {
                    int shift;
                    uint dest;
                    uint destend;
                    /* set up the entry */
                    uint value = MAKE_LOOKUP(curcode, node.numbits);
                    /* fill all matching entries */
                    shift = maxbits - node.numbits;
                    dest = node.bits << shift;
                    destend = ((node.bits + 1) << shift) - 1;
                    while (dest <= destend)
                        lookup[dest++] = (ushort)value;
                }
            }
        }
    }

}