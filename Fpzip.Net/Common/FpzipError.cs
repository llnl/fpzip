namespace Fpzip.Net.Common
{
    /// <summary>
    /// Defines error codes similar to fpzip_errno in the C version.
    /// These can be used by FpzipException.
    /// </summary>
    public enum FpzipError
    {
        /// <summary>
        /// No error occurred.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Cannot read from the input stream.
        /// </summary>
        ReadStreamError = 1,

        /// <summary>
        /// Cannot write to the output stream.
        /// </summary>
        WriteStreamError = 2,

        /// <summary>
        /// Invalid file format or magic number mismatch; not an fpzip stream.
        /// </summary>
        BadFormat = 3,

        /// <summary>
        /// The fpzip format version of the stream is not supported.
        /// </summary>
        BadVersion = 4,

        /// <summary>
        /// The precision specified is not supported for the data type.
        /// </summary>
        BadPrecision = 5,

        /// <summary>
        /// The compressed output buffer is too small (overflow).
        /// </summary>
        BufferOverflow = 6,

        /// <summary>
        /// An internal error or unexpected exception occurred.
        /// </summary>
        InternalError = 7,

        /// <summary>
        /// Input or output data is null or invalid.
        /// </summary>
        InvalidData = 8, // New error for .NET specific checks

        /// <summary>
        /// Metadata (dimensions, type, precision) is invalid.
        /// </summary>
        InvalidMetadata = 9, // New error for .NET specific checks

        /// <summary>
        /// Decompression failed due to corrupted data or other reasons.
        /// </summary>
        DecompressionError = 10, // Generic error for decompression issues
    }
}
