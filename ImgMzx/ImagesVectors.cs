using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImgMzx;

public partial class Images : IDisposable
{
    public float[] CalculateVector(Image<Rgb24> image)
    {
        var inputData = _inputDataPool.TryDequeue(out var pooledInputData) && pooledInputData.Length == InputDataSize
            ? pooledInputData
            : new float[InputDataSize];

        var vectorData = _vectorPool.TryDequeue(out var pooledVector) && pooledVector.Length == VectorSize
            ? pooledVector
            : new float[VectorSize];

        var container = _containerPool.TryDequeue(out var pooledContainer)
            ? pooledContainer
            : new List<NamedOnnxValue>(1);

        container.Clear();

        try {
            ProcessImageOptimized(image, inputData);

            var tensor = new DenseTensor<float>(inputData.AsMemory(0, InputDataSize), new int[] { 1, 3, ImageSize, ImageSize });
            container.Add(NamedOnnxValue.CreateFromTensor("pixel_values", tensor));

            using var results = _session.Run(container);
            var outputTensor = results[0].AsTensor<float>() ?? throw new InvalidOperationException("Model output is null");
            ExtractVectorOptimized(outputTensor, vectorData);
            NormalizeVectorSIMD(vectorData.AsSpan(0, VectorSize));

            var result = new float[VectorSize];
            Array.Copy(vectorData, result, VectorSize);
            return result;
        }
        finally {
            if (inputData.Length == InputDataSize && _inputDataPool.Count < 8) {
                Array.Clear(inputData);
                _inputDataPool.Enqueue(inputData);
            }

            if (vectorData.Length == VectorSize && _vectorPool.Count < 8) {
                Array.Clear(vectorData);
                _vectorPool.Enqueue(vectorData);
            }

            if (_containerPool.Count < 8) {
                container.Clear();
                _containerPool.Enqueue(container);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessImageOptimized(Image<Rgb24> image, float[] inputData)
    {
        using var resizedImage = image.Clone(ctx => ctx.Resize(ImageSize, ImageSize));
        var pixelData = new Rgb24[ImageSize * ImageSize];
        resizedImage.ProcessPixelRows(accessor => {
            for (int y = 0; y < ImageSize; y++) {
                var row = accessor.GetRowSpan(y);
                row.CopyTo(pixelData.AsSpan(y * ImageSize, ImageSize));
            }
        });

        const float scale = 1.0f / 255.0f;
        const float rMean = 0.485f, gMean = 0.456f, bMean = 0.406f;
        const float rStd = 0.229f, gStd = 0.224f, bStd = 0.225f;

        var rSpan = inputData.AsSpan(0, ChannelSize);
        var gSpan = inputData.AsSpan(ChannelSize, ChannelSize);
        var bSpan = inputData.AsSpan(2 * ChannelSize, ChannelSize);

        if (Vector.IsHardwareAccelerated && pixelData.Length >= Vector<float>.Count) {
            ProcessPixelsVectorized(pixelData, rSpan, gSpan, bSpan, scale, rMean, gMean, bMean, rStd, gStd, bStd);
        }
        else {
            ProcessPixelsScalar(pixelData, rSpan, gSpan, bSpan, scale, rMean, gMean, bMean, rStd, gStd, bStd);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessPixelsVectorized(ReadOnlySpan<Rgb24> pixels, Span<float> rSpan, Span<float> gSpan, Span<float> bSpan,
        float scale, float rMean, float gMean, float bMean, float rStd, float gStd, float bStd)
    {
        var scaleVec = new Vector<float>(scale);
        var rMeanVec = new Vector<float>(rMean);
        var gMeanVec = new Vector<float>(gMean);
        var bMeanVec = new Vector<float>(bMean);
        var rStdVec = new Vector<float>(1.0f / rStd);
        var gStdVec = new Vector<float>(1.0f / gStd);
        var bStdVec = new Vector<float>(1.0f / bStd);

        var vectorCount = Vector<float>.Count;
        var vectorizedLength = (pixels.Length / vectorCount) * vectorCount;

        Span<float> rValues = stackalloc float[vectorCount];
        Span<float> gValues = stackalloc float[vectorCount];
        Span<float> bValues = stackalloc float[vectorCount];

        for (int i = 0; i < vectorizedLength; i += vectorCount) {
            for (int j = 0; j < vectorCount && (i + j) < pixels.Length; j++) {
                var pixel = pixels[i + j];
                rValues[j] = pixel.R;
                gValues[j] = pixel.G;
                bValues[j] = pixel.B;
            }

            var rVec = new Vector<float>(rValues);
            var gVec = new Vector<float>(gValues);
            var bVec = new Vector<float>(bValues);

            rVec = (rVec * scaleVec - rMeanVec) * rStdVec;
            gVec = (gVec * scaleVec - gMeanVec) * gStdVec;
            bVec = (bVec * scaleVec - bMeanVec) * bStdVec;

            rVec.CopyTo(rSpan.Slice(i, vectorCount));
            gVec.CopyTo(gSpan.Slice(i, vectorCount));
            bVec.CopyTo(bSpan.Slice(i, vectorCount));
        }

        for (int i = vectorizedLength; i < pixels.Length; i++) {
            var pixel = pixels[i];
            rSpan[i] = (pixel.R * scale - rMean) / rStd;
            gSpan[i] = (pixel.G * scale - gMean) / gStd;
            bSpan[i] = (pixel.B * scale - bMean) / bStd;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessPixelsScalar(ReadOnlySpan<Rgb24> pixels, Span<float> rSpan, Span<float> gSpan, Span<float> bSpan,
        float scale, float rMean, float gMean, float bMean, float rStd, float gStd, float bStd)
    {
        for (int i = 0; i < pixels.Length; i++) {
            var pixel = pixels[i];
            rSpan[i] = (pixel.R * scale - rMean) / rStd;
            gSpan[i] = (pixel.G * scale - gMean) / gStd;
            bSpan[i] = (pixel.B * scale - bMean) / bStd;
        }
    }

    private static void ExtractVectorOptimized(Tensor<float> outputTensor, float[] vectorArray)
    {
        var shape = outputTensor.Dimensions.ToArray();
        var vectorSpan = vectorArray.AsSpan(0, VectorSize);

        if (shape.Length == 3) {
            var hiddenSize = Math.Min(shape[2], VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, 0, i];
            }
        }
        else if (shape.Length == 2) {
            var hiddenSize = Math.Min(shape[1], VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, i];
            }
        }
        else {
            var copyLength = Math.Min(outputTensor.Length, VectorSize);
            for (int i = 0; i < copyLength; i++) {
                vectorSpan[i] = outputTensor.GetValue(i);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void NormalizeVectorSIMD(Span<float> vector)
    {
        var sumOfSquares = 0.0f;

        if (Vector.IsHardwareAccelerated && vector.Length >= Vector<float>.Count) {
            var vectorSpan = MemoryMarshal.Cast<float, Vector<float>>(vector);
            var sumVector = Vector<float>.Zero;

            foreach (var v in vectorSpan) {
                sumVector += v * v;
            }

            for (int i = 0; i < Vector<float>.Count; i++) {
                sumOfSquares += sumVector[i];
            }

            var remaining = vector.Length % Vector<float>.Count;
            var remainingStart = vector.Length - remaining;
            for (int i = remainingStart; i < vector.Length; i++) {
                sumOfSquares += vector[i] * vector[i];
            }
        }
        else {
            foreach (var value in vector) {
                sumOfSquares += value * value;
            }
        }

        var norm = (float)Math.Sqrt(sumOfSquares);
        if (norm > 0) {
            var invNorm = 1.0f / norm;

            if (Vector.IsHardwareAccelerated && vector.Length >= Vector<float>.Count) {
                var invNormVector = new Vector<float>(invNorm);
                var vectorCount = Vector<float>.Count;

                for (int i = 0; i <= vector.Length - vectorCount; i += vectorCount) {
                    var vec = new Vector<float>(vector.Slice(i, vectorCount));
                    (vec * invNormVector).CopyTo(vector.Slice(i, vectorCount));
                }

                var remaining = vector.Length % vectorCount;
                var remainingStart = vector.Length - remaining;
                for (int i = remainingStart; i < vector.Length; i++) {
                    vector[i] *= invNorm;
                }
            }
            else {
                for (int i = 0; i < vector.Length; i++) {
                    vector[i] *= invNorm;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.IsEmpty || y.IsEmpty || x.Length != y.Length)
            return 1f;

        var dotProduct = ComputeDotProductSIMD(x, y);
        var distance = 1f - dotProduct;

        return Math.Max(0f, Math.Min(1f, distance));
    }

    public static float CalculateDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
            return 1f;

        var dotProduct = ComputeDotProductSIMD(x.AsSpan(), y.AsSpan());
        var distance = 1f - dotProduct;

        return Math.Max(0f, Math.Min(1f, distance));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeDotProductSIMD(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        var dotProduct = 0.0f;

        if (Vector.IsHardwareAccelerated && x.Length >= Vector<float>.Count) {
            var xVectors = MemoryMarshal.Cast<float, Vector<float>>(x);
            var yVectors = MemoryMarshal.Cast<float, Vector<float>>(y);
            var sumVector = Vector<float>.Zero;

            for (int i = 0; i < xVectors.Length; i++) {
                sumVector += xVectors[i] * yVectors[i];
            }

            for (int i = 0; i < Vector<float>.Count; i++) {
                dotProduct += sumVector[i];
            }

            var remaining = x.Length % Vector<float>.Count;
            var remainingStart = x.Length - remaining;
            for (int i = remainingStart; i < x.Length; i++) {
                dotProduct += x[i] * y[i];
            }
        }
        else {
            for (int i = 0; i < x.Length; i++) {
                dotProduct += x[i] * y[i];
            }
        }

        return dotProduct;
    }

    public ReadOnlySpan<float> GetVectorSpan(string hash)
    {
        if (string.IsNullOrEmpty(hash) || !_hashToIndex.TryGetValue(hash, out var index)) {
            throw new ArgumentException($"Hash '{hash}' not found");
        }

        return GetVectorSpan(index);
    }

    private ReadOnlySpan<float> GetVectorSpan(int index)
    {
        var offset = index * VectorSize;
        return _vectors.Span.Slice(offset, VectorSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<float> GetMutableVectorSpan(int index)
    {
        var offset = index * VectorSize;
        return _vectors.Span.Slice(offset, VectorSize);
    }

    private void ExpandCapacity()
    {
        var newCapacity = _capacityVectors + 1000;

        var newVectorsOwner = MemoryPool<float>.Shared.Rent(newCapacity * VectorSize);
        var newVectors = newVectorsOwner.Memory[..(newCapacity * VectorSize)];
        var newHashes = new string[newCapacity];

        _vectors.Span[..(_countVectors * VectorSize)].CopyTo(newVectors.Span);
        Array.Copy(_hashes, newHashes, _countVectors);

        _vectorsOwner?.Dispose();
        _vectorsOwner = newVectorsOwner;
        _vectors = newVectors;
        _hashes = newHashes;
        _capacityVectors = newCapacity;
    }

    public bool AddVector(string hash, float[] vector)
    {
        return AddVector(hash, vector.AsSpan());
    }

    private bool AddVector(string hash, ReadOnlySpan<float> vector)
    {
        if (string.IsNullOrEmpty(hash)) {
            throw new ArgumentNullException(nameof(hash));
        }

        if (vector.Length != VectorSize) {
            throw new ArgumentException($"Vector must have exactly {VectorSize} elements", nameof(vector));
        }

        lock (_lock) {
            if (_hashToIndex.ContainsKey(hash)) {
                return false;
            }

            if (_countVectors >= _capacityVectors) {
                ExpandCapacity();
            }

            var index = _countVectors++;
            _hashes[index] = hash;

            var targetSpan = GetMutableVectorSpan(index);
            vector.CopyTo(targetSpan);

            _hashToIndex[hash] = index;
            return true;
        }
    }

    public void ChangeVector(string hash, float[] vector)
    {
        ChangeVector(hash, vector.AsSpan());
    }

    private void ChangeVector(string hash, ReadOnlySpan<float> vector)
    {
        if (string.IsNullOrEmpty(hash)) {
            throw new ArgumentNullException(nameof(hash));
        }

        if (vector.Length != VectorSize) {
            throw new ArgumentException($"Vector must have exactly {VectorSize} elements", nameof(vector));
        }

        if (!_hashToIndex.TryGetValue(hash, out var index)) {
            throw new ArgumentException($"Hash '{hash}' not found");
        }

        var targetSpan = GetMutableVectorSpan(index);
        vector.CopyTo(targetSpan);
    }

    private static float ComputeDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        var dotProduct = 0f;
        for (int i = 0; i < VectorSize; i++) {
            dotProduct += x[i] * y[i];
        }

        var distance = 1f - dotProduct;
        return Math.Max(0f, Math.Min(1f, distance));
    }
}
