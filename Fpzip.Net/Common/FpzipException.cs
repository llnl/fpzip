using System;

namespace Fpzip.Net.Common
{
    /// <summary>
    /// Custom exception for Fpzip.Net operations.
    /// </summary>
    public class FpzipException : Exception
    {
        /// <summary>
        /// Gets the Fpzip error code associated with this exception.
        /// </summary>
        public FpzipError ErrorCode { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FpzipException"/> class
        /// with a specified error code and message.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <param name="message">The message that describes the error.</param>
        public FpzipException(FpzipError errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FpzipException"/> class
        /// with a specified error code, message, and a reference to the inner exception
        /// that is the cause of this exception.
        /// </summary>
        /// <param name="errorCode">The error code.</param>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public FpzipException(FpzipError errorCode, string message, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}
