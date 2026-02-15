using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;

namespace ImgMzx;

public class Vit: IDisposable
{
    private readonly InferenceSession _session;
    private readonly SessionOptions? _sessionOptions;

    private readonly Lock _lock = new();

    private bool disposedValue;

    public Vit(string filevit)
    {
        _sessionOptions = new SessionOptions {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        try {
            _sessionOptions.AppendExecutionProvider_CUDA(0);
        }
        catch (Exception) {
            _sessionOptions.AppendExecutionProvider_CPU();
        }

        _session = new InferenceSession(filevit, _sessionOptions);
    }

    public float[] CalculateVector(Image<Rgb24> image)
    {
        const int TargetSize = 512;
        float[] inputData;
        int inputDataSize;
        float scale = Math.Max((float)TargetSize / image.Width, (float)TargetSize / image.Height);
        int scaledWidth = (int)Math.Round(image.Width * scale);
        int scaledHeight = (int)Math.Round(image.Height * scale);
        using var processedImage = image.Clone(ctx => ctx
            .Resize(new ResizeOptions {
                Size = new Size(scaledWidth, scaledHeight),
                Mode = ResizeMode.Max
            })
            .Crop(new Rectangle(
                (scaledWidth - TargetSize) / 2,
                (scaledHeight - TargetSize) / 2,
                TargetSize,
                TargetSize))
        );

        inputDataSize = 3 * TargetSize * TargetSize;
        inputData = new float[inputDataSize];
        ProcessImageOptimized(processedImage, inputData, TargetSize, TargetSize);

        var dimensions = new[] { 1, 3, TargetSize, TargetSize };
        var tensor = new DenseTensor<float>(inputData, dimensions);
        var container = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("pixel_values", tensor) };

        using var results = _session.Run(container);
        var outputTensor = results[0].AsTensor<float>() ?? throw new InvalidOperationException("Model output is null");

        var vectorData = new float[AppConsts.VectorSize];
        ExtractVectorOptimized(outputTensor, vectorData);
        NormalizeVectorSIMD(vectorData.AsSpan(0, AppConsts.VectorSize));

        var result = new float[AppConsts.VectorSize];
        Array.Copy(vectorData, result, AppConsts.VectorSize);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ProcessImageOptimized(Image<Rgb24> image, float[] inputData, int targetWidth, int targetHeight)
    {
        using var resizedImage = image.Clone(ctx => ctx.Resize(targetWidth, targetHeight));
        var channelSize = targetWidth * targetHeight;
        var pixelData = new Rgb24[channelSize];
        resizedImage.ProcessPixelRows(accessor => {
            for (int y = 0; y < targetHeight; y++) {
                var row = accessor.GetRowSpan(y);
                row.CopyTo(pixelData.AsSpan(y * targetWidth, targetWidth));
            }
        });

        const float scale = 1.0f / 255.0f;
        const float rMean = 0.485f, gMean = 0.456f, bMean = 0.406f;
        const float rStd = 0.229f, gStd = 0.224f, bStd = 0.225f;

        var rSpan = inputData.AsSpan(0, channelSize);
        var gSpan = inputData.AsSpan(channelSize, channelSize);
        var bSpan = inputData.AsSpan(2 * channelSize, channelSize);

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
        var vectorSpan = vectorArray.AsSpan(0, AppConsts.VectorSize);

        if (shape.Length == 3) {
            var hiddenSize = Math.Min(shape[2], AppConsts.VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, 0, i];
            }
        }
        else if (shape.Length == 2) {
            var hiddenSize = Math.Min(shape[1], AppConsts.VectorSize);
            for (var i = 0; i < hiddenSize; i++) {
                vectorSpan[i] = outputTensor[0, i];
            }
        }
        else {
            var copyLength = Math.Min(outputTensor.Length, AppConsts.VectorSize);
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

    public static float ComputeDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length)
            return 1f;

        var dotProduct = 0f;
        for (int i = 0; i < AppConsts.VectorSize; i++) {
            dotProduct += x[i] * y[i];
        }

        var distance = 1f - dotProduct;
        return Math.Max(0f, Math.Min(1f, distance));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue) {
            if (disposing) {
                lock (_lock) {
                    _session?.Dispose();
                    _sessionOptions?.Dispose();
                }
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}