using System;
using Fpzip.Net.Common;
using Fpzip.Net.FloatingPoint;
using Fpzip.Net.Io; // Required for RangeCoder when it's defined
using Fpzip.Net.RangeCoder; // Placeholder for actual RangeCoder classes

namespace Fpzip.Net.Prediction
{
    /// <summary>
    /// Encodes and decodes residuals (differences between mapped actual values and predicted values).
    /// This class incorporates logic from the C++ PCencoder and PCdecoder classes.
    /// </summary>
    internal static class ResidualCodec
    {
        // --- Encoding ---

        public static void EncodeResidual(
            uint actualMappedValue,
            uint predictedMappedValue,
            int precision,
            IRangeEncoder rcEncoder, // Interface to be defined with RangeCoder
            IRangeModel model)       // Interface to be defined with RangeCoder
        {
            if (precision == 0) precision = 32;

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits) // Narrow case
            {
                // const uint bias = (1 << M::bits) - 1;
                // re->encode(static_cast<uint>(bias + r - p), rm[context]);
                uint bits = (uint)precision;
                uint bias = (1u << (int)bits) - 1u;
                long diff = (long)actualMappedValue - predictedMappedValue; // Use long to handle potential negative intermediate
                uint symbol = (uint)(bias + diff);
                rcEncoder.Encode(symbol, model);
            }
            else // Wide case
            {
                // const uint bias = M::bits;
                // if (p < r) { U d = r - p; uint k = PC::bsr(d); re->encode(bias + 1 + k, rm[context]); re->encode(d - (U(1) << k), k); }
                // else if (p > r) { U d = p - r; uint k = PC::bsr(d); re->encode(bias - 1 - k, rm[context]); re->encode(d - (U(1) << k), k); }
                // else { re->encode(bias, rm[context]); }
                uint bias = (uint)precision; // M::bits is precision here

                if (predictedMappedValue < actualMappedValue) // Underprediction: diff > 0
                {
                    uint diff = actualMappedValue - predictedMappedValue;
                    int k = NumericUtils.BitScanReverse(diff); // k = log2(diff)
                    rcEncoder.Encode(bias + 1 + (uint)k, model);
                    rcEncoder.EncodeRaw(diff - (1u << k), k); // Encode k bits directly
                }
                else if (predictedMappedValue > actualMappedValue) // Overprediction: diff < 0
                {
                    uint diff = predictedMappedValue - actualMappedValue;
                    int k = NumericUtils.BitScanReverse(diff);
                    rcEncoder.Encode(bias - 1 - (uint)k, model);
                    rcEncoder.EncodeRaw(diff - (1u << k), k); // Encode k bits directly
                }
                else // Perfect prediction: diff == 0
                {
                    rcEncoder.Encode(bias, model);
                }
            }
        }

        public static void EncodeResidual(
            ulong actualMappedValue,
            ulong predictedMappedValue,
            int precision,
            IRangeEncoder rcEncoder,
            IRangeModel model)
        {
            if (precision == 0) precision = 64;

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits) // Narrow case
            {
                uint bits = (uint)precision;
                ulong bias = (1ul << (int)bits) - 1ul;
                long diff = (long)actualMappedValue - (long)predictedMappedValue; // Use long for difference
                ulong symbol = (ulong)((long)bias + diff); // Ensure positive symbol
                rcEncoder.Encode((uint)symbol, model); // Assuming model/encoder takes uint for small values
            }
            else // Wide case
            {
                uint bias = (uint)precision;

                if (predictedMappedValue < actualMappedValue) // Underprediction
                {
                    ulong diff = actualMappedValue - predictedMappedValue;
                    int k = NumericUtils.BitScanReverse(diff);
                    rcEncoder.Encode(bias + 1 + (uint)k, model);
                    rcEncoder.EncodeRaw64(diff - (1ul << k), k);
                }
                else if (predictedMappedValue > actualMappedValue) // Overprediction
                {
                    ulong diff = predictedMappedValue - actualMappedValue;
                    int k = NumericUtils.BitScanReverse(diff);
                    rcEncoder.Encode(bias - 1 - (uint)k, model);
                    rcEncoder.EncodeRaw64(diff - (1ul << k), k);
                }
                else // Perfect prediction
                {
                    rcEncoder.Encode(bias, model);
                }
            }
        }

        // --- Decoding ---

        public static uint DecodeResidual(
            uint predictedMappedValue,
            int precision,
            IRangeDecoder rcDecoder, // Interface to be defined
            IRangeModel model)      // Interface to be defined
        {
            if (precision == 0) precision = 32;

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits) // Narrow case
            {
                // U r = p + rd->decode(rm[context]) - bias;
                uint bits = (uint)precision;
                uint bias = (1u << (int)bits) - 1u;
                uint symbol = rcDecoder.Decode(model);
                // Need to handle potential negative result from (symbol - bias) carefully before adding to p
                long diff = (long)symbol - bias;
                return (uint)((long)predictedMappedValue + diff);
            }
            else // Wide case
            {
                // uint s = rd->decode(rm[context]);
                // if (s > bias) { uint k = s - bias - 1; U d = (U(1) << k) + rd->template decode<U>(k); r = p + d; }
                // else if (s < bias) { uint k = bias - 1 - s; U d = (U(1) << k) + rd->template decode<U>(k); r = p - d; }
                // else { /* perfect prediction, r = p, but map.identity(pred) is used */ }
                uint bias = (uint)precision;
                uint s = rcDecoder.Decode(model);

                if (s > bias) // Underprediction
                {
                    int k = (int)(s - bias - 1);
                    uint diff_lower = rcDecoder.DecodeRaw(k);
                    uint diff = (1u << k) + diff_lower;
                    return predictedMappedValue + diff;
                }
                else if (s < bias) // Overprediction
                {
                    int k = (int)(bias - 1 - s);
                    uint diff_lower = rcDecoder.DecodeRaw(k);
                    uint diff = (1u << k) + diff_lower;
                    // Check for underflow before subtraction if predictedMappedValue is small and diff is large
                    if (predictedMappedValue < diff)
                        throw new FpzipException(FpzipError.DecompressionError, "Decompression error: residual subtraction would underflow.");
                    return predictedMappedValue - diff;
                }
                else // Perfect prediction
                {
                    return predictedMappedValue; // The map.identity(pred) is handled by caller by re-mapping this.
                }
            }
        }

        public static ulong DecodeResidual(
            ulong predictedMappedValue,
            int precision,
            IRangeDecoder rcDecoder,
            IRangeModel model)
        {
            if (precision == 0) precision = 64;

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits) // Narrow case
            {
                uint bits = (uint)precision;
                ulong bias = (1ul << (int)bits) - 1ul;
                uint symbol_uint = rcDecoder.Decode(model); // Assuming model/decoder gives uint for small values
                ulong symbol = symbol_uint;
                long diff = (long)symbol - (long)bias;
                return (ulong)((long)predictedMappedValue + diff);

            }
            else // Wide case
            {
                uint bias = (uint)precision;
                uint s = rcDecoder.Decode(model);

                if (s > bias) // Underprediction
                {
                    int k = (int)(s - bias - 1);
                    ulong diff_lower = rcDecoder.DecodeRaw64(k);
                    ulong diff = (1ul << k) + diff_lower;
                    return predictedMappedValue + diff;
                }
                else if (s < bias) // Overprediction
                {
                    int k = (int)(bias - 1 - s);
                    ulong diff_lower = rcDecoder.DecodeRaw64(k);
                    ulong diff = (1ul << k) + diff_lower;
                     if (predictedMappedValue < diff)
                        throw new FpzipException(FpzipError.DecompressionError, "Decompression error: residual subtraction would underflow.");
                    return predictedMappedValue - diff;
                }
                else // Perfect prediction
                {
                    return predictedMappedValue;
                }
            }
        }

        // --- Symbol count calculation ---
        // This is needed by the RangeModel to know its alphabet size.

        public static uint GetSymbolCount(int precision)
        {
            if (precision == 0) precision = 32; // Default for float if 0 is passed, can be context-dependent

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits)
            {
                // static const uint symbols = 2 * (1 << M::bits) - 1;
                return (2u * (1u << precision)) - 1u;
            }
            else
            {
                // static const uint symbols = 2 * M::bits + 1;
                return (2u * (uint)precision) + 1u;
            }
        }
         public static uint GetSymbolCount64(int precision) // For double context
        {
            if (precision == 0) precision = 64;

            if (precision <= PredictionConstants.MaxDirectlyEncodableBits)
            {
                // This case is tricky for 64-bit if M::bits is small.
                // The C++ code uses M::bits which is the *actual data precision*.
                // If a double has low precision (e.g. 8 bits), it still uses the narrow path.
                return (2u * (1u << precision)) - 1u;
            }
            else
            {
                return (2u * (uint)precision) + 1u;
            }
        }
    }

    // Placeholder interfaces for RangeCoder components.
    // These will be defined properly in the RangeCoder step.
    // TODO: Move these to a proper RangeCoder namespace/file structure when implementing RangeCoder.
    public interface IRangeEncoder
    {
        void Encode(uint symbol, IRangeModel model);
        void EncodeRaw(uint value, int bits); // For direct bit encoding in wide mode
        void EncodeRaw64(ulong value, int bits); // For direct bit encoding in wide mode for doubles
        void Flush();
    }

    public interface IRangeDecoder
    {
        uint Decode(IRangeModel model);
        uint DecodeRaw(int bits); // For direct bit decoding in wide mode
        ulong DecodeRaw64(int bits); // For direct bit decoding in wide mode for doubles
    }

    public interface IRangeModel
    {
        uint GetSymbolCount();
        // Other model methods like UpdateModel, GetProbability, etc.
    }
}
