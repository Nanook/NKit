using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nanook.NKit.Chd
{
    internal class TreeNodeCompare : IComparer<Node>
    {
        public int Compare(Node node1, Node node2)
        {
            if (node2.weight != node1.weight)
                return (int)(node2.weight - node1.weight);
#if DEBUG
            if (node2.bits - node1.bits == 0)
                Debug.WriteLine("identical node sort keys, should not happen!");
#endif
            return (int)node1.bits - (int)node2.bits;
        }
    }


    //https://cocalc.com/github/stenzek/duckstation/blob/master/dep/libchdr/src/libchdr_huffman.c
    internal partial class Huffman
    {
        public TreeNodeCompare _treeSorter = new TreeNodeCompare();

        private void HistoReset() =>
            //memset(m_datahisto_array, 0, sizeof(m_datahisto_array));
            Array.Clear(datahisto, 0, datahisto.Length);
        //-------------------------------------------------
        //  encode_one - encode a single code to the
        //  huffman stream
        //-------------------------------------------------

        public void EncodeOne(BitStream bitbuf, uint data)
        {
            // write the data
            Node node = huffnode[data];
            bitbuf.write(node.bits, node.numbits);
        }

        //-------------------------------------------------
        //  histo_one - update the histogram
        //-------------------------------------------------
        internal void HistoOne(uint data) => datahisto[data]++;

        //-------------------------------------------------
        //  encode - encode a full CiBuffer
        //-------------------------------------------------

        HuffmanError Encode(byte[] source, int slength, byte[] dest, int dlength, out int complength)
        {
            //first compute the histogram
            complength = 0;

            HistoReset();
            for (int cur = 0; cur < slength; cur++)
                HistoOne(source[cur]);

            // then compute the tree
            HuffmanError err = ComputeTreeFromHisto();
            if (err != HuffmanError.HUFFERR_NONE)
                return err;

            // export the tree
            BitStream bitbuf = new BitStream(dest, 0, dlength);
            err = ExportTreeHuffman(bitbuf);
            if (err != HuffmanError.HUFFERR_NONE)
                return err;

            // then encode the data
            for (int cur = 0; cur < slength; cur++)
                EncodeOne(bitbuf, source[cur]);
            complength = bitbuf.flush();
            return bitbuf.overflow() ? HuffmanError.HUFFERR_OUTPUT_BUFFER_TOO_SMALL : HuffmanError.HUFFERR_NONE;
        }

        //-------------------------------------------------
        //  build_tree - build a huffman tree based on the
        //  data distribution
        //-------------------------------------------------
        public int BuildTree(uint totaldata, uint totalweight)
        {
            // make a list of all non-zero nodes
            huffnode = new Node[numcodes * 2];
            for (int i = 0; i < huffnode.Length; i++) //memset(m_huffnode, 0, m_numcodes * sizeof(m_huffnode[0]));
                huffnode[i] = new Node();

            int listitems = 0;
            for (int curcode = 0; curcode < numcodes; curcode++)
            {
                if (datahisto[curcode] != 0)
                {
                    huffnode[listitems++] = huffnode[curcode]; //copy ref
                    huffnode[curcode].count = datahisto[curcode];
                    huffnode[curcode].bits = (uint)curcode;

                    // scale the weight by the current effective length, ensuring we don't go to 0
                    huffnode[curcode].weight = (uint)((ulong)datahisto[curcode] * (ulong)totalweight / (ulong)totaldata);
                    if (huffnode[curcode].weight == 0)
                        huffnode[curcode].weight = 1;
                }
            }

#if DEBUG
            Debug.WriteLine("Pre-sort:");
            for (int i = 0; i < listitems; i++)
                Debug.WriteLine($"weight: {huffnode[i].weight} code: {huffnode[i].bits}");
#endif
            // sort the list by weight, largest weight first
            Array.Sort(huffnode, 0, listitems, _treeSorter); //qsort(&list[0], listitems, sizeof(list[0]), tree_node_compare);
#if DEBUG
            Debug.WriteLine("Post-sort:");
            for (int i = 0; i < listitems; i++)
                Debug.WriteLine($"weight: {huffnode[i].weight} code: {huffnode[i].bits}");
            Debug.WriteLine("===================");
#endif
            // now build the tree
            uint nextalloc = numcodes;
            while (listitems > 1)
            {
                // remove lowest two items
                Node node1 = huffnode[--listitems];
                Node node0 = huffnode[--listitems];

                // create new node
                Node newnode = huffnode[nextalloc++];
                newnode.parent = null; //nullptr;
                node0.parent = node1.parent = newnode;
                newnode.weight = node0.weight + node1.weight;

                // insert into list at appropriate location
                int curitem;
                for (curitem = 0; curitem < listitems; curitem++)
                {
                    if (newnode.weight > huffnode[curitem].weight)
                    {
                        for (int i = listitems - 1; i >= curitem; i--) //memmove(&list[curitem + 1], huffnode[curitem], (listitems - curitem) * sizeof(list[0]));
                            huffnode[i + 1] = huffnode[i];
                        break;
                    }
                }
                huffnode[curitem] = newnode;
                listitems++;
            }

            // compute the number of bits in each code, and fill in another histogram
            int maxbits = 0;
            for (int curcode = 0; curcode < numcodes; curcode++)
            {
                Node node = huffnode[curcode];
                node.numbits = 0;
                node.bits = 0;

                // if we have a non-zero weight, compute the number of bits
                if (node.weight > 0)
                {
                    // determine the number of bits for this node
                    for (Node curnode = node; curnode.parent != null; curnode = curnode.parent)
                        node.numbits++;
                    if (node.numbits == 0)
                        node.numbits = 1;

                    // keep track of the max
                    maxbits = Math.Max(maxbits, (int)node.numbits);
                }
            }
            return maxbits;
        }

        //-------------------------------------------------
        //  export_tree_huffman - export a huffman tree to
        //  a huffman target data stream
        //-------------------------------------------------

        HuffmanError ExportTreeHuffman(BitStream bitbuf)
        {
            // first RLE compress the lengths of all the nodes
            byte[] dest = new byte[numcodes]; //std::vector<uint8_t> rle_data(m_numcodes);
            ushort[] lengths = new ushort[numcodes / 3]; //std::vector<uint16_t> rle_lengths(m_numcodes/3);
            int last = ~0;
            int repcount = 0;

            // use a small huffman context to create a tree (ignoring RLE lengths)
            Huffman smallhuff = new Huffman(24, 6, bitbuf); //huffman_encoder < 24, 6 > smallhuff;

            // RLE-compress the lengths
            int destIdx = 0;
            int lengthIdx = 0;
            for (int curcode = 0; curcode < numcodes; curcode++)
            {
                // if this is the end of a repeat, flush any accumulation
                int newval = huffnode[curcode].numbits;
                if (newval != last && repcount > 0)
                {
                    if (repcount == 1)
                        smallhuff.HistoOne(dest[destIdx++] = (byte)(last + 1));
                    else
                    {
                        smallhuff.HistoOne(dest[destIdx++] = 0);
                        lengths[lengthIdx++] = (byte)(repcount - 2);
                    }
                }

                // if same as last, just track repeats
                if (newval == last)
                    repcount++;

                // otherwise, write it and start a new run
                else
                {
                    smallhuff.HistoOne(dest[destIdx++] = (byte)(newval + 1));
                    last = newval;
                    repcount = 0;
                }
            }

            // flush any final RLE counts
            if (repcount > 0)
            {
                if (repcount == 1)
                    smallhuff.HistoOne(dest[destIdx++] = (byte)(last + 1));
                else
                {
                    smallhuff.HistoOne(dest[destIdx++] = 0);
                    lengths[lengthIdx++] = (byte)(repcount - 2);
                }
            }

            // compute an optimal tree
            smallhuff.ComputeTreeFromHisto();

            // determine the first and last non-zero nodes
            int first_non_zero = 31, last_non_zero = 0;
            for (int index = 1; index < smallhuff.numcodes; index++)
                if (smallhuff.huffnode[index].numbits != 0)
                {
                    if (first_non_zero == 31)
                        first_non_zero = index;
                    last_non_zero = index;
                }

            // clamp first non-zero to be 8 at a maximum
            first_non_zero = Math.Min(first_non_zero, 8);

            // output the lengths of the each small tree node, starting with the RLE
            // token (0), followed by the first_non_zero value, followed by the data
            // terminated by a 7
            bitbuf.write(smallhuff.huffnode[0].numbits, 3);
            bitbuf.write((uint)first_non_zero - 1, 3);
            for (int index = first_non_zero; index <= last_non_zero; index++)
                bitbuf.write(smallhuff.huffnode[index].numbits, 3);
            bitbuf.write(7, 3);

            // determine the maximum length of an RLE count
            uint temp = numcodes - 9;
            byte rlefullbits = 0;
            while (temp != 0)
            {
                temp >>= 1;
                rlefullbits++;
            }

            // now encode the RLE data
            lengthIdx = 0;
            for (int src = 0; src < dest.Length; src++)
            {
                // encode the data
                byte data = dest[src];
                smallhuff.EncodeOne(bitbuf, data);

                // if this is an RLE token, encode the length following
                if (data == 0)
                {
                    uint count = lengths[lengthIdx++];
                    if (count < 7)
                        bitbuf.write(count, 3);
                    else
                    {
                        bitbuf.write(7, 3);
                        bitbuf.write(count - 7, rlefullbits);
                    }
                }
            }

            // flush the final CiBuffer
            return bitbuf.overflow() ? HuffmanError.HUFFERR_OUTPUT_BUFFER_TOO_SMALL : HuffmanError.HUFFERR_NONE;
        }


        //-------------------------------------------------
        //  compute_tree_from_histo - common backend for
        //  computing a tree based on the data histogram
        //-------------------------------------------------

        internal HuffmanError ComputeTreeFromHisto()
        {
            // compute the number of data items in the histogram
            uint sdatacount = 0;
            for (int i = 0; i < numcodes; i++)
                sdatacount += datahisto[i];

            // binary search to achieve the optimum encoding
            uint lowerweight = 0;
            uint upperweight = sdatacount * 2;
            while (true)
            {
                // build a tree using the current weight
                uint curweight = (upperweight + lowerweight) / 2;
                int curmaxbits = BuildTree(sdatacount, curweight);

                // apply binary search here
                if (curmaxbits <= maxbits)
                {
                    lowerweight = curweight;

                    // early out if it worked with the raw weights, or if we're done searching
                    if (curweight == sdatacount || (upperweight - lowerweight) <= 1)
                        break;
                }
                else
                    upperweight = curweight;
            }

            // assign canonical codes for all nodes based on their code lengths
            return AssignCanonicalCodes();
        }

        //-------------------------------------------------
        //  export_tree_rle - export a huffman tree to an
        //  RLE target data stream
        //-------------------------------------------------

        public HuffmanError ExportTreeRle(BitStream bitbuf)
        {
            // bits per entry depends on the maxbits
            int numbits;
            if (maxbits >= 16)
                numbits = 5;
            else if (maxbits >= 8)
                numbits = 4;
            else
                numbits = 3;

            // RLE encode the lengths
            int lastval = ~0;
            int repcount = 0;
            for (int curcode = 0; curcode < numcodes; curcode++)
            {
                // if we match the previous value, just bump the repcount
                int newval = huffnode[curcode].numbits;
                if (newval == lastval)
                    repcount++;

                // otherwise, we need to flush the previous repeats
                else
                {
                    if (repcount != 0)
                        WriteRleTreeBits(bitbuf, lastval, repcount, numbits);
                    lastval = newval;
                    repcount = 1;
                }
            }

            // flush the last value
            WriteRleTreeBits(bitbuf, lastval, repcount, numbits);
            return bitbuf.overflow() ? HuffmanError.HUFFERR_OUTPUT_BUFFER_TOO_SMALL : HuffmanError.HUFFERR_NONE;
        }

        //-------------------------------------------------
        //  write_rle_tree_bits - write an RLE encoded
        //  set of data to a target stream
        //-------------------------------------------------

        private void WriteRleTreeBits(BitStream bitbuf, int value, int repcount, int numbits)
        {
            // loop until we have output all of the repeats
            while (repcount > 0)
            {
                // if we have a 1, write it twice as it is an escape code
                if (value == 1)
                {
                    bitbuf.write(1, numbits);
                    bitbuf.write(1, numbits);
                    repcount--;
                }

                // if we have two or fewer in a row, write them raw
                else if (repcount <= 2)
                {
                    bitbuf.write((uint)value, numbits);
                    repcount--;
                }

                // otherwise, write a triple using 1 as the escape code
                else
                {
                    int cur_reps = Math.Min(repcount - 3, (1 << numbits) - 1);
                    bitbuf.write(1, numbits);
                    bitbuf.write((uint)value, numbits);
                    bitbuf.write((uint)cur_reps, numbits);
                    repcount -= cur_reps + 3;
                }
            }
        }

    }

}