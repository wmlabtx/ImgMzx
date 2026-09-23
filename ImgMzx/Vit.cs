using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics.Tensors;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace ImgMzx;

public class Vit : IDisposable
{
    private static readonly float[] _rLut = CreateLut(0.485f, 1f / 0.229f);
    private static readonly float[] _gLut = CreateLut(0.456f, 1f / 0.224f);
    private static readonly float[] _bLut = CreateLut(0.406f, 1f / 0.225f);

    private static float[] CreateLut(float mean, float invStd)
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++) {
            lut[i] = (i / 255f - mean) * invStd;
        }
        return lut;
    }

    private readonly InferenceSession _sessionVit;
    private readonly SessionOptions _sessionOptionsGPU;
    private readonly Lock _lock = new();
    private bool _disposed;

    public Vit(string fileVit)
    {
        _sessionOptionsGPU = new SessionOptions {
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
            EnableCpuMemArena = true,
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC
        };

        try {
            var cudaOptions = new OrtCUDAProviderOptions();
            var configs = new Dictionary<string, string> {
                { "device_id", "0" },
                { "cudnn_conv_algo_search", "HEURISTIC" },
                { "do_copy_in_default_stream", "1" }
            };
            cudaOptions.UpdateOptions(configs);
            _sessionOptionsGPU.AppendExecutionProvider_CUDA(cudaOptions);
        }
        catch {
            _sessionOptionsGPU.AppendExecutionProvider_CPU();
        }

        _sessionVit = new InferenceSession(fileVit, _sessionOptionsGPU);
    }

    public static (int scaledW, int scaledH, int cropW, int cropH) GetScaledSize(int imageWidth, int imageHeight, int shortSide = 384)
    {
        float scale = (float)shortSide / Math.Min(imageWidth, imageHeight);
        var scaledW = (int)Math.Round(imageWidth * scale);
        var scaledH = (int)Math.Round(imageHeight * scale);
        var cropW = Math.Min(1024, (scaledW / 16) * 16);
        var cropH = Math.Min(1024, (scaledH / 16) * 16);
        return (scaledW, scaledH, cropW, cropH);
    }

    public float[] CalculateVector(Image<Rgb24> image) => CalculateVector(image, 384);

    public float[] CalculateVector(Image<Rgb24> image, int shortSide)
    {
        (int scaledW, int scaledH, int cropW, int cropH) = GetScaledSize(image.Width, image.Height, shortSide);
        using var prepared = image.Clone(ctx => ctx
            .Resize(scaledW, scaledH)
            .Crop(new Rectangle((scaledW - cropW) / 2, (scaledH - cropH) / 2, cropW, cropH)));

        int channelSize = cropW * cropH;
        var inputData = new float[3 * channelSize];
        var rLut = _rLut;
        var gLut = _gLut;
        var bLut = _bLut;

        prepared.ProcessPixelRows(accessor => {
            for (int y = 0; y < cropH; y++) {
                var row = accessor.GetRowSpan(y);
                int offset = y * cropW;
                for (int x = 0; x < cropW; x++) {
                    var p = row[x];
                    int idx = offset + x;
                    inputData[idx] = rLut[p.R];
                    inputData[channelSize + idx] = gLut[p.G];
                    inputData[2 * channelSize + idx] = bLut[p.B];
                }
            }
        });

        var tensorVit = new DenseTensor<float>(inputData, [1, 3, cropH, cropW]);
        var containerVit = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("pixel_values", tensorVit) };

        using var results = _sessionVit.Run(containerVit);
        var hiddenStates = results.First(r => r.Name == "last_hidden_state").AsTensor<float>()
            ?? throw new InvalidOperationException("Hidden states output is null");

        // CLS token at index 0
        var hiddenDim = hiddenStates.Dimensions[2];
        var result = new float[AppConsts.VectorSize];
        var copyDim = Math.Min(hiddenDim, AppConsts.VectorSize);
        for (int d = 0; d < copyDim; d++) {
            result[d] = hiddenStates[0, 0, d];
        }

        Normalize(result);
        return result;
    }

    private static void Normalize(Span<float> v)
    {
        float norm = TensorPrimitives.Norm(v);
        if (norm > 0f) {
            float inv = 1f / norm;
            for (int i = 0; i < v.Length; i++) {
                v[i] *= inv;
            }
        }
    }

    public static float ComputeDistance(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (float.IsNaN(x[0]) || float.IsNaN(y[0])) {
            return 1f;
        }

        float dot = TensorPrimitives.Dot(x, y);
        return Math.Clamp(1f - dot, 0f, 1f);
    }

    public static float[] CalculateColorMoments(Image<Rgb24> image) => CalculateColorMoments(image, 128);

    private static float[] CalculateColorMoments(Image<Rgb24> image, int shortSide)
    {
        var minSide = Math.Min(image.Width, image.Height);
        Image<Rgb24>? resized = null;
        var source = image;
        if (minSide > shortSide) {
            var scale = (float)shortSide / minSide;
            var w = Math.Max(1, (int)Math.Round(image.Width * scale));
            var h = Math.Max(1, (int)Math.Round(image.Height * scale));
            resized = image.Clone(ctx => ctx.Resize(w, h));
            source = resized;
        }

        var pixelCount = source.Width * source.Height;
        var l = new float[pixelCount];
        var a = new float[pixelCount];
        var b = new float[pixelCount];

        source.ProcessPixelRows(accessor => {
            for (int y = 0; y < accessor.Height; y++) {
                var row = accessor.GetRowSpan(y);
                var offset = y * accessor.Width;
                for (int x = 0; x < accessor.Width; x++) {
                    var p = row[x];
                    var (lv, av, bv) = AppColor.RgbToLab(p.R, p.G, p.B);
                    var idx = offset + x;
                    l[idx] = lv;
                    a[idx] = av;
                    b[idx] = bv;
                }
            }
        });

        resized?.Dispose();

        return [Median(l), Median(a), Median(b), StdDev(l), StdDev(a), StdDev(b)];
    }

    private static float Median(float[] values)
    {
        Array.Sort(values);
        var mid = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[mid - 1] + values[mid]) * 0.5f
            : values[mid];
    }

    private static float StdDev(ReadOnlySpan<float> values)
    {
        var mean = TensorPrimitives.Sum(values) / values.Length;
        var sumSq = 0f;
        foreach (var v in values) {
            var d = v - mean;
            sumSq += d * d;
        }

        return MathF.Sqrt(sumSq / values.Length);
    }

    public const int ColorBucketCount = 512;

    // Quantile cut points fitted on a 20k-image sample of the library (medianL/A/B, sigmaL/A/B).
    // 4 bins each for medianL/A/B, 2 bins each for sigmaL/A/B: 4*4*4*2*2*2 = 512 buckets.
    private static readonly float[] MedianLEdges = [44.31f, 55.65f, 66.45f];
    private static readonly float[] MedianAEdges = [0.05f, 3.53f, 9.35f];
    private static readonly float[] MedianBEdges = [2.32f, 9.72f, 18.51f];
    private static readonly float[] SigmaLEdges = [20.63f];
    private static readonly float[] SigmaAEdges = [8.59f];
    private static readonly float[] SigmaBEdges = [10.92f];

    public static int CalculateColorBucket(ReadOnlySpan<float> moments)
    {
        var biL = BinIndex(moments[0], MedianLEdges);
        var biA = BinIndex(moments[1], MedianAEdges);
        var biB = BinIndex(moments[2], MedianBEdges);
        var biSL = BinIndex(moments[3], SigmaLEdges);
        var biSA = BinIndex(moments[4], SigmaAEdges);
        var biSB = BinIndex(moments[5], SigmaBEdges);

        return biL * 128 + biA * 32 + biB * 8 + biSL * 4 + biSA * 2 + biSB;
    }

    // Edges are the only interior cut points; a value below the first or above the last
    // edge falls into the outer (open-ended) bin, so future outliers always resolve to
    // a valid index instead of needing an explicit +-infinity sentinel.
    private static int BinIndex(float value, ReadOnlySpan<float> edges)
    {
        var idx = 0;
        for (var i = 0; i < edges.Length; i++) {
            if (value > edges[i]) {
                idx++;
            }
        }

        return idx;
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock) {
            _sessionVit?.Dispose();
            _sessionOptionsGPU?.Dispose();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
