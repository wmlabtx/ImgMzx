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
