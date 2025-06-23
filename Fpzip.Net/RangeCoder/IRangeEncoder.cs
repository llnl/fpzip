using System;

namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Interface for a Range Encoder.
    /// </summary>
    public interface IRangeEncoder : IDisposable
    {
        /// <summary>
        /// Encodes a symbol using the provided probability model.
        /// </summary>
        /// <param name="symbol">The symbol to encode.</param>
        /// <param name="model">The probability model to use.</param>
        void Encode(uint symbol, IRangeModel model);

        /// <summary>
        /// Encodes an integer 'value' using 'bits' number of bits, assuming uniform probability.
        /// (Equivalent to RCencoder::encode(value, bits) in C++ which uses encode_shift).
        /// </summary>
        /// <param name="value">The integer value to encode. Assumed to be less than 2^bits.</param>
        /// <param name="bits">The number of bits to represent the value (1-31 for uint, 1-63 for ulong).</param>
        void EncodeUniform(uint value, int bits);

        /// <summary>
        /// Encodes a long integer 'value' using 'bits' number of bits, assuming uniform probability.
        /// </summary>
        void EncodeUniform64(ulong value, int bits);


        // The C++ RCencoder also has:
        // void encode(bool s); // Encode a bit
        // template <typename UINT> void encode(UINT s, UINT l, UINT h); // Encode s in [l, h-1]
        // These can be added if direct porting of those specific methods is needed,
        // or they can be implemented by callers using Encode(symbol, model) with a simple static model.
        // For PCdecoder "wide" mode, a raw bit encoding is needed. Let's call it EncodeRaw.

        /// <summary>
        /// Encodes the lower 'bits' of 'value' directly to the stream without probability modeling.
        /// Used by the "wide" prediction strategy.
        /// </summary>
        /// <param name="value">The value whose lower bits are to be encoded.</param>
        /// <param name="bits">The number of bits to encode (0-31). If 0, nothing is written.</param>
        void EncodeRaw(uint value, int bits);

        /// <summary>
        /// Encodes the lower 'bits' of 'value' directly to the stream without probability modeling.
        /// Used by the "wide" prediction strategy for ulong values.
        /// </summary>
        void EncodeRaw64(ulong value, int bits);

        /// <summary>
        /// Finishes the encoding process, flushing any remaining bits and writing termination symbols if necessary.
        /// </summary>
        void Finish();

        /// <summary>
        /// Gets the total number of bytes written to the underlying stream so far.
        /// </summary>
        long BytesWritten { get; }
    }
}
