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
    private const int MaskSize = 256;

    private readonly InferenceSession _sessionVit;
    private readonly InferenceSession _sessionMask;
    private readonly SessionOptions _sessionOptionsGPU, _sessionOptionsCPU;
    private readonly Lock _lock = new();
    private bool _disposed;

    public Vit(string fileVit, string fileMask)
    {
        _sessionOptionsGPU = new SessionOptions {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = true,
            EnableMemoryPattern = true,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        try {
            _sessionOptionsGPU.AppendExecutionProvider_CUDA(0);
        }
        catch {
            _sessionOptionsGPU.AppendExecutionProvider_CPU();
        }

        _sessionVit = new InferenceSession(fileVit, _sessionOptionsGPU);

        _sessionOptionsCPU = new SessionOptions();
        _sessionOptionsCPU.AppendExecutionProvider_CPU();
        _sessionMask = new InferenceSession(fileMask, _sessionOptionsCPU);
    }

    public float[] CalculateVector(Image<Rgb24> image)
    {
        using var prepared = PrepareImage(image);

        var inputMask = new float[3 * MaskSize * MaskSize];
        FillInputTensorMask(prepared, inputMask);

        var tensorMask = new DenseTensor<float>(inputMask, [1, 3, MaskSize, MaskSize]);
        var containerMask = new[] { NamedOnnxValue.CreateFromTensor("pixel_values", tensorMask) };

        using var resultsMask = _sessionMask.Run(containerMask);
        var output = resultsMask[0].AsTensor<float>();

        // Output shape: [1, 1, 256, 256]
        var mask = new float[MaskSize * MaskSize];
        for (int y = 0; y < MaskSize; y++) {
            for (int x = 0; x < MaskSize; x++) {
                mask[y * MaskSize + x] = output[0, 0, y, x];
            }
        }

        var inputDataVit = new float[3 * VitSize * VitSize];
        FillInputTensorVit(prepared, inputDataVit);

        var tensorVit = new DenseTensor<float>(inputDataVit, [1, 3, VitSize, VitSize]);
        var containerVit = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("pixel_values", tensorVit) };

        using var results = _sessionVit.Run(containerVit);
        var hiddenStates = results.First(r => r.Name == "last_hidden_state").AsTensor<float>()
            ?? throw new InvalidOperationException("Hidden states output is null");

        // Compute per-patch weights from mask (16x16 grid, each patch = 16x16 pixels)
        const int patchStart = 5;
        const int patchGrid = 16;
        const int patchPixels = 16;
        const float invPatchArea = 1f / (patchPixels * patchPixels);
        var weights = new float[patchGrid * patchGrid];

        Parallel.For(0, patchGrid * patchGrid, i => {
            int py = i / patchGrid;
            int px = i % patchGrid;
            float wSum = 0f;
            int baseY = py * patchPixels;
            int baseX = px * patchPixels;
            for (int dy = 0; dy < patchPixels; dy++) {
                int rowOffset = (baseY + dy) * MaskSize + baseX;
                for (int dx = 0; dx < patchPixels; dx++) {
                    wSum += mask[rowOffset + dx];
                }
            }
            weights[i] = wSum * invPatchArea;
        });

        // Weighted mean-pool patch tokens (skip first 5: CLS + 4 register tokens)
        var hiddenDim = hiddenStates.Dimensions[2];
        var result = new float[AppConsts.VectorSize];
        var copyDim = Math.Min(hiddenDim, AppConsts.VectorSize);
        var totalWeight = weights.Sum();
        if (totalWeight < 1e-6f) totalWeight = weights.Length;
        var invWeight = 1f / totalWeight;

        Parallel.For(0, copyDim, d => {
            float sum = 0f;
            for (int p = 0; p < weights.Length; p++) {
                sum += hiddenStates[0, patchStart + p, d] * weights[p];
            }
            result[d] = sum * invWeight;
        });

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
    private static void FillInputTensorVit(Image<Rgb24> image, float[] data)
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

    private static void FillInputTensorMask(Image<Rgb24> image, float[] data)
    {
        // No normalization, no padding, only rescale to [0,1]
        const int channelSize = MaskSize * MaskSize;
        const float scale = 1f / 255f;

        image.ProcessPixelRows(accessor => {
            for (int y = 0; y < MaskSize; y++) {
                var row = accessor.GetRowSpan(y);
                int offset = y * MaskSize;
                for (int x = 0; x < MaskSize; x++) {
                    var p = row[x];
                    int idx = offset + x;
                    data[idx] = p.R * scale;
                    data[channelSize + idx] = p.G * scale;
                    data[2 * channelSize + idx] = p.B * scale;
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
            _sessionVit?.Dispose();
            _sessionMask?.Dispose();
            _sessionOptionsGPU?.Dispose();
            _sessionOptionsCPU?.Dispose();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}