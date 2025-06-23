using System;
using Fpzip.Net.Common;
using Fpzip.Net.Io;

namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Implements the Range Decoder logic based on RCdecoder from fpzip.
    /// </summary>
    internal class RangeDecoder : IRangeDecoder
    {
        private const uint TopValue = 1u << 24;    // 0x01000000
        private const uint BottomValue = 1u << 16; // 0x00010000

        private readonly BitStreamReader _reader;
        private uint _currentLow;    // low in C++
        private uint _currentRange;  // range in C++
        private uint _currentCode;   // code in C++ (buffer for incoming data)
        private bool _disposed;
        private long _bytesReadCount;

        public RangeDecoder(BitStreamReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            // _currentLow, _currentRange, _currentCode are initialized in Init()
        }

        public long BytesRead => _bytesReadCount + (_reader.GetType().GetProperty("BytesRead")?.GetValue(_reader) as long? ?? 0);


        public void Init()
        {
            _currentLow = 0;
            _currentRange = 0xFFFFFFFFu;
            _currentCode = 0;
            _bytesReadCount = 0;

            // C++: get(4); -> reads 4 bytes into _currentCode and shifts _currentLow
            // My GetByteAndShift also shifts low.
            // Here, we just need to fill _currentCode.
            for (int i = 0; i < 4; i++)
            {
                _currentCode = (_currentCode << 8) | _reader.ReadBit(); // Should be ReadByte
            }
            // Correction: init should read full bytes into _currentCode
            _currentCode = 0; // Reset
            for (int i = 0; i < 4; i++)
            {
                _currentCode = (_currentCode << 8) | _reader.ReadByte(); // ReadByte from BitStreamReader if it had it, or directly from stream
                                                                       // For now, assume BitStreamReader gives us bytes or we adapt.
                                                                       // BitStreamReader.ReadBits(8) can work.
                _bytesReadCount++;
            }
        }

        public void Init()
        {
            _currentLow = 0;
            _currentRange = 0xFFFFFFFFu;
            _currentCode = 0;
            _bytesReadCount = 0;
            for (int i = 0; i < 4; i++)
            {
                _currentCode = (_currentCode << 8) | _reader.ReadBits(8); // Read 8 bits as a byte
                _bytesReadCount++; // Assuming ReadBits(8) counts as 1 byte for this purpose
            }
        }


        public uint Decode(IRangeModel model)
        {
            // Equivalent to C++ RCdecoder::decode(RCmodel* rm)
            // rm->normalize(range);
            // uint l_scaled = (code - low) / range;
            // uint s = rm->decode(l_scaled, out actual_l, out freq_r);
            // low += range * actual_l;
            // range *= freq_r;
            // normalize();
            // model.Update(s);

            model.NormalizeRange(ref _currentRange); // range = oldRange / model.TotalFrequency

            if (_currentRange == 0) // Should not happen if model.TotalFrequency is valid
                 throw new FpzipException(FpzipError.DecompressionError, "Range became zero during decode, model error?");

            uint scaledCumulativeFreq = (_currentCode - _currentLow) / _currentRange;

            uint symbol = model.DecodeChar(scaledCumulativeFreq, out uint symLow, out uint symFreq);

            _currentLow += _currentRange * symLow;
            _currentRange *= symFreq;

            model.Update(symbol);

            Normalize();
            return symbol;
        }

        public uint DecodeUniform(int bits) // decode_shift in C++
        {
            if (bits < 0 || bits > 31)
                throw new ArgumentOutOfRangeException(nameof(bits));
            if (bits == 0) return 0;

            _currentRange >>= bits;
            uint symbol = (_currentCode - _currentLow) / _currentRange;

            if (symbol >= (1u << bits)) // Should not happen if stream is valid
                 throw new FpzipException(FpzipError.DecompressionError, "Decoded uniform value out of range.");

            _currentLow += _currentRange * symbol;
            Normalize();
            return symbol;
        }

        public ulong DecodeUniform64(int bits)
        {
            if (bits < 0 || bits > 63)
                throw new ArgumentOutOfRangeException(nameof(bits));
            if (bits == 0) return 0UL;

            ulong result = 0;
            if (bits > 16)
            {
                result = DecodeUniform(16); // Lower 16 bits
                result |= (ulong)DecodeUniform64(bits - 16) << 16; // Upper bits
            }
            else
            {
                _currentRange >>= bits; // Range is uint. This implies this should only be called with small 'bits'
                                       // or that the C++ template handles large UINT by parts.
                ulong symbol = (_currentCode - _currentLow) / _currentRange; // code, low, range are uint.

                if (symbol >= (1UL << bits))
                    throw new FpzipException(FpzipError.DecompressionError, "Decoded uniform 64-bit value out of range.");

                _currentLow += (uint)(_currentRange * symbol); // symbol must fit
                Normalize();
                result = (uint)symbol; // Cast back to uint as current logic assumes small bits
            }
            return result;
        }


        public uint DecodeRaw(int numBits)
        {
            if (numBits < 0 || numBits > 32)
                throw new ArgumentOutOfRangeException(nameof(numBits));
            if (numBits == 0) return 0;
            return _reader.ReadBits(numBits);
            // Note: Raw decoding does not interact with _currentLow, _currentRange, _currentCode
        }

        public ulong DecodeRaw64(int numBits)
        {
             if (numBits < 0 || numBits > 64)
                throw new ArgumentOutOfRangeException(nameof(numBits));
            if (numBits == 0) return 0UL;
            return _reader.ReadBits64(numBits);
        }


        private void Normalize()
        {
            // while (!((low ^ (low + range)) >> 24)) { get(1); range <<= 8; }
            // if (!(range >> 16)) { get(2); range = -low; }
            while (((_currentLow ^ (_currentLow + _currentRange)) >> 24) == 0) // Top byte is same
            {
                GetByteAndShift();
            }
            if ((_currentRange >> 16) == 0) // Range is too small
            {
                // C++: get(2); range = -low;
                _currentCode = (_currentCode << 8) | _reader.ReadBits(8); _bytesReadCount++;
                _currentCode = (_currentCode << 8) | _reader.ReadBits(8); _bytesReadCount++;
                _currentLow <<= 16;
                //_currentRange = (0U - _currentLow); // C++ way
                 _currentRange = 0xFFFFFFFFu - _currentLow; // More explicit way to fill up to max from new low
            }
        }

        private void GetByteAndShift() // Corresponds to C++ get(1) and range <<= 8
        {
            _currentCode = (_currentCode << 8) | _reader.ReadBits(8);
            _bytesReadCount++;
            _currentLow <<= 8;
            _currentRange <<= 8;
            if (_currentRange == 0) _currentRange = 1;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _reader.Dispose();
                _disposed = true;
            }
        }
    }
}
