using System;
using System.IO;
using Fpzip.Net.Common;

namespace Fpzip.Net.Io
{
    /// <summary>
    /// Writes bits to an output stream.
    /// </summary>
    internal class BitStreamWriter : IDisposable
    {
        private readonly Stream _outputStream;
        private byte _buffer;
        private int _bitPosition; // Next bit position to write in _buffer (0-7, from MSB to LSB)
        private bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitStreamWriter"/> class.
        /// </summary>
        /// <param name="outputStream">The stream to write to.</param>
        /// <param name="leaveOpen">True to leave the stream open after the BitStreamWriter is disposed; otherwise, false.</param>
        public BitStreamWriter(Stream outputStream, bool leaveOpen = false)
        {
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));
            if (!outputStream.CanWrite)
                throw new ArgumentException("Stream is not writable.", nameof(outputStream));

            _outputStream = outputStream;
            _leaveOpen = leaveOpen;
            _buffer = 0;
            _bitPosition = 0; // Start writing at the most significant bit of the buffer
        }

        /// <summary>
        /// Writes a single bit to the stream.
        /// </summary>
        /// <param name="bit">The bit to write (0 or 1).</param>
        public void WriteBit(int bit)
        {
            if (bit != 0 && bit != 1)
                throw new ArgumentOutOfRangeException(nameof(bit), "Bit must be 0 or 1.");

            if (bit == 1)
            {
                _buffer |= (byte)(1 << (7 - _bitPosition)); // Set the bit (MSB first)
            }
            // If bit is 0, it's already clear by default, or was cleared when buffer was reset.

            _bitPosition++;
            if (_bitPosition == 8)
            {
                FlushBuffer();
            }
        }

        /// <summary>
        /// Writes a specified number of least significant bits from a value.
        /// Bits are written from MSB to LSB from the value.
        /// </summary>
        /// <param name="value">The value containing the bits to write.</param>
        /// <param name="count">The number of bits to write (1-32).</param>
        public void WriteBits(uint value, int count)
        {
            if (count < 1 || count > 32)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 32.");

            for (int i = count - 1; i >= 0; i--)
            {
                WriteBit((int)((value >> i) & 1));
            }
        }

        /// <summary>
        /// Writes a specified number of least significant bits from a long value.
        /// Bits are written from MSB to LSB from the value.
        /// </summary>
        /// <param name="value">The value containing the bits to write.</param>
        /// <param name="count">The number of bits to write (1-64).</param>
        public void WriteBits64(ulong value, int count)
        {
            if (count < 1 || count > 64)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 64.");

            for (int i = count - 1; i >= 0; i--)
            {
                WriteBit((int)((value >> i) & 1));
            }
        }


        /// <summary>
        /// Flushes any pending bits in the buffer to the underlying stream.
        /// This method should be called when done writing to ensure all bits are written.
        /// The remaining bits in the current byte are padded with zeros.
        /// </summary>
        public void Flush()
        {
            if (_bitPosition > 0)
            {
                FlushBuffer();
            }
            _outputStream.Flush(); // Flush the underlying stream
        }

        private void FlushBuffer()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BitStreamWriter));

            try
            {
                _outputStream.WriteByte(_buffer);
            }
            catch (IOException ex)
            {
                throw new FpzipException(FpzipError.WriteStreamError, "Failed to write to the underlying stream.", ex);
            }

            _buffer = 0;
            _bitPosition = 0;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        Flush(); // Ensure any remaining bits are written
                    }
                    catch (ObjectDisposedException)
                    {
                        // If the stream was already disposed (e.g. by user), this might happen.
                        // Or if this writer itself was disposed.
                    }


                    if (!_leaveOpen)
                    {
                        _outputStream?.Dispose();
                    }
                }
                // Free unmanaged resources (if any) here.
                _disposed = true;
            }
        }
    }
}
