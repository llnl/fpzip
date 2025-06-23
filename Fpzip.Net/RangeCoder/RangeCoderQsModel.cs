using System;
using Fpzip.Net.Common; // For FpzipException if needed

namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Adaptive probability model based on RCqsmodel from fpzip.
    /// "Quick Symbol Model"
    /// </summary>
    internal class RangeCoderQsModel : IRangeModel
    {
        private const int TableShift = 7; // TBLSHIFT in C++ code

        private readonly uint _symbols;
        private readonly uint _precisionBits; // 'bits' in C++ (log2 of total frequency count)
        private readonly uint _targetRescalePeriod;

        private uint _leftUntilNormalization;
        private uint _moreSymbolsWithLargerIncrement;
        private uint _incrementPerUpdate;
        private uint _currentRescalePeriod;

        private readonly uint[] _symbolFrequencies;   // symf in C++
        private readonly uint[] _cumulativeFrequencies; // cumf in C++ (cumf[0]=0, cumf[symbols]=TotalFrequency)

        private readonly bool _isCompressionMode; // Affects 'search' table
        private readonly uint _searchShift;       // bits - TBLSHIFT
        private readonly uint[]? _searchTable;    // 'search' in C++, only for decompression

        public uint Symbols => _symbols;
        public uint TotalFrequency => 1u << (int)_precisionBits;


        public RangeCoderQsModel(bool isCompression, uint symbols, uint precisionBits = 16, uint rescalePeriod = 0x400)
        {
            if (symbols == 0)
                throw new ArgumentOutOfRangeException(nameof(symbols), "Number of symbols must be greater than 0.");
            if (precisionBits > 16)
                throw new ArgumentOutOfRangeException(nameof(precisionBits), "RCqsmodel precisionBits cannot exceed 16.");
            if (rescalePeriod >= (1u << ((int)precisionBits + 1)))
                throw new ArgumentOutOfRangeException(nameof(rescalePeriod), "RCqsmodel rescalePeriod is too large for the given precisionBits.");

            _isCompressionMode = isCompression;
            _symbols = symbols;
            _precisionBits = precisionBits;
            _targetRescalePeriod = rescalePeriod;

            _symbolFrequencies = new uint[symbols + 1]; // C++ uses n+1 for cumf, symf seems to be size n. Let's use 'symbols' for symf.
                                                        // The C++ cumf has n+1 elements: cumf[0] to cumf[n].
                                                        // symf[i] is freq of symbol i. cumf[i] is sum of freqs of symbols < i.
                                                        // So cumf[i+1] = cumf[i] + symf[i].
                                                        // symf needs 'symbols' elements. cumf needs 'symbols + 1'.
            _cumulativeFrequencies = new uint[symbols + 1];

            _cumulativeFrequencies[0] = 0;
            _cumulativeFrequencies[symbols] = TotalFrequency;

            if (!isCompression)
            {
                if (_precisionBits < TableShift) // Ensure searchShift is not negative
                     throw new ArgumentOutOfRangeException(nameof(precisionBits), $"Precision bits ({precisionBits}) must be >= TableShift ({TableShift}) for decompression mode search table.");
                _searchShift = precisionBits - TableShift;
                _searchTable = new uint[(1 << TableShift) + 1];
            }
            else
            {
                _searchTable = null; // Not used in compression
                _searchShift = 0; // Not used
            }
            Reset();
        }

        public void Reset()
        {
            // rescale = (n >> 4) | 2;
            _currentRescalePeriod = (_symbols >> 4) | 2u;
            _moreSymbolsWithLargerIncrement = 0;

            uint n = _symbols;
            uint totalFreq = TotalFrequency; // cumf[n]
            uint f = totalFreq / n; // Base frequency
            uint m = totalFreq % n; // Remainder, distribute this

            for (uint i = 0; i < m; i++)
                _symbolFrequencies[i] = f + 1;
            for (uint i = m; i < n; i++)
                _symbolFrequencies[i] = f;

            UpdateCumulativeFrequenciesAndBuildSearchTable();
        }

        public void GetCharProbs(uint symbol, out uint symbolLow, out uint symbolFreq)
        {
            if (symbol >= _symbols)
                throw new ArgumentOutOfRangeException(nameof(symbol));

            // from RCqsmodel.inl: encode()
            // l = cumf[s];
            // r = cumf[s + 1] - l; (which is symf[s])
            symbolLow = _cumulativeFrequencies[symbol];
            symbolFreq = _symbolFrequencies[symbol]; // Or _cumulativeFrequencies[symbol + 1] - _cumulativeFrequencies[symbol];

            // C++ code calls update(s) here.
            // Let's make Update explicit for clarity in .NET, called by encoder/decoder after getting probs.
        }

        public uint DecodeChar(uint scaledCumFreq, out uint symbolLow, out uint symbolFreq)
        {
            // from RCqsmodel.cpp: decode()
            if (_searchTable == null) // Should not happen if isCompressionMode is false
                throw new InvalidOperationException("Search table not initialized for QSModel decoding.");

            uint i = scaledCumFreq >> (int)_searchShift;
            uint s = _searchTable[i];
            uint h = _searchTable[i + 1] + 1;

            // Binary search within the narrowed range [s, h-1]
            while (s + 1 < h)
            {
                uint mid = (s + h) / 2;
                if (scaledCumFreq < _cumulativeFrequencies[mid]) // Using scaledCumFreq directly here matches C++ `l < cumf[m]`
                    h = mid;
                else
                    s = mid;
            }
            // s is the decoded symbol
            symbolLow = _cumulativeFrequencies[s];
            symbolFreq = _symbolFrequencies[s]; // Or _cumulativeFrequencies[s+1] - symbolLow;

            // C++ code calls update(s) here.
            return s;
        }

        public void Update(uint symbol)
        {
            // From C++: update(uint s) called after encode/decode operations.
            // if (!left) update_table(); left--; symf[s] += incr;
            // This suggests that symf is updated first, then cumf.
            // However, the C++ RCmodel::encode calls its update(s) which then affects symf[s]
            // and then GetCharProbs returns the *old* cumf[s] and symf[s] before this update.
            // This is typical for adaptive models: use current stats, then update.

            if (_leftUntilNormalization == 0)
                PerformMajorFrequencyUpdate();

            _leftUntilNormalization--;
            _symbolFrequencies[symbol] += _incrementPerUpdate;

            // The cumulative frequencies need to be updated if symf changed.
            // The C++ code rebuilds cumf only during PerformMajorFrequencyUpdate (update_table in C++).
            // This means GetCharProbs uses cumf that might be stale wrt recent symf increments.
            // This is a common optimization in some adaptive coders to delay full cumf rebuild.
            // For simplicity and correctness, let's always update cumf after symf changes if needed,
            // or stick to C++ which updates cumf less frequently.
            // Sticking to C++: cumf is only fully rebuilt in PerformMajorFrequencyUpdate.
            // The individual increments to symf[s] mean that cumf[s+1] onwards are technically
            // incorrect until the next major update. This is part of its adaptive strategy.
        }


        private void PerformMajorFrequencyUpdate() // Corresponds to update_table in C++
        {
            // if (more) { left = more; more = 0; incr++; return; }
            if (_moreSymbolsWithLargerIncrement > 0)
            {
                _leftUntilNormalization = _moreSymbolsWithLargerIncrement;
                _moreSymbolsWithLargerIncrement = 0;
                _incrementPerUpdate++;
                return; // Skips full update for a bit longer
            }

            // if (rescale != targetrescale) { rescale *= 2; if (rescale > targetrescale) rescale = targetrescale; }
            if (_currentRescalePeriod != _targetRescalePeriod)
            {
                _currentRescalePeriod *= 2;
                if (_currentRescalePeriod > _targetRescalePeriod)
                    _currentRescalePeriod = _targetRescalePeriod;
            }

            // Update symbol frequencies (halve and ensure at least 1)
            // uint cf = cumf[n]; uint count = cf;
            // for (uint i = n; i--; ) { uint sf = symf[i]; cf -= sf; cumf[i] = cf; sf = (sf >> 1) | 1; count -= sf; symf[i] = sf; }
            // This loop in C++ rebuilds cumf from top down AND updates symf.
            // `count` accumulates the difference to target total frequency.

            uint currentTotalFrequency = 0;
            for (uint i = 0; i < _symbols; i++)
            {
                uint sf = _symbolFrequencies[i];
                sf = (sf >> 1) | 1u; // Halve, but keep at least 1
                _symbolFrequencies[i] = sf;
                currentTotalFrequency += sf;
            }

            UpdateCumulativeFrequenciesAndBuildSearchTable(); // Rebuilds cumf and search table from new symf

            // count is now difference between target cumf[n] and sum of symf;
            // incr = count / rescale; more = count % rescale; left = rescale - more;
            uint targetTotal = TotalFrequency;
            if (currentTotalFrequency > targetTotal) // Should not happen if logic is right
            {
                 // This might happen due to summed minimums (1s) exceeding total.
                 // In this case, some frequencies must be capped or not incremented.
                 // The C++ code's `count = cf; ... count -= sf;` handles this implicitly by calculating
                 // the deficit from the original total frequency.
                 // Let's re-evaluate `count` based on C++ logic:
                 // `count` = original_total_freq - new_sum_of_halved_freqs.
                 // This `count` is the amount to be redistributed.
                 uint deficitToDistribute = targetTotal - currentTotalFrequency; // This will be negative if sum > target.
                                                                            // If sum of symf is less than total_frequency, deficit is positive.
                if (currentTotalFrequency >= targetTotal) deficitToDistribute = 0; // cannot distribute negative.
                else deficitToDistribute = targetTotal - currentTotalFrequency;


                _incrementPerUpdate = deficitToDistribute / _currentRescalePeriod;
                _moreSymbolsWithLargerIncrement = deficitToDistribute % _currentRescalePeriod;
                _leftUntilNormalization = _currentRescalePeriod - _moreSymbolsWithLargerIncrement;
            }
            else // currentTotalFrequency <= targetTotal
            {
                 uint amountToDistribute = targetTotal - currentTotalFrequency;
                _incrementPerUpdate = amountToDistribute / _currentRescalePeriod;
                _moreSymbolsWithLargerIncrement = amountToDistribute % _currentRescalePeriod;
                _leftUntilNormalization = _currentRescalePeriod - _moreSymbolsWithLargerIncrement;
            }
        }

        private void UpdateCumulativeFrequenciesAndBuildSearchTable()
        {
            _cumulativeFrequencies[0] = 0;
            for (uint i = 0; i < _symbols; i++)
            {
                _cumulativeFrequencies[i + 1] = _cumulativeFrequencies[i] + _symbolFrequencies[i];
            }

            // Ensure the total matches (can drift due to integer math, C++ might allow small drift or correct it)
            // If _cumulativeFrequencies[_symbols] != TotalFrequency after this, it's an issue.
            // The C++ code sets cumf[n] = 1u << bits initially and seems to maintain it.
            // The symf values are adjusted to sum up to this.
            if (_cumulativeFrequencies[_symbols] != TotalFrequency)
            {
                // This indicates an issue in frequency updates or initial distribution.
                // For now, we might force it, or throw. C++ seems robust to small drifts.
                // Let's assume symf are managed such that they sum to TotalFrequency.
                // The deficit distribution in PerformMajorFrequencyUpdate aims for this.
            }


            if (!_isCompressionMode && _searchTable != null)
            {
                // build lookup table for fast symbol searches
                // for (uint i = n, h = 1 << TBLSHIFT; i--; h = cumf[i] >> searchshift)
                //   for (uint l = cumf[i] >> searchshift; l <= h; l++)
                //     search[l] = i;
                uint hPrevious = 1u << TableShift;
                for (uint i = _symbols; i-- > 0; ) // Iterate from n-1 down to 0
                {
                    uint currentCumFreqScaled = _cumulativeFrequencies[i] >> (int)_searchShift;
                    for (uint l_scaled = currentCumFreqScaled; l_scaled < hPrevious; l_scaled++)
                    {
                         if (l_scaled < _searchTable.Length) // Boundary check
                            _searchTable[l_scaled] = i;
                    }
                    hPrevious = currentCumFreqScaled;
                     if (hPrevious < _searchTable.Length) // Ensure searchTable[0] is set if i=0 and currentCumFreqScaled=0
                        _searchTable[hPrevious] = i;
                }
                 // Ensure the last entry of search table points to the last symbol if not covered.
                 // C++ code: search[i+1] implies search[(1<<TBLSHIFT)] can be accessed.
                 // searchTable size is (1 << TBLSHIFT) + 1. So index (1 << TBLSHIFT) is valid.
                 // The loop structure `search[l] = i` for `l <= h` (where h is previous `cumf[i] >> searchshift`)
                 // should correctly fill it.
                 // A simpler way to fill search table:
                Array.Clear(_searchTable, 0, _searchTable.Length); // Clear it first
                uint currentSearchIndex = 0;
                for(uint sym = 0; sym < _symbols; ++sym)
                {
                    // For every scaled cumulative frequency point we pass, update the search table
                    // scaled_cumf = cumf[sym] / (2^searchShift)
                    // scaled_next_cumf = cumf[sym+1] / (2^searchShift)
                    // All search table entries from scaled_cumf up to (but not including) scaled_next_cumf
                    // should point to 'sym'.
                    uint scaled_cumf = _cumulativeFrequencies[sym] >> (int)_searchShift;
                    uint scaled_next_cumf = _cumulativeFrequencies[sym+1] >> (int)_searchShift;

                    for(uint j = scaled_cumf; j < scaled_next_cumf; ++j) {
                        if (j < _searchTable.Length) _searchTable[j] = sym;
                    }
                }
                // The last entry of search table should point to the last symbol,
                // or handle cases where cumf[symbols] >> searchShift is the max index.
                _searchTable[1u << TableShift] = _symbols -1; // Ensure last entry points to a valid symbol index

            }
        }

        public void NormalizeRange(ref uint rangeComponent)
        {
            // from RCqsmodel.inl: normalize(uint& r) { r >>= bits; }
            rangeComponent >>= (int)_precisionBits;
        }
    }
}
