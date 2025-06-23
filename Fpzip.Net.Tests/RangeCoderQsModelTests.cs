using Xunit;
using Fpzip.Net.RangeCoder;
using System;

namespace Fpzip.Net.Tests
{
    public class RangeCoderQsModelTests
    {
        [Fact]
        public void Constructor_ValidParameters_InitializesCorrectly()
        {
            uint symbols = 10;
            uint precisionBits = 10; // Total frequency = 1024
            var model = new RangeCoderQsModel(isCompression: true, symbols, precisionBits);

            Assert.Equal(symbols, model.Symbols);
            Assert.Equal(1u << (int)precisionBits, model.TotalFrequency);
        }

        [Fact]
        public void Constructor_InvalidPrecisionBits_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RangeCoderQsModel(true, 10, 17)); // Max is 16
        }

        [Fact]
        public void Constructor_InvalidRescalePeriod_ThrowsArgumentOutOfRangeException()
        {
            uint precisionBits = 10; // Total freq 1024
            uint symbols = 10;
            // Period must be < (1u << (bits + 1)), so < 2048
            Assert.Throws<ArgumentOutOfRangeException>(() => new RangeCoderQsModel(true, symbols, precisionBits, 2048));
        }

        [Fact]
        public void Reset_InitializesFrequenciesEvenly()
        {
            uint symbols = 4;
            uint precisionBits = 8; // Total frequency = 256
            var model = new RangeCoderQsModel(true, symbols, precisionBits); // Compression mode

            // After reset (called by constructor), frequencies should be roughly TotalFrequency / Symbols
            // 256 / 4 = 64
            model.GetCharProbs(0, out uint low0, out uint freq0);
            model.GetCharProbs(1, out uint low1, out uint freq1);
            model.GetCharProbs(2, out uint low2, out uint freq2);
            model.GetCharProbs(3, out uint low3, out uint freq3);

            Assert.Equal(64u, freq0);
            Assert.Equal(64u, freq1);
            Assert.Equal(64u, freq2);
            Assert.Equal(64u, freq3);

            Assert.Equal(0u, low0);
            Assert.Equal(64u, low1);
            Assert.Equal(128u, low2);
            Assert.Equal(192u, low3);

            // Cumulative frequency of last symbol + its freq should be TotalFrequency
            Assert.Equal(model.TotalFrequency, low3 + freq3);
        }

        [Fact]
        public void Reset_InitializesFrequenciesWithRemainder()
        {
            uint symbols = 3;
            uint precisionBits = 8; // Total frequency = 256
            // 256 / 3 = 85 with remainder 1. So, one symbol gets 86, others 85.
            // C++ logic: symf[0] = f+1, symf[1]=f, symf[2]=f for m=1 (remainder)
            // My logic: symf[0]=f+1 for i < m. So symf[0] = 85+1 = 86. symf[1]=85, symf[2]=85.
            var model = new RangeCoderQsModel(true, symbols, precisionBits);

            model.GetCharProbs(0, out uint low0, out uint freq0); // Expected 86
            model.GetCharProbs(1, out uint low1, out uint freq1); // Expected 85
            model.GetCharProbs(2, out uint low2, out uint freq2); // Expected 85

            Assert.Equal(86u, freq0);
            Assert.Equal(85u, freq1);
            Assert.Equal(85u, freq2);

            Assert.Equal(0u, low0);
            Assert.Equal(86u, low1);
            Assert.Equal(86u + 85u, low2);
            Assert.Equal(model.TotalFrequency, low2 + freq2);
        }


        [Fact]
        public void GetCharProbs_AndUpdate_ChangesProbabilities()
        {
            uint symbols = 2;
            uint precisionBits = 8; // Total 256. Initial: 128, 128
            var model = new RangeCoderQsModel(true, symbols, precisionBits, rescalePeriod: 4); // Small period for faster updates

            model.GetCharProbs(0, out _, out uint freq0_before); // 128
            model.Update(0); // Symbol 0 occurred

            // After one update, freq of 0 should increase slightly.
            // The exact change depends on _incrementPerUpdate, _leftUntilNormalization etc.
            // This test is more of a smoke test that Update does *something*.
            // The C++ code updates symf[s] += incr. incr is initially (total_freq_deficit / rescale_period).
            // Initial deficit = 0. So incr = 0 for first few calls until PerformMajorUpdate.
            // Let's trace `Update` and `PerformMajorFrequencyUpdate` based on C++
            // reset(): rescale=(2/4)|2=2. more=0. f=128, m=0. symf[0]=128, symf[1]=128.
            //          calls UpdateCumulativeFrequenciesAndBuildSearchTable().
            //          calls PerformMajorFrequencyUpdate():
            //                more=0. currentRescalePeriod = 2.
            //                currentTotalFrequency = (128/2+1) + (128/2+1) = 65+65=130. (symf values become 65,65)
            //                amountToDistribute = 256 - 130 = 126.
            //                _incrementPerUpdate = 126 / 2 = 63.
            //                _moreSymbolsWithLargerIncrement = 126 % 2 = 0.
            //                _leftUntilNormalization = 2 - 0 = 2.
            // So, after reset, leftUntilNormalization = 2, incrementPerUpdate = 63.

            // Call Update(0): left=1. symf[0] = 65 + 63 = 128. symf[1]=65.
            model.GetCharProbs(0, out _, out uint freq0_after1);
            model.GetCharProbs(1, out _, out uint freq1_after1);

            // symf are internal. GetCharProbs reads from _cumulativeFrequencies which are based on _symbolFrequencies.
            // My GetCharProbs reads _symbolFrequencies directly for freq, and _cumulativeFrequencies for low.
            // After model.Update(0), _symbolFrequencies[0] is incremented.
            // _cumulativeFrequencies are only rebuilt in PerformMajorFrequencyUpdate.
            // This means the freq returned by GetCharProbs should reflect the increment.
            // However, the *low* for the next symbol will be based on old cumulative. This is the C++ way.

            // Expected after reset and its PerformMajorFrequencyUpdate: symf[0]=65, symf[1]=65. _incrementPerUpdate=63.
            // When model.Update(0) is called by encoder/decoder *after* GetCharProbs:
            // _symbolFrequencies[0] becomes 65 + 63 = 128. _symbolFrequencies[1] is 65.
            // Next call to GetCharProbs(0) will return freq=128. GetCharProbs(1) will return freq=65.
            // Low for symbol 0 is still 0. Low for symbol 1 is (old) cumf[1] which was 65.
            Assert.Equal(128u, freq0_after1); // symf[0] got the increment
            Assert.Equal(65u, freq1_after1);  // symf[1] did not

            model.Update(0); // Symbol 0 occurred again. left=0. symf[0] = 128 + 63 = 191.
            // Now left=0, so next Update(any) or GetCharProbs (if it triggered update) would call PerformMajorFrequencyUpdate.
            // My Update() calls PerformMajorFrequencyUpdate if left==0.
            // So, after this Update(0), PerformMajorFrequencyUpdate runs.
            //   more=0. currentRescalePeriod = 2 * 2 = 4 (target is 4).
            //   symf[0]=191, symf[1]=65.
            //   Halved: symf[0]=(191/2)|1 = 95|1=95. symf[1]=(65/2)|1 = 32|1=33.
            //   currentTotal = 95+33 = 128.
            //   amountToDistribute = 256 - 128 = 128.
            //   _incrementPerUpdate = 128 / 4 = 32.
            //   _moreSymbolsWithLargerIncrement = 128 % 4 = 0.
            //   _leftUntilNormalization = 4.
            // New symf: symf[0]=95, symf[1]=33.

            model.GetCharProbs(0, out _, out uint freq0_after2);
            model.GetCharProbs(1, out _, out uint freq1_after2);
            Assert.Equal(95u, freq0_after2);
            Assert.Equal(33u, freq1_after2);
        }

        [Fact]
        public void NormalizeRange_ScalesRangeCorrectly()
        {
            var model = new RangeCoderQsModel(true, 10, 10); // precisionBits = 10
            uint range = 0xFFFFFFFFu; // Example large range
            model.NormalizeRange(ref range);
            // Expected: range = 0xFFFFFFFFu >> 10
            Assert.Equal(0xFFFFFFFFu >> 10, range);
        }

        // More tests needed for DecodeChar with search table, and comprehensive update sequences.
    }
}
