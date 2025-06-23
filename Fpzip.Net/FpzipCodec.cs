using System;
using System.IO;
using Fpzip.Net.Common;
using Fpzip.Net.FloatingPoint;
using Fpzip.Net.Io;
using Fpzip.Net.Prediction;
using Fpzip.Net.RangeCoder;

namespace Fpzip.Net
{
    /// <summary>
    /// Provides static methods for compressing and decompressing N-dimensional floating-point data arrays
    /// using a .NET port of the fpzip algorithm (specifically targeting the FP_SAFE mode of original fpzip).
    /// </summary>
    /// <remarks>
    /// fpzip is a compression algorithm optimized for 2D, 3D, or 4D arrays of single or double-precision
    /// floating-point values that exhibit spatial correlation (e.g., scientific simulation data, sensor readings).
    /// It supports both lossless and lossy compression by specifying the number of bits of precision to retain.
    ///
    /// This implementation aims to be compatible with the core fpzip format for data compressed
    /// using the FP_SAFE floating-point mode.
    ///
    /// Usage example for compression:
    /// <code>
    /// float[]myData = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
    /// FpzipMetadata metadata = new FpzipMetadata
    /// {
    ///     Type = FpzipConstants.TypeFloat,
    ///     Precision = 0, // Lossless
    ///     Nx = 4, Ny = 1, Nz = 1, Nf = 1
    /// };
    /// using (MemoryStream compressedStream = new MemoryStream())
    /// {
    ///     FpzipCodec.Compress(metadata, myData, compressedStream);
    ///     byte[] compressedBytes = compressedStream.ToArray();
    ///     // Store compressedBytes
    /// }
    /// </code>
    ///
    /// Usage example for decompression:
    /// <code>
    /// byte[] compressedBytes = GetCompressedData(); // Load your compressed data
    /// using (MemoryStream inputStream = new MemoryStream(compressedBytes))
    /// {
    ///     // First, decompress to determine metadata which includes array size
    ///     // Option 1: Decompress into a dynamically sized list first (if size unknown)
    ///     // FpzipMetadata metadata = FpzipCodec.Decompress(inputStream, temporaryResizableArray);
    ///     // Then allocate exact span and decompress again (or copy).
    ///
    ///     // Option 2: If you can peek/pre-read metadata (not directly supported by this API surface,
    ///     // but a stream wrapper could do it), or if you stored metadata separately.
    ///     // For this example, assume we will decompress once to get metadata, then again for data.
    ///     // This is not the most efficient for very large data if you can't pre-allocate.
    ///     // A more advanced API might return metadata first or allow a callback for allocation.
    ///
    ///     // Simple case: Decompress to a sufficiently large buffer, then use metadata to know actual size.
    ///     // This example assumes you know an upper bound or will resize.
    ///     // For robust applications, consider how to handle unknown output sizes.
    ///     // One common pattern is to read the header separately if possible.
    ///     // Here, we'll just demonstrate the direct decompress which returns metadata.
    ///
    ///     float[] outputArray = new float[MAX_EXPECTED_SIZE]; // Or determine size from header first
    ///     FpzipMetadata readMetadata = FpzipCodec.Decompress(inputStream, outputArray);
    ///     // Now outputArray contains 'readMetadata.GetTotalNumberOfValues()' elements.
    ///     // You might copy to a perfectly sized array:
    ///     // float[] finalData = new Span&lt;float&gt;(outputArray, 0, (int)readMetadata.GetTotalNumberOfValues()).ToArray();
    /// }
    /// </code>
    /// </remarks>
    public static class FpzipCodec
    {
        private const int HeaderMagic4thByte = '\0'; // As per C++ write.cpp

        /// <summary>
        /// Writes the fpzip header to the stream using the range encoder.
        /// </summary>
        private static void WriteHeader(FpzipMetadata metadata, IRangeEncoder encoder)
        {
            // Magic: 'f', 'p', 'z', '\0'
            encoder.EncodeUniform(FpzipConstants.Magic1, 8);
            encoder.EncodeUniform(FpzipConstants.Magic2, 8);
            encoder.EncodeUniform(FpzipConstants.Magic3, 8);
            encoder.EncodeUniform(HeaderMagic4thByte, 8);

            // Format version: FPZ_MAJ_VERSION (16 bits), FPZ_MIN_VERSION (8 bits)
            // FPZ_MIN_VERSION depends on FPZIP_FP mode. We use FpModeSafe.
            uint majorVersion = FpzipConstants.CodecVersion >> 8; // Top part of CodecVersion constant
            uint minorVersion = FpzipConstants.FpModeSafe;       // Lower part, specific to FP_SAFE
            encoder.EncodeUniform(majorVersion, 16);
            encoder.EncodeUniform(minorVersion, 8);

            // Type and precision
            encoder.EncodeUniform((uint)metadata.Type, 1);
            encoder.EncodeUniform((uint)metadata.Precision, 7);

            // Array dimensions
            encoder.EncodeUniform((uint)metadata.Nx, 32);
            encoder.EncodeUniform((uint)metadata.Ny, 32);
            encoder.EncodeUniform((uint)metadata.Nz, 32);
            encoder.EncodeUniform((uint)metadata.Nf, 32);
        }

        /// <summary>
        /// Reads the fpzip header from the stream using the range decoder.
        /// </summary>
        private static FpzipMetadata ReadHeader(IRangeDecoder decoder)
        {
            byte m1 = (byte)decoder.DecodeUniform(8);
            byte m2 = (byte)decoder.DecodeUniform(8);
            byte m3 = (byte)decoder.DecodeUniform(8);
            byte m4 = (byte)decoder.DecodeUniform(8);

            if (m1 != FpzipConstants.Magic1 || m2 != FpzipConstants.Magic2 || m3 != FpzipConstants.Magic3 || m4 != HeaderMagic4thByte)
                throw new FpzipException(FpzipError.BadFormat, "Invalid fpzip magic number.");

            uint majorVersion = decoder.DecodeUniform(16);
            uint minorVersion = decoder.DecodeUniform(8);

            // Check version (allow current FP_SAFE version)
            uint expectedMajor = FpzipConstants.CodecVersion >> 8;
            uint expectedMinor = FpzipConstants.FpModeSafe;
            if (majorVersion != expectedMajor || minorVersion != expectedMinor)
                throw new FpzipException(FpzipError.BadVersion, $"Unsupported fpzip version. Got {majorVersion}.{minorVersion}, expected {expectedMajor}.{expectedMinor}.");

            var metadata = new FpzipMetadata
            {
                Type = (int)decoder.DecodeUniform(1),
                Precision = (int)decoder.DecodeUniform(7),
                Nx = (int)decoder.DecodeUniform(32),
                Ny = (int)decoder.DecodeUniform(32),
                Nz = (int)decoder.DecodeUniform(32),
                Nf = (int)decoder.DecodeUniform(32)
            };

            if (!metadata.Validate(out string? errorMsg))
                throw new FpzipException(FpzipError.InvalidMetadata, $"Invalid metadata from header: {errorMsg}");

            return metadata;
        }


        /// <summary>
        /// Compresses a single-precision floating-point data array.
        /// </summary>
        /// <param name="metadata">Metadata describing the array. Must be validated by caller. Type must be TypeFloat.</param>
        /// <param name="inputData">The uncompressed float data. Length must match metadata total elements.</param>
        /// <param name="outputStream">The stream to write compressed data to.</param>
        /// <param name="leaveOpen">True to leave the outputStream open after compression; otherwise, false.</param>
        /// <exception cref="FpzipException">If metadata is invalid or an error occurs during compression.</exception>
        /// <exception cref="ArgumentException">If inputData length does not match metadata, or streams are invalid.</exception>
        public static void Compress(FpzipMetadata metadata, ReadOnlySpan<float> inputData, Stream outputStream, bool leaveOpen = false)
        {
            if (!metadata.Validate(out string? errorMsg))
                throw new FpzipException(FpzipError.InvalidMetadata, errorMsg);
            if (metadata.Type != FpzipConstants.TypeFloat)
                throw new FpzipException(FpzipError.InvalidMetadata, "Metadata type is not float.");
            if (inputData.Length < metadata.GetTotalNumberOfValues())
                throw new ArgumentException("Input data is smaller than specified by metadata dimensions.", nameof(inputData));

            using var bitWriter = new BitStreamWriter(outputStream, leaveOpen);
            using var rangeEncoder = new RangeEncoder(bitWriter); // Assuming RangeEncoder implements IRangeEncoder and IDisposable

            WriteHeader(metadata, rangeEncoder);

            int dataIndex = 0;
            int prec = metadata.Precision == 0 ? 32 : metadata.Precision;

            for (int field = 0; field < metadata.Nf; field++)
            {
                // Symbol count depends on precision AND whether it's narrow/wide path.
                // ResidualCodec.GetSymbolCount handles this.
                uint numSymbols = ResidualCodec.GetSymbolCount(prec);
                using var qsModel = new RangeCoderQsModel(true, numSymbols); // true for compression

                var front = new Front<float>( (uint)metadata.Nx, (uint)metadata.Ny);

                for (int z = 0; z < metadata.Nz; z++)
                {
                    front.Advance(0, 0, 1); // Advance for new plane
                    for (int y = 0; y < metadata.Ny; y++)
                    {
                        front.Advance(0, 1, 0); // Advance for new row
                        for (int x = 0; x < metadata.Nx; x++)
                        {
                            front.Advance(1, 0, 0); // Advance for new element

                            // Lorenzo Predictor
                            float p = front.GetNeighbor(1, 1, 1) +
                                      front.GetNeighbor(1, 0, 0) - front.GetNeighbor(0, 1, 1) +
                                      front.GetNeighbor(0, 1, 0) - front.GetNeighbor(1, 0, 1) +
                                      front.GetNeighbor(0, 0, 1) - front.GetNeighbor(1, 1, 0);

                            float actualValue = inputData[dataIndex++];

                            uint mappedActual = FloatingPointMapper.MapFloatToUInt32(actualValue, prec);
                            uint mappedPredicted = FloatingPointMapper.MapFloatToUInt32(p, prec);

                            ResidualCodec.EncodeResidual(mappedActual, mappedPredicted, prec, rangeEncoder, qsModel);

                            // Value to push to front is after precision loss (identity map)
                            // float valueForFront = FloatingPointMapper.MapUInt32ToFloat(mappedActual, prec);
                            // More accurately, from C++ fe->encode returns map.inverse(map.forward(a)).
                            // So, it's the actual value, but as if it was truncated by precision.
                            uint truncatedMappedActual = FloatingPointMapper.MapFloatToUInt32(actualValue, prec); // map.forward(a)
                            float valueForFront = FloatingPointMapper.MapUInt32ToFloat(truncatedMappedActual, prec); // map.inverse(...)

                            front.Push(valueForFront);
                        }
                    }
                }
            }
            rangeEncoder.Finish();
        }

        /// <summary>
        /// Decompresses fpzip-compressed data into a float array.
        /// </summary>
        /// <param name="inputStream">Stream containing compressed data.</param>
        /// <param name="outputData">Span to write decompressed float data to. Length must be sufficient for metadata total elements.</param>
        /// <param name="leaveOpen">True to leave the inputStream open after decompression; otherwise, false.</param>
        /// <returns>Metadata read from the stream.</returns>
        /// <exception cref="FpzipException">If an error occurs during decompression or header parsing.</exception>
        /// <exception cref="ArgumentException">If outputData is too small, or streams are invalid.</exception>
        public static FpzipMetadata Decompress(Stream inputStream, Span<float> outputData, bool leaveOpen = false)
        {
            using var bitReader = new BitStreamReader(inputStream, leaveOpen);
            using var rangeDecoder = new RangeDecoder(bitReader); // Assuming RangeDecoder implements IRangeDecoder and IDisposable

            rangeDecoder.Init();

            FpzipMetadata metadata = ReadHeader(rangeDecoder);

            if (metadata.Type != FpzipConstants.TypeFloat)
                throw new FpzipException(FpzipError.InvalidMetadata, "Stream metadata type is not float.");
            if (outputData.Length < metadata.GetTotalNumberOfValues())
                throw new ArgumentException("Output data span is smaller than specified by metadata dimensions.", nameof(outputData));

            int dataIndex = 0;
            int prec = metadata.Precision == 0 ? 32 : metadata.Precision;

            for (int field = 0; field < metadata.Nf; field++)
            {
                uint numSymbols = ResidualCodec.GetSymbolCount(prec);
                using var qsModel = new RangeCoderQsModel(false, numSymbols); // false for decompression

                var front = new Front<float>((uint)metadata.Nx, (uint)metadata.Ny);

                for (int z = 0; z < metadata.Nz; z++)
                {
                    front.Advance(0, 0, 1);
                    for (int y = 0; y < metadata.Ny; y++)
                    {
                        front.Advance(0, 1, 0);
                        for (int x = 0; x < metadata.Nx; x++)
                        {
                            front.Advance(1, 0, 0);

                            float p_float = front.GetNeighbor(1, 1, 1) +
                                            front.GetNeighbor(1, 0, 0) - front.GetNeighbor(0, 1, 1) +
                                            front.GetNeighbor(0, 1, 0) - front.GetNeighbor(1, 0, 1) +
                                            front.GetNeighbor(0, 0, 1) - front.GetNeighbor(1, 1, 0);

                            uint mappedPredicted = FloatingPointMapper.MapFloatToUInt32(p_float, prec);

                            uint mappedActual = ResidualCodec.DecodeResidual(mappedPredicted, prec, rangeDecoder, qsModel);

                            float actualValue;
                            if (mappedActual == mappedPredicted && ResidualCodec.IsPerfectPredictionSymbol(prec, rangeDecoder, qsModel)) // Pseudo-code for checking if perfect pred symbol was decoded
                            {
                                // C++: if perfect prediction, return map.identity(pred);
                                // map.identity(p_float) = MapUInt32ToFloat( MapFloatToUInt32(p_float, prec), prec )
                                uint truncatedMappedPredicted = FloatingPointMapper.MapFloatToUInt32(p_float, prec);
                                actualValue = FloatingPointMapper.MapUInt32ToFloat(truncatedMappedPredicted, prec);
                            }
                            else
                            {
                                actualValue = FloatingPointMapper.MapUInt32ToFloat(mappedActual, prec);
                            }

                            outputData[dataIndex++] = actualValue;
                            front.Push(actualValue);
                        }
                    }
                }
            }
            return metadata;
        }

        // Helper method to check for perfect prediction symbol (pseudo-code, needs actual implementation detail from ResidualCodec)
        // This was based on a thought process, but ResidualCodec's wide mode already returns predictedMappedValue
        // for perfect predictions, which when mapped back to float IS map.identity(pred).
        // So this helper might not be needed if ResidualCodec is correctly implemented.
        // private static bool IsPerfectPredictionSymbol_Placeholder(int precision, IRangeDecoder decoder, IRangeModel model)
        // {
        //     // This is complex. The "symbol" for perfect prediction depends on narrow/wide mode.
        //     // Narrow: bias. Wide: bias.
        //     // The currently decoded symbol would need to be inspected, which is internal to DecodeResidual.
        //     // For now, this logic is simplified in Decompress method.
        //     return false;
        // }


        /// <summary>
        /// Compresses a double-precision floating-point data array.
        /// </summary>
        /// <param name="metadata">Metadata describing the array. Must be validated by caller. Type must be TypeDouble.</param>
        /// <param name="inputData">The uncompressed double data. Length must match metadata total elements.</param>
        /// <param name="outputStream">The stream to write compressed data to.</param>
        /// <param name="leaveOpen">True to leave the outputStream open after compression; otherwise, false.</param>
        /// <exception cref="FpzipException">If metadata is invalid or an error occurs during compression.</exception>
        /// <exception cref="ArgumentException">If inputData length does not match metadata, or streams are invalid.</exception>
        public static void Compress(FpzipMetadata metadata, ReadOnlySpan<double> inputData, Stream outputStream, bool leaveOpen = false)
        {
            if (!metadata.Validate(out string? errorMsg))
                throw new FpzipException(FpzipError.InvalidMetadata, errorMsg);
            if (metadata.Type != FpzipConstants.TypeDouble)
                throw new FpzipException(FpzipError.InvalidMetadata, "Metadata type is not double.");
            if (inputData.Length < metadata.GetTotalNumberOfValues())
                throw new ArgumentException("Input data is smaller than specified by metadata dimensions.", nameof(inputData));

            using var bitWriter = new BitStreamWriter(outputStream, leaveOpen);
            using var rangeEncoder = new RangeEncoder(bitWriter);

            WriteHeader(metadata, rangeEncoder);

            int dataIndex = 0;
            int prec = metadata.Precision == 0 ? 64 : metadata.Precision;

            for (int field = 0; field < metadata.Nf; field++)
            {
                uint numSymbols = ResidualCodec.GetSymbolCount64(prec); // Use 64-bit version for symbol count
                using var qsModel = new RangeCoderQsModel(true, numSymbols);

                var front = new Front<double>((uint)metadata.Nx, (uint)metadata.Ny);

                for (int z = 0; z < metadata.Nz; z++)
                {
                    front.Advance(0, 0, 1);
                    for (int y = 0; y < metadata.Ny; y++)
                    {
                        front.Advance(0, 1, 0);
                        for (int x = 0; x < metadata.Nx; x++)
                        {
                            front.Advance(1, 0, 0);

                            double p = front.GetNeighbor(1, 1, 1) +
                                       front.GetNeighbor(1, 0, 0) - front.GetNeighbor(0, 1, 1) +
                                       front.GetNeighbor(0, 1, 0) - front.GetNeighbor(1, 0, 1) +
                                       front.GetNeighbor(0, 0, 1) - front.GetNeighbor(1, 1, 0);

                            double actualValue = inputData[dataIndex++];

                            ulong mappedActual = FloatingPointMapper.MapDoubleToUInt64(actualValue, prec);
                            ulong mappedPredicted = FloatingPointMapper.MapDoubleToUInt64(p, prec);

                            ResidualCodec.EncodeResidual(mappedActual, mappedPredicted, prec, rangeEncoder, qsModel);

                            ulong truncatedMappedActual = FloatingPointMapper.MapDoubleToUInt64(actualValue, prec);
                            double valueForFront = FloatingPointMapper.MapUInt64ToDouble(truncatedMappedActual, prec);

                            front.Push(valueForFront);
                        }
                    }
                }
            }
            rangeEncoder.Finish();
        }

        /// <summary>
        /// Decompresses fpzip-compressed data into a double array.
        /// </summary>
        /// <param name="inputStream">Stream containing compressed data.</param>
        /// <param name="outputData">Span to write decompressed double data to. Length must be sufficient for metadata total elements.</param>
        /// <param name="leaveOpen">True to leave the inputStream open after decompression; otherwise, false.</param>
        /// <returns>Metadata read from the stream.</returns>
        /// <exception cref="FpzipException">If an error occurs during decompression or header parsing.</exception>
        /// <exception cref="ArgumentException">If outputData is too small, or streams are invalid.</exception>
        public static FpzipMetadata Decompress(Stream inputStream, Span<double> outputData, bool leaveOpen = false)
        {
            using var bitReader = new BitStreamReader(inputStream, leaveOpen);
            using var rangeDecoder = new RangeDecoder(bitReader);

            rangeDecoder.Init();

            FpzipMetadata metadata = ReadHeader(rangeDecoder);

            if (metadata.Type != FpzipConstants.TypeDouble)
                throw new FpzipException(FpzipError.InvalidMetadata, "Stream metadata type is not double.");
            if (outputData.Length < metadata.GetTotalNumberOfValues())
                throw new ArgumentException("Output data span is smaller than specified by metadata dimensions.", nameof(outputData));

            int dataIndex = 0;
            int prec = metadata.Precision == 0 ? 64 : metadata.Precision;

            for (int field = 0; field < metadata.Nf; field++)
            {
                uint numSymbols = ResidualCodec.GetSymbolCount64(prec); // Use 64-bit version
                using var qsModel = new RangeCoderQsModel(false, numSymbols);

                var front = new Front<double>((uint)metadata.Nx, (uint)metadata.Ny);

                for (int z = 0; z < metadata.Nz; z++)
                {
                    front.Advance(0, 0, 1);
                    for (int y = 0; y < metadata.Ny; y++)
                    {
                        front.Advance(0, 1, 0);
                        for (int x = 0; x < metadata.Nx; x++)
                        {
                            front.Advance(1, 0, 0);

                            double p_double = front.GetNeighbor(1, 1, 1) +
                                             front.GetNeighbor(1, 0, 0) - front.GetNeighbor(0, 1, 1) +
                                             front.GetNeighbor(0, 1, 0) - front.GetNeighbor(1, 0, 1) +
                                             front.GetNeighbor(0, 0, 1) - front.GetNeighbor(1, 1, 0);

                            ulong mappedPredicted = FloatingPointMapper.MapDoubleToUInt64(p_double, prec);
                            ulong mappedActual = ResidualCodec.DecodeResidual(mappedPredicted, prec, rangeDecoder, qsModel);

                            // If ResidualCodec.DecodeResidual returns mappedPredicted for perfect prediction,
                            // then mapping it back IS map.identity(pred).
                            double actualValue = FloatingPointMapper.MapUInt64ToDouble(mappedActual, prec);

                            outputData[dataIndex++] = actualValue;
                            front.Push(actualValue);
                        }
                    }
                }
            }
            return metadata;
        }
    }
}
