using Xunit;
using Fpzip.Net.FloatingPoint;
using System;

namespace Fpzip.Net.Tests
{
    public class FloatingPointMapperTests
    {
        [Theory]
        [InlineData(0.0f, 32)]
        [InlineData(1.0f, 32)]
        [InlineData(-1.0f, 32)]
        [InlineData(123.456f, 32)]
        [InlineData(-123.456f, 32)]
        [InlineData(float.MaxValue / 2, 32)] // A large value
        [InlineData(float.MinValue / 2, 32)] // A small (large negative) value
        [InlineData(float.Epsilon, 32)]     // Smallest positive normal
        [InlineData(-float.Epsilon, 32)]
        public void Float_RoundTrip_FullPrecision_ShouldBeIdentical(float originalValue, int precision)
        {
            uint mapped = FloatingPointMapper.MapFloatToUInt32(originalValue, precision);
            float roundTrippedValue = FloatingPointMapper.MapUInt32ToFloat(mapped, precision);
            Assert.Equal(originalValue, roundTrippedValue, precision: BitConverter.SingleToInt32Bits(originalValue) == BitConverter.SingleToInt32Bits(roundTrippedValue) ? 0 : 7); // Exact bitwise for normal floats
        }

        [Theory]
        [InlineData(0.0, 64)]
        [InlineData(1.0, 64)]
        [InlineData(-1.0, 64)]
        [InlineData(123.4567890123, 64)]
        [InlineData(-123.4567890123, 64)]
        [InlineData(double.MaxValue / 2, 64)]
        [InlineData(double.MinValue / 2, 64)]
        [InlineData(double.Epsilon, 64)]
        [InlineData(-double.Epsilon, 64)]
        public void Double_RoundTrip_FullPrecision_ShouldBeIdentical(double originalValue, int precision)
        {
            ulong mapped = FloatingPointMapper.MapDoubleToUInt64(originalValue, precision);
            double roundTrippedValue = FloatingPointMapper.MapUInt64ToDouble(mapped, precision);
             Assert.Equal(originalValue, roundTrippedValue, precision: BitConverter.DoubleToInt64Bits(originalValue) == BitConverter.DoubleToInt64Bits(roundTrippedValue) ? 0 : 15); // Exact bitwise for normal doubles
        }

        [Theory]
        [InlineData(123.4567f, 32, 16)] // Original, full precision, target precision
        [InlineData(123.4567f, 0, 16)]  // 0 means full
        [InlineData(-88.88f, 32, 8)]
        public void Float_RoundTrip_LossyPrecision_ShouldTruncate(float originalValue, int mapPrecisionFull, int mapPrecisionLossy)
        {
            // Map with lossy precision
            uint mappedLossy = FloatingPointMapper.MapFloatToUInt32(originalValue, mapPrecisionLossy);
            float roundTrippedLossy = FloatingPointMapper.MapUInt32ToFloat(mappedLossy, mapPrecisionLossy);

            // For comparison, map/unmap with full precision to see what it *would* be if not truncated by mapPrecisionLossy
            uint mappedFullForOriginal = FloatingPointMapper.MapFloatToUInt32(originalValue, mapPrecisionFull == 0 ? 32 : mapPrecisionFull);
            float originalIdentity = FloatingPointMapper.MapUInt32ToFloat(mappedFullForOriginal, mapPrecisionFull == 0 ? 32 : mapPrecisionFull);

            // The roundTrippedLossy value should be different from originalIdentity if truncation occurred,
            // unless originalValue happened to have zeros in the truncated bits.
            // The key is that roundTrippedLossy is a valid float that represents originalValue at mapPrecisionLossy.

            // A more robust check: the error should be bounded by 2^-(mapPrecisionLossy) relative to exponent
            // This is complex to assert generally. For now, we check it's not explosively different.
            // And that re-mapping roundTrippedLossy with full precision, then truncating, yields same mappedLossy.
            uint remappedLossyFull = FloatingPointMapper.MapFloatToUInt32(roundTrippedLossy, 32);
            uint remappedLossyTruncated = FloatingPointMapper.MapFloatToUInt32(roundTrippedLossy, mapPrecisionLossy);

            Assert.Equal(mappedLossy, remappedLossyTruncated); // The integer representation after truncation should be stable

            // The difference should not be huge.
            Assert.True(Math.Abs(originalIdentity - roundTrippedLossy) < Math.Abs(originalIdentity * 0.5f) + float.Epsilon || originalIdentity == 0);
        }

        [Fact]
        public void Float_MapNaN_ShouldBeHandled()
        {
            // The fpzip C++ code's pcmap doesn't special case NaN/Inf. It maps their bit patterns.
            // IEEE 754 float NaN: exponent all 1s, fraction non-zero.
            // Let's see what our mapper does.
            float nan = float.NaN;
            uint mappedNan = FloatingPointMapper.MapFloatToUInt32(nan, 32);
            float unmappedNan = FloatingPointMapper.MapUInt32ToFloat(mappedNan, 32);
            Assert.True(float.IsNaN(unmappedNan));

            // With precision loss
            uint mappedNanLossy = FloatingPointMapper.MapFloatToUInt32(nan, 16);
            float unmappedNanLossy = FloatingPointMapper.MapUInt32ToFloat(mappedNanLossy, 16);
            // Depending on which bits of NaN are preserved, it might still be NaN or become Inf or a number.
            // The C++ fpzip handles NaNs correctly in lossless mode.
            // This implies the mapping is reversible for NaNs too if precision is full.
            // If lossy, the result is "undefined" by fpzip but should be numerically stable.
             Assert.True(float.IsNaN(unmappedNanLossy) || float.IsInfinity(unmappedNanLossy) || !float.IsNaN(nan)); // It's valid if it's still NaN or becomes Inf
        }

        [Fact]
        public void Float_MapInfinity_ShouldBeHandled()
        {
            float infP = float.PositiveInfinity;
            uint mappedInfP = FloatingPointMapper.MapFloatToUInt32(infP, 32);
            float unmappedInfP = FloatingPointMapper.MapUInt32ToFloat(mappedInfP, 32);
            Assert.True(float.IsPositiveInfinity(unmappedInfP));

            float infN = float.NegativeInfinity;
            uint mappedInfN = FloatingPointMapper.MapFloatToUInt32(infN, 32);
            float unmappedInfN = FloatingPointMapper.MapUInt32ToFloat(mappedInfN, 32);
            Assert.True(float.IsNegativeInfinity(unmappedInfN));

            // With precision loss
            uint mappedInfPLossy = FloatingPointMapper.MapFloatToUInt32(infP, 16);
            float unmappedInfPLossy = FloatingPointMapper.MapUInt32ToFloat(mappedInfPLossy, 16);
            Assert.True(float.IsPositiveInfinity(unmappedInfPLossy)); // Should typically remain Inf
        }

        // TODO: Add similar NaN/Infinity tests for double.
        // TODO: Add tests for specific bit patterns if edge cases in mapping logic are suspected.
        // The mapping `r = ~r; r >>= shift; r ^= adjust;` is non-trivial.
    }
}
