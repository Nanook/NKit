using System.Text;

namespace Nanook.NKit
{
    internal class BitState
    {
        public byte[] Bytes { get; private set; }

        internal BitState()
        {
            Bytes = null;
        }

        internal BitState(int byteCount)
        {
            Populate(byteCount);
        }

        internal BitState(byte[] bytes)
        {
            this.Bytes = bytes;
        }

        public void Populate(int byteCount) => this.Bytes = new byte[byteCount];

        public void Clear()
        {
            for (int i = 0; i < Bytes.Length; i++)
                Bytes[i] = 0;
        }

        public bool IsClear()
        {
            if (Bytes == null)
                return true;

            for (int i = 0; i < Bytes.Length; i++)
            {
                if (Bytes[i] != 0)
                    return false;
            }
            return true;
        }

        public BitState Clone() => new BitState() { Bytes = (byte[])this.Bytes.Clone() };

        public bool this[int index]
        {
            get => (Bytes[index >> 3] & (1 << (7 - (index & 0x7)))) != 0;
            set
            {
                if (value)
                    Bytes[index >> 3] |= (byte)(1 << (7 - (index & 0x7)));
                else
                    Bytes[index >> 3] &= (byte)~(1 << (7 - (index & 0x7)));
            }
        }

        public override string ToString()
        {
            if (Bytes == null)
                return "";

            //bool set = false;
            StringBuilder sb = new StringBuilder(Bytes.Length << 2);
            for (int i = 0; i < Bytes.Length; i++)
                sb.Append(Bytes[i].ToString("X2"));
            return sb.ToString();
        }
    }


}