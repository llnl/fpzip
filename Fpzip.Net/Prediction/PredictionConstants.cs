namespace Fpzip.Net.Prediction
{
    internal static class PredictionConstants
    {
        /// <summary>
        /// Maximum bit width of directly encodable integers (residuals).
        /// If the precision of the mapped floating-point value (M::bits) is greater than this,
        /// a more complex Golomb-Rice like encoding is used for residuals. Otherwise, a simpler
        /// direct encoding of the difference is used.
        /// Default from pccodec.h is 8.
        /// </summary>
        public const int MaxDirectlyEncodableBits = 8; // PC_BIT_MAX
    }
}
