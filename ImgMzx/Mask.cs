using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;

namespace ImgMzx;

public class Mask : IDisposable
{
    private const int MaskSize = 256;
    private readonly InferenceSession _session;
    private readonly SessionOptions _sessionOptions;
    private bool _disposed;

    public Mask(string filePath)
    {
        _sessionOptions = new SessionOptions();
        _sessionOptions.AppendExecutionProvider_CPU();
        _session = new InferenceSession(filePath, _sessionOptions);
    }

    public float[] GetMask(Image<Rgb24> image)
    {
        float scale = Math.Max((float)MaskSize / image.Width, (float)MaskSize / image.Height);
        int scaledW = (int)Math.Round(image.Width * scale);
        int scaledH = (int)Math.Round(image.Height * scale);
        using var resized = image.Clone(ctx => ctx
            .Resize(new ResizeOptions { Size = new Size(scaledW, scaledH), Mode = ResizeMode.Max })
            .Crop(new Rectangle((scaledW - MaskSize) / 2, (scaledH - MaskSize) / 2, MaskSize, MaskSize))
        );

        var input = new float[3 * MaskSize * MaskSize];
        FillInputTensor(resized, input);

        var tensor = new DenseTensor<float>(input, [1, 3, MaskSize, MaskSize]);
        var container = new[] { NamedOnnxValue.CreateFromTensor("pixel_values", tensor) };

        using var results = _session.Run(container);
        var output = results[0].AsTensor<float>();

        // Output shape: [1, 1, 256, 256]
        var mask = new float[MaskSize * MaskSize];
        for (int y = 0; y < MaskSize; y++)
        {
            for (int x = 0; x < MaskSize; x++)
            {
                mask[y * MaskSize + x] = output[0, 0, y, x];
            }
        }

        return mask;
    }

    private static void FillInputTensor(Image<Rgb24> image, float[] data)
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

    public void Dispose()
    {
        if (_disposed) return;
        _session.Dispose();
        _sessionOptions.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}