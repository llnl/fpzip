namespace Fpzip.Net.Common
{
    /// <summary>
    /// Defines various constants used by the Fpzip codec.
    /// These are derived from fpzip.h, codec.h, and version.cpp.
    /// </summary>
    internal static class FpzipConstants
    {
        // Library version information (from fpzip.h and version.cpp)
        public const int LibraryVersionMajor = 1;
        public const int LibraryVersionMinor = 3;
        public const int LibraryVersionPatch = 0;

        public const uint LibraryVersion =
            (LibraryVersionMajor << 8) +
            (LibraryVersionMinor << 4) +
            (LibraryVersionPatch << 0); // Should be 304

        public const string LibraryVersionString = "fpzip version 1.3.0 (December 20, 2019)"; // From version.cpp

        // Floating-point implementation modes (from fpzip.h)
        // public const int FpModeFast = 1; // FPZIP_FP_FAST
        public const int FpModeSafe = 2; // FPZIP_FP_SAFE - Primary target for this port
        // public const int FpModeEmul = 3; // FPZIP_FP_EMUL
        // public const int FpModeInt = 4;  // FPZIP_FP_INT

        // Codec version (from codec.h and fpzip.h)
        // FPZ_MAJ_VERSION from codec.h is 0x0110 (272)
        // FPZ_MIN_VERSION from codec.h is FPZIP_FP (e.g., 2 for FP_SAFE)
        // FPZIP_CODEC = ((0x0110u << 8) + (FPZIP_FP))
        // For FP_SAFE (2), CodecVersion = (272 << 8) + 2 = 69632 + 2 = 69634
        public const uint CodecVersion = (0x0110u << 8) + FpModeSafe;

        // Data types (from fpzip.h)
        public const int TypeFloat = 0;  // Corresponds to C++ float
        public const int TypeDouble = 1; // Corresponds to C++ double

        // Magic number for fpzip streams, often 'fpz' followed by a version byte.
        // The C++ code writes 'f', 'p', 'z', then a byte derived from FPZ_MIN_VERSION (which is FPZIP_FP).
        // For FP_SAFE, this would be 'f', 'p', 'z', 2.
        // Let's define the core part and the version byte separately for header writing.
        public const byte Magic1 = (byte)'f';
        public const byte Magic2 = (byte)'p';
        public const byte Magic3 = (byte)'z';
        public const byte Magic4_FpSafe = (byte)FpModeSafe; // The 4th byte of the magic number for FP_SAFE

        // Default precision for lossless (0 means full precision)
        public const int DefaultPrecision = 0;

        // Maximum dimensions (from C++ code, usually for array indexing)
        // Original code uses int for dimensions, nx, ny, nz, nf.
        // While not strictly a constant from the headers, it's an implicit limit.
        // For practical purposes in .NET, array/span indices are int.
        // The format itself might not impose a hard limit other than what fits in int.
        // In C++, array indexing is typically limited by ptrdiff_t/size_t, but dimensions are int.
        // Max values for nx, ny, nz, nf are practically int.MaxValue.
        // The number of values is nx * ny * nz * nf. This product must not overflow size_t.
        // For now, no explicit max dimension constants are needed unless found in implementation details.
    }
}
