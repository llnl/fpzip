using Xunit;
using Fpzip.Net.Prediction;
using Fpzip.Net.RangeCoder;
using System.Collections.Generic;
using System.IO; // For using real range coder in one test
using Fpzip.Net.Io; // For BitStream I/O

namespace Fpzip.Net.Tests
{
    // Basic stub for IRangeModel for testing ResidualCodec symbol calculation
    class StubRangeModel : IRangeModel
    {
        public uint Symbols { get; }
        public StubRangeModel(uint symbols) { Symbols = symbols; }
        public void GetCharProbs(uint symbol, out uint symbolLow, out uint symbolFreq) { symbolLow = symbol; symbolFreq = 1; } // Simplistic
        public uint DecodeChar(uint scaledCumFreq, out uint symbolLow, out uint symbolFreq) { symbolLow = scaledCumFreq; symbolFreq = 1; return scaledCumFreq; } // Simplistic
        public void NormalizeRange(ref uint rangeComponent) { rangeComponent /= Symbols == 0 ? 1: Symbols; } // Simplistic
        public uint TotalFrequency => Symbols == 0 ? 1 : Symbols; // Simplistic
        public void Update(uint symbol) { } // No-op
        public void Reset() { } // No-op
    }

    // Stub for IRangeEncoder/Decoder to capture calls
    class CapturingRangeCoder : IRangeEncoder, IRangeDecoder
    {
        public List<(uint symbol, uint modelSymbols)> EncodedSymbols { get; } = new List<(uint, uint)>();
        public List<(uint value, int bits)> EncodedRaw { get; } = new List<(uint, int)>();
        public List<(ulong value, int bits)> EncodedRaw64 { get; } = new List<(ulong, int)>();

        public Queue<(uint symbol, uint modelSymbols)> SymbolsToDecode { get; } = new Queue<(uint, uint)>();
        public Queue<(uint value, int bits)> RawToDecode { get; } = new Queue<(uint, int)>();
        public Queue<(ulong value, int bits)> Raw64ToDecode { get; } = new Queue<(ulong, int)>();

        public long BytesWritten => 0;
        public long BytesRead => 0;

        public void Dispose() { }
        public void Finish() { }
        public void Init() { }

        public void Encode(uint symbol, IRangeModel model) => EncodedSymbols.Add((symbol, model.Symbols));
        public void EncodeRaw(uint value, int bits) => EncodedRaw.Add((value, bits));
        public void EncodeRaw64(ulong value, int bits) => EncodedRaw64.Add((value, bits));
        public void EncodeUniform(uint value, int bits) => EncodeRaw(value, bits); // For stub, treat uniform as raw
        public void EncodeUniform64(ulong value, int bits) => EncodeRaw64(value, bits);


        public uint Decode(IRangeModel model) => SymbolsToDecode.Dequeue().symbol;
        public uint DecodeRaw(int bits) => RawToDecode.Dequeue().value;
        public ulong DecodeRaw64(int bits) => Raw64ToDecode.Dequeue().value;
        public uint DecodeUniform(int bits) => DecodeRaw(bits);
        public ulong DecodeUniform64(int bits) => DecodeRaw64(bits);
    }


    public class ResidualCodecTests
    {
        [Theory]
        // Narrow cases (precision <= MaxDirectlyEncodableBits (8))
        [InlineData(100u, 90u, 8)]  // diff = 10
        [InlineData(90u, 100u, 8)]  // diff = -10
        [InlineData(100u, 100u, 8)] // diff = 0
        public void EncodeDecode_UInt_NarrowStrategy_ShouldMatch(uint actual, uint predicted, int precision)
        {
            var coder = new CapturingRangeCoder();
            uint modelSymbols = ResidualCodec.GetSymbolCount(precision);
            var model = new StubRangeModel(modelSymbols);

            ResidualCodec.EncodeResidual(actual, predicted, precision, coder, model);

            // Prepare decoder
            coder.SymbolsToDecode.Enqueue(coder.EncodedSymbols[0]);

            uint decodedActual = ResidualCodec.DecodeResidual(predicted, precision, coder, model);
            Assert.Equal(actual, decodedActual);
        }

        [Theory]
        // Wide cases (precision > MaxDirectlyEncodableBits (8))
        [InlineData(50000u, 40000u, 16)] // diff = 10000
        [InlineData(40000u, 50000u, 16)] // diff = -10000
        [InlineData(50000u, 50000u, 16)] // diff = 0
        [InlineData(0xFFFFFFFFu, 0u, 32)] // Large positive diff
        [InlineData(0u, 0xFFFFFFFFu, 32)] // Large negative diff (mapped)
        public void EncodeDecode_UInt_WideStrategy_ShouldMatch(uint actual, uint predicted, int precision)
        {
            var coder = new CapturingRangeCoder();
            uint modelSymbols = ResidualCodec.GetSymbolCount(precision);
            var model = new StubRangeModel(modelSymbols);

            ResidualCodec.EncodeResidual(actual, predicted, precision, coder, model);

            // Prepare decoder
            coder.SymbolsToDecode.Enqueue(coder.EncodedSymbols[0]);
            if (coder.EncodedRaw.Count > 0) // If not perfect prediction
                coder.RawToDecode.Enqueue(coder.EncodedRaw[0]);

            uint decodedActual = ResidualCodec.DecodeResidual(predicted, precision, coder, model);
            Assert.Equal(actual, decodedActual);
        }

        [Theory]
        // Narrow cases for ulong
        [InlineData(100ul, 90ul, 8)]
        // Wide cases for ulong
        [InlineData(5000000000ul, 4000000000ul, 32)] // Requires ulong, precision still for symbol part
        [InlineData(0xFFFFFFFFFFFFFFFFul, 0ul, 64)]
        public void EncodeDecode_ULong_Strategies_ShouldMatch(ulong actual, ulong predicted, int precision)
        {
            var coder = new CapturingRangeCoder();
            uint modelSymbols = ResidualCodec.GetSymbolCount64(precision); // Use 64-bit version for appropriate symbol count
            var model = new StubRangeModel(modelSymbols);

            ResidualCodec.EncodeResidual(actual, predicted, precision, coder, model);

            coder.SymbolsToDecode.Enqueue(coder.EncodedSymbols[0]);
            if (coder.EncodedRaw64.Count > 0)
                coder.Raw64ToDecode.Enqueue(coder.EncodedRaw64[0]);
            else if (coder.EncodedRaw.Count > 0 && precision > PredictionConstants.MaxDirectlyEncodableBits) // Check for uint raw part for ulong narrow
                 coder.RawToDecode.Enqueue(coder.EncodedRaw[0]);


            ulong decodedActual = ResidualCodec.DecodeResidual(predicted, precision, coder, model);
            Assert.Equal(actual, decodedActual);
        }


        [Fact]
        public void GetSymbolCount_CalculatesCorrectly()
        {
            // Narrow (e.g., 8 bits)
            int narrowPrecision = 8;
            uint expectedNarrowSymbols = (2u * (1u << narrowPrecision)) - 1u; // 2 * 256 - 1 = 511
            Assert.Equal(expectedNarrowSymbols, ResidualCodec.GetSymbolCount(narrowPrecision));

            // Wide (e.g., 16 bits)
            int widePrecision = 16;
            uint expectedWideSymbols = (2u * (uint)widePrecision) + 1u; // 2 * 16 + 1 = 33
            Assert.Equal(expectedWideSymbols, ResidualCodec.GetSymbolCount(widePrecision));

            // For 64-bit (double context)
            Assert.Equal(expectedNarrowSymbols, ResidualCodec.GetSymbolCount64(narrowPrecision));
            Assert.Equal((2u * (uint)64) + 1u, ResidualCodec.GetSymbolCount64(64));

        }

        // Integration test with actual RangeCoder
        [Theory]
        [InlineData(50000u, 40000u, 16)]
        [InlineData(40000u, 50000u, 16)]
        [InlineData(50000u, 50000u, 16)]
        public void EncodeDecode_WithActualRangeCoder_UInt_WideStrategy(uint actual, uint predicted, int precision)
        {
            using var ms = new MemoryStream();
            uint decodedActual;

            // Encode
            using (var bitWriter = new BitStreamWriter(ms, leaveOpen: true))
            using (var rangeEncoder = new RangeEncoder(bitWriter))
            {
                uint modelSymbols = ResidualCodec.GetSymbolCount(precision);
                using var model = new RangeCoderQsModel(isCompression: true, modelSymbols, 16); // Use 16 precision bits for QSModel
                ResidualCodec.EncodeResidual(actual, predicted, precision, rangeEncoder, model);
                rangeEncoder.Finish();
            }

            ms.Position = 0;

            // Decode
            using (var bitReader = new BitStreamReader(ms, leaveOpen: true))
            using (var rangeDecoder = new RangeDecoder(bitReader))
            {
                rangeDecoder.ActualInit();
                uint modelSymbols = ResidualCodec.GetSymbolCount(precision);
                using var model = new RangeCoderQsModel(isCompression: false, modelSymbols, 16);
                decodedActual = ResidualCodec.DecodeResidual(predicted, precision, rangeDecoder, model);
            }
            Assert.Equal(actual, decodedActual);
        }
    }
}
