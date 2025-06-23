using System;
using System.IO;
using Fpzip.Net.Common;

namespace Fpzip.Net.Io
{
    /// <summary>
    /// Reads bits from an input stream.
    /// </summary>
    internal class BitStreamReader : IDisposable
    {
        private readonly Stream _inputStream;
        private byte _buffer;
        private int _bitPosition; // Next bit position to read in _buffer (0-7, from MSB to LSB)
        private bool _leaveOpen;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="BitStreamReader"/> class.
        /// </summary>
        /// <param name="inputStream">The stream to read from.</param>
        /// <param name="leaveOpen">True to leave the stream open after the BitStreamReader is disposed; otherwise, false.</param>
        public BitStreamReader(Stream inputStream, bool leaveOpen = false)
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));
            if (!inputStream.CanRead)
                throw new ArgumentException("Stream is not readable.", nameof(inputStream));

            _inputStream = inputStream;
            _leaveOpen = leaveOpen;
            _buffer = 0;
            _bitPosition = 8; // Indicates buffer is empty, need to read first
        }

        /// <summary>
        /// Reads a single bit from the stream.
        /// </summary>
        /// <returns>The bit read (0 or 1).</returns>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream is reached prematurely.</exception>
        public int ReadBit()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BitStreamReader));

            if (_bitPosition == 8)
            {
                FillBuffer();
            }

            int bit = (_buffer >> (7 - _bitPosition)) & 1; // Read MSB first
            _bitPosition++;
            return bit;
        }

        /// <summary>
        /// Reads a specified number of bits into a uint value.
        /// Bits are read into the LSB of the returned value.
        /// </summary>
        /// <param name="count">The number of bits to read (1-32).</param>
        /// <returns>A uint value containing the bits read.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream is reached prematurely.</exception>
        public uint ReadBits(int count)
        {
            if (count < 1 || count > 32)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 32.");

            uint value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | (uint)ReadBit();
            }
            return value;
        }

        /// <summary>
        /// Reads a specified number of bits into a ulong value.
        /// Bits are read into the LSB of the returned value.
        /// </summary>
        /// <param name="count">The number of bits to read (1-64).</param>
        /// <returns>A ulong value containing the bits read.</returns>
        /// <exception cref="EndOfStreamException">Thrown if the end of the stream is reached prematurely.</exception>
        public ulong ReadBits64(int count)
        {
            if (count < 1 || count > 64)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 64.");

            ulong value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | (ulong)ReadBit();
            }
            return value;
        }


        private void FillBuffer()
        {
            int byteRead;
            try
            {
                byteRead = _inputStream.ReadByte();
            }
            catch (IOException ex)
            {
                 throw new FpzipException(FpzipError.ReadStreamError, "Failed to read from the underlying stream.", ex);
            }

            if (byteRead == -1)
            {
                // End of stream. This is only an error if we were expecting more bits.
                // The ReadBit/ReadBits methods will determine if this is premature.
                // For now, we'll throw if FillBuffer is called and hits EOS,
                // as it means a ReadBit operation cannot be satisfied.
                throw new EndOfStreamException("Reached end of stream while trying to read bits.");
            }
            _buffer = (byte)byteRead;
            _bitPosition = 0;
        }

        /// <summary>
        /// Checks if the underlying stream has been fully read (or if the bit buffer is also empty).
        /// Note: This might involve reading a byte to check, if the current buffer is exhausted.
        /// It's not a perfect "is there more data" without attempting a read.
        /// A more reliable way is to catch EndOfStreamException.
        /// </summary>
        public bool IsEndOfStream
        {
            get
            {
                if (_bitPosition < 8) return false; // Still bits in buffer
                // Try to peek or read next byte to see if stream has more.
                // This is tricky without consuming. For now, rely on ReadByte() == -1 in FillBuffer.
                // A simple check:
                if (_inputStream.Position >= _inputStream.Length && _bitPosition == 8) return true;
                return false; // Potentially more data
            }
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
                    if (!_leaveOpen)
                    {
                        _inputStream?.Dispose();
                    }
                }
                // Free unmanaged resources (if any) here.
                _disposed = true;
            }
        }
    }
}
