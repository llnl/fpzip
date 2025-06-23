using System;
using System.Runtime.InteropServices; // For BitConverter alternative if needed, but BitConverter is fine
using Fpzip.Net.Common;

namespace Fpzip.Net.FloatingPoint
{
    /// <summary>
    /// Handles the reversible transformation of floating-point numbers
    /// to integer representations based on specified precision, and back.
    /// This is based on the PCmap logic from the C++ fpzip implementation.
    /// </summary>
    internal static class FloatingPointMapper
    {
        private const int FloatTotalBits = 32;
        private const int DoubleTotalBits = 64;

        // --- Float to UInt32 and back ---

        /// <summary>
        /// Converts a float to its UInt32 bit representation.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static uint FloatToUInt32Bits(float value)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            return BitConverter.SingleToUInt32Bits(value);
#else
            // Fallback for older .NET versions if necessary, though project targets .NET 9
            return (uint)BitConverter.SingleToInt32Bits(value);
#endif
        }

        /// <summary>
        /// Converts a UInt32 bit representation back to a float.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static float UInt32BitsToFloat(uint value)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            return BitConverter.UInt32BitsToSingle(value);
#else
            return BitConverter.Int32BitsToSingle((int)value);
#endif
        }

        /// <summary>
        /// Maps a float value to a UInt32 integer representation suitable for compression.
        /// </summary>
        /// <param name="value">The float value to map.</param>
        /// <param name="precision">The number of bits of precision to retain (1-32).
        /// If 0, full precision (32 bits) is used.</param>
        /// <returns>The mapped UInt32 value.</returns>
        public static uint MapFloatToUInt32(float value, int precision)
        {
            if (precision == 0) precision = FloatTotalBits; // Full precision
            if (precision < 1 || precision > FloatTotalBits)
                throw new ArgumentOutOfRangeException(nameof(precision), $"Float precision must be 1-{FloatTotalBits}.");

            uint r = FloatToUInt32Bits(value);

            // Equivalent to PCmap<float, width>::forward
            // const uint bits = width;
            // const uint shift = bitsizeof(Range) - bits; // Domain\Range bits
            // Range r = fcast(d);
            // r = ~r;
            // r >>= shift;
            // r ^= -(r >> (bits - 1)) >> (shift + 1);
            // return r;

            int bits = precision;
            int shift = FloatTotalBits - bits;

            r = ~r;
            r >>= shift; // Truncate LSBs

            // Conditional sign flip based on the MSB of the *remaining* bits
            // -(r >> (bits - 1)) creates a mask (all 1s if MSB is 1, else 0)
            // This mask is then shifted right by (shift + 1)
            // The C++ '-(val)' for unsigned types effectively means '0 - val' which is (~val + 1) or two's complement negative.
            // For a single bit B (0 or 1), -(B) where B is (r >> (bits - 1)) :
            // if B=0, -(0) -> 0.
            // if B=1, -(1) -> all 1s mask (0xFFFFFFFF for uint32).
            uint signBitMask = (r >> (bits - 1)) & 1; // Get the MSB of the truncated 'bits'
            uint conditionalFlipMask = (signBitMask == 1) ? uint.MaxValue : 0; // 0xFFFFFFFF or 0x00000000

            r ^= conditionalFlipMask >> (shift + 1);

            return r;
        }

        /// <summary>
        /// Maps a UInt32 integer representation back to a float value.
        /// </summary>
        /// <param name="mappedValue">The UInt32 value obtained from MapFloatToUInt32.</param>
        /// <param name="precision">The number of bits of precision used during mapping (1-32).
        /// If 0, full precision (32 bits) is assumed.</param>
        /// <returns>The reconstructed float value.</returns>
        public static float MapUInt32ToFloat(uint mappedValue, int precision)
        {
            if (precision == 0) precision = FloatTotalBits; // Full precision
            if (precision < 1 || precision > FloatTotalBits)
                throw new ArgumentOutOfRangeException(nameof(precision), $"Float precision must be 1-{FloatTotalBits}.");

            uint r = mappedValue;

            // Equivalent to PCmap<float, width>::inverse
            // r ^= -(r >> (bits - 1)) >> (shift + 1);
            // r = ~r;
            // r <<= shift;
            // return icast(r);

            int bits = precision;
            int shift = FloatTotalBits - bits;

            uint signBitMask = (r >> (bits - 1)) & 1;
            uint conditionalFlipMask = (signBitMask == 1) ? uint.MaxValue : 0;

            r ^= conditionalFlipMask >> (shift + 1); // Reverse conditional sign flip
            r = ~r; // Reverse bitwise NOT
            r <<= shift; // Restore LSBs (as zeros)

            return UInt32BitsToFloat(r);
        }

        // --- Double to UInt64 and back ---

        /// <summary>
        /// Converts a double to its UInt64 bit representation.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong DoubleToUInt64Bits(double value)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            return BitConverter.DoubleToUInt64Bits(value);
#else
            return (ulong)BitConverter.DoubleToInt64Bits(value);
#endif
        }

        /// <summary>
        /// Converts a UInt64 bit representation back to a double.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static double UInt64BitsToDouble(ulong value)
        {
#if NETCOREAPP || NETSTANDARD2_1_OR_GREATER
            return BitConverter.UInt64BitsToDouble(value);
#else
            return BitConverter.Int64BitsToDouble((long)value);
#endif
        }

        /// <summary>
        /// Maps a double value to a UInt64 integer representation suitable for compression.
        /// </summary>
        /// <param name="value">The double value to map.</param>
        /// <param name="precision">The number of bits of precision to retain (1-64).
        /// If 0, full precision (64 bits) is used.</param>
        /// <returns>The mapped UInt64 value.</returns>
        public static ulong MapDoubleToUInt64(double value, int precision)
        {
            if (precision == 0) precision = DoubleTotalBits; // Full precision
            if (precision < 1 || precision > DoubleTotalBits) // Original C code notes 4-64 for doubles, but map should handle 1-64.
                throw new ArgumentOutOfRangeException(nameof(precision), $"Double precision must be 1-{DoubleTotalBits}.");

            ulong r = DoubleToUInt64Bits(value);

            int bits = precision;
            int shift = DoubleTotalBits - bits;

            r = ~r;
            r >>= shift;

            ulong signBitMask = (r >> (bits - 1)) & 1;
            ulong conditionalFlipMask = (signBitMask == 1) ? ulong.MaxValue : 0;

            r ^= conditionalFlipMask >> (shift + 1);

            return r;
        }

        /// <summary>
        /// Maps a UInt64 integer representation back to a double value.
        /// </summary>
        /// <param name="mappedValue">The UInt64 value obtained from MapDoubleToUInt64.</param>
        /// <param name="precision">The number of bits of precision used during mapping (1-64).
        /// If 0, full precision (64 bits) is assumed.</param>
        /// <returns>The reconstructed double value.</returns>
        public static double MapUInt64ToDouble(ulong mappedValue, int precision)
        {
            if (precision == 0) precision = DoubleTotalBits; // Full precision
            if (precision < 1 || precision > DoubleTotalBits)
                throw new ArgumentOutOfRangeException(nameof(precision), $"Double precision must be 1-{DoubleTotalBits}.");

            ulong r = mappedValue;

            int bits = precision;
            int shift = DoubleTotalBits - bits;

            ulong signBitMask = (r >> (bits - 1)) & 1;
            ulong conditionalFlipMask = (signBitMask == 1) ? ulong.MaxValue : 0;

            r ^= conditionalFlipMask >> (shift + 1);
            r = ~r;
            r <<= shift;

            return UInt64BitsToDouble(r);
        }
    }
}
