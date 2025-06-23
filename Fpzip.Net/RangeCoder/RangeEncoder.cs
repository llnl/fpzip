using System;
using Fpzip.Net.Common;
using Fpzip.Net.Io;

namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Implements the Range Encoder logic based on RCencoder from fpzip.
    /// </summary>
    internal class RangeEncoder : IRangeEncoder
    {
        private const uint TopValue = 1u << 24; // 0x01000000
        private const uint BottomValue = 1u << 16; // 0x00010000. Used for underflow check.

        private readonly BitStreamWriter _writer;
        private ulong _low; // Changed to ulong to avoid overflow with range * l, C++ uses uint and relies on overflow.
                           // low is uint in C++, range is uint. low + range * l can overflow uint.
                           // The C++ code uses low as uint and relies on modular arithmetic.
                           // For .NET, to be safe and explicit, using ulong for low and range might be easier to manage,
                           // but C++ arithmetic coder often relies on uint overflow properties.
                           // Let's stick to uint and manage carries carefully or use ulong for intermediate calcs.
                           // The C++ code's `low` represents the lower bound of a 32-bit integer range.
                           // `range` is also 32-bit.
                           // Let's try to stick to uint for low and range, and be careful. C++ normalizes out the top byte.

        private uint _currentLow; // low in C++
        private uint _currentRange; // range in C++
        private bool _disposed;
        private long _bytesWrittenCount; // To track bytes for BytesWritten property

        public RangeEncoder(BitStreamWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _currentLow = 0;
            _currentRange = 0xFFFFFFFFu; // Max uint, (-1u) in C++
            _bytesWrittenCount = 0;
        }

        public long BytesWritten => _bytesWrittenCount + (_writer.GetType().GetProperty("BytesWritten")?.GetValue(_writer) as long? ?? 0);


        public void Encode(uint symbol, IRangeModel model)
        {
            // Equivalent to C++ RCencoder::encode(uint s, RCmodel* rm)
            // rm->encode(s, l, r);
            // rm->normalize(range);
            // low += range * l;
            // range *= r;
            // normalize();

            model.GetCharProbs(symbol, out uint symLow, out uint symFreq);

            // Scale range by (1 / TotalModelFrequency)
            // C++: rm->normalize(range) which is range >>= model_precision_bits
            // This is done *before* calculating new low and range.
            uint oldRange = _currentRange;
            model.NormalizeRange(ref _currentRange); // range = oldRange / model.TotalFrequency

            _currentLow += _currentRange * symLow;
            _currentRange *= symFreq;

            model.Update(symbol); // Update model after using its probabilities

            Normalize();
        }

        public void EncodeUniform(uint value, int bits) // encode_shift in C++
        {
            if (bits < 0 || bits > 31) // 0 bits means nothing to encode, 32 bits needs ulong
                throw new ArgumentOutOfRangeException(nameof(bits), "Bits must be 0-31 for uint uniform encoding.");
            if (bits == 0) return;
            if (value >= (1u << bits))
                throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds representable range for given bits.");

            _currentRange >>= bits;
            _currentLow += _currentRange * value;
            Normalize();
        }

        public void EncodeUniform64(ulong value, int bits)
        {
             if (bits < 0 || bits > 63)
                throw new ArgumentOutOfRangeException(nameof(bits), "Bits must be 0-63 for ulong uniform encoding.");
            if (bits == 0) return;
            if (value >= (1ul << bits))
                 throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds representable range for given bits.");

            // This needs care if _currentRange is uint. C++ template handles this.
            // For now, let's assume this might be called in chunks if bits > 16 or so.
            // The C++ RCencoder::encode<UINT>(s,n) unrolls this.
            // For a simple port, if bits > 16, do it in two steps.
            if (bits > 16)
            {
                EncodeUniform((uint)(value & 0xFFFF), 16); // Lower 16 bits
                EncodeUniform64(value >> 16, bits - 16);    // Upper bits
            }
            else
            {
                 _currentRange >>= bits; // This is uint, if bits is large, range becomes 0.
                                         // This implies that EncodeUniform should not be called with very large 'bits'
                                         // if _currentRange itself is small. The C++ code's loop suggests this.
                                         // Let's trust that bits will be small enough, matching C++ template behavior.
                _currentLow += (uint)(_currentRange * value); // value must fit in what range can scale
                Normalize();
            }
        }


        public void EncodeRaw(uint value, int numBits)
        {
            if (numBits < 0 || numBits > 32)
                throw new ArgumentOutOfRangeException(nameof(numBits));
            if (numBits == 0) return;
            _writer.WriteBits(value, numBits);
            // Note: Raw encoding does not interact with _currentLow, _currentRange
        }

        public void EncodeRaw64(ulong value, int numBits)
        {
            if (numBits < 0 || numBits > 64)
                throw new ArgumentOutOfRangeException(nameof(numBits));
            if (numBits == 0) return;
            _writer.WriteBits64(value, numBits);
        }


        private void Normalize()
        {
            // while (!((low ^ (low + range)) >> 24)) { put(1); range <<= 8; }
            // if (!(range >> 16)) { put(2); range = -low; }
            while (((_currentLow ^ (_currentLow + _currentRange)) >> 24) == 0) // Top byte is same
            {
                PutByteAndShift();
            }

            if ((_currentRange >> 16) == 0) // Range is too small (underflow imminent)
            {
                // Occurs when low and low + range are like 0x00FFxxxx and 0x0100xxxx
                // Top byte differs, but range < 0x00010000 (BottomValue)
                // C++: put(2); range = -low; (equivalent to 0 - low)
                // This is an underflow handling step.
                // Output current low (2 bytes) and then set range to cover up to 0xFFFFFFFF from new low.
                _writer.WriteByte((byte)(_currentLow >> 24));
                _bytesWrittenCount++;
                _writer.WriteByte((byte)((_currentLow >> 16) & 0xFF));
                _bytesWrittenCount++;

                _currentLow <<= 16;
                _currentRange = 0xFFFFFFFFu - _currentLow; // Effectively -_currentLow if _currentLow was the full value after shift
                                                          // Or, more directly from C++: range = (uint)(0U - _currentLow);
            }
        }

        private void PutByteAndShift() // Corresponds to C++ put(1) and range <<= 8
        {
            _writer.WriteByte((byte)(_currentLow >> 24));
            _bytesWrittenCount++;
            _currentLow <<= 8;
            _currentRange <<= 8;
            if (_currentRange == 0) _currentRange = 1; // Prevent range from becoming zero if it was small before shift
        }


        public void Finish()
        {
            // C++: put(4); flush();
            // Output 4 more bytes of low to ensure enough precision for decoder.
            // The exact value written depends on the state of 'low'.
            // A common way is to output 'low' plus a bit to ensure it's in the lower half of final range.
            // The C++ code just calls put(4) which writes top 4 bytes of 'low' after left shifts.

            // To ensure the stream is uniquely decodable, we need to output enough bits
            // to distinguish the final 'low' value.
            // One simple way is to output 'low' + (range/2) or just 'low' if it's sufficient.
            // The C++ RCencoder::finish() calls put(4).
            // My PutByteAndShift writes _currentLow >> 24.
            // So put(4) means:
            // WriteByte(_currentLow >> 24); _currentLow <<= 8;
            // WriteByte(_currentLow >> 24); _currentLow <<= 8;
            // WriteByte(_currentLow >> 24); _currentLow <<= 8;
            // WriteByte(_currentLow >> 24); _currentLow <<= 8;

            for (int i = 0; i < 4; i++)
            {
                 _writer.WriteByte((byte)(_currentLow >> 24));
                 _bytesWrittenCount++;
                _currentLow <<= 8;
                // Range also shifts, but it's not used after this.
            }
            _writer.Flush();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Finish might try to write to a disposed stream if _writer is disposed first
                // However, BitStreamWriter's Dispose calls its own Flush.
                // If not already finishing, do it.
                // This is tricky. The user of RangeEncoder should call Finish().
                _writer.Dispose(); // BitStreamWriter handles its own flush on dispose.
                _disposed = true;
            }
        }
    }
}
