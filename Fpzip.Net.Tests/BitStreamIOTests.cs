using Xunit;
using Fpzip.Net.Io;
using System.IO;
using System.Linq;

namespace Fpzip.Net.Tests
{
    public class BitStreamIOTests
    {
        [Fact]
        public void BitStreamWriter_WriteSingleBits_ShouldProduceCorrectBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BitStreamWriter(ms);

            // 10110011 -> 0xB3
            writer.WriteBit(1);
            writer.WriteBit(0);
            writer.WriteBit(1);
            writer.WriteBit(1);
            writer.WriteBit(0);
            writer.WriteBit(0);
            writer.WriteBit(1);
            writer.WriteBit(1);

            // 01101110 -> 0x6E
            writer.WriteBit(0);
            writer.WriteBit(1);
            writer.WriteBit(1);
            writer.WriteBit(0);
            writer.WriteBit(1);
            writer.WriteBit(1);
            writer.WriteBit(1);
            writer.WriteBit(0);

            writer.Flush(); // Important to flush the last byte

            byte[] expected = { 0xB3, 0x6E };
            Assert.Equal(expected, ms.ToArray());
        }

        [Fact]
        public void BitStreamWriter_WriteMultipleBits_ShouldProduceCorrectBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BitStreamWriter(ms);

            // Value: 0101 (5), Count: 4 bits
            // Value: 110 (6), Count: 3 bits
            // Value: 1 (1), Count: 1 bit
            // Combined: 0101 110 1 -> 01011101 -> 0x5D
            writer.WriteBits(0b0101, 4);
            writer.WriteBits(0b110, 3);
            writer.WriteBits(0b1, 1);

            // Value: 10011010 (0x9A), Count: 8 bits
            writer.WriteBits(0x9A, 8);

            writer.Flush();

            byte[] expected = { 0x5D, 0x9A };
            Assert.Equal(expected, ms.ToArray());
        }

        [Fact]
        public void BitStreamWriter_WriteBits64_ShouldProduceCorrectBytes()
        {
            using var ms = new MemoryStream();
            using var writer = new BitStreamWriter(ms);

            // Example: Write 12 bits from a ulong
            // 1111 0000 1010 (0xF0A)
            ulong val1 = 0xF0A;
            writer.WriteBits64(val1, 12);
            // This will be 11110000 1010xxxx (pad last nibble)
            // Byte1: 11110000 (0xF0)
            // Byte2: 10100000 (0xA0) (assuming flush pads with 0s)

            writer.Flush();
            byte[] result = ms.ToArray();
            Assert.Equal(2, result.Length);
            Assert.Equal(0xF0, result[0]);
            Assert.Equal(0xA0, result[1]); // After flush, remaining bits of the byte are 0
        }


        [Fact]
        public void BitStreamWriter_FlushPartialByte_ShouldPadWithZeros()
        {
            using var ms = new MemoryStream();
            using var writer = new BitStreamWriter(ms);

            writer.WriteBit(1); // 10000000
            writer.WriteBit(0); // 10000000
            writer.WriteBit(1); // 10100000

            writer.Flush(); // Should write 0xA0

            byte[] expected = { 0xA0 };
            Assert.Equal(expected, ms.ToArray());
        }

        [Fact]
        public void BitStreamReader_ReadSingleBits_ShouldMatchWritten()
        {
            byte[] data = { 0xB3, 0x6E }; // 10110011 01101110
            using var ms = new MemoryStream(data);
            using var reader = new BitStreamReader(ms);

            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());

            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
        }

        [Fact]
        public void BitStreamReader_ReadMultipleBits_ShouldMatchWritten()
        {
            byte[] data = { 0x5D, 0x9A }; // 01011101 10011010
            // 0101 (5), 110 (6), 1 (1)
            // 10011010 (0x9A)
            using var ms = new MemoryStream(data);
            using var reader = new BitStreamReader(ms);

            Assert.Equal(0b0101u, reader.ReadBits(4));
            Assert.Equal(0b110u,  reader.ReadBits(3));
            Assert.Equal(0b1u,    reader.ReadBits(1));
            Assert.Equal(0x9Au,   reader.ReadBits(8));
        }

        [Fact]
        public void BitStreamReader_ReadBits64_ShouldMatchWritten()
        {
            byte[] data = { 0xF0, 0xA0 }; // 11110000 10100000
            // Represents 0xF0A written with 12 bits, padded with four 0s by writer's flush.
            using var ms = new MemoryStream(data);
            using var reader = new BitStreamReader(ms);

            ulong expectedVal = 0xF0A;
            Assert.Equal(expectedVal, reader.ReadBits64(12));
        }


        [Fact]
        public void BitStreamReader_ReadPastEnd_ShouldThrowEndOfStreamException()
        {
            byte[] data = { 0xFF };
            using var ms = new MemoryStream(data);
            using var reader = new BitStreamReader(ms);

            reader.ReadBits(8); // Read all 8 bits
            Assert.Throws<EndOfStreamException>(() => reader.ReadBit());
        }

        [Fact]
        public void BitStream_WriterReader_ComplexSequence()
        {
            using var ms = new MemoryStream();
            uint[] valuesToWrite = { 1, 0, 1, 1, 0, 0, 1, 0, 1, 1, 1, 0, 0, 0, 1 }; // 15 bits
            int[] bitCounts =    { 1, 2, 3, 1, 2, 1, 1, 3, 1, 1, 1, 1, 1, 1, 1 };
                                 // Total bits: 1+2+3+1+2+1+1+3+1+1+1+1+1+1 = 20 bits

            using (var writer = new BitStreamWriter(ms, leaveOpen: true))
            {
                for(int i=0; i<valuesToWrite.Length; ++i)
                {
                    writer.WriteBits(valuesToWrite[i], bitCounts[i]);
                }
                writer.Flush();
            }

            ms.Position = 0; // Reset for reading

            using (var reader = new BitStreamReader(ms))
            {
                for(int i=0; i<valuesToWrite.Length; ++i)
                {
                    Assert.Equal(valuesToWrite[i], reader.ReadBits(bitCounts[i]));
                }
            }
        }
    }
}
