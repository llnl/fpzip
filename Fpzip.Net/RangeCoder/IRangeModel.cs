namespace Fpzip.Net.RangeCoder
{
    /// <summary>
    /// Interface for a probability model used by the Range Coder.
    /// </summary>
    public interface IRangeModel
    {
        /// <summary>
        /// Gets the total number of unique symbols this model handles.
        /// </summary>
        uint Symbols { get; }

        /// <summary>
        /// Gets the frequency information for encoding a symbol.
        /// </summary>
        /// <param name="symbol">The symbol to encode.</param>
        /// <param name="symbolLow">Outputs the cumulative frequency of all symbols less than 'symbol'.</param>
        /// <param name="symbolFreq">Outputs the frequency of the 'symbol' itself.</param>
        void GetCharProbs(uint symbol, out uint symbolLow, out uint symbolFreq);
        // In C++ RCmodel::encode: void encode(uint s, uint& l, uint& r)

        /// <summary>
        /// Decodes a symbol based on the scaled cumulative frequency.
        /// </summary>
        /// <param name="scaledCumFreq">The scaled cumulative frequency (code - low) / range.</param>
        /// <param name="symbolLow">Outputs the cumulative frequency of symbols less than the decoded symbol.</param>
        /// <param name="symbolFreq">Outputs the frequency of the decoded symbol.</param>
        /// <returns>The decoded symbol.</returns>
        uint DecodeChar(uint scaledCumFreq, out uint symbolLow, out uint symbolFreq);
        // In C++ RCmodel::decode: uint decode(uint& l, uint& r) where l is input scaledCumFreq, output actual cumFreq.

        /// <summary>
        /// Normalizes the range based on the model's total frequency count.
        /// The new range will be old_range / total_model_frequency.
        /// This method should update the passed range value.
        /// </summary>
        /// <param name="range">The current range of the arithmetic coder, to be scaled by (1 / TotalFrequency).</param>
        void NormalizeRange(ref uint range);
        // In C++ RCmodel::normalize: void normalize(uint &r) (r >>= bits)

        /// <summary>
        /// Gets the total frequency count used by the model (typically 1u &lt;&lt; bits).
        /// </summary>
        uint TotalFrequency { get; }

        /// <summary>
        /// Updates the model with the occurrence of a symbol (for adaptive models).
        /// </summary>
        /// <param name="symbol">The symbol that occurred.</param>
        void Update(uint symbol);

        /// <summary>
        /// Resets the model to its initial state.
        /// </summary>
        void Reset();
    }
}
