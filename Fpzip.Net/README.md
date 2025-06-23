# Fpzip.Net

**Fpzip.Net** is a .NET port of the core fpzip compression algorithm, designed for compressing arrays of single and double-precision floating-point numbers. It is particularly effective for data with spatial correlation, such as scientific simulation outputs or sensor readings.

This library provides lossless and lossy compression capabilities. Lossy compression is achieved by specifying the number of bits of precision to retain from the original floating-point values.

## Features

-   Compresses and decompresses `float[]` and `double[]` (via `ReadOnlySpan<T>` and `Span<T>`).
-   Supports N-dimensional data (1D, 2D, 3D, 4D via `Nx, Ny, Nz, Nf` metadata fields).
-   Lossless and lossy compression (controlled by `Precision` metadata field).
-   Targets compatibility with the original C++ fpzip's FP_SAFE mode.
-   Pure .NET Standard 2.0 / .NET Core implementation, no native dependencies.

## Basic Usage

### Compression

```csharp
using Fpzip.Net;
using Fpzip.Net.Common;
using System.IO;

// Sample data
float[] myData = new float[] { 1.0f, 1.1f, 1.2f, 2.0f, 2.1f, 2.2f, 3.0f, 3.1f, 3.2f };

// Define metadata
FpzipMetadata metadata = new FpzipMetadata
{
    Type = FpzipConstants.TypeFloat, // 0 for float, 1 for double
    Precision = 0,                  // 0 for lossless, or specify bits (e.g., 16 for 16-bit precision)
    Nx = 3,                         // Dimension X
    Ny = 3,                         // Dimension Y
    Nz = 1,                         // Dimension Z (1 for 2D data)
    Nf = 1                          // Number of fields/components (1 for scalar data)
};

// Validate metadata (optional but recommended)
if (!metadata.Validate(out string errorMsg))
{
    throw new ArgumentException($"Invalid metadata: {errorMsg}");
}

byte[] compressedBytes;
using (MemoryStream compressedStream = new MemoryStream())
{
    FpzipCodec.Compress(metadata, myData, compressedStream);
    compressedBytes = compressedStream.ToArray();
}

// Now 'compressedBytes' contains the compressed data.
// You can save it to a file, send over network, etc.
File.WriteAllBytes("data.fpz", compressedBytes);
```

### Decompression

```csharp
using Fpzip.Net;
using Fpzip.Net.Common; // For FpzipMetadata if you need to inspect it
using System.IO;
using System; // For Span<T>

// Load compressed data (e.g., from a file)
byte[] compressedBytesFromFile = File.ReadAllBytes("data.fpz");

using (MemoryStream inputStream = new MemoryStream(compressedBytesFromFile))
{
    // Allocate an output buffer.
    // For robust applications, you might need a way to determine the exact size first,
    // e.g., by storing metadata separately or having a two-pass read (not directly supported by this simple API).
    // Here, we assume a sufficiently large buffer or that you know the dimensions.
    // The Decompress method will throw if outputData is too small based on header info.

    // If you know the max possible size:
    // float[] decompressedData = new float[MAX_POSSIBLE_ELEMENTS];
    // FpzipMetadata readMetadata = FpzipCodec.Decompress(inputStream, decompressedData);
    // int actualElements = (int)readMetadata.GetTotalNumberOfValues();
    // Now use decompressedData up to actualElements.

    // For this example, let's assume we know the original dimensions for allocation.
    // (In a real scenario, you'd get this from the 'readMetadata' after a first pass or external source)
    int expectedNx = 3, expectedNy = 3, expectedNz = 1, expectedNf = 1;
    long expectedTotalElements = (long)expectedNx * expectedNy * expectedNz * expectedNf;
    float[] decompressedData = new float[expectedTotalElements];

    FpzipMetadata readMetadata = FpzipCodec.Decompress(inputStream, decompressedData);

    // Verify metadata if needed
    // Console.WriteLine($"Decompressed: {readMetadata.Nx}x{readMetadata.Ny}x{readMetadata.Nz}x{readMetadata.Nf}, Prec: {readMetadata.Precision}");

    // Now 'decompressedData' contains the original data (or its lossy representation)
    // Example: Print first few values
    // for(int i=0; i<Math.Min(10, decompressedData.Length); ++i)
    // {
    //    Console.WriteLine(decompressedData[i]);
    // }
}
```

## License

This library is a port of the original fpzip C++ library. The original fpzip library is distributed under the BSD-3-Clause license. This .NET port is also intended to be compatible with or released under the BSD-3-Clause license. Please refer to the LICENSE file for full details.

## Acknowledgements

Based on the original C++ fpzip library by Peter Lindstrom, Lawrence Livermore National Laboratory.
Project URL: [https://github.com/LLNL/fpzip](https://github.com/LLNL/fpzip)
