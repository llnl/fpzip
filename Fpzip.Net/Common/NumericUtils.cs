using System;
using System.Numerics; // For BitOperations

namespace Fpzip.Net.Common
{
    internal static class NumericUtils
    {
        /// <summary>
        /// Bit Scan Reverse: finds the 0-based index of the most significant bit (MSB).
        /// Equivalent to floor(log2(value)).
        /// </summary>
        /// <param name="value">The value to scan. Must be non-zero.</param>
        /// <returns>The 0-based index of the MSB.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If value is zero.</exception>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static int BitScanReverse(uint value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Value cannot be zero for BitScanReverse (Log2).");
            return BitOperations.Log2(value);
        }

        /// <summary>
        /// Bit Scan Reverse: finds the 0-based index of the most significant bit (MSB).
        /// Equivalent to floor(log2(value)).
        /// </summary>
        /// <param name="value">The value to scan. Must be non-zero.</param>
        /// <returns>The 0-based index of the MSB.</returns>
        /// <exception cref="ArgumentOutOfRangeException">If value is zero.</exception>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static int BitScanReverse(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Value cannot be zero for BitScanReverse (Log2).");
            return BitOperations.Log2(value);
        }
    }
}
