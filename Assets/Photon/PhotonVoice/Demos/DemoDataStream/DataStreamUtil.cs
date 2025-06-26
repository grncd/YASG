namespace DataStreamDemo
{
    static class Util
    {
        public static uint CalculateCrc(byte[] buffer, int offset, int length)
        {
            uint num = uint.MaxValue;
            uint polynomial = 3988292384u;
            if (crcLookupTable == null)
            {
                crcLookupTable = InitializeTable(polynomial);
            }

            for (int i = 0; i < length; i++)
            {
                num = (num >> 8) ^ crcLookupTable[buffer[offset + i] ^ (num & 0xFF)];
            }

            return num;
        }

        private static uint[] crcLookupTable;

        private static uint[] InitializeTable(uint polynomial)
        {
            uint[] array = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                uint num = (uint)i;
                for (int j = 0; j < 8; j++)
                {
                    num = (((num & 1) != 1) ? (num >> 1) : ((num >> 1) ^ polynomial));
                }

                array[i] = num;
            }

            return array;
        }
    }
}

