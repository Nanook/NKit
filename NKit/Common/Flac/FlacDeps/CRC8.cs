namespace CUETools.Codecs.Flake
{
    internal class Crc8
    {
        private const ushort _Poly8 = 0x07;

        private ushort[] _table = new ushort[256];

        public Crc8()
        {
            int bits = 8;
            ushort poly = (ushort)(_Poly8 + (1U << bits));
            for (ushort i = 0; i < _table.Length; i++)
            {
                ushort crc = i;
                for (int j = 0; j < bits; j++)
                {
                    if ((crc & (1U << (bits - 1))) != 0)
                        crc = (ushort)((crc << 1) ^ poly);
                    else
                        crc <<= 1;
                }
                _table[i] = (ushort)(crc & 0x00ff);
            }
        }

        public byte ComputeChecksum(byte[] bytes, int pos, int count)
        {
            ushort crc = 0;
            for (int i = pos; i < pos + count; i++)
                crc = _table[crc ^ bytes[i]];
            return (byte)crc;
        }

        public unsafe byte ComputeChecksum(byte* bytes, int pos, int count)
        {
            ushort crc = 0;
            for (int i = pos; i < pos + count; i++)
                crc = _table[crc ^ bytes[i]];
            return (byte)crc;
        }
    }
}
