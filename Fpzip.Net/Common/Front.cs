using System;
using System.Linq; // For Enumerable.Repeat

namespace Fpzip.Net.Common
{
    /// <summary>
    /// Circular buffer storing a "front" of recently processed samples
    /// for the Lorenzo predictor. Based on front.h from fpzip.
    /// </summary>
    /// <typeparam name="T">The data type of the samples (float or double).</typeparam>
    internal class Front<T> where T : struct // Typically float or double
    {
        private readonly T _zero;
        private readonly uint _dxStride; // Stride for x dimension in circular buffer
        private readonly uint _dyStride; // Stride for y dimension
        private readonly uint _dzStride; // Stride for z dimension
        private readonly uint _mask;   // Mask for circular buffer indexing (size - 1)
        private uint _currentIndex;    // Current position in the circular buffer
        private readonly T[] _buffer;  // Circular array of samples

        /// <summary>
        /// Initializes a new instance of the <see cref="Front&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="nx">Number of samples in x dimension of the 3D grid being processed.</param>
        /// <param name="ny">Number of samples in y dimension of the 3D grid being processed.</param>
        /// <param name="zeroValue">The value to use for padding/default.</param>
        public Front(uint nx, uint ny, T zeroValue = default)
        {
            _zero = zeroValue;

            // Strides to access neighbors:
            // To get (i-1, j, k), current is at index `idx`. Previous x is at `idx - dxStride`.
            // To get (i, j-1, k), previous y is at `idx - dyStride`.
            // To get (i, j, k-1), previous z is at `idx - dzStride`.
            // These strides depend on the dimensions of the "virtual" 2D/3D slice stored flattened in the buffer.
            // The C++ code uses: dx(1), dy(nx + 1), dz(dy * (ny + 1))
            // This implies the buffer stores enough for a plane for y-neighbors, and multiple planes for z-neighbors.
            // The minimum size of the buffer needs to accommodate the largest required offset.
            // The largest offset is dz for (0,0,1), or dx+dy+dz for (1,1,1).
            // Let's assume the strides are for a conceptual full cube of neighbors needed for 3D prediction.
            // The Lorenzo predictor typically needs neighbors up to (i-1, j-1, k-1).
            // The buffer needs to be large enough to hold (nx+1)*(ny+1) elements for a 2D slice if dz is based on that.
            // Or, more simply, it needs to hold enough elements to make sure that when we index
            // (i - dx*x - dy*y - dz*z), we don't alias with data that's too recent or too old
            // in a way that breaks causality for the predictor.
            // The C++ code has:
            // dx = 1 (previous x is 1 element away in the flattened buffer storing current z-plane)
            // dy = nx + 1 (previous y is (nx+1) elements away, implies storing (nx+1) wide rows)
            // dz = dy * (ny + 1) (previous z is a full (ny+1) by (nx+1) plane away)
            // The buffer size is then determined by mask(dx + dy + dz), which means at least dx+dy+dz elements.
            // This seems to be for a specific way of traversing and storing.
            // For fpzip's predictor x(i-1,j,k), x(i,j-1,k), x(i,j,k-1) and their combinations,
            // we need to store at least one previous plane (for z), one previous row (for y), one previous element (for x).

            _dxStride = 1;
            // If ny=1 (2D data in XY plane, or 1D data in X axis), dy needs to be large enough
            // to not interfere with dx, but f(0,1,0) might not be used.
            // If nx=1, ny=1 (1D data in Z axis for example, if processed that way), dy=2.
            _dyStride = (nx > 0 ? nx : 1) + 1; // To store a row plus padding/boundary
            _dzStride = _dyStride * ((ny > 0 ? ny : 1) + 1); // To store a plane plus padding

            uint bufferSizeRequest = _dxStride + _dyStride + _dzStride; // Minimum elements for furthest neighbor (1,1,1)
            _mask = CalculateMask(bufferSizeRequest); // mask = 2^k - 1, where 2^k >= bufferSizeRequest

            _currentIndex = 0;
            _buffer = new T[_mask + 1];

            // Initialize buffer with zeroValue, especially important if we start accessing before pushing enough.
            // The C++ code seems to rely on `advance` calls to fill initial parts.
            // We can prefill, or ensure `advance` is called appropriately.
            for(int i=0; i < _buffer.Length; ++i)
            {
                _buffer[i] = _zero;
            }
        }

        /// <summary>
        /// Calculates m = 2^k - 1 such that 2^k >= n.
        /// </summary>
        private uint CalculateMask(uint n)
        {
            if (n == 0) return 0; // Should not happen with strides
            uint mask = n - 1;
            // Rounds up to the next power of 2, then subtracts 1.
            // Example: n=7, mask=6. n-1=6.
            // 6 | (6>>1)=3 -> 6 | 3 = 7.  (0110 | 0011 = 0111)
            // 7 | (7>>2)=1 -> 7 | 1 = 7.  (0111 | 0001 = 0111)
            // ...
            // This is a common bit hack to find next power of 2 minus 1.
            mask |= mask >> 1;
            mask |= mask >> 2;
            mask |= mask >> 4;
            mask |= mask >> 8;
            mask |= mask >> 16; // Works for uint
            return mask;
            // C++ version: for (n--; n & (n + 1); n |= n + 1); return n;
            // This is different. Example n=7. n-- -> 6.
            // 6 & 7 (0110 & 0111 = 0110). True. n = 6 | 7 = 7.
            // 7 & 8 (0111 & 1000 = 0000). False. Returns 7.
            // So for n=7, mask = 7 (size 8).
            // My CalculateMask for n=7: mask=6. 6|=3 (7). 7|=1 (7). Returns 7.
            // Okay, my version of CalculateMask seems to achieve the same as C++ `n | (n+1)` loop.
        }

        /// <summary>
        /// Fetches a neighbor relative to the current sample's position.
        /// (x,y,z) = (0,0,0) is current sample (if it were already pushed and index not moved).
        /// (x,y,z) = (1,0,0) is previous in x.
        /// (x,y,z) = (0,1,0) is previous in y.
        /// (x,y,z) = (0,0,1) is previous in z.
        /// </summary>
        public T GetNeighbor(uint x, uint y, uint z)
        {
            // Index calculation: (i - dx*x - dy*y - dz*z) & m
            // Need to handle potential underflow of (_currentIndex - offset) if using uint directly.
            long offset = _dxStride * x + _dyStride * y + _dzStride * z;
            long effectiveIndex = (long)_currentIndex - offset;
            return _buffer[effectiveIndex & _mask]; // Apply mask after ensuring positive result if needed
                                                    // Since _mask is 2^k-1, (val & _mask) handles positive/negative correctly for modular arithmetic.
        }

        /// <summary>
        /// Adds a sample to the front.
        /// The C++ version can push n copies, default 1.
        /// </summary>
        public void Push(T sample)
        {
            _buffer[_currentIndex & _mask] = sample;
            _currentIndex++;
        }

        /// <summary>
        /// Advances the front as if (x,y,z) elements were processed, filling with the zero value.
        /// This is used at boundaries.
        /// </summary>
        public void Advance(uint xSteps, uint ySteps, uint zSteps)
        {
            uint numZerosToPush = _dxStride * xSteps + _dyStride * ySteps + _dzStride * zSteps;
            for (uint i = 0; i < numZerosToPush; i++)
            {
                Push(_zero);
            }
        }
    }
}
