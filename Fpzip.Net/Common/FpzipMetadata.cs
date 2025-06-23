namespace Fpzip.Net.Common
{
    /// <summary>
    /// Represents the metadata for an fpzip compressed stream, similar to the FPZ struct in C.
    /// This information is crucial for both compression and decompression.
    /// </summary>
    public class FpzipMetadata
    {
        /// <summary>
        /// Gets or sets the data type.
        /// 0 for single-precision float (FpzipConstants.TypeFloat).
        /// 1 for double-precision float (FpzipConstants.TypeDouble).
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// Gets or sets the number of bits of precision to retain.
        /// 0 indicates full precision (lossless).
        /// For floats (TypeFloat), valid range is typically 2-32.
        /// For doubles (TypeDouble), valid range is typically 4-64 (often in increments of 2).
        /// </summary>
        public int Precision { get; set; }

        /// <summary>
        /// Gets or sets the number of samples in the x dimension. Must be > 0.
        /// </summary>
        public int Nx { get; set; }

        /// <summary>
        /// Gets or sets the number of samples in the y dimension.
        /// For 1D arrays, set Ny = 1. Must be > 0.
        /// </summary>
        public int Ny { get; set; }

        /// <summary>
        /// Gets or sets the number of samples in the z dimension.
        /// For 1D or 2D arrays, set Nz = 1. Must be > 0.
        /// </summary>
        public int Nz { get; set; }

        /// <summary>
        /// Gets or sets the number of fields (scalar components per sample).
        /// For scalar arrays, set Nf = 1. Must be > 0.
        /// </summary>
        public int Nf { get; set; } // Number of fields (components)

        /// <summary>
        /// Validates the metadata for consistency and correctness.
        /// </summary>
        /// <param name="errorMessage">Outputs a message describing the validation error if validation fails; otherwise, null.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public bool Validate(out string? errorMessage)
        {
            if (Type != FpzipConstants.TypeFloat && Type != FpzipConstants.TypeDouble)
            {
                errorMessage = $"Invalid Type: {Type}. Must be {FpzipConstants.TypeFloat} (float) or {FpzipConstants.TypeDouble} (double).";
                return false;
            }

            if (Nx <= 0)
            {
                errorMessage = $"Invalid Nx: {Nx}. Must be positive.";
                return false;
            }
            if (Ny <= 0)
            {
                errorMessage = $"Invalid Ny: {Ny}. Must be positive.";
                return false;
            }
            if (Nz <= 0)
            {
                errorMessage = $"Invalid Nz: {Nz}. Must be positive.";
                return false;
            }
            if (Nf <= 0)
            {
                errorMessage = $"Invalid Nf: {Nf}. Must be positive.";
                return false;
            }

            if (Precision < 0) // 0 is full precision
            {
                errorMessage = $"Invalid Precision: {Precision}. Cannot be negative.";
                return false;
            }

            if (Type == FpzipConstants.TypeFloat)
            {
                // C++ version: precisions 2-32 for floats. 0 for full.
                if (Precision > 32)
                {
                    errorMessage = $"Invalid Precision for float: {Precision}. Max is 32.";
                    return false;
                }
            }
            else // TypeDouble
            {
                // C++ version: precisions 4-64 for doubles. 0 for full.
                if (Precision > 64)
                {
                    errorMessage = $"Invalid Precision for double: {Precision}. Max is 64.";
                    return false;
                }
                // The C++ code also mentions "increments of two bits" for doubles,
                // but the core check is usually just the max.
                // We might add (Precision % 2 != 0 && Precision != 0) if strict adherence is needed later.
            }

            // Check for potential overflow if calculating total elements, though this class doesn't store the array itself.
            // long totalElements = (long)Nx * Ny * Nz * Nf;
            // if (totalElements <= 0 || totalElements > int.MaxValue) // Example check if it were to fit in an int-indexed array
            // {
            //    errorMessage = "Total number of elements (Nx*Ny*Nz*Nf) is too large or invalid.";
            //    return false;
            // }


            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Calculates the total number of values based on dimensions.
        /// </summary>
        /// <returns>The total number of values.</returns>
        /// <exception cref="OverflowException">If the total number of elements exceeds long.MaxValue.</exception>
        public long GetTotalNumberOfValues()
        {
            // Use long for intermediate multiplication to prevent overflow with large int dimensions
            long count = (long)Nx * Ny * Nz * Nf;
            return count;
        }
    }
}
