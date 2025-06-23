using Xunit;
using Fpzip.Net.RangeCoder;
using Fpzip.Net.Io;
using System.IO;
using System.Linq;

namespace Fpzip.Net.Tests
{
    public class RangeCoderTests
    {
        [Theory]
        [InlineData(new uint[] { 0, 1, 0, 1, 0, 0, 0, 1, 1, 1, 0 }, 2, 10)] // Symbols, NumSymbols, PrecisionBits for QsModel
        [InlineData(new uint[] { 0, 0, 0, 0, 0 }, 2, 8)]
        [InlineData(new uint[] { 1, 1, 1, 1, 1 }, 2, 8)]
        [InlineData(new uint[] { 0, 1, 2, 3, 2, 1, 0 }, 4, 8)]
        [InlineData(new uint[] { 7, 15, 1, 0, 8, 4, 2, 10, 3, 5, 6, 9, 11, 12, 13, 14 }, 16, 10)] // Many symbols
        public void EncodeDecode_WithQsModel_ShouldMatch(uint[] symbolsToEncode, uint numModelSymbols, uint modelPrecisionBits)
        {
            using var ms = new MemoryStream();

            // Encoding
            using (var bitWriter = new BitStreamWriter(ms, leaveOpen: true))
            using (var encoder = new RangeEncoder(bitWriter))
            {
                var model = new RangeCoderQsModel(isCompression: true, numModelSymbols, modelPrecisionBits);
                foreach (uint sym in symbolsToEncode)
                {
                    encoder.Encode(sym, model);
                }
                encoder.Finish();
            }

            ms.Position = 0; // Reset for reading
            var encodedBytes = ms.ToArray(); // For debugging if needed

            // Decoding
            uint[] decodedSymbols = new uint[symbolsToEncode.Length];
            using (var bitReader = new BitStreamReader(ms, leaveOpen: true))
            using (var decoder = new RangeDecoder(bitReader))
            {
                decoder.ActualInit(); // Use the corrected init
                var model = new RangeCoderQsModel(isCompression: false, numModelSymbols, modelPrecisionBits);
                for (int i = 0; i < decodedSymbols.Length; i++)
                {
                    decodedSymbols[i] = decoder.Decode(model);
                }
            }
            Assert.Equal(symbolsToEncode, decodedSymbols);
        }

        [Fact]
        public void EncodeDecode_Uniform_SmallValues()
        {
            using var ms = new MemoryStream();
            uint[] values = { 0, 1, 2, 3, 0, 1, 2, 3 };
            int bits = 2; // Each value needs 2 bits

            using (var bitWriter = new BitStreamWriter(ms, leaveOpen: true))
            using (var encoder = new RangeEncoder(bitWriter))
            {
                foreach (uint val in values)
                {
                    encoder.EncodeUniform(val, bits);
                }
                encoder.Finish();
            }

            ms.Position = 0;
            uint[] decodedValues = new uint[values.Length];
            using (var bitReader = new BitStreamReader(ms, leaveOpen: true))
            using (var decoder = new RangeDecoder(bitReader))
            {
                decoder.ActualInit();
                for (int i = 0; i < decodedValues.Length; i++)
                {
                    decodedValues[i] = decoder.DecodeUniform(bits);
                }
            }
            Assert.Equal(values, decodedValues);
        }

        [Fact]
        public void EncodeDecode_Uniform_LargerValues()
        {
            using var ms = new MemoryStream();
            uint[] values = { 150, 200, 10, 0, 255 }; // Max 255, requires 8 bits
            int bits = 8;

            using (var bitWriter = new BitStreamWriter(ms, leaveOpen: true))
            using (var encoder = new RangeEncoder(bitWriter))
            {
                foreach (uint val in values)
                {
                    encoder.EncodeUniform(val, bits);
                }
                encoder.Finish();
            }

            ms.Position = 0;
            uint[] decodedValues = new uint[values.Length];
            using (var bitReader = new BitStreamReader(ms, leaveOpen: true))
            using (var decoder = new RangeDecoder(bitReader))
            {
                decoder.ActualInit();
                for (int i = 0; i < decodedValues.Length; i++)
                {
                    decodedValues[i] = decoder.DecodeUniform(bits);
                }
            }
            Assert.Equal(values, decodedValues);
        }


        [Fact]
        public void EncodeDecode_RawBits()
        {
            using var ms = new MemoryStream();
            // Raw bits don't use the RangeCoder's arithmetic properties, just BitStream
            // This test is more for completeness of IRangeEncoder/Decoder interface usage
            // if ResidualCodec were to call these (which it does).

            uint[] values = { 0b101, 0b1111, 0b0, 0b101010 };
            int[] numBits = { 3,     4,      1,   6       };

            using (var bitWriter = new BitStreamWriter(ms, leaveOpen: true))
            // Raw methods are on encoder/decoder but pass through to BitStream
            // So, we don't need full RangeEncoder for this, but using it to test interface.
            using (var encoder = new RangeEncoder(bitWriter))
            {
                for(int i=0; i<values.Length; ++i)
                {
                    encoder.EncodeRaw(values[i], numBits[i]);
                }
                // No Finish() for encoder as raw bits are not part of arithmetic stream state usually
                // But BitStreamWriter needs flush.
                bitWriter.Flush();
            }

            ms.Position = 0;
            uint[] decodedValues = new uint[values.Length];
            using (var bitReader = new BitStreamReader(ms, leaveOpen: true))
            using (var decoder = new RangeDecoder(bitReader))
            {
                // No Init() for decoder if only reading raw
                for (int i = 0; i < decodedValues.Length; i++)
                {
                    decodedValues[i] = decoder.DecodeRaw(numBits[i]);
                }
            }
            Assert.Equal(values, decodedValues);
        }

        // Add EncodeUniform64 / DecodeUniform64 tests
        // Add EncodeRaw64 / DecodeRaw64 tests
    }
}
