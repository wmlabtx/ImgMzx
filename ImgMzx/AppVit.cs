using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Image = System.Drawing.Image;
using Rectangle = System.Drawing.Rectangle;

namespace ImgMzx;

public static class AppVit
{
    private static readonly InferenceSession _session = new(AppConsts.FileVit);

    public static float[] GetVector(Image<Rgb24> image)
    {
        const int ImageSize = 448;
        using var processedImage = image.CloneAs<Rgb24>();
        image.Mutate(ctx => {
            ctx.Resize(new ResizeOptions {
                Size = new SixLabors.ImageSharp.Size(ImageSize, ImageSize),
                Mode = ResizeMode.Pad,
                PadColor = SixLabors.ImageSharp.Color.Black
            });
        });
        var tensor = new DenseTensor<float>([1, 3, ImageSize, ImageSize]);
        image.ProcessPixelRows(accessor => {
            for (var y = 0; y < accessor.Height; y++) {
                var pixelRow = accessor.GetRowSpan(y);
                for (var x = 0; x < pixelRow.Length; x++) {
                    var pixel = pixelRow[x];
                    var red = (pixel.R / 255f - 0.48145466f) / 0.26862954f;
                    var green = (pixel.G / 255f - 0.48145466f) / 0.26862954f;
                    var blue = (pixel.B / 255f - 0.40821073f) / 0.27577711f;
                    tensor[0, 0, y, x] = red;
                    tensor[0, 1, y, x] = green;
                    tensor[0, 2, y, x] = blue;
                }
            }
        });

        var container = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("pixel_values", tensor) };
        var results = _session.Run(container);
        var vector = results[0].AsEnumerable<float>().ToArray();
        var norm = (float)Math.Sqrt(vector.Sum(t => t * t));
        Parallel.For(0, vector.Length, i => {
            vector[i] /= norm;
        });

        return vector;
    }

    public static float GetDistance(float[] x, float[] y)
    {
        if (x.Length == 0 || y.Length == 0 || x.Length != y.Length) {
            return 1.1f;
        }

        var distance = x.Select((t, i) => t * y[i]).Sum();
        distance = 1f - distance;
        return distance;
    }
}