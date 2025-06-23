using System;

namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Interface for a Range Decoder.
    /// </summary>
    public interface IRangeDecoder : IDisposable
    {
        /// <summary>
        /// Initializes the decoder by reading the first few bytes from the stream.
        /// Must be called before any Decode operations.
        /// </summary>
        void Init();

        /// <summary>
        /// Decodes a symbol using the provided probability model.
        /// </summary>
        /// <param name="model">The probability model to use.</param>
        /// <returns>The decoded symbol.</returns>
        uint Decode(IRangeModel model);

        /// <summary>
        /// Decodes an integer value represented by 'bits' number of bits, assuming uniform probability.
        /// (Equivalent to RCdecoder::decode(bits) in C++ which uses decode_shift).
        /// </summary>
        /// <param name="bits">The number of bits representing the value (1-31 for uint, 1-63 for ulong).</param>
        /// <returns>The decoded integer value.</returns>
        uint DecodeUniform(int bits);

        /// <summary>
        /// Decodes a long integer value represented by 'bits' number of bits, assuming uniform probability.
        /// </summary>
        ulong DecodeUniform64(int bits);

        // Similar to IRangeEncoder, specific C++ RCdecoder methods like decode(bool) or decode(l,h)
        // can be added or implemented by callers.
        // For PCdecoder "wide" mode, a raw bit decoding is needed.

        /// <summary>
        /// Decodes 'bits' number of bits directly from the stream without probability modeling.
        /// Used by the "wide" prediction strategy.
        /// </summary>
        /// <param name="bits">The number of bits to decode (0-31). If 0, returns 0.</param>
        /// <returns>The decoded value from the lower bits.</returns>
        uint DecodeRaw(int bits);

        /// <summary>
        /// Decodes 'bits' number of bits directly from the stream without probability modeling for ulong values.
        /// Used by the "wide" prediction strategy.
        /// </summary>
        /// <param name="bits">The number of bits to decode (0-63). If 0, returns 0.</param>
        /// <returns>The decoded value from the lower bits.</returns>
        ulong DecodeRaw64(int bits);

        /// <summary>
        /// Gets the total number of bytes read from the underlying stream so far.
        /// </summary>
        long BytesRead { get; }
    }
}
