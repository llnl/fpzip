using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Fpzip.Net;
using Fpzip.Net.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices; // For OSPlatform

namespace Fpzip.Net.Benchmark
{
    // Configuration for BenchmarkDotNet, can be customized
    // Example: [ShortRunJob] or custom configs like [Config(typeof(Config))]
    // public class Config : ManualConfig { /* ... */ }

    [MemoryDiagnoser] // Adds memory allocation diagnostics
    // [MinColumn, MaxColumn, MeanColumn, MedianColumn] // Show various stats
    public class FpzipBenchmarks
    {
        // --- Benchmark Parameters ---
        // These parameters can be varied by BenchmarkDotNet using [ParamsSource] or [Params]
        // For simplicity, using a fixed size and type for now.

        [Params(128, 256)] // Nx, Ny, Nz will be this value for a cube
        public int ArrayDim;

        [Params(0, 16)] // 0 for lossless, 16 for lossy (16-bit precision)
        public int Precision;

        private float[] _floatData;
        private double[] _doubleData;
        private FpzipMetadata _floatMetadata;
        private FpzipMetadata _doubleMetadata;

        private string _nativeFpzipExePath;
        private string _tempDir;

        // --- Setup and Cleanup ---

        [GlobalSetup]
        public void GlobalSetup()
        {
            // Initialize data arrays
            long totalElements = (long)ArrayDim * ArrayDim * ArrayDim;
            _floatData = new float[totalElements];
            _doubleData = new double[totalElements];

            Random rand = new Random(42); // Seed for reproducibility
            for (int i = 0; i < totalElements; i++)
            {
                _floatData[i] = (float)(rand.NextDouble() * 2000.0 - 1000.0); // Values between -1000 and 1000
                _doubleData[i] = rand.NextDouble() * 2000.0 - 1000.0;
            }

            _floatMetadata = new FpzipMetadata
            {
                Type = FpzipConstants.TypeFloat,
                Precision = Precision,
                Nx = ArrayDim, Ny = ArrayDim, Nz = ArrayDim, Nf = 1
            };

            _doubleMetadata = new FpzipMetadata
            {
                Type = FpzipConstants.TypeDouble,
                Precision = Precision,
                Nx = ArrayDim, Ny = ArrayDim, Nz = ArrayDim, Nf = 1
            };

            // Determine path to native fpzip executable
            // This path will be set by the GitHub Actions workflow (e.g., ./native/fpzip-1.3.0/build/bin/fpzip_cli)
            // For local testing, you might need to adjust this or pass it via an environment variable.
            string nativeExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "fpzip_cli.exe" : "fpzip_cli";
            _nativeFpzipExePath = Path.Combine(AppContext.BaseDirectory, "native_cli", nativeExeName); // Placeholder search path

            // Check if specified path exists, if not, try a common build path from assumed structure
            if (!File.Exists(_nativeFpzipExePath))
            {
                 // Path relative to solution root if benchmark is run from there, or adjust as needed.
                 // This path needs to be solid for the workflow.
                 // The workflow will build the C++ exe into a known location.
                 // Let's assume the workflow places it in a dir accessible via env var or relative path.
                 // For now, this will likely fail if not set up by workflow.
                string searchPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "native", "fpzip-1.3.0", "build", "bin", nativeExeName));
                 if (File.Exists(searchPath)) _nativeFpzipExePath = searchPath;
                 else {
                    searchPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "fpzip_cli")); // if placed alongside benchmark exe
                    if(File.Exists(searchPath)) _nativeFpzipExePath = searchPath;
                    else _nativeFpzipExePath = nativeExeName; // Fallback to PATH, or ensure it's copied to output
                 }
            }
            Console.WriteLine($"Using native fpzip executable: {_nativeFpzipExePath}");


            _tempDir = Path.Combine(Path.GetTempPath(), "FpzipBenchmarkTemp");
            Directory.CreateDirectory(_tempDir);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* Best effort cleanup */ }
            }
        }

        // --- .NET Fpzip.Net Benchmarks ---

        [Benchmark(Description = "Fpzip.Net Float Compress")]
        public byte[] NetFloatCompress()
        {
            using var ms = new MemoryStream();
            FpzipCodec.Compress(_floatMetadata, _floatData, ms);
            return ms.ToArray();
        }

        // Pre-compressed data for .NET Decompress benchmarks
        private byte[] _netFloatCompressedData;
        private byte[] _netDoubleCompressedData;

        [IterationSetup(Target = nameof(NetFloatDecompress))]
        public void SetupNetFloatDecompress()
        {
            _netFloatCompressedData = NetFloatCompress(); // Compress once to get data for decompress benchmark
        }

        [Benchmark(Description = "Fpzip.Net Float Decompress")]
        public float[] NetFloatDecompress()
        {
            using var ms = new MemoryStream(_netFloatCompressedData);
            float[] output = new float[_floatData.Length];
            FpzipCodec.Decompress(ms, output);
            return output;
        }

        [Benchmark(Description = "Fpzip.Net Double Compress")]
        public byte[] NetDoubleCompress()
        {
            using var ms = new MemoryStream();
            FpzipCodec.Compress(_doubleMetadata, _doubleData, ms);
            return ms.ToArray();
        }

        [IterationSetup(Target = nameof(NetDoubleDecompress))]
        public void SetupNetDoubleDecompress()
        {
            _netDoubleCompressedData = NetDoubleCompress();
        }

        [Benchmark(Description = "Fpzip.Net Double Decompress")]
        public double[] NetDoubleDecompress()
        {
            using var ms = new MemoryStream(_netDoubleCompressedData);
            double[] output = new double[_doubleData.Length];
            FpzipCodec.Decompress(ms, output);
            return output;
        }


        // --- Native C++ fpzip CLI Benchmarks ---
        // These are more complex as they involve process execution and file I/O.
        // BenchmarkDotNet might not perfectly attribute time if process overhead is high,
        // but it's a common way to compare.

        private void RunNativeFpzip(string inputFile, string outputFile, string dataType, FpzipMetadata metadata, bool compressMode)
        {
            if (!File.Exists(_nativeFpzipExePath))
            {
                Console.WriteLine($"Native fpzip executable not found at '{_nativeFpzipExePath}'. Skipping native benchmark run.");
                Trace.TraceWarning($"Native fpzip executable not found at '{_nativeFpzipExePath}'. Skipping native benchmark run.");
                //This will cause benchmark to show 0 or error if exe not found.
                //Ideally, throw an exception or have a way for BenchmarkDotNet to skip if setup fails.
                //For now, let it run and potentially show errors if exe is missing.
                //A better way: [GlobalSetup] checks for exe and sets a flag, benchmarks skip if flag is true.
                return;
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _nativeFpzipExePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Base arguments: type, dimensions, precision
            // fpzip -t float|double -1 nx -2 ny -3 nz [-p precision] -i input -o output
            // For decompression, only -i and -o are strictly needed as header is in input.fpz
            // However, to be safe, providing full metadata to CLI for compression is good.
            // The CLI utility fpzip.cpp might not use all metadata for decompression if header exists.

            string args;
            if (compressMode)
            {
                args = $"-t {dataType} -1 {metadata.Nx} -2 {metadata.Ny} -3 {metadata.Nz} -f {metadata.Nf} -p {metadata.Precision} -i \"{inputFile}\" -o \"{outputFile}\"";
                if (metadata.Precision == 0) // Original CLI might not need -p 0 for lossless
                    args = $"-t {dataType} -1 {metadata.Nx} -2 {metadata.Ny} -3 {metadata.Nz} -f {metadata.Nf} -i \"{inputFile}\" -o \"{outputFile}\"";
            }
            else // Decompress
            {
                args = $"-d -i \"{inputFile}\" -o \"{outputFile}\""; // -d for decompress
            }

            psi.Arguments = args;

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit(); // Wait for the native process to complete
                if (process.ExitCode != 0)
                {
                    string stderr = process.StandardError.ReadToEnd();
                    string stdout = process.StandardOutput.ReadToEnd();
                    throw new Exception($"Native fpzip CLI failed with exit code {process.ExitCode}. Args: {args}\nStdout: {stdout}\nStderr: {stderr}");
                }
            }
        }

        [Benchmark(Description = "Native C++ Float Compress")]
        public string NativeFloatCompress()
        {
            string rawInputFile = Path.Combine(_tempDir, "native_float_input.raw");
            string compressedFile = Path.Combine(_tempDir, "native_float_output.fpz");

            // Write _floatData to rawInputFile (as raw bytes)
            byte[] rawBytes = new byte[_floatData.Length * sizeof(float)];
            Buffer.BlockCopy(_floatData, 0, rawBytes, 0, rawBytes.Length);
            File.WriteAllBytes(rawInputFile, rawBytes);

            RunNativeFpzip(rawInputFile, compressedFile, "float", _floatMetadata, true);
            return compressedFile; // Return path for potential verification, or just to have an output
        }

        private string _nativeFloatCompressedFileForDecompress;

        [IterationSetup(Target = nameof(NativeFloatDecompress))]
        public void SetupNativeFloatDecompress()
        {
            _nativeFloatCompressedFileForDecompress = NativeFloatCompress();
        }

        [Benchmark(Description = "Native C++ Float Decompress")]
        public string NativeFloatDecompress()
        {
            string compressedFile = _nativeFloatCompressedFileForDecompress;
            if (string.IsNullOrEmpty(compressedFile) || !File.Exists(compressedFile))
            {
                 // This happens if NativeFloatCompress was skipped (e.g. exe not found)
                 // or failed. We need to handle this.
                 // For now, just return an error indicator or skip.
                 return "SKIPPED_OR_ERROR_IN_COMPRESSION_SETUP";
            }

            string decompressedRawFile = Path.Combine(_tempDir, "native_float_decompress_output.raw");
            RunNativeFpzip(compressedFile, decompressedRawFile, "float", _floatMetadata, false);

            // Optionally, read back the decompressed data to verify and ensure it's part of benchmarked work
            // byte[] rawBytes = File.ReadAllBytes(decompressedRawFile);
            // float[] output = new float[rawBytes.Length / sizeof(float)];
            // Buffer.BlockCopy(rawBytes, 0, output, 0, rawBytes.Length);
            // return output; // If BenchmarkDotNet should measure this too.
            return decompressedRawFile;
        }

        // TODO: Add NativeDoubleCompress and NativeDoubleDecompress benchmarks
        // They would be very similar to the float versions, just using _doubleData, _doubleMetadata, and "double" type string.
    }

    // Main entry point for the benchmark console application
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting Fpzip.Net Benchmarks...");
            // To run specific benchmarks, you can use filters, e.g.:
            // var summary = BenchmarkRunner.Run<FpzipBenchmarks>(ManualConfig.Create(DefaultConfig.Instance).WithOption(ConfigOptions.JoinSummary, true).AddJob(Job.ShortRun));
            var summary = BenchmarkRunner.Run<FpzipBenchmarks>();
            Console.WriteLine("Fpzip.Net Benchmarks completed.");

            // After benchmarks run, results are in BenchmarkDotNet.Artifacts directory
            // This is where a script would parse results for comparison.
        }
    }
}
