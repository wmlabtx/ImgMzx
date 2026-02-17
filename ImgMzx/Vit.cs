using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.CompilerServices;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;

namespace ImgMzx;

public class Vit : IDisposable
{
    private const int VitSize = 256;

    private readonly InferenceSession _session;
    private readonly SessionOptions _sessionOptions;
    private readonly Lock _lock = new();
    private bool _disposed;

    public Vit(string fileVit)
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
        catch {
            _sessionOptions.AppendExecutionProvider_CPU();
        }

        _session = new InferenceSession(fileVit, _sessionOptions);
    }

    public float[] CalculateVector(Image<Rgb24> image)
    {
        using var prepared = PrepareImage(image);

        var inputData = new float[3 * VitSize * VitSize];
        FillInputTensor(prepared, inputData);

        var tensor = new DenseTensor<float>(inputData, [1, 3, VitSize, VitSize]);
        var container = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("pixel_values", tensor) };

        using var results = _session.Run(container);
        var hiddenStates = results.First(r => r.Name == "last_hidden_state").AsTensor<float>()
            ?? throw new InvalidOperationException("Hidden states output is null");

        // Extract CLS token (index 0)
        var hiddenDim = hiddenStates.Dimensions[2];
        var result = new float[AppConsts.VectorSize];
        var copyDim = Math.Min(hiddenDim, AppConsts.VectorSize);

        for (int d = 0; d < copyDim; d++) {
            result[d] = hiddenStates[0, 0, d];
        }

        Normalize(result);
        return result;
    }

    private static Image<Rgb24> PrepareImage(Image<Rgb24> image)
    {
        float scale = Math.Max((float)VitSize / image.Width, (float)VitSize / image.Height);
        int w = (int)Math.Round(image.Width * scale);
        int h = (int)Math.Round(image.Height * scale);

        return image.Clone(ctx => ctx
            .Resize(new ResizeOptions { Size = new Size(w, h), Mode = ResizeMode.Max })
            .Crop(new Rectangle((w - VitSize) / 2, (h - VitSize) / 2, VitSize, VitSize))
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillInputTensor(Image<Rgb24> image, float[] data)
    {
        const int channelSize = VitSize * VitSize;
        const float scale = 1f / 255f;
        const float rMean = 0.485f, gMean = 0.456f, bMean = 0.406f;
        const float invRStd = 1f / 0.229f, invGStd = 1f / 0.224f, invBStd = 1f / 0.225f;

        image.ProcessPixelRows(accessor => {
            for (int y = 0; y < VitSize; y++) {
                var row = accessor.GetRowSpan(y);
                int offset = y * VitSize;
                for (int x = 0; x < VitSize; x++) {
                    var p = row[x];
                    int idx = offset + x;
                    data[idx] = (p.R * scale - rMean) * invRStd;
                    data[channelSize + idx] = (p.G * scale - gMean) * invGStd;
                    data[2 * channelSize + idx] = (p.B * scale - bMean) * invBStd;
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Normalize(Span<float> v)
    {
        float sum = 0f;
        foreach (var x in v) sum += x * x;
        if (sum > 0) {
            float inv = 1f / MathF.Sqrt(sum);
            for (int i = 0; i < v.Length; i++) v[i] *= inv;
        }
    }

    public static float ComputeDistance(float[] x, float[] y)
    {
        if (x.Length != y.Length || x.Length == 0) return 1f;

        float dot = 0f;
        for (int i = 0; i < x.Length; i++) dot += x[i] * y[i];

        return Math.Clamp(1f - dot, 0f, 1f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock) {
            _session?.Dispose();
            _sessionOptions?.Dispose();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}