namespace Nanook.NKit
{
    internal class SuspRockRidge
    {
        // CD-ROM XA System Use entry (appended to a directory record's System Use field). Layout:
        //   +0 Owner ID (2, BE)   +2 Group ID (2, BE)   +4 Attributes (2, BE)
        //   +6 Signature "XA" (2, ASCII)   +8 File Number (1)   +9 Reserved (5, zero)
        // = 14 (0x0E) bytes total. The XA record, when present, sits at the START of the System Use
        // area (before any SUSP/Rock Ridge entries).
        private const int CdxaRecordSize = 0x0E;

        internal static bool IsCdxa(byte[] data, int offset, int size)
        {
            if (offset % 2 == 1)
            {
                offset++;
                size--;
            }

            // Require room for the full 14-byte record (the old `size >= 5` guard read past its bound
            // at +6/+7 and let a coincidental "XA" in unrelated System Use bytes false-positive), that
            // the buffer actually holds those bytes, and the "XA" signature at +6.
            if (size < CdxaRecordSize || offset + CdxaRecordSize > data.Length)
                return false;

            return data.ReadString(offset + 6, 2) == "XA";
        }

        internal static SuspRockRidge Parse(byte[] data, int offset, int size)
        {
            SuspRockRidge rr = null;
            if (offset % 2 == 1)
            {
                offset++;
                size--;
            }

            while (size >= 3)
            {
                string id = data.ReadString(offset, 2);
                int sz = data.Read8(offset + 2);
                if (id == "RR" ||
                    id == "PX" ||
                    id == "PN" ||
                    id == "SL" ||
                    id == "NM" ||
                    id == "CL" ||
                    id == "PL" ||
                    id == "RE" ||
                    id == "TF" ||
                    id == "SF")
                {
                    if (rr == null)
                        rr = new SuspRockRidge();
                    byte flags = data.Read8(4);
                    if (id == "NM")
                    {
                        if (rr.AlternativeName == null || (flags & 1) == 0)
                            rr.AlternativeName = data.ReadString(offset + 5, sz - 5);
                        else
                            rr.AlternativeName += data.ReadString(offset + 5, sz - 5);
                    }
                }
                //add other RRIP items such as file permissions etc

                if (sz == 0)
                    break;

                offset += sz;
                size -= sz;
            }

            return rr; //can be null
        }

        public string AlternativeName { get; private set; }

    }
}