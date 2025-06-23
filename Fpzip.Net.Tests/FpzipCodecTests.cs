using Xunit;
using Fpzip.Net;
using Fpzip.Net.Common;
using System.IO;
using System;
using System.Linq;

namespace Fpzip.Net.Tests
{
    public class FpzipCodecTests
    {
        private void CompressDecompressAndVerify(float[] originalData, int nx, int ny, int nz, int nf, int precision, string? testName = null)
        {
            var metadata = new FpzipMetadata
            {
                Type = FpzipConstants.TypeFloat,
                Nx = nx, Ny = ny, Nz = nz, Nf = nf,
                Precision = precision
            };

            Assert.True(metadata.GetTotalNumberOfValues() == originalData.Length, $"Test setup error for {testName ?? "Unnamed"}: Total elements mismatch.");

            using var memoryStream = new MemoryStream();
            FpzipCodec.Compress(metadata, originalData, memoryStream, leaveOpen: true);

            memoryStream.Position = 0; // Rewind for decompression

            float[] decompressedData = new float[originalData.Length];
            FpzipMetadata decompressedMetadata = FpzipCodec.Decompress(memoryStream, decompressedData, leaveOpen: true);

            // Verify metadata
            Assert.Equal(metadata.Type, decompressedMetadata.Type);
            Assert.Equal(metadata.Nx, decompressedMetadata.Nx);
            Assert.Equal(metadata.Ny, decompressedMetadata.Ny);
            Assert.Equal(metadata.Nz, decompressedMetadata.Nz);
            Assert.Equal(metadata.Nf, decompressedMetadata.Nf);
            // Precision read from header might be different if original was 0 (full)
            // C++ code writes actual bits (e.g. 32 for float if prec=0), then reads it back.
            // My header write: encoder.EncodeUniform((uint)metadata.Precision, 7);
            // My header read: Precision = (int)decoder.DecodeUniform(7);
            // So, if metadata.Precision was 0, it's written as 0 and read as 0.
            // The effective precision used internally would be 32.
            Assert.Equal(metadata.Precision, decompressedMetadata.Precision);


            // Verify data
            // For lossless (precision 0 or full bits), data should be identical.
            // For lossy, it should be "close". Defining "close" is tricky.
            // The C++ fpzip ensures that if you compress with precision P, the decompressed values,
            // when re-compressed with precision P, yield the same compressed stream.
            // Or, values are within 2^-(P-1) of original, roughly.

            bool isLossless = precision == 0 || precision == 32;
            if (isLossless)
            {
                Assert.Equal(originalData, decompressedData);
            }
            else
            {
                // For lossy, check if values are reasonably close.
                // One way: map original and decompressed to the lossy precision; their integer forms should match.
                for (int i = 0; i < originalData.Length; i++)
                {
                    uint mappedOriginalLossy = Fpzip.Net.FloatingPoint.FloatingPointMapper.MapFloatToUInt32(originalData[i], precision);
                    uint mappedDecompressedLossy = Fpzip.Net.FloatingPoint.FloatingPointMapper.MapFloatToUInt32(decompressedData[i], precision);
                    Assert.True(mappedOriginalLossy == mappedDecompressedLossy,
                                $"Lossy mismatch at index {i} for test '{testName ?? "Unnamed"}'. Original: {originalData[i]}, Decompressed: {decompressedData[i]}. MappedOrig: {mappedOriginalLossy}, MappedDecomp: {mappedDecompressedLossy}");
                }
            }
        }

        [Fact]
        public void CompressDecompress_Simple1D_Lossless()
        {
            float[] data = { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f };
            CompressDecompressAndVerify(data, 8, 1, 1, 1, 0, "Simple1D_Lossless");
        }

        [Fact]
        public void CompressDecompress_Constant1D_Lossless()
        {
            float[] data = Enumerable.Repeat(123.45f, 100).ToArray();
            CompressDecompressAndVerify(data, 100, 1, 1, 1, 0, "Constant1D_Lossless");
        }

        [Fact]
        public void CompressDecompress_Simple2D_Lossless()
        {
            float[] data = {
                1.1f, 2.2f, 3.3f,
                4.4f, 5.5f, 6.6f,
                7.7f, 8.8f, 9.9f
            };
            CompressDecompressAndVerify(data, 3, 3, 1, 1, 0, "Simple2D_Lossless");
        }

        [Fact]
        public void CompressDecompress_Simple3D_Lossless()
        {
            // 2x2x2 cube
            float[] data = {
                1, 2,  // z=0, y=0
                3, 4,  // z=0, y=1

                5, 6,  // z=1, y=0
                7, 8   // z=1, y=1
            };
            CompressDecompressAndVerify(data, 2, 2, 2, 1, 0, "Simple3D_Lossless");
        }

        [Fact]
        public void CompressDecompress_MultiField_Lossless()
        {
            // 2x1x1, 2 fields (e.g. velocity X and Y components)
            // Field 1: 10, 20
            // Field 2: 100, 200
            // Data layout: 10, 20, 100, 200 (if fields are contiguous)
            // Or: 10, 100, 20, 200 (if interleaved - fpzip expects contiguous fields)
            float[] data = { 10f, 20f, 100f, 200f };
            CompressDecompressAndVerify(data, 2, 1, 1, 2, 0, "MultiField_Lossless");
        }


        [Theory]
        [InlineData(16)] // 16-bit precision
        [InlineData(8)]  // 8-bit precision
        [InlineData(24)] // 24-bit precision
        public void CompressDecompress_Simple1D_Lossy(int precision)
        {
            float[] data = new float[256];
            for(int i=0; i<data.Length; ++i) data[i] = (float)Math.Sin(i * 0.1) * 100f;
            CompressDecompressAndVerify(data, data.Length, 1, 1, 1, precision, $"Simple1D_Lossy_P{precision}");
        }

        [Fact]
        public void CompressDecompress_Zeros_Lossless()
        {
            float[] data = new float[100]; // All zeros
            CompressDecompressAndVerify(data, 10, 10, 1, 1, 0, "Zeros_Lossless");
        }

        [Fact]
        public void CompressDecompress_SmallValues_Lossless()
        {
            float[] data = { 1e-5f, -2e-5f, 3.123e-6f, 0f, 5e-7f};
            CompressDecompressAndVerify(data, data.Length, 1, 1, 1, 0, "SmallValues_Lossless");
        }

        [Fact]
        public void CompressDecompress_LargeValues_Lossless()
        {
            float[] data = { 1e10f, -2e12f, 3.123e15f, 0f, 5e18f};
            CompressDecompressAndVerify(data, data.Length, 1, 1, 1, 0, "LargeValues_Lossless");
        }

        // TODO: Add tests for double precision
        // TODO: Add tests for edge case dimensions (e.g., nx=1, ny=50, nz=1)
        // TODO: Add tests for data that might stress the range coder or QSModel (e.g. highly unpredictable data)
    }
}
