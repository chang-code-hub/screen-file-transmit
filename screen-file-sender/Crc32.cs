using System;

namespace screen_file_transmit
{
    public static class Crc32
    {
        private static readonly uint[] Table = new uint[256];

        static Crc32()
        {
            const uint poly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 8; j > 0; j--)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ poly;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        public static byte[] ComputeHash(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc = (crc >> 8) ^ Table[(crc & 0xFF) ^ b];
            }
            crc ^= 0xFFFFFFFF;
            return BitConverter.GetBytes(crc);
        }
    }
}